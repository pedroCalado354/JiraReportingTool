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

    // Jira Cloud hard-caps responses at 100 items per request.
    // Uses POST /rest/api/3/search/jql with cursor-based pagination (nextPageToken).
    private async Task<string> FetchAllPagesAsync(string jql, string fields)
    {
        var allIssues = new List<string>();
        var fieldList = fields.Split(',').Select(f => f.Trim()).ToArray();
        string? nextPageToken = null;

        while (true)
        {
            var bodyDict = new Dictionary<string, object>
            {
                ["jql"] = Uri.UnescapeDataString(jql),
                ["fields"] = fieldList,
                ["maxResults"] = 100
            };
            if (nextPageToken != null)
                bodyDict["nextPageToken"] = nextPageToken;

            var body = JsonSerializer.Serialize(bodyDict);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/rest/api/3/search/jql", content);
            response.EnsureSuccessStatusCode();
            var pageJson = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(pageJson);

            if (!doc.RootElement.TryGetProperty("issues", out var issuesEl) || issuesEl.GetArrayLength() == 0)
                break;

            foreach (var issue in issuesEl.EnumerateArray())
                allIssues.Add(issue.GetRawText());

            nextPageToken = doc.RootElement.TryGetProperty("nextPageToken", out var npt) && npt.ValueKind == JsonValueKind.String
                ? npt.GetString()
                : null;

            if (nextPageToken == null)
                break;
        }

        return $"{{\"issues\":[{string.Join(",", allIssues)}]}}";
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

        var searchJson = await FetchAllPagesAsync(jql, fields);
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

        var json = await FetchAllPagesAsync(jql, fields);
        var (report, truncated) = ParseSprintReport(json, projectKey);
        await FetchMissingWorklogsAsync(report, truncated);
        return report;
    }

    // Fetches ALL issues in a sprint (no epic filter) with epic grouping — used by Team Delivery Dashboard
    public Task<SprintReport> GetDeliveryDataAsync(int sprintId)
    {
        var jql = Uri.EscapeDataString($"sprint = {sprintId} ORDER BY status ASC, priority DESC");
        return FetchDeliveryReportAsync(jql);
    }

    // Fetches all issues for a given epic inside a specific sprint — used by Epic Delivery Forecast
    public async Task<SprintReport> GetEpicSprintForecastAsync(string epicKey, int sprintId)
    {
        var jql = Uri.EscapeDataString(
            $"sprint = {sprintId} AND (\"Epic Link\" = \"{epicKey}\" OR parent = \"{epicKey}\") ORDER BY status ASC, priority DESC");
        const string fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,customfield_10014,customfield_10016,customfield_10020,parent";
        var json = await FetchAllPagesAsync(jql, fields);
        var (report, truncated) = ParseSprintReport(json, "");
        await FetchMissingWorklogsAsync(report, truncated);
        return report;
    }

    // Loads delivery data using the JQL from a saved Jira filter
    public Task<SprintReport> GetDeliveryDataByFilterAsync(string filterJql)
    {
        var jql = Uri.EscapeDataString(filterJql);
        return FetchDeliveryReportAsync(jql);
    }

    // Returns the current user's saved Jira filters
    public async Task<List<JiraFilter>> GetMyFiltersAsync()
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/rest/api/3/filter/my?expand=jql");
        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var filters = new List<JiraFilter>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            filters.Add(new JiraFilter
            {
                Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Jql = item.TryGetProperty("jql", out var jql) ? jql.GetString() ?? "" : "",
                Description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : ""
            });
        }
        return filters;
    }

    // Shared delivery fetch: paginate → parse → enrich epic names
    private async Task<SprintReport> FetchDeliveryReportAsync(string encodedJql)
    {
        const string fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,customfield_10014,customfield_10016,customfield_10020,parent";

        var json = await FetchAllPagesAsync(encodedJql, fields);
        var (report, truncated) = ParseSprintReport(json, "");
        await FetchMissingWorklogsAsync(report, truncated);

        var epicKeys = report.Issues
            .Where(i => !string.IsNullOrEmpty(i.EpicKey))
            .Select(i => i.EpicKey)
            .Distinct()
            .ToList();

        if (epicKeys.Any())
        {
            var epicNames = await FetchEpicNamesAsync(epicKeys);
            foreach (var issue in report.Issues)
                if (epicNames.TryGetValue(issue.EpicKey, out var name))
                    issue.EpicName = name;
        }

        return report;
    }

    private async Task<Dictionary<string, string>> FetchEpicNamesAsync(List<string> epicKeys)
    {
        var keysJql = string.Join(",", epicKeys.Select(k => $"\"{k}\""));
        var jql = Uri.EscapeDataString($"key in ({keysJql})");
        var response = await _httpClient.GetAsync(
            $"{_baseUrl}/rest/api/3/search/jql?jql={jql}&fields=summary&maxResults=500");

        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var map = new Dictionary<string, string>();
        if (!doc.RootElement.TryGetProperty("issues", out var issues)) return map;
        foreach (var item in issues.EnumerateArray())
        {
            var key = item.GetProperty("key").GetString() ?? "";
            var summary = item.GetProperty("fields").GetProperty("summary").GetString() ?? key;
            map[key] = summary;
        }
        return map;
    }

    private (SprintReport Report, List<string> TruncatedKeys) ParseSprintReport(string json, string projectKey)
    {
        using var doc = JsonDocument.Parse(json);
        var report = new SprintReport { ProjectKey = projectKey.ToUpper() };
        var truncatedKeys = new List<string>();

        if (!doc.RootElement.TryGetProperty("issues", out var issuesArray))
            return (report, truncatedKeys);

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

            // Epic Link — classic Jira: customfield_10014 holds the epic key directly
            if (fields.TryGetProperty("customfield_10014", out var el) && el.ValueKind == JsonValueKind.String)
                issue.EpicKey = el.GetString() ?? "";

            // Fallback for next-gen: check if direct parent is an Epic
            if (string.IsNullOrEmpty(issue.EpicKey) &&
                fields.TryGetProperty("parent", out var parent) && parent.ValueKind != JsonValueKind.Null)
            {
                var parentType = parent.TryGetProperty("fields", out var pf) &&
                                 pf.TryGetProperty("issuetype", out var pit)
                    ? pit.GetProperty("name").GetString() ?? ""
                    : "";
                if (parentType == "Epic")
                    issue.EpicKey = parent.GetProperty("key").GetString() ?? "";
            }

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
                var worklogTotal = worklog.TryGetProperty("total", out var wt) ? wt.GetInt32() : 0;
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
                // Jira returns at most 20 worklogs in search responses — flag for re-fetch if truncated
                if (worklogTotal > issue.Worklogs.Count)
                    truncatedKeys.Add(issue.Key);
            }

            report.Issues.Add(issue);
        }

        return (report, truncatedKeys);
    }

    // Fetches complete worklogs for issues where the search API returned a truncated list (max 20).
    private async Task FetchMissingWorklogsAsync(SprintReport report, List<string> truncatedKeys)
    {
        foreach (var key in truncatedKeys)
        {
            var issue = report.Issues.FirstOrDefault(i => i.Key == key);
            if (issue == null) continue;

            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/rest/api/3/issue/{key}/worklog?maxResults=5000");
            if (!response.IsSuccessStatusCode) continue;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("worklogs", out var wls)) continue;

            issue.Worklogs.Clear();
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
