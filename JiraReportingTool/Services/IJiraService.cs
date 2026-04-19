using JiraReportingTool.Models;

/// <summary>
/// Abstraction over Jira data access — implementations can serve from the API
/// directly or from a local database cache.
/// </summary>
public interface IJiraService
{
    Task<string> GetIssue(string issueKey);
    Task<JiraEpicReport> GetEpicReportAsync(string epicKey);
    Task<SprintReport> GetSprintReportAsync(string projectKey, int sprintId);
    Task<SprintReport> GetDeliveryDataAsync(int sprintId);
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
}
