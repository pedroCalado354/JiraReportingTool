using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JiraReportingTool.Models;

public class JiraService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly string _baseUrl;

    public JiraService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;

        _baseUrl = _config["Jira:BaseUrl"] ?? "";
        var email = _config["Jira:Email"];
        var token = _config["Jira:ApiToken"];

        var authBytes = Encoding.ASCII.GetBytes($"{email}:{token}");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
    }

    public async Task<string> GetIssue(string issueKey)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/rest/api/3/issue/{issueKey}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<JiraEpicReport> GetEpicReportAsync(string epicKey)
    {
        // Fetch the epic itself
        var epicResponse = await _httpClient.GetAsync(
            $"{_baseUrl}/rest/api/3/issue/{epicKey}?fields=summary,status,assignee");
        epicResponse.EnsureSuccessStatusCode();
        var epicJson = await epicResponse.Content.ReadAsStringAsync();
        var report = ParseEpic(epicJson);

        // Fetch child issues using the modern "parent" field (Jira Cloud deprecated "Epic Link" — returns 410)
        var jql = Uri.EscapeDataString(
            $"issueType != Epic AND parent = \"{epicKey}\" ORDER BY created ASC");
        var fields = "summary,status,assignee,issuetype,timetracking,worklog";

        var searchResponse = await _httpClient.GetAsync(
            $"{_baseUrl}/rest/api/3/search/jql?jql={jql}&fields={fields}&maxResults=100");
        searchResponse.EnsureSuccessStatusCode();
        var searchJson = await searchResponse.Content.ReadAsStringAsync();

        report.Issues = ParseIssues(searchJson);
        return report;
    }

    private JiraEpicReport ParseEpic(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var fields = root.GetProperty("fields");
        var status = fields.GetProperty("status");

        return new JiraEpicReport
        {
            Key = root.GetProperty("key").GetString() ?? "",
            Summary = fields.GetProperty("summary").GetString() ?? "",
            Status = status.GetProperty("name").GetString() ?? "",
            StatusCategoryKey = status.TryGetProperty("statusCategory", out var sc)
                ? sc.GetProperty("key").GetString() ?? ""
                : "",
            Assignee = fields.TryGetProperty("assignee", out var assignee) && assignee.ValueKind != JsonValueKind.Null
                ? assignee.GetProperty("displayName").GetString() ?? "Unassigned"
                : "Unassigned"
        };
    }

    private List<JiraIssueModel> ParseIssues(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var issues = new List<JiraIssueModel>();

        if (!doc.RootElement.TryGetProperty("issues", out var issuesArray))
            return issues;

        foreach (var item in issuesArray.EnumerateArray())
        {
            var fields = item.GetProperty("fields");
            var issue = new JiraIssueModel
            {
                Key = item.GetProperty("key").GetString() ?? "",
                Summary = fields.GetProperty("summary").GetString() ?? "",
                IssueType = fields.TryGetProperty("issuetype", out var it) && it.ValueKind != JsonValueKind.Null
                    ? it.GetProperty("name").GetString() ?? ""
                    : "",
                Assignee = fields.TryGetProperty("assignee", out var assignee) && assignee.ValueKind != JsonValueKind.Null
                    ? assignee.GetProperty("displayName").GetString() ?? "Unassigned"
                    : "Unassigned"
            };

            if (fields.TryGetProperty("status", out var status))
            {
                issue.Status = status.GetProperty("name").GetString() ?? "";
                if (status.TryGetProperty("statusCategory", out var sc))
                    issue.StatusCategoryKey = sc.GetProperty("key").GetString() ?? "";
            }

            if (fields.TryGetProperty("timetracking", out var tt) && tt.ValueKind != JsonValueKind.Null)
            {
                if (tt.TryGetProperty("originalEstimate", out var oe) && oe.ValueKind != JsonValueKind.Null)
                    issue.OriginalEstimate = oe.GetString() ?? "-";
                if (tt.TryGetProperty("originalEstimateSeconds", out var oes))
                    issue.OriginalEstimateSeconds = oes.GetInt32();
                if (tt.TryGetProperty("timeSpent", out var ts) && ts.ValueKind != JsonValueKind.Null)
                    issue.TimeSpent = ts.GetString() ?? "-";
                if (tt.TryGetProperty("timeSpentSeconds", out var tss))
                    issue.TimeSpentSeconds = tss.GetInt32();
                if (tt.TryGetProperty("remainingEstimate", out var re) && re.ValueKind != JsonValueKind.Null)
                    issue.RemainingEstimate = re.GetString() ?? "-";
            }

            if (fields.TryGetProperty("worklog", out var worklog) &&
                worklog.TryGetProperty("worklogs", out var wls))
            {
                foreach (var wl in wls.EnumerateArray())
                {
                    var entry = new WorklogEntry
                    {
                        Author = wl.TryGetProperty("author", out var author) && author.ValueKind != JsonValueKind.Null
                            ? author.GetProperty("displayName").GetString() ?? ""
                            : "",
                        TimeSpent = wl.TryGetProperty("timeSpent", out var tSpent)
                            ? tSpent.GetString() ?? ""
                            : "",
                        TimeSpentSeconds = wl.TryGetProperty("timeSpentSeconds", out var tSec)
                            ? tSec.GetInt32()
                            : 0,
                        Comment = ExtractAdfText(wl)
                    };

                    if (wl.TryGetProperty("started", out var started) &&
                        DateTime.TryParse(started.GetString(), out var dt))
                        entry.Started = dt;

                    issue.Worklogs.Add(entry);
                }
            }

            issues.Add(issue);
        }

        return issues;
    }

    public async Task<SprintReport> GetSprintReportAsync(string projectKey, int sprintId)
    {
        // customfield_10016 = Story Points, customfield_10020 = Sprint metadata
        var jql = Uri.EscapeDataString(
            $"project = Jimpisoft AND \"Epic Link\" in \"{projectKey}\" AND sprint in {sprintId} ORDER BY status ASC, priority DESC");
        var fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,customfield_10016,customfield_10020";

        var response = await _httpClient.GetAsync(
            $"{_baseUrl}/rest/api/3/search/jql?jql={jql}&fields={fields}&maxResults=100");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        return ParseSprintReport(json, projectKey);
    }

    private SprintReport ParseSprintReport(string json, string projectKey)
    {
        using var doc = JsonDocument.Parse(json);
        var report = new SprintReport { ProjectKey = projectKey.ToUpper() };

        if (!doc.RootElement.TryGetProperty("issues", out var issuesArray))
            return report;

        foreach (var item in issuesArray.EnumerateArray())
        {
            var fields = item.GetProperty("fields");
            var issue = new SprintIssue
            {
                Key = item.GetProperty("key").GetString() ?? "",
                Summary = fields.GetProperty("summary").GetString() ?? "",
                IssueType = fields.TryGetProperty("issuetype", out var it) && it.ValueKind != JsonValueKind.Null
                    ? it.GetProperty("name").GetString() ?? ""
                    : "",
                Assignee = fields.TryGetProperty("assignee", out var assignee) && assignee.ValueKind != JsonValueKind.Null
                    ? assignee.GetProperty("displayName").GetString() ?? "Unassigned"
                    : "Unassigned",
                Priority = fields.TryGetProperty("priority", out var prio) && prio.ValueKind != JsonValueKind.Null
                    ? prio.GetProperty("name").GetString() ?? "Medium"
                    : "Medium"
            };

            if (fields.TryGetProperty("status", out var status))
            {
                issue.Status = status.GetProperty("name").GetString() ?? "";
                if (status.TryGetProperty("statusCategory", out var sc))
                    issue.StatusCategoryKey = sc.GetProperty("key").GetString() ?? "";
            }

            if (fields.TryGetProperty("timetracking", out var tt) && tt.ValueKind != JsonValueKind.Null)
            {
                if (tt.TryGetProperty("originalEstimate", out var oe) && oe.ValueKind != JsonValueKind.Null)
                    issue.OriginalEstimate = oe.GetString() ?? "-";
                if (tt.TryGetProperty("originalEstimateSeconds", out var oes))
                    issue.OriginalEstimateSeconds = oes.GetInt32();
                if (tt.TryGetProperty("timeSpent", out var ts) && ts.ValueKind != JsonValueKind.Null)
                    issue.TimeSpent = ts.GetString() ?? "-";
                if (tt.TryGetProperty("timeSpentSeconds", out var tss))
                    issue.TimeSpentSeconds = tss.GetInt32();
                if (tt.TryGetProperty("remainingEstimate", out var re) && re.ValueKind != JsonValueKind.Null)
                    issue.RemainingEstimate = re.GetString() ?? "-";
                if (tt.TryGetProperty("remainingEstimateSeconds", out var res))
                    issue.RemainingEstimateSeconds = res.GetInt32();
            }

            // Story points (customfield_10016)
            if (fields.TryGetProperty("customfield_10016", out var sp) && sp.ValueKind == JsonValueKind.Number)
                issue.StoryPoints = sp.GetInt32();

            // Sprint metadata from first active sprint in customfield_10020
            if (report.SprintName == "" &&
                fields.TryGetProperty("customfield_10020", out var sprints) &&
                sprints.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sprints.EnumerateArray())
                {
                    if (!s.TryGetProperty("state", out var state) || state.GetString() != "active") continue;
                    report.SprintName = s.TryGetProperty("name", out var sn) ? sn.GetString() ?? "" : "";
                    if (s.TryGetProperty("startDate", out var sd) && DateTime.TryParse(sd.GetString(), out var sdParsed))
                        report.StartDate = sdParsed;
                    if (s.TryGetProperty("endDate", out var ed) && DateTime.TryParse(ed.GetString(), out var edParsed))
                        report.EndDate = edParsed;
                    break;
                }
            }

            if (fields.TryGetProperty("worklog", out var worklog) &&
                worklog.TryGetProperty("worklogs", out var wls))
            {
                foreach (var wl in wls.EnumerateArray())
                {
                    var entry = new WorklogEntry
                    {
                        Author = wl.TryGetProperty("author", out var author) && author.ValueKind != JsonValueKind.Null
                            ? author.GetProperty("displayName").GetString() ?? ""
                            : "",
                        TimeSpent = wl.TryGetProperty("timeSpent", out var tSpent) ? tSpent.GetString() ?? "" : "",
                        TimeSpentSeconds = wl.TryGetProperty("timeSpentSeconds", out var tSec) ? tSec.GetInt32() : 0,
                        Comment = ExtractAdfText(wl)
                    };
                    if (wl.TryGetProperty("started", out var started) &&
                        DateTime.TryParse(started.GetString(), out var dt))
                        entry.Started = dt;
                    issue.Worklogs.Add(entry);
                }
            }

            report.Issues.Add(issue);
        }

        return report;
    }

    // Jira API v3 returns comments in Atlassian Document Format (ADF)
    private static string ExtractAdfText(JsonElement wl)
    {
        if (!wl.TryGetProperty("comment", out var comment) || comment.ValueKind != JsonValueKind.Object)
            return "";
        if (!comment.TryGetProperty("content", out var paragraphs))
            return "";

        var texts = new List<string>();
        foreach (var para in paragraphs.EnumerateArray())
        {
            if (!para.TryGetProperty("content", out var inlines)) continue;
            foreach (var inline in inlines.EnumerateArray())
                if (inline.TryGetProperty("text", out var txt))
                    texts.Add(txt.GetString() ?? "");
        }
        return string.Join(" ", texts);
    }
}
