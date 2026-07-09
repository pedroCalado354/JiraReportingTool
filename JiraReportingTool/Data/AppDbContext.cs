using Microsoft.EntityFrameworkCore;
using JiraReportingTool.Models;

namespace JiraReportingTool.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<JiraEpicReport> JiraEpicReports => Set<JiraEpicReport>();
    public DbSet<WorklogEntry> WorklogEntries => Set<WorklogEntry>();
    public DbSet<SprintReport> SprintReports => Set<SprintReport>();
    public DbSet<SprintIssue> SprintIssues => Set<SprintIssue>();
    public DbSet<JiraFilter> JiraFilters => Set<JiraFilter>();
    public DbSet<EpicSummary> EpicSummaries => Set<EpicSummary>();

    // ── Support Bugs · SLAs daily history snapshots ──────────────────────────
    public DbSet<SlaSnapshot> SlaSnapshots => Set<SlaSnapshot>();

    // ── Working Hours frozen sprint snapshots ─────────────────────────────────
    public DbSet<WorkingHoursSnapshot> WorkingHoursSnapshots => Set<WorkingHoursSnapshot>();

    // ── Sprint / Epic configuration (drives default inputs on dashboards) ────
    public DbSet<SprintConfig> SprintConfigs => Set<SprintConfig>();

    // ── Team roster + shared holidays (editable backoffice config) ───────────
    public DbSet<RosterMember>       Roster              => Set<RosterMember>();
    public DbSet<SharedHoliday>      SharedHolidays      => Set<SharedHoliday>();
    public DbSet<CustomTaskTemplate> CustomTaskTemplates => Set<CustomTaskTemplate>();
    public DbSet<RosterVacation>     RosterVacations     => Set<RosterVacation>();

    // ── Sprint Plan CRUD ──────────────────────────────────────────────────────
    public DbSet<SprintPlanHeader>     SprintPlans           => Set<SprintPlanHeader>();
    public DbSet<SprintPlanAllocation> SprintPlanAllocations => Set<SprintPlanAllocation>();
    public DbSet<SprintPlanCustomTask> SprintPlanCustomTasks => Set<SprintPlanCustomTask>();
    public DbSet<SprintPlanHoliday>    SprintPlanHolidays    => Set<SprintPlanHoliday>();
    public DbSet<SprintPlanTimeOff>    SprintPlanTimeOffs    => Set<SprintPlanTimeOff>();
    public DbSet<SprintPlanVersion>    SprintPlanVersions    => Set<SprintPlanVersion>();
    public DbSet<SprintPlanRemovalLog> SprintPlanRemovalLogs => Set<SprintPlanRemovalLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── JiraEpicReport → SprintIssue (1-to-many, cascade) ───────────────
        modelBuilder.Entity<SprintIssue>()
            .HasOne<JiraEpicReport>()
            .WithMany(e => e.Issues)
            .HasForeignKey(i => i.JiraEpicReportId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        // ── SprintReport → SprintIssue (1-to-many, cascade) ─────────────────
        modelBuilder.Entity<SprintIssue>()
            .HasOne<SprintReport>()
            .WithMany(s => s.Issues)
            .HasForeignKey(i => i.SprintReportId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        // ── SprintIssue → WorklogEntry (1-to-many) ───────────────────────────
        modelBuilder.Entity<WorklogEntry>()
            .HasOne<SprintIssue>()
            .WithMany(i => i.Worklogs)
            .HasForeignKey(w => w.SprintIssueId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        // ── SprintIssue.Labels → stored as JSON ─────────────────────────────
        modelBuilder.Entity<SprintIssue>()
            .Property(e => e.Labels)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new()
            )
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                v => v.ToList()));

        // ── SprintIssue.LinkedIssueKeys → stored as JSON ─────────────────────
        // Deserialize guards against "" — rows created before this column existed
        // carry the migration's empty-string default, which is not valid JSON.
        modelBuilder.Entity<SprintIssue>()
            .Property(e => e.LinkedIssueKeys)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new()
                    : System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new()
            )
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                v => v.ToList()));

        // ── SprintIssue.Sprints (full sprint history) → stored as JSON ───────
        modelBuilder.Entity<SprintIssue>()
            .Property(e => e.Sprints)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? new()
                    : System.Text.Json.JsonSerializer.Deserialize<List<IssueSprint>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new()
            )
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<IssueSprint>>(
                (a, b) => a != null && b != null && a.Count == b.Count,
                v => v.Count,
                v => v.ToList()));

        // ── SprintReport: unique lookup index on ReportIdentifier ────────────
        modelBuilder.Entity<SprintReport>()
            .HasIndex(s => s.ReportIdentifier)
            .IsUnique()
            .HasFilter("[ReportIdentifier] IS NOT NULL");

        // ── EpicSummary: standalone lookup table (Issues are [NotMapped]) ────
        modelBuilder.Entity<EpicSummary>()
            .HasIndex(e => e.Key);

        // ── SprintPlanHeader → children (cascade delete) ─────────────────────
        modelBuilder.Entity<SprintPlanAllocation>()
            .HasOne<SprintPlanHeader>()
            .WithMany(p => p.Allocations)
            .HasForeignKey(a => a.SprintPlanId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SprintPlanAllocation>()
            .Property(a => a.HoursAllocated)
            .HasPrecision(6, 2);

        modelBuilder.Entity<SprintPlanCustomTask>()
            .Property(t => t.Hours)
            .HasPrecision(6, 2);

        modelBuilder.Entity<SprintPlanCustomTask>()
            .HasOne<SprintPlanHeader>()
            .WithMany(p => p.CustomTasks)
            .HasForeignKey(t => t.SprintPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SprintPlanHoliday>()
            .HasOne<SprintPlanHeader>()
            .WithMany(p => p.Holidays)
            .HasForeignKey(h => h.SprintPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SprintPlanTimeOff>()
            .HasOne<SprintPlanHeader>()
            .WithMany(p => p.TimeOffs)
            .HasForeignKey(t => t.SprintPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── SprintPlanVersion → SprintPlanHeader (cascade delete, no nav on header) ──
        modelBuilder.Entity<SprintPlanVersion>()
            .Property(v => v.DataJson)
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<SprintPlanVersion>()
            .HasOne<SprintPlanHeader>()
            .WithMany()
            .HasForeignKey(v => v.SprintPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── SprintPlanRemovalLog → SprintPlanHeader (cascade delete) ─────────
        modelBuilder.Entity<SprintPlanRemovalLog>()
            .HasOne<SprintPlanHeader>()
            .WithMany()
            .HasForeignKey(l => l.SprintPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Team roster + shared holidays ────────────────────────────────────
        modelBuilder.Entity<RosterMember>()
            .Property(m => m.HoursPerDay)
            .HasPrecision(4, 2);

        modelBuilder.Entity<SharedHoliday>()
            .HasIndex(h => h.Date)
            .IsUnique();

        modelBuilder.Entity<RosterVacation>()
            .HasOne<RosterMember>()
            .WithMany()
            .HasForeignKey(v => v.RosterMemberId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── SLA history snapshot: one row per day, JSON payload ──────────────
        modelBuilder.Entity<SlaSnapshot>()
            .HasIndex(s => s.SnapshotDate)
            .IsUnique();
        modelBuilder.Entity<SlaSnapshot>()
            .Property(s => s.DataJson)
            .HasColumnType("nvarchar(max)");

        // ── Working Hours frozen snapshot: one row per sprint, JSON payload ──
        modelBuilder.Entity<WorkingHoursSnapshot>()
            .HasIndex(s => s.SprintId)
            .IsUnique();
        modelBuilder.Entity<WorkingHoursSnapshot>()
            .Property(s => s.ReportJson)
            .HasColumnType("nvarchar(max)");
    }
}
