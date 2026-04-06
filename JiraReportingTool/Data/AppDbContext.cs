using Microsoft.EntityFrameworkCore;
using JiraReportingTool.Models;

namespace JiraReportingTool.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<JiraEpicReport> JiraEpicReports => Set<JiraEpicReport>();
    public DbSet<JiraIssueModel> JiraIssues => Set<JiraIssueModel>();
    public DbSet<WorklogEntry> WorklogEntries => Set<WorklogEntry>();
    public DbSet<SprintReport> SprintReports => Set<SprintReport>();
    public DbSet<SprintIssue> SprintIssues => Set<SprintIssue>();
    public DbSet<JiraFilter> JiraFilters => Set<JiraFilter>();
    public DbSet<EpicSummary> EpicSummaries => Set<EpicSummary>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── JiraEpicReport → JiraIssueModel (1-to-many, cascade) ────────────
        modelBuilder.Entity<JiraIssueModel>()
            .HasOne<JiraEpicReport>()
            .WithMany(e => e.Issues)
            .HasForeignKey(i => i.JiraEpicReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── JiraIssueModel → WorklogEntry (1-to-many, cascade) ──────────────
        modelBuilder.Entity<WorklogEntry>()
            .HasOne<JiraIssueModel>()
            .WithMany(i => i.Worklogs)
            .HasForeignKey(w => w.JiraIssueModelId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        // ── SprintReport → SprintIssue (1-to-many, cascade) ─────────────────
        modelBuilder.Entity<SprintIssue>()
            .HasOne<SprintReport>()
            .WithMany(s => s.Issues)
            .HasForeignKey(i => i.SprintReportId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── SprintIssue → WorklogEntry (1-to-many, client-side cascade) ──────
        // Using ClientCascade instead of SQL Cascade to avoid multiple cascade
        // path conflicts in SQL Server (WorklogEntry has two separate FK chains).
        modelBuilder.Entity<WorklogEntry>()
            .HasOne<SprintIssue>()
            .WithMany(i => i.Worklogs)
            .HasForeignKey(w => w.SprintIssueId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.ClientCascade);

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
    }
}
