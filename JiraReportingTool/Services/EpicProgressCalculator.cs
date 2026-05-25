using JiraReportingTool.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Epic Progress Calculator
//
// Pure, deterministic metric computation.
// No DI, no side effects — pass data in, get metrics out.
// Every public method is static so it can be unit-tested without a Blazor host.
//
// Design trade-off: metrics are re-computed on every render cycle rather than
// cached, which keeps the data contract simple at the cost of a few microseconds
// of LINQ per render. For the data sizes involved (dozens of issues) this is
// negligible; lift to a lazy-computed property if sprint sizes grow.
// ─────────────────────────────────────────────────────────────────────────────

// ── Output records ────────────────────────────────────────────────────────────

/// <summary>Task-count based delivery health.</summary>
public sealed record EpicDeliveryMetrics(
    int Total,
    int TotalTasks,
    int TotalBugs,
    int Done,
    int InProgress,
    int ToDo,
    double CompletionPct         // Done / Total × 100
);

/// <summary>
/// Effort (hours) tracking for the epic.
/// <see cref="IsApproxRemaining"/> is always false — ComputedRemainingSeconds comes directly from Jira.
/// </summary>
public sealed record EpicEffortMetrics(
    long TotalEstimateSec,
    long SpentSec,
    long RemainingEstSec,
    double EffortPct,            // Spent / Estimate × 100
    bool IsApproxRemaining
);

/// <summary>Risk signals that need attention.</summary>
public sealed record EpicRiskMetrics(
    int Unestimated,             // OriginalEstimateSeconds == 0
    int Unassigned,              // no Assignee
    int Overlogged,              // TimeSpent > OriginalEstimate (and estimate > 0)
    int StaleInProgress          // In Progress + no worklog in last 2 days
);

/// <summary>Per-assignee task breakdown row (all issue types).</summary>
public sealed record TaskAssigneeRow(
    string Assignee,
    int    Total,
    int    Done,
    int    InProgress,
    int    ToDo,
    double CompletionPct,              // Done / Total × 100
    double EpicSharePct,               // Total / AllIssues × 100
    long   TotalSpentSec,              // time logged across all their issues
    long   TotalOriginalEstimateSec,   // sum of original estimates
    long   TotalRemainingEstimateSec   // sum of remaining estimates
);

/// <summary>Per-assignee bug breakdown row.</summary>
public sealed record BugAssigneeRow(
    string Assignee,
    int    BugCount,
    int    TotalTasks,
    double BugRate,              // BugCount / TotalTasks × 100
    long   BugSpentSec,          // time logged on this assignee's bugs
    long   TotalSpentSec,        // time logged on all this assignee's issues
    double BugTimeSharePct       // BugSpentSec / TotalSpentSec × 100
);

/// <summary>Bug / quality health for the epic.</summary>
public sealed record EpicQualityMetrics(
    int TotalBugs,
    int OpenBugs,                // Total − Done
    int DoneBugs,
    double BugFixRate,           // Done / Total × 100
    double BugRatio,             // TotalBugs / AllIssues × 100
    int UnassignedBugs,
    int UnestimatedBugs,
    int StaleBugs,               // In Progress bug + no worklog in last 2 days
    long BugSpentSec,            // total time logged on all bugs
    long AvgBugFixTimeSec,       // avg time logged on fixed (done) bugs
    double BugOverhead,          // BugHours / FeatureHours × 100
    IReadOnlyList<BugAssigneeRow> ByAssignee
);

/// <summary>
/// Sprint-scoped overlay — only present when a sprint filter is active.
/// All values are scoped to the sprint; <see cref="EpicContributionPct"/> relates
/// sprint-done tasks back to the epic's total task count for context.
/// </summary>
public sealed record SprintScopedMetrics(
    int SprintId,
    string SprintName,
    DateTime? SprintStart,
    DateTime? SprintEnd,
    int Total,
    int Done,
    int InProgress,
    int ToDo,
    double CompletionPct,        // Sprint Done / Sprint Total × 100
    long EstimateSec,
    long SpentSec,
    long RemainingEstSec,
    double EffortPct,            // Sprint Spent / Sprint Estimate × 100
    double EpicContributionPct   // Sprint Done / Epic Total × 100
);

/// <summary>
/// Full result object — one value per metric category.
/// <see cref="Sprint"/> is null when no sprint filter is applied.
/// </summary>
public sealed record EpicProgressResult(
    string EpicKey,
    EpicDeliveryMetrics Delivery,
    EpicEffortMetrics Effort,
    EpicRiskMetrics Risk,
    EpicQualityMetrics Quality,
    SprintScopedMetrics? Sprint,
    IReadOnlyList<TaskAssigneeRow> AssigneeBreakdown
);

// ── Calculator ────────────────────────────────────────────────────────────────

public static class EpicProgressCalculator
{
    // ── Entry points ──────────────────────────────────────────────────────────

    /// <summary>
    /// Compute metrics for an epic without a sprint filter.
    /// All metrics are derived from <see cref="JiraEpicReport.Issues"/>.
    /// </summary>
    public static EpicProgressResult Compute(JiraEpicReport epic)
    {
        var issues = epic.Issues ?? new List<SprintIssue>();
        return new EpicProgressResult(
            EpicKey:            epic.Key,
            Delivery:           ComputeDelivery(issues),
            Effort:             ComputeEffort(issues),
            Risk:               ComputeRisk(issues),
            Quality:            ComputeQuality(issues),
            Sprint:             null,
            AssigneeBreakdown:  ComputeAssigneeBreakdown(issues)
        );
    }

    /// <summary>
    /// Compute metrics with an active sprint filter.
    /// Epic-wide metrics (Delivery, Effort, Risk) still come from the full epic
    /// so the user can compare sprint scope against the whole.
    /// Sprint-scoped metrics come from <see cref="SprintReport.Issues"/>.
    /// </summary>
    public static EpicProgressResult Compute(JiraEpicReport epic, SprintReport sprint)
    {
        var epicIssues = epic.Issues ?? new List<SprintIssue>();
        return new EpicProgressResult(
            EpicKey:            epic.Key,
            Delivery:           ComputeDelivery(epicIssues),
            Effort:             ComputeEffort(epicIssues),
            Risk:               ComputeRisk(epicIssues),
            Quality:            ComputeQuality(epicIssues),
            Sprint:             ComputeSprint(epic, sprint),
            AssigneeBreakdown:  ComputeAssigneeBreakdown(epicIssues)
        );
    }

    // ── Delivery ─────────────────────────────────────────────────────────────

    private static EpicDeliveryMetrics ComputeDelivery(List<SprintIssue> issues)
    {
        var total      = issues.Count;
        var totalTasks = issues.Count(f=> f.IssueType == "Task" );
        var totalBugs = issues.Count(f=> f.IssueType == "Bug" );
        var done       = issues.Count(i => Cat(i.StatusCategoryKey) == "done");
        var inProgress = issues.Count(i => Cat(i.StatusCategoryKey) == "indeterminate");
        var todo       = Math.Max(0, total - done - inProgress);
        var pct        = SafePct(done, total);
        return new(total, totalTasks, totalBugs, done, inProgress, todo, pct);
    }

    // ── Effort ────────────────────────────────────────────────────────────────

    private static EpicEffortMetrics ComputeEffort(List<SprintIssue> issues)
    {
        var totalEst  = issues.Sum(i => (long)i.OriginalEstimateSeconds);
        var spent     = issues.Sum(i => (long)i.TimeSpentSeconds);
        var remaining = issues.Sum(i => (long)i.ComputedRemainingSeconds);
        var pct       = SafePct(spent, totalEst);
        return new(totalEst, spent, remaining, pct, IsApproxRemaining: false);
    }

    // ── Risk ──────────────────────────────────────────────────────────────────

    private static EpicRiskMetrics ComputeRisk(List<SprintIssue> issues)
    {
        var now = DateTime.UtcNow;
        return new(
            Unestimated:    issues.Count(i => i.OriginalEstimateSeconds == 0),
            Unassigned:     issues.Count(i => string.IsNullOrWhiteSpace(i.Assignee)),
            Overlogged:     issues.Count(i =>
                                i.OriginalEstimateSeconds > 0 &&
                                i.TimeSpentSeconds > i.OriginalEstimateSeconds),
            StaleInProgress: issues.Count(i =>
                                Cat(i.StatusCategoryKey) == "indeterminate" &&
                                IsStale(i.Worklogs, now))
        );
    }

    // ── Quality ───────────────────────────────────────────────────────────────

    private static EpicQualityMetrics ComputeQuality(List<SprintIssue> issues)
    {
        var bugs = issues
            .Where(i => string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var now   = DateTime.UtcNow;
        var total = bugs.Count;
        var done  = bugs.Count(i => Cat(i.StatusCategoryKey) == "done");

        // Time spent on bugs
        var bugSpentSec = bugs.Sum(i => (long)i.TimeSpentSeconds);

        // Avg fix time — only fixed bugs that have any time logged
        var fixedWithTime = bugs
            .Where(i => Cat(i.StatusCategoryKey) == "done" && i.TimeSpentSeconds > 0)
            .ToList();
        var avgBugFixTimeSec = fixedWithTime.Count > 0
            ? (long)fixedWithTime.Average(i => (double)i.TimeSpentSeconds)
            : 0L;

        // Bug overhead: bug hours as a percentage of non-bug (feature) hours
        var featureSpentSec = issues
            .Where(i => !string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase))
            .Sum(i => (long)i.TimeSpentSeconds);
        var bugOverhead = featureSpentSec > 0
            ? Math.Round(bugSpentSec * 100.0 / featureSpentSec, 1)
            : 0.0;

        // Bug rate per assignee — only people who have at least one bug
        var byAssignee = issues
            .Where(i => !string.IsNullOrWhiteSpace(i.Assignee) &&
                        !string.Equals(i.Assignee, "Unassigned", StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Assignee, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var assigneeBugs = g
                    .Where(i => string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var bugSec   = assigneeBugs.Sum(i => (long)i.TimeSpentSeconds);
                var totalSec = g.Sum(i => (long)i.TimeSpentSeconds);
                return new BugAssigneeRow(
                    Assignee:        g.Key,
                    BugCount:        assigneeBugs.Count,
                    TotalTasks:      g.Count(),
                    BugRate:         SafePct(assigneeBugs.Count, g.Count()),
                    BugSpentSec:     bugSec,
                    TotalSpentSec:   totalSec,
                    BugTimeSharePct: SafePct(bugSec, totalSec)
                );
            })
            .Where(r => r.BugCount > 0)
            .OrderByDescending(r => r.BugCount)
            .ToList();

        return new(
            TotalBugs:        total,
            OpenBugs:         total - done,
            DoneBugs:         done,
            BugFixRate:       SafePct(done, total),
            BugRatio:         SafePct(total, issues.Count),
            UnassignedBugs:   bugs.Count(i =>
                                  string.IsNullOrWhiteSpace(i.Assignee) ||
                                  string.Equals(i.Assignee, "Unassigned", StringComparison.OrdinalIgnoreCase)),
            UnestimatedBugs:  bugs.Count(i => i.OriginalEstimateSeconds == 0),
            StaleBugs:        bugs.Count(i =>
                                  Cat(i.StatusCategoryKey) == "indeterminate" &&
                                  IsStale(i.Worklogs, now)),
            BugSpentSec:      bugSpentSec,
            AvgBugFixTimeSec: avgBugFixTimeSec,
            BugOverhead:      bugOverhead,
            ByAssignee:       byAssignee
        );
    }

    // ── Assignee breakdown ────────────────────────────────────────────────────

    private static IReadOnlyList<TaskAssigneeRow> ComputeAssigneeBreakdown(List<SprintIssue> issues)
    {
        var epicTotal = issues.Count;
        return issues
            .Where(i => !string.IsNullOrWhiteSpace(i.Assignee) &&
                        !string.Equals(i.Assignee, "Unassigned", StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Assignee, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var list       = g.ToList();
                var done       = list.Count(i => Cat(i.StatusCategoryKey) == "done");
                var inProgress = list.Count(i => Cat(i.StatusCategoryKey) == "indeterminate");
                var todo       = Math.Max(0, list.Count - done - inProgress);
                return new TaskAssigneeRow(
                    Assignee:                   g.Key,
                    Total:                      list.Count,
                    Done:                       done,
                    InProgress:                 inProgress,
                    ToDo:                       todo,
                    CompletionPct:              SafePct(done, list.Count),
                    EpicSharePct:               SafePct(list.Count, epicTotal),
                    TotalSpentSec:              list.Sum(i => (long)i.TimeSpentSeconds),
                    TotalOriginalEstimateSec:   list.Sum(i => (long)i.OriginalEstimateSeconds),
                    TotalRemainingEstimateSec:  list
                        .Where(i => Cat(i.StatusCategoryKey) != "done" &&
                                    !string.Equals(i.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
                        .Sum(i => (long)i.ComputedRemainingSeconds)
                );
            })
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    // ── Sprint ────────────────────────────────────────────────────────────────

    private static SprintScopedMetrics ComputeSprint(JiraEpicReport epic, SprintReport sprint)
    {
        var si        = sprint.Issues ?? new List<SprintIssue>();
        var epicTotal = epic.Issues?.Count ?? 0;

        var total      = si.Count;
        var done       = si.Count(i => Cat(i.StatusCategoryKey) == "done");
        var inProgress = si.Count(i => Cat(i.StatusCategoryKey) == "indeterminate");
        var todo       = Math.Max(0, total - done - inProgress);
        var est        = si.Sum(i => (long)i.OriginalEstimateSeconds);
        var spent      = si.Sum(i => (long)i.TimeSpentSeconds);
        var remaining  = si.Sum(i => (long)i.ComputedRemainingSeconds);

        return new(
            SprintId:             sprint.JiraSprintId ?? 0,
            SprintName:           sprint.SprintName ?? "Sprint",
            SprintStart:          sprint.StartDate,
            SprintEnd:            sprint.EndDate,
            Total:                total,
            Done:                 done,
            InProgress:           inProgress,
            ToDo:                 todo,
            CompletionPct:        SafePct(done, total),
            EstimateSec:          est,
            SpentSec:             spent,
            RemainingEstSec:      remaining,
            EffortPct:            SafePct(spent, est),
            EpicContributionPct:  SafePct(done, epicTotal)
        );
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static string Cat(string? key) =>
        (key ?? "new").Trim().ToLowerInvariant();

    private static double SafePct(long numerator, long denominator) =>
        denominator > 0 ? Math.Round(numerator * 100.0 / denominator, 1) : 0.0;

    private static double SafePct(int numerator, int denominator) =>
        denominator > 0 ? Math.Round(numerator * 100.0 / denominator, 1) : 0.0;

    private static bool IsStale(List<WorklogEntry>? logs, DateTime now)
    {
        if (logs is null || logs.Count == 0) return true;
        var last = logs.Max(w => w.Started);
        return (now - last).TotalDays > 2;
    }

    // ── Public formatting helpers (used by UI, tested the same as metric logic) ──

    public static string FormatSeconds(long sec)
    {
        if (sec == 0) return "0h";
        var abs = Math.Abs(sec);
        var h = abs / 3600;
        var m = (abs % 3600) / 60;
        var s = m > 0 ? $"{h}h {m}m" : $"{h}h";
        return sec < 0 ? $"-{s}" : s;
    }

    public static string FormatPct(double pct) => $"{pct:F1}%";
}
