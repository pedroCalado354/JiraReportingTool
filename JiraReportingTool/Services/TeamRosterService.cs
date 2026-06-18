using JiraReportingTool.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// CRUD access for the editable team roster and shared (bank) holidays.
/// Replaces the previously hard-coded member list on the Sprint Planning board.
/// </summary>
public class TeamRosterService(AppDbContext db)
{
    // ── Roster ────────────────────────────────────────────────────────────────
    public Task<List<RosterMember>> GetAllAsync() =>
        db.Roster.AsNoTracking()
          .OrderBy(m => m.Team).ThenBy(m => m.SortOrder).ThenBy(m => m.Name)
          .ToListAsync();

    public Task<List<RosterMember>> GetActiveAsync() =>
        db.Roster.AsNoTracking()
          .Where(m => m.Active)
          .OrderBy(m => m.Team).ThenBy(m => m.SortOrder).ThenBy(m => m.Name)
          .ToListAsync();

    public async Task SaveAsync(RosterMember member)
    {
        if (member.Id == 0) db.Roster.Add(member);
        else                db.Roster.Update(member);
        await db.SaveChangesAsync();
    }

    public async Task SaveManyAsync(IEnumerable<RosterMember> members)
    {
        foreach (var m in members)
        {
            if (m.Id == 0) db.Roster.Add(m);
            else           db.Roster.Update(m);
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entry = await db.Roster.FindAsync(id);
        if (entry is not null)
        {
            db.Roster.Remove(entry);
            await db.SaveChangesAsync();
        }
    }

    // ── Shared holidays ─────────────────────────────────────────────────────────
    public Task<List<SharedHoliday>> GetHolidaysAsync() =>
        db.SharedHolidays.AsNoTracking().OrderBy(h => h.Date).ToListAsync();

    public async Task AddHolidayAsync(DateOnly date, string name)
    {
        if (await db.SharedHolidays.AnyAsync(h => h.Date == date)) return;
        db.SharedHolidays.Add(new SharedHoliday { Date = date, Name = name });
        await db.SaveChangesAsync();
    }

    public async Task DeleteHolidayAsync(int id)
    {
        var entry = await db.SharedHolidays.FindAsync(id);
        if (entry is not null)
        {
            db.SharedHolidays.Remove(entry);
            await db.SaveChangesAsync();
        }
    }

    // ── Custom task templates ─────────────────────────────────────────────────────
    public Task<List<CustomTaskTemplate>> GetCustomTaskTemplatesAsync() =>
        db.CustomTaskTemplates.AsNoTracking()
          .OrderBy(t => t.SortOrder).ThenBy(t => t.Category)
          .ToListAsync();

    public Task<List<CustomTaskTemplate>> GetActiveCustomTaskTemplatesAsync() =>
        db.CustomTaskTemplates.AsNoTracking()
          .Where(t => t.Active)
          .OrderBy(t => t.SortOrder).ThenBy(t => t.Category)
          .ToListAsync();

    public async Task SaveCustomTaskTemplatesAsync(IEnumerable<CustomTaskTemplate> templates)
    {
        foreach (var t in templates)
        {
            if (t.Id == 0) db.CustomTaskTemplates.Add(t);
            else           db.CustomTaskTemplates.Update(t);
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteCustomTaskTemplateAsync(int id)
    {
        var entry = await db.CustomTaskTemplates.FindAsync(id);
        if (entry is not null)
        {
            db.CustomTaskTemplates.Remove(entry);
            await db.SaveChangesAsync();
        }
    }

    // ── One-time seed of the historical hard-coded roster ─────────────────────────
    public async Task SeedDefaultsIfEmptyAsync()
    {
        if (await db.Roster.AnyAsync()) return;

        var defaults = new (string Name, string Team)[]
        {
            ("Duarte", "Backend"), ("Freire", "Backend"), ("Amado", "Backend"),
            ("Rosa", "Backend"), ("Patrick", "Backend"), ("Rui", "Backend"),
            ("Mateus", "Backend"), ("Dasa", "Backend"), ("Shashank", "Backend"),
            ("Sangram", "Backend"), ("Ranjit", "Backend"),
            ("Bruno", "Frontend"), ("Samuel", "Frontend"), ("Murilo", "Frontend"),
            ("Omar", "Frontend"), ("Vinicius", "Frontend"), ("Ronit", "Frontend"),
            ("Tanaya", "Frontend"), ("Vijay", "Frontend"), ("Bhavini", "Frontend"),
            ("Daria", "QA"), ("Daniel", "QA"), ("Jessica", "QA"), ("Katarina", "QA"),
            ("Tejaswini", "QA"), ("Anushka", "QA"), ("Muskan", "QA"),
        };

        var order = 0;
        foreach (var (name, team) in defaults)
            db.Roster.Add(new RosterMember
            {
                Name = name, Team = team, HoursPerDay = 6m, Active = true, SortOrder = order++
            });

        await db.SaveChangesAsync();
    }
}
