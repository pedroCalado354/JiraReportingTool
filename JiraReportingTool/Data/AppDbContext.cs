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

    // ── Sprint Plan CRUD ──────────────────────────────────────────────────────
    public DbSet<SprintPlanHeader>     SprintPlans           => Set<SprintPlanHeader>();
    public DbSet<SprintPlanAllocation> SprintPlanAllocations => Set<SprintPlanAllocation>();
    public DbSet<SprintPlanCustomTask> SprintPlanCustomTasks => Set<SprintPlanCustomTask>();
    public DbSet<SprintPlanHoliday>    SprintPlanHolidays    => Set<SprintPlanHoliday>();
    public DbSet<SprintPlanTimeOff>    SprintPlanTimeOffs    => Set<SprintPlanTimeOff>();
    public DbSet<SprintPlanVersion>    SprintPlanVersions    => Set<SprintPlanVersion>();

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
    }
}
