using JiraReportingTool.Data;
using Microsoft.EntityFrameworkCore;

public class SprintPlanService(AppDbContext db) : ISprintPlanService
{
    // ── List (header only — no children needed for the dropdown) ──────────────

    public Task<List<SprintPlanHeader>> GetAllAsync() =>
        db.SprintPlans
          .OrderByDescending(p => p.UpdatedAt)
          .ToListAsync();

    // ── Single plan with all children ─────────────────────────────────────────

    public Task<SprintPlanHeader?> GetAsync(int id) =>
        db.SprintPlans
          .Include(p => p.Allocations)
          .Include(p => p.CustomTasks)
          .Include(p => p.Holidays)
          .Include(p => p.TimeOffs)
          .FirstOrDefaultAsync(p => p.Id == id);

    // ── Save (create or full-replace) ─────────────────────────────────────────

    public async Task<SprintPlanHeader> SaveAsync(SprintPlanHeader plan)
    {
        plan.UpdatedAt = DateTime.UtcNow;

        if (plan.Id == 0)
        {
            // ── New plan ──
            plan.CreatedAt = DateTime.UtcNow;
            db.SprintPlans.Add(plan);
        }
        else
        {
            // ── Update: delete all children, then re-insert via the header ──
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

    // ── Delete (cascade removes children via FK) ──────────────────────────────

    public Task DeleteAsync(int id) =>
        db.SprintPlans.Where(p => p.Id == id).ExecuteDeleteAsync();
}
