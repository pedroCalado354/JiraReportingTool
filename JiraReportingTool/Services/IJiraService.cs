using JiraReportingTool.Models;

/// <summary>
/// Abstraction over Jira data access — implementations can serve from the API
/// directly or from a local database cache.
/// </summary>
public interface IJiraService
{
    Task<string> GetIssue(string issueKey);

    /// <summary>
    /// Resolves the dynamic field ID for the "Customers Jimpisoft" custom field
    /// from /rest/api/3/field. Cached after the first call. Returns null if not found.
    /// </summary>
    Task<string?> GetCustomerFieldIdAsync();
    Task<JiraEpicReport> GetEpicReportAsync(string epicKey);
    Task<SprintReport> GetSprintReportAsync(string projectKey, int sprintId);
    Task<SprintReport> GetDeliveryDataAsync(int sprintId, bool bypassCache = false);
    Task<List<EpicSummary>> GetEpicsInSprintAsync(int sprintId);
    Task<SprintReport> GetEpicSprintForecastAsync(string epicKey, int sprintId);
    Task<SprintReport> GetDeliveryDataByFilterAsync(string filterJql);
    Task<List<JiraFilter>> GetMyFiltersAsync();
    Task<SprintReport> GetAllEpicIssuesAsync(string epicKey);

    /// <summary>
    /// Fetches current Status and time-tracking data for a specific set of issue keys in one JQL query.
    /// Used to refresh Report/Daily view data without re-loading full epics.
    /// </summary>
    Task<SprintReport> GetIssuesByKeysAsync(List<string> issueKeys);

    /// <summary>
    /// Fetches issues from an epic, including complete worklogs. When <paramref name="bugsOnly"/>
    /// is true (default) only Bug-type issues are returned; when false, every issue type under the
    /// epic is returned — matching a Jira "parent = EPIC" filter so worklog totals reconcile with Jira.
    /// </summary>
    Task<SprintReport> GetEpicBugsAsync(string epicKey, bool bugsOnly = true);

    /// <summary>
    /// Fetches epic summaries (key → name) for the given epic keys.
    /// Used by the Excel export to resolve epic names when LoadedEpics is empty.
    /// </summary>
    Task<Dictionary<string, string>> FetchEpicNamesAsync(List<string> epicKeys);

    /// <summary>
    /// Fetches Committed Date and Committed Customers custom fields from the epic issues themselves.
    /// Field IDs are discovered dynamically from /rest/api/3/field on first call.
    /// </summary>
    Task<Dictionary<string, EpicMeta>> FetchEpicMetaAsync(List<string> epicKeys);

    /// <summary>
    /// Fetches highest-priority open bugs for the Rentway Pro project that have been open for more than 7 days.
    /// </summary>
    Task<SprintReport> GetPriorityBugsAsync();

    /// <summary>
    /// Fetches bugs from a raw JQL string. Includes customer, PrioListDate (customfield_12437), and standard
    /// time-tracking fields. Used by the Bug Master Dashboard priority buckets.
    /// </summary>
    Task<SprintReport> GetBugsByJqlAsync(string rawJql);

    /// <summary>
    /// Fetches bugs created inside the given window (project JM) with issue links populated
    /// (SprintIssue.LinkedIssueKeys) so callers can filter by linked issues — e.g. bugs
    /// relating to JSSUPPORT tickets. Not cached.
    /// </summary>
    Task<SprintReport> GetBugsWithLinksAsync(DateOnly createdFrom, DateOnly createdTo);

    /// <summary>
    /// Fetches bugs from a raw JQL string, additionally resolving the "JS Project" (Product) radio-button field.
    /// Used by the SLA dashboard so tickets can be grouped by both customer and product.
    /// </summary>
    Task<SprintReport> GetSlaBugsByJqlAsync(string rawJql);

    /// <summary>
    /// Fetches development status (branches, commits, PRs with URLs, builds) for a Jira issue.
    /// </summary>
    Task<IssueDevStatus> GetDevStatusAsync(string jiraId);
}
