using JiraReportingTool.Data;
using JiraReportingTool.Models;

/// <summary>
/// Force-refresh service that always bypasses the cache and re-fetches from the Jira API.
/// Used by the Sync page to refresh individual reports or the full inventory.
/// </summary>
public class DataSyncService(JiraService api, JiraDbRepository repo)
{
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
