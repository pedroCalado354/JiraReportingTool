using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JiraReportingTool.Models;

// ── Epic Report ──────────────────────────────────────────────────────────────

public class JiraEpicReport
{
    public int Id { get; set; }
    public DateTime? SyncedAt { get; set; }
    public string Key { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusCategoryKey { get; set; } = "";
    public string Assignee { get; set; } = "Unassigned";
    public List<SprintIssue> Issues { get; set; } = new();

    [NotMapped] public int TotalIssues => Issues.Count;
    [NotMapped] public int DoneCount => Issues.Count(i => i.StatusCategoryKey == "done");
    [NotMapped] public int InProgressCount => Issues.Count(i => i.StatusCategoryKey == "indeterminate");
    [NotMapped] public int ToDoCount => Issues.Count(i => i.StatusCategoryKey == "new");
    [NotMapped] public int TotalTimeSpentSeconds => Issues.Sum(i => i.TimeSpentSeconds);
    [NotMapped] public int TotalOriginalEstimateSeconds => Issues.Sum(i => i.OriginalEstimateSeconds);
}

public class WorklogEntry
{
    public int Id { get; set; }
    public int? SprintIssueId { get; set; }

    public string Author { get; set; } = "";
    public string TimeSpent { get; set; } = "";
    public int TimeSpentSeconds { get; set; }
    public string Comment { get; set; } = "";
    public DateTime Started { get; set; }
}

// ── Sprint Report ─────────────────────────────────────────────────────────────

public enum SprintRisk { OnTrack, AtRisk, Behind }

public class SprintReport
{
    public int Id { get; set; }
    public DateTime? SyncedAt { get; set; }

    /// <summary>Jira internal sprint ID — used as DB cache key.</summary>
    public int? JiraSprintId { get; set; }

    /// <summary>Unique identifier used to look up a cached report (e.g. "sprint:PROJ:42", "delivery:42", "epicall:KEY-1").</summary>
    public string? ReportIdentifier { get; set; }

    public string ProjectKey { get; set; } = "";
    public string SprintName { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<SprintIssue> Issues { get; set; } = new();

    [NotMapped] public int TotalIssues => Issues.Count;
    [NotMapped] public int DoneCount => Issues.Count(i => i.StatusCategoryKey == "done");
    [NotMapped] public int InProgressCount => Issues.Count(i => i.StatusCategoryKey == "indeterminate");
    [NotMapped] public int ToDoCount => Issues.Count(i => i.StatusCategoryKey == "new");

    [NotMapped] public int TotalStoryPoints => Issues.Sum(i => i.StoryPoints ?? 0);
    [NotMapped] public int DoneStoryPoints => Issues.Where(i => i.StatusCategoryKey == "done").Sum(i => i.StoryPoints ?? 0);
    [NotMapped] public bool HasStoryPoints => Issues.Any(i => i.StoryPoints.HasValue);

    [NotMapped] public int TotalTimeSpentSeconds => Issues.Sum(i => i.TimeSpentSeconds);
    [NotMapped] public int TotalOriginalEstimateSeconds => Issues.Sum(i => i.OriginalEstimateSeconds);
    [NotMapped] public int TotalRemainingEstimateSeconds => Issues.Sum(i => i.RemainingEstimateSeconds);

    [NotMapped] public int TotalSprintDays => (StartDate.HasValue && EndDate.HasValue)
        ? Math.Max(1, CountWeekdays(StartDate.Value.Date, EndDate.Value.Date))
        : 10; // 2-week sprint = 10 weekdays

    [NotMapped] public int ElapsedDays => StartDate.HasValue
        ? Math.Clamp(CountWeekdays(StartDate.Value.Date, DateTime.Today.AddDays(-1)), 0, TotalSprintDays)
        : 0;

    [NotMapped] public int DaysRemaining => EndDate.HasValue
        ? Math.Max(0, CountWeekdays(DateTime.Today, EndDate.Value.Date))
        : 0;

    // Counts weekdays (Mon–Fri) between two dates, inclusive of both endpoints.
    private static int CountWeekdays(DateTime from, DateTime to)
    {
        int count = 0;
        var d = from.Date;
        while (d <= to.Date)
        {
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                count++;
            d = d.AddDays(1);
        }
        return count;
    }

    [NotMapped] public double SprintProgressPct => (double)ElapsedDays / TotalSprintDays * 100;
    [NotMapped] public double CompletionPct => TotalIssues == 0 ? 0 : (double)DoneCount / TotalIssues * 100;

    [NotMapped] public SprintRisk RiskLevel
    {
        get
        {
            if (TotalIssues == 0) return SprintRisk.OnTrack;
            var gap = (SprintProgressPct - CompletionPct) / 100.0;
            return gap switch
            {
                <= 0.15 => SprintRisk.OnTrack,
                <= 0.35 => SprintRisk.AtRisk,
                _       => SprintRisk.Behind
            };
        }
    }

    [NotMapped] public List<SprintIssue> AtRiskIssues => Issues
        .Where(i => (i.StatusCategoryKey != "done" && i.Status != "In Review") && IsAtRisk(i))
        .ToList();

    // Epics derived from sprint issues (used by delivery dashboard)
    [NotMapped] public List<EpicSummary> EpicSummaries => Issues
        .GroupBy(i => i.EpicKey)
        .Select(g => new EpicSummary
        {
            Key = g.Key,
            Name = g.First().EpicName,
            Issues = g.ToList()
        })
        .OrderBy(e => string.IsNullOrEmpty(e.Key) ? "ZZZ" : e.Key)
        .ToList();

    private bool IsAtRisk(SprintIssue issue)
    {
        if (SprintProgressPct > 50 && issue.StatusCategoryKey == "new")
            return true;
        if (issue.StatusCategoryKey == "indeterminate" && issue.DaysSinceLastWorklog >= 2)
            return true;
        if (DaysRemaining > 0 && issue.RemainingEstimateSeconds > 0 &&
            issue.RemainingEstimateSeconds > DaysRemaining * 6 * 3600)
            return true;
        return false;
    }
}

public class SprintIssue
{
    public int Id { get; set; }

    /// <summary>FK to SprintReport. Null when the issue belongs to an epic report instead.</summary>
    public int? SprintReportId { get; set; }

    /// <summary>FK to JiraEpicReport. Null when the issue belongs to a sprint report instead.</summary>
    public int? JiraEpicReportId { get; set; }

    public string Key { get; set; } = "";
    public string Summary { get; set; } = "";
    public string IssueType { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusCategoryKey { get; set; } = "";
    public string Assignee { get; set; } = "Unassigned";
    public string Priority { get; set; } = "Medium";
    public int? StoryPoints { get; set; }
    public string OriginalEstimate { get; set; } = "-";
    public int OriginalEstimateSeconds { get; set; }
    public string TimeSpent { get; set; } = "-";
    public int TimeSpentSeconds { get; set; }
    public string RemainingEstimate { get; set; } = "-";
    public int RemainingEstimateSeconds { get; set; }
    public string EpicKey { get; set; } = "";
    public string EpicName { get; set; } = "No Epic";
    public string SprintName { get; set; } = "";
    public List<WorklogEntry> Worklogs { get; set; } = new();
    public List<string> Labels { get; set; } = new();
    public DateTime? Created { get; set; }
    public DateTime? ResolutionDate { get; set; }

    [NotMapped] public bool IsExpanded { get; set; }

    [NotMapped] public int DaysSinceLastWorklog => Worklogs.Any()
        ? Math.Max(0, (int)(DateTime.Today - Worklogs.Max(w => w.Started).Date).TotalDays)
        : 999;
}

// ── Jira Saved Filter ─────────────────────────────────────────────────────────

public class JiraFilter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Jql { get; set; } = "";
    public string Description { get; set; } = "";
}

// ── Epic Summary (aggregated from sprint issues) ──────────────────────────────

public class EpicSummary
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";

    [NotMapped] public List<SprintIssue> Issues { get; set; } = new();

    [NotMapped] public int TotalIssues => Issues.Count;
    [NotMapped] public int DoneCount => Issues.Count(i => i.StatusCategoryKey == "done");
    [NotMapped] public int InProgressCount => Issues.Count(i => i.StatusCategoryKey == "indeterminate");
    [NotMapped] public int ToDoCount => Issues.Count(i => i.StatusCategoryKey == "new");

    [NotMapped] public int TotalEstimateSeconds => Issues.Sum(i => i.OriginalEstimateSeconds);
    [NotMapped] public int TotalSpentSeconds => Issues.Sum(i => i.TimeSpentSeconds);
    [NotMapped] public int TotalRemainingSeconds => Issues.Sum(i => i.RemainingEstimateSeconds);

    [NotMapped] public double CompletionPct => TotalIssues == 0 ? 0
        : (double)DoneCount / TotalIssues * 100;

    [NotMapped] public double HoursVariancePct => TotalEstimateSeconds == 0 ? 0
        : (TotalSpentSeconds - TotalEstimateSeconds) / (double)TotalEstimateSeconds * 100;

    [NotMapped] public bool IsOverBudget => TotalEstimateSeconds > 0 && TotalSpentSeconds > TotalEstimateSeconds;

    [NotMapped] public string DisplayName => string.IsNullOrEmpty(Name) || Name == "No Epic"
        ? (string.IsNullOrEmpty(Key) ? "No Epic" : Key)
        : $"{Key} {Name}" ;
}
