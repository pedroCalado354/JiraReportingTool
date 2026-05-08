using JiraReportingTool.Data;
using JiraReportingTool.Models;

/// <summary>
/// Database-first implementation of IJiraService.
/// When <c>Database:UseCache</c> is <c>true</c>, data is read from the local SQL Server database.
/// A cached result is used only if it is fresher than <c>Database:CacheTtlMinutes</c> (default 60).
/// On a cache miss or stale hit, the Jira API is called and the result is persisted.
/// When <c>UseCache</c> is <c>false</c> every call flows straight through to the Jira API.
/// </summary>
public class JiraCacheService(JiraService api, JiraDbRepository repo, IConfiguration config) : IJiraService
{
    private readonly bool _useCache = config.GetValue<bool>("Database:UseCache");
    private readonly int _ttlMinutes = config.GetValue<int>("Database:CacheTtlMinutes", 60);

    private bool IsFresh(DateTime? syncedAt)
        => syncedAt.HasValue && (DateTime.UtcNow - syncedAt.Value).TotalMinutes < _ttlMinutes;

    // ── Pass-through (no caching benefit) ───────────────────────────────────

    public Task<string> GetIssue(string issueKey)
        => api.GetIssue(issueKey);

    public Task<List<EpicSummary>> GetEpicsInSprintAsync(int sprintId)
        => api.GetEpicsInSprintAsync(sprintId);

    public Task<SprintReport> GetDeliveryDataByFilterAsync(string filterJql)
        => api.GetDeliveryDataByFilterAsync(filterJql);

    public Task<SprintReport> GetIssuesByKeysAsync(List<string> issueKeys)
        => api.GetIssuesByKeysAsync(issueKeys);

    public Task<SprintReport> GetEpicBugsAsync(string epicKey)
        => api.GetEpicBugsAsync(epicKey);

    public Task<SprintReport> GetPriorityBugsAsync()
        => api.GetPriorityBugsAsync();

    public Task<Dictionary<string, string>> FetchEpicNamesAsync(List<string> epicKeys)
        => api.FetchEpicNamesAsync(epicKeys);

    public Task<SprintReport> GetEpicSprintForecastAsync(string epicKey, int sprintId)
        => api.GetEpicSprintForecastAsync(epicKey, sprintId);

    // ── Cached methods ───────────────────────────────────────────────────────

    public async Task<JiraEpicReport> GetEpicReportAsync(string epicKey)
    {
        if (_useCache)
        {
            var cached = await repo.GetEpicReportServiceAsync(epicKey);
            if (cached is not null && cached.Issues.Any() && IsFresh(cached.SyncedAt)) return cached;
        }

        var report = await api.GetEpicReportAsync(epicKey);
        if (_useCache) await repo.UpsertEpicReportAsync(report);
        return report;
    }

    public async Task<SprintReport> GetSprintReportAsync(string projectKey, int sprintId)
    {
        var key = $"sprint:{projectKey.ToUpper()}:{sprintId}";

        if (_useCache)
        {
            var cached = await repo.GetSprintReportAsync(key);
            if (cached is not null && IsFresh(cached.SyncedAt)) return cached;
        }

        var report = await api.GetSprintReportAsync(projectKey, sprintId);
        report.JiraSprintId = sprintId;
        report.ReportIdentifier = key;
        if (_useCache) await repo.UpsertSprintReportAsync(report);
        return report;
    }

    public async Task<SprintReport> GetDeliveryDataAsync(int sprintId)
    {
        var key = $"delivery:{sprintId}";

        if (_useCache)
        {
            var cached = await repo.GetSprintReportAsync(key);
            if (cached is not null && IsFresh(cached.SyncedAt)) return cached;
        }

        var report = await api.GetDeliveryDataAsync(sprintId);
        report.JiraSprintId = sprintId;
        report.ReportIdentifier = key;
        if (_useCache) await repo.UpsertSprintReportAsync(report);
        return report;
    }

    public async Task<SprintReport> GetAllEpicIssuesAsync(string epicKey)
    {
        var key = $"epicall:{epicKey}";

        if (_useCache)
        {
            var cached = await repo.GetSprintReportAsync(key);
            if (cached is not null && IsFresh(cached.SyncedAt)) return cached;
        }

        var report = await api.GetAllEpicIssuesAsync(epicKey);
        report.ReportIdentifier = key;
        if (_useCache) await repo.UpsertSprintReportAsync(report);
        return report;
    }

    public async Task<List<JiraFilter>> GetMyFiltersAsync()
    {
        if (_useCache)
        {
            var cached = await repo.GetFiltersAsync();
            if (cached.Count > 0) return cached;
        }

        var filters = await api.GetMyFiltersAsync();
        if (_useCache) await repo.UpsertFiltersAsync(filters);
        return filters;
    }
}
