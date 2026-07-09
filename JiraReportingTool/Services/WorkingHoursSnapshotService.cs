using System.Text.Json;
using JiraReportingTool.Data;
using JiraReportingTool.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Freezes a sprint's Working Hours data once it has settled (its end date is a few
/// days in the past) so historical numbers never drift and stop costing a Jira call.
/// Snapshots are immutable — once written for a sprint id, they are never overwritten.
/// </summary>
public class WorkingHoursSnapshotService(AppDbContext db)
{
    /// <summary>How long after a sprint ends before its data is considered settled enough to freeze.</summary>
    public const int SettleDays = 3;

    public Task<bool> HasSnapshotAsync(int sprintId) =>
        db.WorkingHoursSnapshots.AnyAsync(s => s.SprintId == sprintId);

    public async Task<SprintReport?> GetSnapshotAsync(int sprintId)
    {
        var row = await db.WorkingHoursSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SprintId == sprintId);
        return row is null ? null : JsonSerializer.Deserialize<SprintReport>(row.ReportJson);
    }

    public Task<List<WorkingHoursSnapshot>> GetAllMetaAsync() =>
        db.WorkingHoursSnapshots.AsNoTracking()
          .Select(s => new WorkingHoursSnapshot { Id = s.Id, SprintId = s.SprintId, SprintName = s.SprintName, CreatedAt = s.CreatedAt })
          .ToListAsync();

    /// <summary>True once a sprint's end date is far enough in the past to be considered settled.</summary>
    public static bool IsSettled(SprintReport report) =>
        report.EndDate.HasValue && report.EndDate.Value.Date <= DateTime.Today.AddDays(-SettleDays);

    /// <summary>No-op if a snapshot already exists for this sprint id (snapshots are immutable).</summary>
    public async Task SaveIfSettledAsync(SprintReport report)
    {
        if (!report.JiraSprintId.HasValue || !IsSettled(report)) return;
        if (await HasSnapshotAsync(report.JiraSprintId.Value)) return;

        db.WorkingHoursSnapshots.Add(new WorkingHoursSnapshot
        {
            SprintId = report.JiraSprintId.Value,
            SprintName = report.SprintName,
            ReportJson = JsonSerializer.Serialize(report)
        });
        await db.SaveChangesAsync();
    }
}
