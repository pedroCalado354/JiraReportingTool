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
    private readonly int _hotWindowDays = config.GetValue<int>("Database:TrendsHotWindowDays", 5);

    private bool IsFresh(DateTime? syncedAt)
        => syncedAt.HasValue && (DateTime.UtcNow - syncedAt.Value).TotalMinutes < _ttlMinutes;

    // ── Pass-through (no caching benefit) ───────────────────────────────────

    public Task<string> GetIssue(string issueKey)
        => api.GetIssue(issueKey);

    public Task<string?> GetCustomerFieldIdAsync()
        => api.GetCustomerFieldIdAsync();

    public Task<List<EpicSummary>> GetEpicsInSprintAsync(int sprintId)
        => api.GetEpicsInSprintAsync(sprintId);

    public Task<SprintReport> GetDeliveryDataByFilterAsync(string filterJql)
        => api.GetDeliveryDataByFilterAsync(filterJql);

    public Task<SprintReport> GetIssuesByProductInRangeAsync(string product, DateTime start, DateTime end, int? sprintId = null)
        => api.GetIssuesByProductInRangeAsync(product, start, end, sprintId);

    public Task<List<string>> GetJsProjectFieldOptionsAsync()
        => api.GetJsProjectFieldOptionsAsync();

    public Task<SprintReport> GetIssuesByKeysAsync(List<string> issueKeys)
        => api.GetIssuesByKeysAsync(issueKeys);

    public Task<SprintReport> GetEpicBugsAsync(string epicKey, bool bugsOnly = true)
        => api.GetEpicBugsAsync(epicKey, bugsOnly);

    public Task<SprintReport> GetPriorityBugsAsync()
        => api.GetPriorityBugsAsync();

    public Task<SprintReport> GetBugsByJqlAsync(string rawJql)
        => api.GetBugsByJqlAsync(rawJql);

    public Task<SprintReport> GetBugsWithLinksAsync(DateOnly createdFrom, DateOnly createdTo)
        => api.GetBugsWithLinksAsync(createdFrom, createdTo);

    public Task<SprintReport> GetSlaBugsByJqlAsync(string rawJql)
        => api.GetSlaBugsByJqlAsync(rawJql);

    public Task<IssueDevStatus> GetDevStatusAsync(string jiraId)
        => api.GetDevStatusAsync(jiraId);

    public Task<Dictionary<string, string>> FetchEpicNamesAsync(List<string> epicKeys)
        => api.FetchEpicNamesAsync(epicKeys);

    public Task<Dictionary<string, EpicMeta>> FetchEpicMetaAsync(List<string> epicKeys)
        => api.FetchEpicMetaAsync(epicKeys);

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

    // Sprint-aware cache, mirroring the Support Trends freeze/delta pattern (see
    // GetSupportEpicBugsAsync): once Jira reports a sprint closed AND the stored copy was
    // synced on or after its final day, that snapshot is the sprint's permanent historical
    // record — carried-over tasks keep the state they had at close instead of picking up
    // the next sprint's activity. While the sprint is still open (or the last sync predates
    // its close), only issues updated in the hot window are re-fetched and merged over the
    // stored copy, so a daily page load stays cheap.
    public async Task<SprintReport> GetDeliveryDataAsync(int sprintId, bool bypassCache = false)
    {
        var key = $"delivery:{sprintId}";

        // bypassCache (full refresh button) always re-fetches every issue live from Jira and
        // rebuilds the stored copy from scratch — the explicit escape hatch for a frozen
        // sprint, at the cost of overwriting its historical snapshot with today's Jira state.
        if (_useCache && !bypassCache)
        {
            var cached = await repo.GetSprintReportAsync(key);
            if (cached is not null && cached.Issues.Count > 0)
            {
                if (cached.IsFrozen) return cached;

                var sinceDays = HotSinceDays(cached.SyncedAt ?? DateTime.UtcNow.AddDays(-_hotWindowDays));
                var delta = await api.GetDeliveryDataUpdatedSinceAsync(sprintId, sinceDays);
                var merged = MergeIssuesByKey(cached.Issues, delta.Issues);
                var toStore = new SprintReport
                {
                    ReportIdentifier = key,
                    JiraSprintId = sprintId,
                    ProjectKey   = cached.ProjectKey,
                    SprintName   = string.IsNullOrEmpty(delta.SprintName)  ? cached.SprintName  : delta.SprintName,
                    SprintState  = string.IsNullOrEmpty(delta.SprintState) ? cached.SprintState : delta.SprintState,
                    StartDate    = delta.StartDate ?? cached.StartDate,
                    EndDate      = delta.EndDate   ?? cached.EndDate,
                    Issues       = merged
                };
                await repo.UpsertSprintReportAsync(toStore);
                return toStore;
            }
        }

        var report = await api.GetDeliveryDataAsync(sprintId);
        report.JiraSprintId = sprintId;
        report.ReportIdentifier = key;
        if (_useCache) await repo.UpsertSprintReportAsync(report);
        return report;
    }

    public Task<SprintReport?> SetSprintFreezeAsync(int sprintId, bool frozen)
        => repo.SetManualFreezeAsync($"delivery:{sprintId}", frozen);

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

    // ── Support Trends: sprint-aware epic cache ─────────────────────────────
    // Closed sprints are frozen in the DB; the current sprint only re-fetches the
    // slice of issues updated inside the hot window (default 5 days) and merges it
    // over the stored report. NOTE: not thread-safe across parallel calls (shared
    // scoped DbContext) — callers must invoke sequentially.

    public async Task<SprintReport> GetSupportEpicBugsAsync(string epicKey, DateOnly? sprintEnd, bool forceRefresh = false)
    {
        var key = $"epicbugs:{epicKey.ToUpperInvariant()}";

        if (_useCache)
        {
            var cached = await repo.GetSprintReportAsync(key);
            if (cached is { SyncedAt: not null } && cached.Issues.Count > 0)
            {
                // Frozen: either manually closed (ManuallyFrozen), or the sprint ended more than
                // a hot-window ago AND the stored copy was synced after that point — its final
                // state is already captured. This check runs even when forceRefresh is set: a
                // settled sprint's historical snapshot must never be overwritten with later Jira
                // activity, not even by an explicit "force reload" click — and in particular must
                // never silently drop a bug that has since been re-parented to a different epic
                // (a forced re-fetch is a full "parent = epicKey" replace, not a merge).
                if (cached.ManuallyFrozen ||
                    (sprintEnd is DateOnly end &&
                    cached.SyncedAt.Value >= end.ToDateTime(TimeOnly.MinValue).AddDays(_hotWindowDays) &&
                    DateTime.UtcNow >= end.ToDateTime(TimeOnly.MinValue).AddDays(_hotWindowDays)))
                    return cached;

                if (!forceRefresh)
                {
                    // Hot: fetch only recently-updated issues and merge them over the cache.
                    // The window always covers the gap since the last sync, so nothing is
                    // missed even if the page wasn't opened for a while.
                    var sinceDays = HotSinceDays(cached.SyncedAt.Value);
                    var delta = await api.GetEpicBugsUpdatedSinceAsync(epicKey, bugsOnly: false, sinceDays);
                    var merged = MergeIssuesByKey(cached.Issues, delta.Issues);
                    var toStore = new SprintReport
                    {
                        ReportIdentifier = key,
                        ProjectKey  = cached.ProjectKey,
                        SprintName  = cached.SprintName,
                        StartDate   = cached.StartDate,
                        EndDate     = cached.EndDate,
                        JiraSprintId = cached.JiraSprintId,
                        Issues      = merged
                    };
                    await repo.UpsertSprintReportAsync(toStore);
                    return toStore;
                }
            }
        }

        var report = await api.GetEpicBugsAsync(epicKey, bugsOnly: false);
        report.ReportIdentifier = key;
        if (_useCache) await repo.UpsertSprintReportAsync(report);
        return report;
    }

    public Task<SprintReport?> SetSupportEpicFreezeAsync(string epicKey, bool frozen)
        => repo.SetManualFreezeAsync($"epicbugs:{epicKey.ToUpperInvariant()}", frozen);

    public async Task<bool> IsSupportEpicFrozenAsync(string epicKey, DateOnly? sprintEnd)
    {
        if (!_useCache) return false;

        var cached = await repo.GetSprintReportAsync($"epicbugs:{epicKey.ToUpperInvariant()}");
        if (cached is not { SyncedAt: not null }) return false;
        if (cached.ManuallyFrozen) return true;
        if (sprintEnd is not DateOnly end) return false;

        var settlePoint = end.ToDateTime(TimeOnly.MinValue).AddDays(_hotWindowDays);
        return cached.SyncedAt.Value >= settlePoint && DateTime.UtcNow >= settlePoint;
    }

    public async Task<SprintReport> GetJsSupportLinkedBugsAsync(DateOnly from, DateOnly to, bool forceRefresh = false)
    {
        const string key = "jssupportlinked:JM";

        if (_useCache && !forceRefresh)
        {
            var cached = await repo.GetSprintReportAsync(key);
            // The cached window must cover the requested one (a newly-configured older
            // sprint widens `from` past the stored StartDate → full refetch).
            if (cached is { SyncedAt: not null } && cached.Issues.Count > 0 &&
                cached.StartDate.HasValue && DateOnly.FromDateTime(cached.StartDate.Value) <= from)
            {
                var sinceDays = HotSinceDays(cached.SyncedAt.Value);
                var deltaFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-sinceDays));
                var delta = await api.GetBugsWithLinksAsync(deltaFrom, to);
                var merged = MergeIssuesByKey(cached.Issues, delta.Issues);
                var toStore = new SprintReport
                {
                    ReportIdentifier = key,
                    StartDate = cached.StartDate,
                    EndDate   = to.ToDateTime(TimeOnly.MinValue),
                    Issues    = merged
                };
                await repo.UpsertSprintReportAsync(toStore);
                return toStore;
            }
        }

        var report = await api.GetBugsWithLinksAsync(from, to);
        report.ReportIdentifier = key;
        report.StartDate = from.ToDateTime(TimeOnly.MinValue);
        report.EndDate   = to.ToDateTime(TimeOnly.MinValue);
        if (_useCache) await repo.UpsertSprintReportAsync(report);
        return report;
    }

    // Simple exact-range cache (no incremental delta-merge) for the on-demand JSSUPPORT
    // browser tab — kept on its own key per date range so it never overwrites the
    // narrower report GetJsSupportLinkedBugsAsync maintains for the main pool.
    public async Task<SprintReport> GetJsSupportBugsBrowseAsync(DateOnly from, DateOnly to, bool forceRefresh = false)
    {
        var key = $"jssupportbrowse:JM:{from:yyyyMMdd}-{to:yyyyMMdd}";

        if (_useCache && !forceRefresh)
        {
            var cached = await repo.GetSprintReportAsync(key);
            if (cached is { SyncedAt: not null } && cached.Issues.Count > 0)
                return cached;
        }

        var report = await api.GetBugsWithLinksAsync(from, to);
        report.ReportIdentifier = key;
        report.StartDate = from.ToDateTime(TimeOnly.MinValue);
        report.EndDate   = to.ToDateTime(TimeOnly.MinValue);
        if (_useCache) await repo.UpsertSprintReportAsync(report);
        return report;
    }

    /// <summary>Delta window in days: at least the hot window, stretched to cover the gap since the last sync.</summary>
    private int HotSinceDays(DateTime syncedAtUtc)
        => Math.Max(_hotWindowDays, (int)Math.Ceiling((DateTime.UtcNow - syncedAtUtc).TotalDays) + 1);

    // Replaces cached issues with their fresh delta versions (matched by Key) and appends new
    // ones. All entity ids are reset so the full-replace upsert re-inserts a clean graph.
    private static List<SprintIssue> MergeIssuesByKey(List<SprintIssue> cachedIssues, List<SprintIssue> deltaIssues)
    {
        var merged = new Dictionary<string, SprintIssue>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in cachedIssues) merged[i.Key] = i;
        foreach (var i in deltaIssues)  merged[i.Key] = i;

        var list = merged.Values.ToList();
        foreach (var issue in list)
        {
            issue.Id = 0;
            issue.SprintReportId = null;
            issue.JiraEpicReportId = null;
            foreach (var w in issue.Worklogs)
            {
                w.Id = 0;
                w.SprintIssueId = null;
            }
        }
        return list;
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
