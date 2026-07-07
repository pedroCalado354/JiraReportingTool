using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JiraReportingTool.Models;

public class JiraService : IJiraService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly string _baseUrl;

    private string? _customerFieldId;
    private bool    _customerFieldIdLookedUp;

    private string? _jsProjectFieldId;
    private bool    _jsProjectFieldIdLookedUp;

    private string? _committedDateFieldId;
    private string? _committedCustomersFieldId;
    private bool    _epicMetaFieldsLookedUp;

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

    public async Task<string?> GetCustomerFieldIdAsync()
    {
        if (_customerFieldIdLookedUp) return _customerFieldId;
        _customerFieldIdLookedUp = true;
        try
        {
            var resp = await _httpClient.GetAsync($"{_baseUrl}/rest/api/3/field");
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            foreach (var field in doc.RootElement.EnumerateArray())
            {
                if (!field.TryGetProperty("name", out var nameEl)) continue;
                if ((nameEl.GetString() ?? "").Contains("Customers Jimpisoft", StringComparison.OrdinalIgnoreCase) &&
                    field.TryGetProperty("id", out var idEl))
                {
                    _customerFieldId = idEl.GetString();
                    return _customerFieldId;
                }
            }
        }
        catch { }
        return null;
    }

    public async Task<string?> GetJsProjectFieldIdAsync()
    {
        if (_jsProjectFieldIdLookedUp) return _jsProjectFieldId;
        _jsProjectFieldIdLookedUp = true;
        try
        {
            var resp = await _httpClient.GetAsync($"{_baseUrl}/rest/api/3/field");
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            foreach (var field in doc.RootElement.EnumerateArray())
            {
                if (!field.TryGetProperty("name", out var nameEl)) continue;
                if ((nameEl.GetString() ?? "").Equals("JS Project", StringComparison.OrdinalIgnoreCase) &&
                    field.TryGetProperty("id", out var idEl))
                {
                    _jsProjectFieldId = idEl.GetString();
                    return _jsProjectFieldId;
                }
            }
        }
        catch { }
        return null;
    }

    private async Task EnsureEpicMetaFieldIdsAsync()
    {
        if (_epicMetaFieldsLookedUp) return;
        _epicMetaFieldsLookedUp = true;
        try
        {
            var resp = await _httpClient.GetAsync($"{_baseUrl}/rest/api/3/field");
            if (!resp.IsSuccessStatusCode) return;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            foreach (var field in doc.RootElement.EnumerateArray())
            {
                if (!field.TryGetProperty("name", out var nameEl) ||
                    !field.TryGetProperty("id", out var idEl)) continue;
                var name = nameEl.GetString() ?? "";
                if (name.Contains("Committed Date", StringComparison.OrdinalIgnoreCase))
                    _committedDateFieldId = idEl.GetString();
                else if (name.Contains("Committed Customer", StringComparison.OrdinalIgnoreCase))
                    _committedCustomersFieldId = idEl.GetString();
            }
        }
        catch { }
    }

    public async Task<Dictionary<string, EpicMeta>> FetchEpicMetaAsync(List<string> epicKeys)
    {
        if (epicKeys.Count == 0) return [];
        await EnsureEpicMetaFieldIdsAsync();

        var fieldIds = new List<string> { "summary" };
        if (_committedDateFieldId != null) fieldIds.Add(_committedDateFieldId);
        if (_committedCustomersFieldId != null) fieldIds.Add(_committedCustomersFieldId);
        if (fieldIds.Count == 1) return []; // no custom fields found

        var keysJql = string.Join(",", epicKeys.Select(k => $"\"{k}\""));
        var jql = Uri.EscapeDataString($"key in ({keysJql})");
        var fieldsParam = string.Join(",", fieldIds);
        var response = await _httpClient.GetAsync(
            $"{_baseUrl}/rest/api/3/search/jql?jql={jql}&fields={fieldsParam}&maxResults=500");
        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, EpicMeta>(StringComparer.OrdinalIgnoreCase);
        if (!doc.RootElement.TryGetProperty("issues", out var issues)) return result;

        foreach (var item in issues.EnumerateArray())
        {
            var key = item.GetProperty("key").GetString() ?? "";
            var fields = item.GetProperty("fields");
            DateTime? committedDate = null;
            string committedCustomers = "";

            if (_committedDateFieldId != null &&
                fields.TryGetProperty(_committedDateFieldId, out var dateProp) &&
                dateProp.ValueKind != JsonValueKind.Null &&
                DateTime.TryParse(dateProp.GetString(), out var dt))
                committedDate = dt;

            if (_committedCustomersFieldId != null &&
                fields.TryGetProperty(_committedCustomersFieldId, out var custProp) &&
                custProp.ValueKind != JsonValueKind.Null)
            {
                committedCustomers = custProp.ValueKind switch
                {
                    JsonValueKind.String => custProp.GetString() ?? "",
                    JsonValueKind.Object when custProp.TryGetProperty("value", out var val)
                        => val.GetString() ?? "",
                    JsonValueKind.Array => string.Join(",", custProp.EnumerateArray()
                        .Select(v => v.ValueKind == JsonValueKind.String
                            ? v.GetString() ?? ""
                            : v.TryGetProperty("value", out var vv) ? vv.GetString() ?? "" : "")
                        .Where(v => !string.IsNullOrEmpty(v))),
                    _ => ""
                };
            }

            result[key] = new EpicMeta(committedDate, committedCustomers);
        }
        return result;
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
        // POST /rest/api/3/search/jql does not support expand=changelog in the body; fetch QA rejection counts separately
        await FetchQaRejectionCountsAsync(sprintReport.Issues);

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
    // bypassCache is honoured by the caching decorator (JiraCacheService); the direct API
    // path always fetches live, so the parameter is accepted only to satisfy the interface.
    public Task<SprintReport> GetDeliveryDataAsync(int sprintId, bool bypassCache = false)
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
        await ReconcileLiveFieldsAsync(report);

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

    // The JQL search endpoint (/rest/api/3/search/jql) serves results from Jira's
    // eventually-consistent search index, so a field that just changed — e.g. an issue
    // moved to Done minutes (or longer) ago — can come back stale, and re-querying the
    // same endpoint will keep returning the stale value. Re-read the authoritative
    // status / resolution / timetracking straight from the issue store via the per-issue
    // GET endpoint (live, not the index) and patch the report so the UI reflects Jira now.
    private async Task ReconcileLiveFieldsAsync(SprintReport report)
    {
        var issues = report.Issues.Where(i => !string.IsNullOrEmpty(i.Key)).ToList();
        if (issues.Count == 0) return;

        const int batchSize = 10;
        for (int i = 0; i < issues.Count; i += batchSize)
        {
            var batch = issues.Skip(i).Take(batchSize).ToList();
            await Task.WhenAll(batch.Select(ReconcileOneIssueAsync));
        }
    }

    private async Task ReconcileOneIssueAsync(SprintIssue issue)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issue.Key)}?fields=status,resolutiondate,timetracking");
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("fields", out var fields)) return;

            if (fields.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object)
            {
                if (status.TryGetProperty("name", out var sn) && sn.ValueKind == JsonValueKind.String)
                    issue.Status = sn.GetString() ?? issue.Status;
                if (status.TryGetProperty("statusCategory", out var sc) &&
                    sc.TryGetProperty("key", out var sck) && sck.ValueKind == JsonValueKind.String)
                    issue.StatusCategoryKey = sck.GetString() ?? issue.StatusCategoryKey;
            }

            issue.ResolutionDate =
                fields.TryGetProperty("resolutiondate", out var resEl) && resEl.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(resEl.GetString(), out var rd) ? rd : null;

            if (fields.TryGetProperty("timetracking", out var tt) && tt.ValueKind == JsonValueKind.Object)
            {
                issue.OriginalEstimateSeconds  = tt.TryGetProperty("originalEstimateSeconds",  out var oes) ? oes.GetInt32() : 0;
                issue.RemainingEstimateSeconds = tt.TryGetProperty("remainingEstimateSeconds", out var res2) ? res2.GetInt32() : 0;
                if (tt.TryGetProperty("timeSpentSeconds", out var tss)) issue.TimeSpentSeconds = tss.GetInt32();
                issue.OriginalEstimate  = tt.TryGetProperty("originalEstimate",  out var oe) && oe.ValueKind == JsonValueKind.String ? oe.GetString() ?? "-" : "-";
                issue.RemainingEstimate = tt.TryGetProperty("remainingEstimate", out var re) && re.ValueKind == JsonValueKind.String ? re.GetString() ?? "-" : "-";
                if (tt.TryGetProperty("timeSpent", out var ts) && ts.ValueKind == JsonValueKind.String) issue.TimeSpent = ts.GetString() ?? "-";
            }
        }
        catch { /* reconciliation is best-effort; fall back to the indexed values */ }
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

    private (SprintReport Report, List<string> TruncatedKeys) ParseSprintReport(string json, string projectKey, string? customerFieldId = null, string? productFieldId = null)
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
                Key    = item.GetProperty("key").GetString() ?? "",
                JiraId = item.TryGetProperty("id", out var jiraIdEl) ? jiraIdEl.GetString() ?? "" : "",
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

            // JM Prio List[Date] (customfield_12437)
            if (fields.TryGetProperty("customfield_12437", out var prioProp) && prioProp.ValueKind != JsonValueKind.Null &&
                DateTime.TryParse(prioProp.GetString(), out var prioDate))
                issue.PrioListDate = prioDate;

            // Labels
            if (fields.TryGetProperty("labels", out var labelsEl) && labelsEl.ValueKind == JsonValueKind.Array)
                issue.Labels = labelsEl.EnumerateArray()
                    .Select(l => l.GetString() ?? "")
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToList();

            // Issue links (inward + outward, any link type) — only present when the
            // "issuelinks" field is requested.
            if (fields.TryGetProperty("issuelinks", out var linksEl) && linksEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in linksEl.EnumerateArray())
                {
                    if (link.TryGetProperty("inwardIssue", out var inw) &&
                        inw.TryGetProperty("key", out var inwKey) && !string.IsNullOrEmpty(inwKey.GetString()))
                        issue.LinkedIssueKeys.Add(inwKey.GetString()!);
                    if (link.TryGetProperty("outwardIssue", out var outw) &&
                        outw.TryGetProperty("key", out var outwKey) && !string.IsNullOrEmpty(outwKey.GetString()))
                        issue.LinkedIssueKeys.Add(outwKey.GetString()!);
                }
            }

            // Customers Jimpisoft dropdown (dynamic field ID)
            if (customerFieldId != null &&
                fields.TryGetProperty(customerFieldId, out var custEl) &&
                custEl.ValueKind != JsonValueKind.Null &&
                custEl.TryGetProperty("value", out var custVal))
                issue.Customer = custVal.GetString() ?? "";

            // JS Project radio-button field (Rentway Legacy / Rentway Pro / Integrations)
            if (productFieldId != null &&
                fields.TryGetProperty(productFieldId, out var prodEl) &&
                prodEl.ValueKind != JsonValueKind.Null &&
                prodEl.TryGetProperty("value", out var prodVal))
                issue.Product = prodVal.GetString() ?? "";

            // Sprint metadata from customfield_10020 (array of sprints the issue belongs to,
            // in chronological order — first entry = first sprint, last entry = most recent).
            if (fields.TryGetProperty("customfield_10020", out var sprints) &&
                sprints.ValueKind == JsonValueKind.Array)
            {
                string? activeSprintName = null;
                foreach (var s in sprints.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object) continue;
                    var sprintState = s.TryGetProperty("state", out var stateEl) ? stateEl.GetString() ?? "" : "";
                    var sprintNameVal = s.TryGetProperty("name", out var sn) ? sn.GetString() ?? "" : "";
                    DateTime? sprintStart = s.TryGetProperty("startDate", out var sd) && DateTime.TryParse(sd.GetString(), out var sdP) ? sdP : null;
                    DateTime? sprintEnd   = s.TryGetProperty("endDate", out var ed) && DateTime.TryParse(ed.GetString(), out var edP) ? edP : null;
                    int sprintId          = s.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var idV) ? idV : 0;

                    // Full ordered history — powers carry-over analysis on Support Trends.
                    issue.Sprints.Add(new IssueSprint
                    {
                        Id = sprintId, Name = sprintNameVal, State = sprintState,
                        StartDate = sprintStart, EndDate = sprintEnd
                    });

                    // Per-issue single name: prefer the active sprint, else first in the list.
                    if (string.IsNullOrEmpty(issue.SprintName))
                        issue.SprintName = sprintNameVal;
                    if (sprintState == "active" && activeSprintName == null)
                    {
                        activeSprintName = sprintNameVal;
                        // Report-level: populate start/end from the first active sprint found
                        if (report.SprintName == "")
                        {
                            report.SprintName = sprintNameVal;
                            if (sprintStart.HasValue) report.StartDate = sprintStart.Value;
                            if (sprintEnd.HasValue)   report.EndDate   = sprintEnd.Value;
                        }
                    }
                }
                if (activeSprintName != null) issue.SprintName = activeSprintName;
            }

            if (fields.TryGetProperty("worklog", out var worklog) &&
                worklog.TryGetProperty("worklogs", out var wls))
            {
                var worklogTotal = worklog.TryGetProperty("total", out var wt) ? wt.GetInt32() : 0;
                foreach (var wl in wls.EnumerateArray())
                {
                    var entry = new WorklogEntry
                    {
                        Author          = ParseWorklogAuthorName(wl),
                        AuthorAccountId = ParseWorklogAuthorField(wl, "accountId"),
                        AuthorEmail     = ParseWorklogAuthorField(wl, "emailAddress"),
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
                    Author          = ParseWorklogAuthorName(wl),
                    AuthorAccountId = ParseWorklogAuthorField(wl, "accountId"),
                    AuthorEmail     = ParseWorklogAuthorField(wl, "emailAddress"),
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

    // Worklog author helpers — the author object may be null, and emailAddress is often
    // omitted by Jira privacy settings, so every field is read defensively.
    private static string ParseWorklogAuthorName(JsonElement wl)
        => ParseWorklogAuthorField(wl, "displayName");

    private static string ParseWorklogAuthorField(JsonElement wl, string field)
        => wl.TryGetProperty("author", out var author)
           && author.ValueKind == JsonValueKind.Object
           && author.TryGetProperty(field, out var val)
           && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? ""
            : "";

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

    public Task<SprintReport> GetEpicBugsAsync(string epicKey, bool bugsOnly = true)
        => GetEpicBugsCoreAsync(epicKey, bugsOnly, updatedSinceDays: null);

    /// <summary>
    /// Delta variant of GetEpicBugsAsync: only issues under the epic whose `updated` date is
    /// within the last <paramref name="sinceDays"/> days. Used by the cache layer to refresh
    /// just the recently-touched slice of a stored epic report (a new worklog or status change
    /// bumps `updated`, so this catches everything that moved).
    /// </summary>
    public Task<SprintReport> GetEpicBugsUpdatedSinceAsync(string epicKey, bool bugsOnly, int sinceDays)
        => GetEpicBugsCoreAsync(epicKey, bugsOnly, updatedSinceDays: sinceDays);

    // Live (uncached) implementations of the Support Trends cached-fetch interface methods —
    // caching policy lives in JiraCacheService; here the extra parameters are ignored.
    public Task<SprintReport> GetSupportEpicBugsAsync(string epicKey, DateOnly? sprintEnd, bool forceRefresh = false)
        => GetEpicBugsCoreAsync(epicKey, bugsOnly: false, updatedSinceDays: null);

    public Task<SprintReport> GetJsSupportLinkedBugsAsync(DateOnly from, DateOnly to, bool forceRefresh = false)
        => GetBugsWithLinksAsync(from, to);

    private async Task<SprintReport> GetEpicBugsCoreAsync(string epicKey, bool bugsOnly, int? updatedSinceDays)
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

        // bugsOnly=true → bugs only (Support Bugs/Analytics pages).
        // bugsOnly=false → every issue type under the epic, matching a Jira "parent = EPIC"
        // filter — used for worklog reconciliation so logged-hour totals line up with Jira.
        var typeClause = bugsOnly ? "issueType = Bug" : "issueType != Epic";
        var updatedClause = updatedSinceDays is int d ? $" AND updated >= -{d}d" : "";
        var jql = Uri.EscapeDataString(
            $"{typeClause} AND (\"Epic Link\" = \"{epicKey}\" OR parent = \"{epicKey}\"){updatedClause} ORDER BY created ASC");
        var customerFieldId = await GetCustomerFieldIdAsync();
        var jsProjectFieldId = await GetJsProjectFieldIdAsync();
        var fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,customfield_10014,customfield_10020,parent,created,resolutiondate,duedate,labels"
            + (customerFieldId != null ? $",{customerFieldId}" : "")
            + (jsProjectFieldId != null ? $",{jsProjectFieldId}" : "");
        var json = await FetchAllPagesAsync(jql, fields);
        var (report, truncated) = ParseSprintReport(json, "", customerFieldId, jsProjectFieldId);
        await FetchMissingWorklogsAsync(report, truncated);
        await FetchLifecycleTransitionsAsync(report.Issues);

        foreach (var issue in report.Issues)
        {
            issue.EpicKey  = epicKey;
            issue.EpicName = epicSummary;
        }

        return report;
    }

    // For each resolved issue, walks the changelog ONCE (per-issue GET; POST search can't
    // expand changelog) and extracts the lifecycle markers used by the Support dashboards:
    //   • DevReadyDate    — FIRST transition into "Dev Ready"
    //   • QaReadyDate     — FIRST transition into "QA Ready"
    //   • QaRejectedCount — number of transitions into "QA REJECTED"
    // Only resolved issues are queried — the Dev-Ready→QA-Ready→Done timings only complete
    // for done bugs, and this keeps the request count identical to fetching Dev Ready alone.
    private async Task FetchLifecycleTransitionsAsync(List<SprintIssue> issues)
    {
        var done = issues.Where(i => i.StatusCategoryKey == "done").ToList();
        if (done.Count == 0) return;

        const int batchSize = 10;
        for (int i = 0; i < done.Count; i += batchSize)
        {
            var batch = done.Skip(i).Take(batchSize).ToList();
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

                    DateTime? devReady = null;
                    DateTime? qaReady  = null;
                    int qaRejected = 0;
                    foreach (var history in histories.EnumerateArray())
                    {
                        if (!history.TryGetProperty("items", out var histItems)) continue;
                        DateTime? created = history.TryGetProperty("created", out var createdEl) &&
                                            DateTimeOffset.TryParse(createdEl.GetString(), out var dto)
                            ? dto.DateTime : null;
                        foreach (var change in histItems.EnumerateArray())
                        {
                            if (!change.TryGetProperty("field", out var fieldEl) ||
                                fieldEl.GetString() != "status" ||
                                !change.TryGetProperty("toString", out var toEl)) continue;
                            var to = toEl.GetString();

                            if (to?.Equals("Dev Ready", StringComparison.OrdinalIgnoreCase) == true &&
                                created.HasValue && (devReady is null || created < devReady))
                                devReady = created;
                            else if (to?.Equals("QA Ready", StringComparison.OrdinalIgnoreCase) == true &&
                                created.HasValue && (qaReady is null || created < qaReady))
                                qaReady = created;
                            else if (to?.Equals("QA REJECTED", StringComparison.OrdinalIgnoreCase) == true)
                                qaRejected++;
                        }
                    }
                    issue.DevReadyDate    = devReady;
                    issue.QaReadyDate     = qaReady;
                    issue.QaRejectedCount = qaRejected;
                }
                catch { /* ignore individual failures */ }
            }));
        }
    }

    public async Task<SprintReport> GetPriorityBugsAsync()
    {
        var jql = Uri.EscapeDataString(
            "project = JM AND \"Customers Jimpisoft[Dropdown]\" is not EMPTY AND issuetype = Bug AND status not in (Done, Rejected) AND priority in (Highest) AND createdDate <= -7d AND \"JS Project[Radio Buttons]\" = \"Rentway Pro\"");
        const string fields = "summary,status,assignee,issuetype,priority,timetracking,created,duedate,labels";
        var json = await FetchAllPagesAsync(jql, fields);
        var (report, _) = ParseSprintReport(json, "");
        return report;
    }

    public async Task<SprintReport> GetBugsByJqlAsync(string rawJql)
    {
        var customerFieldId = await GetCustomerFieldIdAsync();
        var fields = "summary,status,assignee,issuetype,priority,timetracking,created,duedate,labels,customfield_12437"
            + (customerFieldId != null ? $",{customerFieldId}" : "");
        var jql = Uri.EscapeDataString(rawJql);
        var json = await FetchAllPagesAsync(jql, fields);
        var (report, _) = ParseSprintReport(json, "", customerFieldId);
        return report;
    }

    /// <summary>
    /// Bugs created or updated since 00:00 of the start date (project JM), with
    /// <see cref="SprintIssue.LinkedIssueKeys"/> populated so callers can filter by linked
    /// issues (e.g. bugs related to JSSUPPORT tickets). No upper bound on `updated`: adding a
    /// worklog bumps it, so this catches every bug with worklog activity since the start date —
    /// callers apply their own worklog-date filtering client-side. Not cached.
    /// </summary>
    public async Task<SprintReport> GetBugsWithLinksAsync(DateOnly createdFrom, DateOnly createdTo)
    {
        var customerFieldId = await GetCustomerFieldIdAsync();
        var jsProjectFieldId = await GetJsProjectFieldIdAsync();
        var fields = "summary,status,assignee,issuetype,priority,timetracking,worklog,created,resolutiondate,duedate,labels,issuelinks,customfield_10020"
            + (customerFieldId != null ? $",{customerFieldId}" : "")
            + (jsProjectFieldId != null ? $",{jsProjectFieldId}" : "");
        var jql = Uri.EscapeDataString(
            $"project = JM AND issuetype = Bug AND (" +
            $"(createdDate >= \"{createdFrom:yyyy-MM-dd}\" AND createdDate <= \"{createdTo:yyyy-MM-dd}\") OR " +
            $"updated >= \"{createdFrom:yyyy-MM-dd}\"" +
            $") ORDER BY created DESC");
        var json = await FetchAllPagesAsync(jql, fields);
        var (report, truncated) = ParseSprintReport(json, "", customerFieldId, jsProjectFieldId);
        await FetchMissingWorklogsAsync(report, truncated);
        return report;
    }

    public async Task<SprintReport> GetSlaBugsByJqlAsync(string rawJql)
    {
        var customerFieldId  = await GetCustomerFieldIdAsync();
        var jsProjectFieldId = await GetJsProjectFieldIdAsync();
        var fields = "summary,status,assignee,issuetype,priority,timetracking,created,duedate,labels,customfield_12437"
            + (customerFieldId  != null ? $",{customerFieldId}"  : "")
            + (jsProjectFieldId != null ? $",{jsProjectFieldId}" : "");
        var jql = Uri.EscapeDataString(rawJql);
        var json = await FetchAllPagesAsync(jql, fields);
        var (report, _) = ParseSprintReport(json, "", customerFieldId, jsProjectFieldId);
        return report;
    }

    public async Task<IssueDevStatus> GetDevStatusAsync(string jiraId)
    {
        var result = new IssueDevStatus();
        var discoveredAppTypes = new List<string>();

        // Summary: branch/commit/build counts.
        // Jira Cloud wraps counts under an "overall" sub-object: summary.branch.overall.count
        var summaryResp = await _httpClient.GetAsync(
            $"{_baseUrl}/rest/dev-status/1.0/issue/summary?issueId={Uri.EscapeDataString(jiraId)}");
        if (summaryResp.IsSuccessStatusCode)
        {
            var summaryJson = await summaryResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(summaryJson);
            if (doc.RootElement.TryGetProperty("summary", out var summary))
            {
                if (summary.TryGetProperty("branch", out var br))
                {
                    var node = br.TryGetProperty("overall", out var o) ? o : br;
                    if (node.TryGetProperty("count", out var c)) result.BranchCount = c.GetInt32();
                    // Discover connected app types (e.g. "GitHub", "Bitbucket") for the detail call
                    if (br.TryGetProperty("byInstanceType", out var bit))
                        foreach (var p in bit.EnumerateObject())
                            if (!discoveredAppTypes.Contains(p.Name)) discoveredAppTypes.Add(p.Name);
                }
                if (summary.TryGetProperty("commit", out var cm))
                {
                    var node = cm.TryGetProperty("overall", out var o) ? o : cm;
                    if (node.TryGetProperty("count", out var c)) result.CommitCount = c.GetInt32();
                }
                if (summary.TryGetProperty("build", out var build))
                {
                    var node = build.TryGetProperty("overall", out var o) ? o : build;
                    if (node.TryGetProperty("count",        out var bc)) result.BuildCount        = bc.GetInt32();
                    if (node.TryGetProperty("successCount", out var sc)) result.BuildSuccessCount = sc.GetInt32();
                    if (node.TryGetProperty("failCount",    out var fc)) result.BuildFailCount    = fc.GetInt32();
                }
            }
        }

        // PR detail: use discovered app types from summary; fall back to configured/default value
        var appTypesToTry = discoveredAppTypes.Count > 0
            ? discoveredAppTypes
            : new List<string> { _config["Jira:DevStatusAppType"] ?? "GitHub" };

        foreach (var appType in appTypesToTry)
        {
            var prResp = await _httpClient.GetAsync(
                $"{_baseUrl}/rest/dev-status/1.0/issue/detail?issueId={Uri.EscapeDataString(jiraId)}&applicationType={Uri.EscapeDataString(appType)}&dataType=pullrequest");
            if (!prResp.IsSuccessStatusCode) continue;

            var prJson = await prResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(prJson);
            if (!doc.RootElement.TryGetProperty("detail", out var detail)) continue;

            foreach (var provider in detail.EnumerateArray())
            {
                if (!provider.TryGetProperty("pullRequests", out var prs)) continue;
                foreach (var pr in prs.EnumerateArray())
                {
                    var devPr = new DevPullRequest
                    {
                        Title  = pr.TryGetProperty("title",  out var t) ? t.GetString() ?? "" : "",
                        Url    = pr.TryGetProperty("url",    out var u) ? u.GetString() ?? "" : "",
                        Status = pr.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
                    };
                    if (pr.TryGetProperty("source", out var src) &&
                        src.TryGetProperty("branch", out var srcBranch) &&
                        srcBranch.TryGetProperty("name", out var branchName))
                        devPr.SourceBranch = branchName.GetString() ?? "";
                    if (pr.TryGetProperty("lastUpdate", out var lu) &&
                        DateTime.TryParse(lu.GetString(), out var luDt))
                        devPr.LastUpdated = luDt;
                    result.PullRequests.Add(devPr);
                }
            }
        }

        return result;
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
