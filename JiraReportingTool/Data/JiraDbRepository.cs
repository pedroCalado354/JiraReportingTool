using Microsoft.EntityFrameworkCore;
using JiraReportingTool.Models;

namespace JiraReportingTool.Data;

/// <summary>
/// Persistence layer for all Jira domain models.
/// Upsert methods replace all child collections on re-sync to stay in lock-step with the API.
/// </summary>
public class JiraDbRepository(AppDbContext db)
{
    // ── Epic Report ──────────────────────────────────────────────────────────

    public async Task<JiraEpicReport?> GetEpicReportServiceAsync(string epicKey)
        => await db.JiraEpicReports
            // No-tracking read: the circuit-scoped DbContext is long-lived, so a tracking
            // query here would merge freshly-loaded rows into stale tracked child collections
            // (e.g. after a /sync replaced them in another context), duplicating issues.
            // Identity resolution still dedupes the Include join.
            .AsNoTrackingWithIdentityResolution()
            .Include(e => e.Issues).ThenInclude(i => i.Worklogs)
            .FirstOrDefaultAsync(e => e.Key == epicKey);

    public async Task<List<JiraEpicReport>> GetAllEpicReportsAsync()
        => await db.JiraEpicReports
            .OrderByDescending(e => e.SyncedAt)
            .ToListAsync();

    public async Task UpsertEpicReportAsync(JiraEpicReport report)
    {
        report.SyncedAt = DateTime.UtcNow;

        var existing = await db.JiraEpicReports
            .Include(e => e.Issues).ThenInclude(i => i.Worklogs)
            .FirstOrDefaultAsync(e => e.Key == report.Key);

        if (existing is not null)
        {
            db.WorklogEntries.RemoveRange(existing.Issues.SelectMany(i => i.Worklogs));
            db.SprintIssues.RemoveRange(existing.Issues);

            existing.SyncedAt = report.SyncedAt;
            existing.Summary = report.Summary;
            existing.Status = report.Status;
            existing.StatusCategoryKey = report.StatusCategoryKey;
            existing.Assignee = report.Assignee;
            existing.Issues = report.Issues;
        }
        else
        {
            db.JiraEpicReports.Add(report);
        }

        await db.SaveChangesAsync();
    }

    // ── Sprint Report ────────────────────────────────────────────────────────

    public async Task<SprintReport?> GetSprintReportAsync(string reportIdentifier)
        => await db.SprintReports
            // No-tracking read — see GetEpicReportServiceAsync for the long-lived-context rationale.
            .AsNoTrackingWithIdentityResolution()
            .Include(s => s.Issues).ThenInclude(i => i.Worklogs)
            .FirstOrDefaultAsync(s => s.ReportIdentifier == reportIdentifier);

    public async Task<List<SprintReport>> GetAllSprintReportsAsync()
        => await db.SprintReports
            .OrderByDescending(s => s.SyncedAt)
            .ToListAsync();

    public async Task UpsertSprintReportAsync(SprintReport report)
    {
        report.SyncedAt = DateTime.UtcNow;

        if (report.ReportIdentifier is null)
        {
            db.SprintReports.Add(report);
            await db.SaveChangesAsync();
            return;
        }

        var existing = await db.SprintReports
            .Include(s => s.Issues).ThenInclude(i => i.Worklogs)
            .FirstOrDefaultAsync(s => s.ReportIdentifier == report.ReportIdentifier);

        if (existing is not null)
        {
            // ClientCascade handles WorklogEntry deletion when SprintIssues are removed.
            db.SprintIssues.RemoveRange(existing.Issues);
            await db.SaveChangesAsync();

            existing.SyncedAt = report.SyncedAt;
            existing.JiraSprintId = report.JiraSprintId;
            existing.ProjectKey = report.ProjectKey;
            existing.SprintName = report.SprintName;
            existing.StartDate = report.StartDate;
            existing.EndDate = report.EndDate;
            existing.Issues = report.Issues;
        }
        else
        {
            db.SprintReports.Add(report);
        }

        await db.SaveChangesAsync();
    }

    // ── Filters ──────────────────────────────────────────────────────────────

    public async Task<List<JiraFilter>> GetFiltersAsync()
        => await db.JiraFilters.ToListAsync();

    public async Task UpsertFiltersAsync(List<JiraFilter> filters)
    {
        var existing = await db.JiraFilters.ToListAsync();
        db.JiraFilters.RemoveRange(existing);
        db.JiraFilters.AddRange(filters);
        await db.SaveChangesAsync();
    }

    // ── Epic Summaries ───────────────────────────────────────────────────────

    public async Task<List<EpicSummary>> GetEpicSummariesAsync(IEnumerable<string> epicKeys)
        => await db.EpicSummaries
            .Where(e => epicKeys.Contains(e.Key))
            .ToListAsync();

    public async Task UpsertEpicSummariesAsync(IEnumerable<EpicSummary> summaries)
    {
        var keys = summaries.Select(s => s.Key).ToList();
        var existing = await db.EpicSummaries.Where(e => keys.Contains(e.Key)).ToListAsync();
        db.EpicSummaries.RemoveRange(existing);
        db.EpicSummaries.AddRange(summaries.Select(s => new EpicSummary { Key = s.Key, Name = s.Name }));
        await db.SaveChangesAsync();
    }
}
