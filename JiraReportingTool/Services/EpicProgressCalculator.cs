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
    int Done,
    int InProgress,
    int ToDo,
    double CompletionPct         // Done / Total × 100
);

/// <summary>
/// Effort (hours) tracking for the epic.
/// <see cref="IsApproxRemaining"/> is true when the source model (JiraIssueModel)
/// does not expose an explicit RemainingEstimateSeconds and the value is
/// approximated as max(0, Estimate − Spent).
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

/// <summary>Bug / quality health for the epic.</summary>
public sealed record EpicQualityMetrics(
    int TotalBugs,
    int OpenBugs,                // Total − Done
    int DoneBugs,
    double BugFixRate,           // Done / Total × 100
    double BugRatio,             // TotalBugs / AllIssues × 100
    int UnassignedBugs,
    int UnestimatedBugs,
    int StaleBugs                // In Progress bug + no worklog in last 2 days
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
    SprintScopedMetrics? Sprint
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
        var issues = epic.Issues ?? new List<JiraIssueModel>();
        return new EpicProgressResult(
            EpicKey:  epic.Key,
            Delivery: ComputeDelivery(issues),
            Effort:   ComputeEffort(issues),
            Risk:     ComputeRisk(issues),
            Quality:  ComputeQuality(issues),
            Sprint:   null
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
        var epicIssues = epic.Issues ?? new List<JiraIssueModel>();
        return new EpicProgressResult(
            EpicKey:  epic.Key,
            Delivery: ComputeDelivery(epicIssues),
            Effort:   ComputeEffort(epicIssues),
            Risk:     ComputeRisk(epicIssues),
            Quality:  ComputeQuality(epicIssues),
            Sprint:   ComputeSprint(epic, sprint)
        );
    }

    // ── Delivery ─────────────────────────────────────────────────────────────

    private static EpicDeliveryMetrics ComputeDelivery(List<JiraIssueModel> issues)
    {
        var total      = issues.Count;
        var done       = issues.Count(i => Cat(i.StatusCategoryKey) == "done");
        var inProgress = issues.Count(i => Cat(i.StatusCategoryKey) == "indeterminate");
        var todo       = Math.Max(0, total - done - inProgress);
        var pct        = SafePct(done, total);
        return new(total, done, inProgress, todo, pct);
    }

    // ── Effort ────────────────────────────────────────────────────────────────

    private static EpicEffortMetrics ComputeEffort(List<JiraIssueModel> issues)
    {
        var totalEst  = issues.Sum(i => (long)i.OriginalEstimateSeconds);
        var spent     = issues.Sum(i => (long)i.TimeSpentSeconds);
        // JiraIssueModel has no RemainingEstimateSeconds → approximate
        var remaining = issues.Sum(i => Math.Max(0L, (long)i.OriginalEstimateSeconds - (long)i.TimeSpentSeconds));
        var pct       = SafePct(spent, totalEst);
        return new(totalEst, spent, remaining, pct, IsApproxRemaining: true);
    }

    // ── Risk ──────────────────────────────────────────────────────────────────

    private static EpicRiskMetrics ComputeRisk(List<JiraIssueModel> issues)
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

    private static EpicQualityMetrics ComputeQuality(List<JiraIssueModel> issues)
    {
        var bugs = issues
            .Where(i => string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var now  = DateTime.UtcNow;
        var total = bugs.Count;
        var done  = bugs.Count(i => Cat(i.StatusCategoryKey) == "done");
        return new(
            TotalBugs:       total,
            OpenBugs:        total - done,
            DoneBugs:        done,
            BugFixRate:      SafePct(done, total),
            BugRatio:        SafePct(total, issues.Count),
            UnassignedBugs:  bugs.Count(i =>
                                 string.IsNullOrWhiteSpace(i.Assignee) ||
                                 string.Equals(i.Assignee, "Unassigned", StringComparison.OrdinalIgnoreCase)),
            UnestimatedBugs: bugs.Count(i => i.OriginalEstimateSeconds == 0),
            StaleBugs:       bugs.Count(i =>
                                 Cat(i.StatusCategoryKey) == "indeterminate" &&
                                 IsStale(i.Worklogs, now))
        );
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
        var remaining  = si.Sum(i => (long)i.RemainingEstimateSeconds);

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
        if (sec <= 0) return "0h";
        var h = sec / 3600;
        var m = (sec % 3600) / 60;
        return m > 0 ? $"{h}h {m}m" : $"{h}h";
    }

    public static string FormatPct(double pct) => $"{pct:F1}%";
}
