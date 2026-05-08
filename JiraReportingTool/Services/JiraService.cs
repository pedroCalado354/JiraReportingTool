using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JiraReportingTool.Models;

public class JiraService : IJiraService
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
    private async Task<string> FetchAllPagesAsync(string jql, string fields, bool includeChangelog = false)
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
            if (includeChangelog)
                bodyDict["expand"] = new[] { "changelog" };
            if (nextPageToken != null)
                bodyDict["nextPageToken"] = nextPageToken;

            var body = JsonSerializer.Serialize(bodyDict);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/rest/api/3/search/jql", content);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"Jira API {(int)response.StatusCode}: {errorBody}", null, response.StatusCode);
            }
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
        var fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,labels,created,resolutiondate,customfield_10020";

        var searchJson = await FetchAllPagesAsync(jql, fields);
        var (sprintReport, truncated) = ParseSprintReport(searchJson, "");
        await FetchMissingWorklogsAsync(sprintReport, truncated);

        // Tag all issues with their parent epic context
        foreach (var issue in sprintReport.Issues)
        {
            issue.EpicKey  = epicKey;
            issue.EpicName = report.Summary;
        }

        report.Issues = sprintReport.Issues;
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

    // Returns the distinct epics that have issues in a given sprint
    public async Task<List<EpicSummary>> GetEpicsInSprintAsync(int sprintId)
    {
        var jql = Uri.EscapeDataString($"sprint = {sprintId} AND issueType != Epic");
        const string fields = "customfield_10014,parent,issuetype";

        var json = await FetchAllPagesAsync(jql, fields);

        using var doc = JsonDocument.Parse(json);
        var epicKeys = new HashSet<string>();

        if (doc.RootElement.TryGetProperty("issues", out var issuesEl))
        {
            foreach (var item in issuesEl.EnumerateArray())
            {
                var f = item.GetProperty("fields");

                // Classic epic link (customfield_10014)
                if (f.TryGetProperty("customfield_10014", out var el) && el.ValueKind == JsonValueKind.String)
                {
                    var k = el.GetString();
                    if (!string.IsNullOrEmpty(k)) { epicKeys.Add(k); continue; }
                }

                // Next-gen: direct parent is an Epic
                if (f.TryGetProperty("parent", out var parent) && parent.ValueKind != JsonValueKind.Null)
                {
                    var parentType = parent.TryGetProperty("fields", out var pf) &&
                                     pf.TryGetProperty("issuetype", out var pit)
                        ? pit.GetProperty("name").GetString() ?? "" : "";
                    if (parentType == "Epic")
                    {
                        var k = parent.GetProperty("key").GetString();
                        if (!string.IsNullOrEmpty(k)) epicKeys.Add(k);
                    }
                }
            }
        }

        if (epicKeys.Count == 0) return [];

        var epicNames = await FetchEpicNamesAsync(epicKeys.ToList());

        return epicKeys
            .Select(k => new EpicSummary
            {
                Key = k,
                Name = epicNames.TryGetValue(k, out var name) ? name : k
            })
            .OrderBy(e => e.Key)
            .ToList();
    }

    // Fetches all issues for a given epic inside a specific sprint — used by Epic Delivery Forecast
    public async Task<SprintReport> GetEpicSprintForecastAsync(string epicKey, int sprintId)
    {
        var jql = Uri.EscapeDataString(
            $"sprint = {sprintId} AND (\"Epic Link\" = \"{epicKey}\" OR parent = \"{epicKey}\") ORDER BY status ASC, priority DESC");
        const string fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,customfield_10014,customfield_10016,customfield_10020,parent,created,resolutiondate";
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
        const string fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,customfield_10014,customfield_10016,customfield_10020,parent,created,resolutiondate,labels,duedate";

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

    public async Task<Dictionary<string, string>> FetchEpicNamesAsync(List<string> epicKeys)
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

            // Created date
            if (fields.TryGetProperty("created", out var createdProp) && createdProp.ValueKind != JsonValueKind.Null &&
                DateTime.TryParse(createdProp.GetString(), out var createdDt))
                issue.Created = createdDt;

            // Resolution date
            if (fields.TryGetProperty("resolutiondate", out var resProp) && resProp.ValueKind != JsonValueKind.Null &&
                DateTime.TryParse(resProp.GetString(), out var resolvedDt))
                issue.ResolutionDate = resolvedDt;

            // Due date
            if (fields.TryGetProperty("duedate", out var dueProp) && dueProp.ValueKind != JsonValueKind.Null &&
                DateTime.TryParse(dueProp.GetString(), out var dueDt))
                issue.DueDate = dueDt;

            // Labels
            if (fields.TryGetProperty("labels", out var labelsEl) && labelsEl.ValueKind == JsonValueKind.Array)
                issue.Labels = labelsEl.EnumerateArray()
                    .Select(l => l.GetString() ?? "")
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToList();

            // Sprint metadata from customfield_10020 (array of sprints the issue belongs to)
            if (fields.TryGetProperty("customfield_10020", out var sprints) &&
                sprints.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sprints.EnumerateArray())
                {
                    if (!s.TryGetProperty("state", out var stateEl)) continue;
                    var sprintState = stateEl.GetString() ?? "";
                    var sprintNameVal = s.TryGetProperty("name", out var sn) ? sn.GetString() ?? "" : "";

                    // Per-issue: prefer active sprint; fall back to first sprint in the list
                    if (string.IsNullOrEmpty(issue.SprintName))
                        issue.SprintName = sprintNameVal;
                    if (sprintState == "active")
                    {
                        issue.SprintName = sprintNameVal;
                        // Report-level: populate start/end from the first active sprint found
                        if (report.SprintName == "")
                        {
                            report.SprintName = sprintNameVal;
                            if (s.TryGetProperty("startDate", out var sd) && DateTime.TryParse(sd.GetString(), out var sdParsed))
                                report.StartDate = sdParsed;
                            if (s.TryGetProperty("endDate", out var ed) && DateTime.TryParse(ed.GetString(), out var edParsed))
                                report.EndDate = edParsed;
                        }
                        break; // active sprint found — done for this issue
                    }
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
                        DateTimeOffset.TryParse(started.GetString(), out var dto))
                        entry.Started = dto.DateTime;   // preserves original local date from Jira's timezone offset
                    issue.Worklogs.Add(entry);
                }
                // Jira returns at most 20 worklogs in search responses — flag for re-fetch if truncated
                if (worklogTotal > issue.Worklogs.Count)
                    truncatedKeys.Add(issue.Key);
            }

            // Changelog — count how many times this issue transitioned to "QA REJECTED"
            if (item.TryGetProperty("changelog", out var changelog) &&
                changelog.TryGetProperty("histories", out var histories) &&
                histories.ValueKind == JsonValueKind.Array)
            {
                foreach (var history in histories.EnumerateArray())
                {
                    if (!history.TryGetProperty("items", out var histItems)) continue;
                    foreach (var change in histItems.EnumerateArray())
                    {
                        if (change.TryGetProperty("field", out var fieldEl) &&
                            fieldEl.GetString() == "status" &&
                            change.TryGetProperty("toString", out var toStatus) &&
                            toStatus.GetString()?.Equals("QA REJECTED", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            issue.QaRejectedCount++;
                        }
                    }
                }
            }

            report.Issues.Add(issue);
        }

        return (report, truncatedKeys);
    }

    // Fetches complete worklogs for issues where:
    //   (a) the search API returned a truncated list (max 20 inline), OR
    //   (b) the issue has TimeSpentSeconds > 0 but zero inline worklogs (Jira search index lag)
    private async Task FetchMissingWorklogsAsync(SprintReport report, List<string> truncatedKeys)
    {
        var noWorklogs = report.Issues
            .Where(i => i.TimeSpentSeconds > 0 && i.Worklogs.Count == 0)
            .Select(i => i.Key)
            .Where(k => !truncatedKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var allKeys = truncatedKeys.Concat(noWorklogs).ToList();

        foreach (var key in allKeys)
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
                    DateTimeOffset.TryParse(started.GetString(), out var dto))
                    entry.Started = dto.DateTime;   // preserves original local date from Jira's timezone offset
                issue.Worklogs.Add(entry);
            }
        }
    }

    // Fetches QA rejection counts for each issue via individual GET calls (changelog not available in POST search)
    private async Task FetchQaRejectionCountsAsync(List<SprintIssue> issues)
    {
        if (issues.Count == 0) return;

        const int batchSize = 10;
        for (int i = 0; i < issues.Count; i += batchSize)
        {
            var batch = issues.Skip(i).Take(batchSize).ToList();
            await Task.WhenAll(batch.Select(async issue =>
            {
                try
                {
                    var response = await _httpClient.GetAsync(
                        $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issue.Key)}?expand=changelog&fields=key");
                    if (!response.IsSuccessStatusCode) return;

                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("changelog", out var changelog) ||
                        !changelog.TryGetProperty("histories", out var histories) ||
                        histories.ValueKind != JsonValueKind.Array) return;

                    foreach (var history in histories.EnumerateArray())
                    {
                        if (!history.TryGetProperty("items", out var histItems)) continue;
                        foreach (var change in histItems.EnumerateArray())
                        {
                            if (change.TryGetProperty("field", out var fieldEl) &&
                                fieldEl.GetString() == "status" &&
                                change.TryGetProperty("toString", out var toStatus) &&
                                toStatus.GetString()?.Equals("QA REJECTED", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                issue.QaRejectedCount++;
                            }
                        }
                    }
                }
                catch { /* ignore individual failures */ }
            }));
        }
    }

    // Fetches ALL issues for an epic regardless of sprint — used by Quality Metrics drill-down
    public async Task<SprintReport> GetAllEpicIssuesAsync(string epicKey)
    {
        var jql = Uri.EscapeDataString(
            $"issueType != Epic AND (\"Epic Link\" = \"{epicKey}\" OR parent = \"{epicKey}\") ORDER BY created ASC");
        const string fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,customfield_10014,parent,created,resolutiondate";
        // POST /rest/api/3/search/jql does not support expand=changelog in the body; fetch changelogs separately below
        var json = await FetchAllPagesAsync(jql, fields);
        var (report, truncated) = ParseSprintReport(json, "");
        await FetchMissingWorklogsAsync(report, truncated);
        await FetchQaRejectionCountsAsync(report.Issues);

        var epicNames = await FetchEpicNamesAsync([epicKey]);
        foreach (var issue in report.Issues)
        {
            issue.EpicKey = epicKey;
            if (epicNames.TryGetValue(epicKey, out var name))
                issue.EpicName = name;
        }

        return report;
    }

    public async Task<SprintReport> GetIssuesByKeysAsync(List<string> issueKeys)
    {
        if (issueKeys.Count == 0) return new SprintReport();
        var keyList = string.Join(",", issueKeys.Select(k => $"\"{k}\""));
        var jql = Uri.EscapeDataString($"key in ({keyList}) ORDER BY key ASC");
        const string fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,labels,created,resolutiondate,customfield_10014,parent";
        var json = await FetchAllPagesAsync(jql, fields);
        var (report, truncated) = ParseSprintReport(json, "");
        await FetchMissingWorklogsAsync(report, truncated);
        return report;
    }

    public async Task<SprintReport> GetEpicBugsAsync(string epicKey)
    {
        var epicResponse = await _httpClient.GetAsync(
            $"{_baseUrl}/rest/api/3/issue/{epicKey}?fields=summary");
        var epicSummary = "";
        if (epicResponse.IsSuccessStatusCode)
        {
            var epicJson = await epicResponse.Content.ReadAsStringAsync();
            using var epicDoc = JsonDocument.Parse(epicJson);
            epicSummary = epicDoc.RootElement.GetProperty("fields").GetProperty("summary").GetString() ?? "";
        }

        var jql = Uri.EscapeDataString(
            $"issueType = Bug AND (\"Epic Link\" = \"{epicKey}\" OR parent = \"{epicKey}\") ORDER BY created ASC");
        const string fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,customfield_10014,parent,created,resolutiondate,duedate";
        var json = await FetchAllPagesAsync(jql, fields);
        var (report, truncated) = ParseSprintReport(json, "");
        await FetchMissingWorklogsAsync(report, truncated);

        foreach (var issue in report.Issues)
        {
            issue.EpicKey  = epicKey;
            issue.EpicName = epicSummary;
        }

        return report;
    }

    public async Task<SprintReport> GetPriorityBugsAsync()
    {
        var jql = Uri.EscapeDataString(
            "project = JM AND \"Customers Jimpisoft[Dropdown]\" is not EMPTY AND issuetype = Bug AND status not in (Done, Rejected) AND priority in (Highest) AND createdDate <= -7d AND \"JS Project[Radio Buttons]\" = \"Rentway Pro\"");
        const string fields = "summary,status,assignee,issuetype,priority,timetracking,created,duedate";
        var json = await FetchAllPagesAsync(jql, fields);
        var (report, _) = ParseSprintReport(json, "");
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
