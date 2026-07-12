using JiraReportingTool.Data;
using JiraReportingTool.Models;

/// <summary>
/// Force-refresh service that always bypasses the cache and re-fetches from the Jira API.
/// Used by the Sync page to refresh individual reports or the full inventory.
/// </summary>
public class DataSyncService(JiraService api, JiraDbRepository repo, SprintConfigService sprintConfigs, IConfiguration config)
{
    private readonly int _hotWindowDays = config.GetValue<int>("Database:TrendsHotWindowDays", 5);

    /// <summary>
    /// True if this report is a settled historical snapshot that must never be silently
    /// overwritten by a bulk/automatic refresh sweep — mirrors the freeze checks in
    /// JiraCacheService (delivery/sprint reports via IsFrozen; Support Trends epic-bugs
    /// reports via the sprint-end + hot-window settle rule, since those don't carry their
    /// own SprintState/EndDate). Other report types (epicall, jssupportlinked) have no
    /// freeze concept and are never protected here. Takes the already-loaded config list so
    /// callers checking many reports don't hit the DB once per report.
    /// </summary>
    private bool IsProtected(SprintReport report, List<SprintConfig> configs)
    {
        var id = report.ReportIdentifier ?? "";

        if (id.StartsWith("delivery:") || id.StartsWith("sprint:"))
            return report.IsFrozen;

        if (id.StartsWith("epicbugs:"))
        {
            if (report.ManuallyFrozen) return true;

            var epicKey = id["epicbugs:".Length..];
            var sprintEnd = configs
                .Where(c => string.Equals(c.EpicKey.Trim(), epicKey, StringComparison.OrdinalIgnoreCase))
                .Select(c => (DateOnly?)c.EndDate)
                .DefaultIfEmpty(null)
                .Max();
            if (sprintEnd is not DateOnly end || !report.SyncedAt.HasValue) return false;
            var settleThreshold = end.ToDateTime(TimeOnly.MinValue).AddDays(_hotWindowDays);
            return report.SyncedAt.Value >= settleThreshold && DateTime.UtcNow >= settleThreshold;
        }

        return false;
    }

    public async Task<(List<SprintReport> sprints, List<JiraEpicReport> epics)> GetCachedInventoryAsync()
    {
        var sprints = await repo.GetAllSprintReportsAsync();
        var epics = await repo.GetAllEpicReportsAsync();
        return (sprints, epics);
    }

    public async Task RefreshSprintReportAsync(SprintReport report)
    {
        var id = report.ReportIdentifier ?? "";
        SprintReport fresh;

        if (id.StartsWith("sprint:"))
        {
            // "sprint:PROJ:42"
            var parts = id.Split(':');
            var projectKey = parts[1];
            var sprintId = int.Parse(parts[2]);
            fresh = await api.GetSprintReportAsync(projectKey, sprintId);
            fresh.JiraSprintId = sprintId;
        }
        else if (id.StartsWith("delivery:"))
        {
            // "delivery:42"
            var sprintId = int.Parse(id["delivery:".Length..]);
            fresh = await api.GetDeliveryDataAsync(sprintId);
            fresh.JiraSprintId = sprintId;
        }
        else if (id.StartsWith("epicall:"))
        {
            // "epicall:KEY-1"
            var epicKey = id["epicall:".Length..];
            fresh = await api.GetAllEpicIssuesAsync(epicKey);
        }
        else if (id.StartsWith("epicbugs:"))
        {
            // "epicbugs:KEY-1" — Support Trends epic report (all issue types under the epic)
            var epicKey = id["epicbugs:".Length..];
            fresh = await api.GetEpicBugsAsync(epicKey, bugsOnly: false);
        }
        else if (id.StartsWith("jssupportlinked:"))
        {
            // "jssupportlinked:JM" — Support Trends JSSUPPORT-linked pool; re-fetch over the
            // stored window (fall back to the last 6 months if the window was never stamped).
            var from = report.StartDate.HasValue
                ? DateOnly.FromDateTime(report.StartDate.Value)
                : DateOnly.FromDateTime(DateTime.Today.AddMonths(-6));
            var to = report.EndDate.HasValue
                ? DateOnly.FromDateTime(report.EndDate.Value)
                : DateOnly.FromDateTime(DateTime.Today);
            fresh = await api.GetBugsWithLinksAsync(from, to);
            fresh.StartDate = from.ToDateTime(TimeOnly.MinValue);
            fresh.EndDate   = to.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            throw new InvalidOperationException($"Cannot refresh unknown report identifier: '{id}'");
        }

        fresh.ReportIdentifier = report.ReportIdentifier;
        await repo.UpsertSprintReportAsync(fresh);
    }

    public async Task RefreshEpicReportAsync(JiraEpicReport report)
    {
        var fresh = await api.GetEpicReportAsync(report.Key);
        await repo.UpsertEpicReportAsync(fresh);
    }

    /// <summary>
    /// Refreshes all cached reports, or only stale ones if <paramref name="staleOnly"/> is true.
    /// Returns counts of reports that were actually refreshed.
    /// </summary>
    public async Task<(int sprintCount, int epicCount)> RefreshAllAsync(bool staleOnly, int ttlMinutes)
    {
        var (sprints, epics) = await GetCachedInventoryAsync();
        var cutoff = DateTime.UtcNow.AddMinutes(-ttlMinutes);

        // A settled/frozen sprint's SyncedAt is always old (that's the point — it never gets
        // touched), so it would otherwise look "stale" and get swept up here. Bulk/automatic
        // refreshes must never silently overwrite a permanent historical snapshot — that's
        // only ever done through an explicit single-report refresh (RefreshSprintReportAsync).
        var configs = await sprintConfigs.GetAllAsync();
        sprints = sprints.Where(s => !IsProtected(s, configs)).ToList();

        if (staleOnly)
        {
            sprints = sprints.Where(s => !s.SyncedAt.HasValue || s.SyncedAt < cutoff).ToList();
            epics   = epics  .Where(e => !e.SyncedAt.HasValue || e.SyncedAt < cutoff).ToList();
        }

        foreach (var s in sprints) await RefreshSprintReportAsync(s);
        foreach (var e in epics)   await RefreshEpicReportAsync(e);

        return (sprints.Count, epics.Count);
    }
}
