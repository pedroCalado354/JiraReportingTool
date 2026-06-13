using JiraReportingTool.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// CRUD access for SprintConfig rows.
/// GetCurrentAsync returns the row whose date range contains today — used by
/// dashboards to auto-fill their default sprint / epic inputs.
/// </summary>
public class SprintConfigService(AppDbContext db)
{
    public Task<List<SprintConfig>> GetAllAsync()
        => db.SprintConfigs.OrderByDescending(c => c.StartDate).ToListAsync();

    public Task<SprintConfig?> GetCurrentAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return db.SprintConfigs
            .Where(c => c.StartDate <= today && c.EndDate >= today)
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync();
    }

    public async Task SaveAsync(SprintConfig config)
    {
        if (config.Id == 0)
            db.SprintConfigs.Add(config);
        else
            db.SprintConfigs.Update(config);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entry = await db.SprintConfigs.FindAsync(id);
        if (entry is not null)
        {
            db.SprintConfigs.Remove(entry);
            await db.SaveChangesAsync();
        }
    }
}
