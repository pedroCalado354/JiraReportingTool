using System.Text.Json;
using JiraReportingTool.Data;
using Microsoft.EntityFrameworkCore;

public class SprintPlanService(AppDbContext db) : ISprintPlanService
{
    // ── List (header only — no children needed for the dropdown) ──────────────

    public Task<List<SprintPlanHeader>> GetAllAsync() =>
        db.SprintPlans
          .AsNoTracking()
          .OrderByDescending(p => p.UpdatedAt)
          .ToListAsync();

    // ── Single plan with all children ─────────────────────────────────────────

    public Task<SprintPlanHeader?> GetAsync(int id) =>
        db.SprintPlans
          .AsNoTracking()
          .Include(p => p.Allocations)
          .Include(p => p.CustomTasks)
          .Include(p => p.Holidays)
          .Include(p => p.TimeOffs)
          .FirstOrDefaultAsync(p => p.Id == id);

    // ── Save (create or full-replace) ─────────────────────────────────────────

    public async Task<SprintPlanHeader> SaveAsync(SprintPlanHeader plan)
    {
        db.ChangeTracker.Clear();
        plan.UpdatedAt = DateTime.UtcNow;

        if (plan.Id == 0)
        {
            // ── New plan ──
            plan.CreatedAt = DateTime.UtcNow;
            db.SprintPlans.Add(plan);
        }
        else
        {
            // ── Update: snapshot current state as a version, then full-replace ──
            var json = JsonSerializer.Serialize(plan);
            var maxVersion = await db.SprintPlanVersions
                .Where(v => v.SprintPlanId == plan.Id)
                .MaxAsync(v => (int?)v.VersionNumber) ?? 0;
            db.SprintPlanVersions.Add(new SprintPlanVersion
            {
                SprintPlanId  = plan.Id,
                VersionNumber = maxVersion + 1,
                SavedAt       = DateTime.UtcNow,
                DataJson      = json,
            });

            // Delete all children, then re-insert via the header.
            // ExecuteDeleteAsync avoids loading the rows into memory.
            await db.SprintPlanAllocations.Where(a => a.SprintPlanId == plan.Id).ExecuteDeleteAsync();
            await db.SprintPlanCustomTasks.Where(t => t.SprintPlanId == plan.Id).ExecuteDeleteAsync();
            await db.SprintPlanHolidays  .Where(h => h.SprintPlanId == plan.Id).ExecuteDeleteAsync();
            await db.SprintPlanTimeOffs  .Where(t => t.SprintPlanId == plan.Id).ExecuteDeleteAsync();

            // Reset child IDs so EF inserts them as new rows
            foreach (var a in plan.Allocations) a.Id = 0;
            foreach (var t in plan.CustomTasks)  t.Id = 0;
            foreach (var h in plan.Holidays)     h.Id = 0;
            foreach (var t in plan.TimeOffs)     t.Id = 0;

            db.SprintPlans.Update(plan);
        }

        await db.SaveChangesAsync();
        return plan;
    }

    // ── Upsert by name ────────────────────────────────────────────────────────

    public async Task<SprintPlanHeader> UpsertByNameAsync(SprintPlanHeader plan)
    {
        // Only resolve by name when caller hasn't already set an ID
        if (plan.Id == 0)
        {
            var existing = await db.SprintPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Name == plan.Name);

            if (existing is not null)
            {
                plan.Id        = existing.Id;
                plan.CreatedAt = existing.CreatedAt;
            }
        }

        return await SaveAsync(plan);
    }

    // ── Delete (cascade removes children via FK) ──────────────────────────────

    public Task DeleteAsync(int id) =>
        db.SprintPlans.Where(p => p.Id == id).ExecuteDeleteAsync();

    // ── Concurrency: lightweight header-only timestamp lookup ─────────────────

    public async Task<DateTime?> GetUpdatedAtAsync(int id)
    {
        var ts = await db.SprintPlans
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => (DateTime?)p.UpdatedAt)
            .FirstOrDefaultAsync();
        return ts;
    }

    // ── Version history ───────────────────────────────────────────────────────

    public Task<List<SprintPlanVersion>> GetVersionsAsync(int planId) =>
        db.SprintPlanVersions
          .Where(v => v.SprintPlanId == planId)
          .OrderByDescending(v => v.VersionNumber)
          .ToListAsync();

    // ── Removal log ───────────────────────────────────────────────────────────

    public async Task<SprintPlanRemovalLog> LogRemovalAsync(SprintPlanRemovalLog log)
    {
        log.RemovedAt = DateTime.UtcNow;
        db.SprintPlanRemovalLogs.Add(log);
        await db.SaveChangesAsync();
        return log;
    }

    public Task<List<SprintPlanRemovalLog>> GetRemovalLogsAsync(int planId) =>
        db.SprintPlanRemovalLogs
          .Where(l => l.SprintPlanId == planId)
          .OrderByDescending(l => l.RemovedAt)
          .ToListAsync();
}
