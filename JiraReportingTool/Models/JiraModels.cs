namespace JiraReportingTool.Models;

// ── Epic Report ──────────────────────────────────────────────────────────────

public class JiraEpicReport
{
    public string Key { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusCategoryKey { get; set; } = "";
    public string Assignee { get; set; } = "Unassigned";
    public List<JiraIssueModel> Issues { get; set; } = new();

    public int TotalIssues => Issues.Count;
    public int DoneCount => Issues.Count(i => i.StatusCategoryKey == "done");
    public int InProgressCount => Issues.Count(i => i.StatusCategoryKey == "indeterminate");
    public int ToDoCount => Issues.Count(i => i.StatusCategoryKey == "new");
    public int TotalTimeSpentSeconds => Issues.Sum(i => i.TimeSpentSeconds);
    public int TotalOriginalEstimateSeconds => Issues.Sum(i => i.OriginalEstimateSeconds);
}

public class JiraIssueModel
{
    public string Key { get; set; } = "";
    public string Summary { get; set; } = "";
    public string IssueType { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusCategoryKey { get; set; } = ""; // "new" | "indeterminate" | "done"
    public string Assignee { get; set; } = "Unassigned";
    public string OriginalEstimate { get; set; } = "-";
    public int OriginalEstimateSeconds { get; set; }
    public string TimeSpent { get; set; } = "-";
    public int TimeSpentSeconds { get; set; }
    public string RemainingEstimate { get; set; } = "-";
    public List<WorklogEntry> Worklogs { get; set; } = new();
    public bool IsExpanded { get; set; }
}

public class WorklogEntry
{
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
    public string ProjectKey { get; set; } = "";
    public string SprintName { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<SprintIssue> Issues { get; set; } = new();

    // ── Counts ────────────────────────────────────────────────────────────────
    public int TotalIssues => Issues.Count;
    public int DoneCount => Issues.Count(i => i.StatusCategoryKey == "done");
    public int InProgressCount => Issues.Count(i => i.StatusCategoryKey == "indeterminate");
    public int ToDoCount => Issues.Count(i => i.StatusCategoryKey == "new");

    // ── Story points ──────────────────────────────────────────────────────────
    public int TotalStoryPoints => Issues.Sum(i => i.StoryPoints ?? 0);
    public int DoneStoryPoints => Issues.Where(i => i.StatusCategoryKey == "done").Sum(i => i.StoryPoints ?? 0);
    public bool HasStoryPoints => Issues.Any(i => i.StoryPoints.HasValue);

    // ── Time ──────────────────────────────────────────────────────────────────
    public int TotalTimeSpentSeconds => Issues.Sum(i => i.TimeSpentSeconds);
    public int TotalOriginalEstimateSeconds => Issues.Sum(i => i.OriginalEstimateSeconds);

    // ── Sprint timeline ───────────────────────────────────────────────────────
    public int TotalSprintDays => (StartDate.HasValue && EndDate.HasValue)
        ? Math.Max(1, (int)(EndDate.Value.Date - StartDate.Value.Date).TotalDays)
        : 14;

    public int ElapsedDays => StartDate.HasValue
        ? Math.Clamp((int)(DateTime.Today - StartDate.Value.Date).TotalDays, 0, TotalSprintDays)
        : 0;

    public int DaysRemaining => EndDate.HasValue
        ? Math.Max(0, (int)(EndDate.Value.Date - DateTime.Today).TotalDays)
        : 0;

    public double SprintProgressPct => (double)ElapsedDays / TotalSprintDays * 100;
    public double CompletionPct => TotalIssues == 0 ? 0 : (double)DoneCount / TotalIssues * 100;

    // ── Risk ──────────────────────────────────────────────────────────────────
    // Gap between how far through the sprint we are and how much work is done.
    // >15% gap = At Risk, >35% gap = Behind.
    public SprintRisk RiskLevel
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

    // ── At-risk issues ────────────────────────────────────────────────────────
    // Not-done issues that are likely to miss the sprint:
    //   • Still "To Do" when sprint is >50% through
    //   • "In Progress" but no worklog in the last 2 days (stale)
    //   • Remaining estimate > days remaining
    public List<SprintIssue> AtRiskIssues => Issues
        .Where(i => (i.StatusCategoryKey != "done" && i.Status != "In Review")  && IsAtRisk(i))
        .ToList();

    private bool IsAtRisk(SprintIssue issue)
    {
        if (SprintProgressPct > 50 && issue.StatusCategoryKey == "new")
            return true;
        if (issue.StatusCategoryKey == "indeterminate" && issue.DaysSinceLastWorklog >= 2)
            return true;
        if (DaysRemaining > 0 && issue.RemainingEstimateSeconds > 0 &&
            issue.RemainingEstimateSeconds > DaysRemaining * 8 * 3600)
            return true;
        return false;
    }
}

public class SprintIssue
{
    public string Key { get; set; } = "";
    public string Summary { get; set; } = "";
    public string IssueType { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusCategoryKey { get; set; } = ""; // "new" | "indeterminate" | "done"
    public string Assignee { get; set; } = "Unassigned";
    public string Priority { get; set; } = "Medium";
    public int? StoryPoints { get; set; }
    public string OriginalEstimate { get; set; } = "-";
    public int OriginalEstimateSeconds { get; set; }
    public string TimeSpent { get; set; } = "-";
    public int TimeSpentSeconds { get; set; }
    public string RemainingEstimate { get; set; } = "-";
    public int RemainingEstimateSeconds { get; set; }
    public List<WorklogEntry> Worklogs { get; set; } = new();
    public bool IsExpanded { get; set; }

    public int DaysSinceLastWorklog => Worklogs.Any()
        ? Math.Max(0, (int)(DateTime.Today - Worklogs.Max(w => w.Started).Date).TotalDays)
        : 999;
}
