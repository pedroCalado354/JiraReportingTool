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

    public int TotalIssues => Issues.Count;
    public int DoneCount => Issues.Count(i => i.StatusCategoryKey == "done");
    public int InProgressCount => Issues.Count(i => i.StatusCategoryKey == "indeterminate");
    public int ToDoCount => Issues.Count(i => i.StatusCategoryKey == "new");

    public int TotalStoryPoints => Issues.Sum(i => i.StoryPoints ?? 0);
    public int DoneStoryPoints => Issues.Where(i => i.StatusCategoryKey == "done").Sum(i => i.StoryPoints ?? 0);
    public bool HasStoryPoints => Issues.Any(i => i.StoryPoints.HasValue);

    public int TotalTimeSpentSeconds => Issues.Sum(i => i.TimeSpentSeconds);
    public int TotalOriginalEstimateSeconds => Issues.Sum(i => i.OriginalEstimateSeconds);
    public int TotalRemainingEstimateSeconds => Issues.Sum(i => i.RemainingEstimateSeconds);

    public int TotalSprintDays => (StartDate.HasValue && EndDate.HasValue)
        ? Math.Max(1, CountWeekdays(StartDate.Value.Date, EndDate.Value.Date))
        : 10; // 2-week sprint = 10 weekdays

    public int ElapsedDays => StartDate.HasValue
        ? Math.Clamp(CountWeekdays(StartDate.Value.Date, DateTime.Today.AddDays(-1)), 0, TotalSprintDays)
        : 0;

    public int DaysRemaining => EndDate.HasValue
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

    public double SprintProgressPct => (double)ElapsedDays / TotalSprintDays * 100;
    public double CompletionPct => TotalIssues == 0 ? 0 : (double)DoneCount / TotalIssues * 100;

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

    public List<SprintIssue> AtRiskIssues => Issues
        .Where(i => (i.StatusCategoryKey != "done" && i.Status != "In Review") && IsAtRisk(i))
        .ToList();

    // Epics derived from sprint issues (used by delivery dashboard)
    public List<EpicSummary> EpicSummaries => Issues
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
    public List<WorklogEntry> Worklogs { get; set; } = new();
    public bool IsExpanded { get; set; }
    public DateTime? Created { get; set; }
    public DateTime? ResolutionDate { get; set; }

    public int DaysSinceLastWorklog => Worklogs.Any()
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
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public List<SprintIssue> Issues { get; set; } = new();

    public int TotalIssues => Issues.Count;
    public int DoneCount => Issues.Count(i => i.StatusCategoryKey == "done");
    public int InProgressCount => Issues.Count(i => i.StatusCategoryKey == "indeterminate");
    public int ToDoCount => Issues.Count(i => i.StatusCategoryKey == "new");

    public int TotalEstimateSeconds => Issues.Sum(i => i.OriginalEstimateSeconds);
    public int TotalSpentSeconds => Issues.Sum(i => i.TimeSpentSeconds);
    public int TotalRemainingSeconds => Issues.Sum(i => i.RemainingEstimateSeconds);

    public double CompletionPct => TotalIssues == 0 ? 0
        : (double)DoneCount / TotalIssues * 100;

    public double HoursVariancePct => TotalEstimateSeconds == 0 ? 0
        : (TotalSpentSeconds - TotalEstimateSeconds) / (double)TotalEstimateSeconds * 100;

    public bool IsOverBudget => TotalEstimateSeconds > 0 && TotalSpentSeconds > TotalEstimateSeconds;

    public string DisplayName => string.IsNullOrEmpty(Name) || Name == "No Epic"
        ? (string.IsNullOrEmpty(Key) ? "No Epic" : Key)
        : $"{Key} {Name}" ;
}
