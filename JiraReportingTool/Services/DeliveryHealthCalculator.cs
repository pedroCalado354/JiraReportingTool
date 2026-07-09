using JiraReportingTool.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Delivery Health Calculator
//
// Mirrors the "Sprint Health Score" KPI formulas defined inline on
// TeamDeliveryV3.razor (the Delivery Work / All Issue Types gauge + KPI grid).
// Extracted so DeliveryComparison.razor can show the same numbers per sprint
// without depending on that page's UI state (roster overrides, tab selection,
// etc). Pure, deterministic — pass a SprintReport in, get metrics out.
//
// If the formulas on TeamDeliveryV3.razor change, mirror the change here too;
// there is no shared code path between the two by design (extracting one would
// have meant refactoring the primary daily-use dashboard to prove parity, which
// carries more regression risk than keeping these in sync manually).
// ─────────────────────────────────────────────────────────────────────────────

public sealed record DeliveryHeaviestMember(string Name, double RemainingHours);

public sealed record DeliveryHealthMetrics(
    int Score,
    string Band,           // success | warn | danger
    string Message,
    double TimeElapsedPct,
    double CompletionPct,
    bool CompletionBehind,
    int TotalCount,
    int DoneCount,
    int InProgressCount,
    int ToDoCount,
    double DoneHours,
    double DoneAndInReviewHours,
    double PlannedHours,
    double FullSprintCapacityHours,
    bool PlanOverCommit,
    int EstimatedCount,
    int UnestimatedCount,
    double CapacityHours,
    double RemainingHours,
    bool OverCapacity,
    double HoursLoggedPerWorkday,
    double TotalLoggedHours,
    double ProjectedFinishHours,
    double SprintWindowLoggedHours,
    double SprintWindowDoneLoggedHours,
    double SprintWindowDoneReviewLoggedHours,
    int IdleActiveCount,
    int StaleCount,
    double ThroughputPerDay,
    double NeededThroughputPerDay,
    bool ThroughputBehind,
    double WorkloadImbalancePct,
    DeliveryHeaviestMember? HeaviestMember,
    double AvgRemainingPerMember,
    double EstimationCoveragePct,
    int UnassignedCount
);

public static class DeliveryHealthCalculator
{
    // Same exclusion as TeamDeliveryV3's IsExcludedFromMetrics: Product issue type and
    // Documentation/Discovery workflow statuses don't represent delivery work.
    public static bool IsExcludedFromMetrics(SprintIssue i) =>
        string.Equals(i.IssueType, "Product",       StringComparison.OrdinalIgnoreCase) ||
        string.Equals(i.Status,    "Documentation", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(i.Status,    "Discovery",     StringComparison.OrdinalIgnoreCase);

    // Same population as TeamDeliveryV3's ActiveIssues: excludes Epics, support tickets,
    // and the exclusions above.
    public static List<SprintIssue> ActiveIssues(SprintReport report) => report.Issues
        .Where(i => !string.Equals(i.IssueType, "Epic", StringComparison.OrdinalIgnoreCase)
                 && !i.Key.Contains("JSSUPPORT", StringComparison.OrdinalIgnoreCase)
                 && !IsExcludedFromMetrics(i))
        .ToList();

    public static bool IsDeliveryType(SprintIssue i) =>
        string.IsNullOrWhiteSpace(i.IssueType) || string.Equals(i.IssueType, "Task", StringComparison.OrdinalIgnoreCase);

    public static DeliveryHealthMetrics ComputeAllIssueTypes(SprintReport report, int effectiveDaysRemaining,
        List<RosterMember>? roster = null, List<RosterVacation>? vacations = null)
        => Compute(report, ActiveIssues(report), effectiveDaysRemaining, isTaskOnly: false, roster ?? [], vacations ?? []);

    public static DeliveryHealthMetrics ComputeDeliveryWork(SprintReport report, int effectiveDaysRemaining,
        List<RosterMember>? roster = null, List<RosterVacation>? vacations = null)
    {
        var taskOnly = ActiveIssues(report).Where(IsDeliveryType).ToList();
        return Compute(report, taskOnly, effectiveDaysRemaining, isTaskOnly: true, roster ?? [], vacations ?? []);
    }

    // Same formulas as ComputeAllIssueTypes/ComputeDeliveryWork, but against a caller-supplied
    // (already filtered — e.g. by Product) issue list rather than deriving it from the report.
    public static DeliveryHealthMetrics ComputeForIssues(SprintReport report, List<SprintIssue> issues, int effectiveDaysRemaining, bool isTaskOnly,
        List<RosterMember>? roster = null, List<RosterVacation>? vacations = null)
        => Compute(report, issues, effectiveDaysRemaining, isTaskOnly, roster ?? [], vacations ?? []);

    // "JS Project[Radio Buttons]" field — Rentway Legacy / Rentway Pro / Integrations / etc.
    // Mirrors WorkingHoursV2's ProductOf so the same "—" fallback is used for unset values.
    public const string NoProductLabel = "—";
    public static string ProductOf(SprintIssue i) => string.IsNullOrWhiteSpace(i.Product) ? NoProductLabel : i.Product;

    // Hours logged within the sprint's date window only — the same convention as the KPI grid's
    // "Logged Hours" (Compute()'s sprintWindowLoggedHours), so hour totals shown elsewhere on the
    // page (e.g. Bug vs Feature / Bug Origin) reconcile with it instead of pulling in an issue's
    // full historical TimeSpentSeconds (which can include time logged in earlier sprints).
    public static double SprintWindowHours(IEnumerable<SprintIssue> issues, DateTime? start, DateTime? end) =>
        issues.SelectMany(i => i.Worklogs)
              .Where(w => start == null || w.Started.Date >= start.Value.Date)
              .Where(w => end   == null || w.Started.Date <= end.Value.Date)
              .Sum(w => (long)w.TimeSpentSeconds) / 3600.0;

    // ── Bug vs Feature split: signals firefighting (bugs) vs building (everything else) ──
    public sealed record BugFeatureSplit(int BugCount, int FeatureCount, double BugHours, double FeatureHours, double BugPct);

    public static BugFeatureSplit ComputeBugFeatureSplit(SprintReport report, List<SprintIssue> issues)
    {
        var bugs = issues.Where(i => string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase)).ToList();
        var features = issues.Where(i => !string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase)).ToList();
        var bugHours = SprintWindowHours(bugs, report.StartDate, report.EndDate);
        var featureHours = SprintWindowHours(features, report.StartDate, report.EndDate);
        var totalHours = bugHours + featureHours;
        return new BugFeatureSplit(
            BugCount: bugs.Count,
            FeatureCount: features.Count,
            BugHours: bugHours,
            FeatureHours: featureHours,
            BugPct: totalHours <= 0 ? 0 : Math.Round(bugHours * 100.0 / totalHours, 1));
    }

    // ── Bug origin: of the bugs above, how many trace back to a customer-reported JSSUPPORT
    // ticket vs were found during feature work. Same "linked to JSSUPPORT" definition as the
    // Support Bugs page's "JSSUPPORT Linked" tab and Support Trends (LinkedIssueKeys requires
    // the "issuelinks" field to have been requested) — but unlike those pages, not scoped to
    // any specific epic; every bug in the passed-in issue list is considered.
    public sealed record BugOriginSplit(int SupportLinkedCount, int FeatureFoundCount, double SupportLinkedHours, double FeatureFoundHours, double SupportLinkedPct);

    public static bool IsSupportLinkedBug(SprintIssue i) =>
        i.Key.Contains("JSSUPPORT", StringComparison.OrdinalIgnoreCase) ||
        i.LinkedIssueKeys.Any(k => k.StartsWith("JSSUPPORT", StringComparison.OrdinalIgnoreCase));

    public static BugOriginSplit ComputeBugOriginSplit(SprintReport report, List<SprintIssue> issues)
    {
        var bugs = issues.Where(i => string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase)).ToList();
        var supportLinked = bugs.Where(IsSupportLinkedBug).ToList();
        var featureFound = bugs.Where(i => !IsSupportLinkedBug(i)).ToList();
        var supportHours = SprintWindowHours(supportLinked, report.StartDate, report.EndDate);
        var featureHours = SprintWindowHours(featureFound, report.StartDate, report.EndDate);
        return new BugOriginSplit(
            SupportLinkedCount: supportLinked.Count,
            FeatureFoundCount: featureFound.Count,
            SupportLinkedHours: supportHours,
            FeatureFoundHours: featureHours,
            SupportLinkedPct: bugs.Count == 0 ? 0 : Math.Round(supportLinked.Count * 100.0 / bugs.Count, 1));
    }

    // ── Scope comparison: initial sprint scope vs work added after the sprint started ──
    public sealed record ScopeSplit(int Total, int Done, int Active, int ToDo, double CompletionPct);
    public sealed record ScopeComparison(ScopeSplit Initial, ScopeSplit Added);

    public static ScopeComparison ComputeScopeComparison(SprintReport report, List<SprintIssue> scopedIssues)
    {
        var start = report.StartDate?.Date;
        var added = start is null ? [] : scopedIssues.Where(i => i.Created.HasValue && i.Created.Value.Date >= start.Value).ToList();
        var initial = start is null ? scopedIssues : scopedIssues.Where(i => !(i.Created.HasValue && i.Created.Value.Date >= start.Value)).ToList();
        return new ScopeComparison(ScopeStatFor(initial), ScopeStatFor(added));
    }

    private static ScopeSplit ScopeStatFor(List<SprintIssue> src)
    {
        var total = src.Count;
        var done = src.Count(i => i.StatusCategoryKey == "done");
        return new ScopeSplit(
            Total: total,
            Done: done,
            Active: src.Count(i => i.StatusCategoryKey == "indeterminate"),
            ToDo: src.Count(i => i.StatusCategoryKey == "new"),
            CompletionPct: total == 0 ? 0 : Math.Round(done * 100.0 / total, 1));
    }

    // ── Time series: day-by-day burndown (remaining estimate) and hours logged per day ──
    public static List<(DateTime Date, double RemainingHours)> ComputeBurndown(SprintReport report, List<SprintIssue> scopedIssues)
    {
        var start = report.StartDate?.Date;
        var end = report.EndDate?.Date;
        if (start is null || end is null) return [];

        var totalHours = scopedIssues.Sum(i => i.OriginalEstimateSeconds) / 3600.0;
        if (totalHours == 0) totalHours = scopedIssues.Count;

        var loggedByDay = new Dictionary<DateTime, double>();
        foreach (var w in scopedIssues.SelectMany(i => i.Worklogs))
        {
            var d = w.Started.Date;
            loggedByDay[d] = loggedByDay.GetValueOrDefault(d) + w.TimeSpentSeconds / 3600.0;
        }

        var cap = end.Value < DateTime.Today ? end.Value : DateTime.Today;
        var points = new List<(DateTime, double)>();
        var remaining = totalHours;
        for (var d = start.Value; d <= end.Value; d = d.AddDays(1))
        {
            if (loggedByDay.TryGetValue(d, out var logged)) remaining -= logged;
            if (d <= cap) points.Add((d, Math.Max(0, remaining)));
        }
        return points;
    }

    // Ideal burndown: straight-line trajectory from total scoped hours (sprint start) to zero
    // (sprint end), for every calendar day in the sprint — unlike ComputeBurndown, this is not
    // capped at "today" so the full reference line renders even for days still to come.
    public static List<(DateTime Date, double IdealRemainingHours)> ComputeIdealBurndown(SprintReport report, List<SprintIssue> scopedIssues)
    {
        var start = report.StartDate?.Date;
        var end = report.EndDate?.Date;
        if (start is null || end is null) return [];

        var totalHours = scopedIssues.Sum(i => i.OriginalEstimateSeconds) / 3600.0;
        if (totalHours == 0) totalHours = scopedIssues.Count;

        var totalDays = (end.Value - start.Value).Days;
        var points = new List<(DateTime, double)>();
        for (var d = start.Value; d <= end.Value; d = d.AddDays(1))
        {
            var elapsedDays = (d - start.Value).Days;
            var idealRemaining = totalDays <= 0 ? 0 : totalHours * (1 - (double)elapsedDays / totalDays);
            points.Add((d, Math.Max(0, idealRemaining)));
        }
        return points;
    }

    public static List<(DateTime Date, double Hours)> ComputeHoursPerDay(SprintReport report, List<SprintIssue> scopedIssues)
    {
        var start = report.StartDate?.Date;
        var end = report.EndDate?.Date;

        var worklogs = scopedIssues.SelectMany(i => i.Worklogs).AsEnumerable();
        if (start is not null && end is not null)
            worklogs = worklogs.Where(w => w.Started.Date >= start.Value && w.Started.Date <= end.Value);

        return worklogs
            .GroupBy(w => w.Started.Date)
            .Select(g => (Date: g.Key, Hours: g.Sum(w => w.TimeSpentSeconds) / 3600.0))
            .OrderBy(x => x.Date)
            .ToList();
    }

    // Counts weekdays (Mon-Fri) between two dates, inclusive of both endpoints.
    private static int CountWeekdays(DateTime from, DateTime to)
    {
        var count = 0;
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                count++;
        return count;
    }

    // Hours subtracted from roster capacity for booked vacation days (/team-vacations)
    // that overlap the given range — mirrors TeamDeliveryV3's VacationHoursInRange.
    private static double VacationHoursInRange(DateTime? rangeStart, DateTime? rangeEnd, List<RosterMember> roster, List<RosterVacation> vacations)
    {
        if (rangeStart is null || rangeEnd is null || vacations.Count == 0) return 0;
        var from = rangeStart.Value.Date;
        var to = rangeEnd.Value.Date;
        if (to < from) return 0;

        double total = 0;
        foreach (var v in vacations)
        {
            var member = roster.FirstOrDefault(m => m.Id == v.RosterMemberId);
            if (member is null) continue;

            var vs = v.StartDate.ToDateTime(TimeOnly.MinValue);
            var ve = v.EndDate.ToDateTime(TimeOnly.MinValue);
            var s = vs > from ? vs : from;
            var e = ve < to ? ve : to;
            if (s > e) continue;

            total += CountWeekdays(s, e) * (double)member.HoursPerDay;
        }
        return total;
    }

    private static DeliveryHealthMetrics Compute(SprintReport report, List<SprintIssue> issues, int effectiveDaysRemaining, bool isTaskOnly,
        List<RosterMember> roster, List<RosterVacation> vacations)
    {
        var doneCount       = issues.Count(i => i.StatusCategoryKey == "done");
        var inProgressCount = issues.Count(i => i.StatusCategoryKey == "indeterminate");
        var toDoCount       = issues.Count(i => i.StatusCategoryKey == "new");
        var completionPct   = issues.Count == 0 ? 0 : doneCount * 100.0 / issues.Count;
        var completionBehind = report.SprintProgressPct - completionPct > 12;

        var doneHours = issues.Where(i => i.StatusCategoryKey == "done").Sum(i => i.TimeSpentSeconds) / 3600.0;
        var doneAndInReviewHours = issues
            .Where(i => i.StatusCategoryKey == "done" || string.Equals(i.Status, "In Review", StringComparison.OrdinalIgnoreCase))
            .Sum(i => i.TimeSpentSeconds) / 3600.0;

        var plannedHours     = issues.Sum(i => i.OriginalEstimateSeconds) / 3600.0;
        var estimatedCount   = issues.Count(i => i.OriginalEstimateSeconds > 0);
        var unestimatedCount = issues.Count(i => i.OriginalEstimateSeconds == 0 && i.StatusCategoryKey != "done");

        var totalRemainingHours = issues.Where(i => i.StatusCategoryKey != "done").Sum(i => i.ComputedRemainingSeconds) / 3600.0;

        double rosterHoursPerDay;
        if (roster.Count > 0) rosterHoursPerDay = (double)roster.Sum(m => m.HoursPerDay);
        else
        {
            var contributors = Math.Max(1, issues.Where(i => i.Assignee != "Unassigned").Select(i => i.Assignee).Distinct().Count());
            rosterHoursPerDay = 6.0 * contributors;
        }

        var capacityHours = Math.Max(0, effectiveDaysRemaining * rosterHoursPerDay
            - VacationHoursInRange(DateTime.Today, report.EndDate, roster, vacations));
        var fullSprintCapacityHours = Math.Max(0, report.TotalSprintDays * rosterHoursPerDay
            - VacationHoursInRange(report.StartDate, report.EndDate, roster, vacations));
        var overCapacity = totalRemainingHours > capacityHours;
        var planOverCommit = plannedHours > fullSprintCapacityHours;

        var totalLoggedHours = issues.Sum(i => i.TimeSpentSeconds) / 3600.0;
        var elapsed = Math.Max(1, report.ElapsedDays);
        var hoursLoggedPerWorkday = totalLoggedHours / elapsed;
        var projectedFinishHours = hoursLoggedPerWorkday * report.TotalSprintDays;

        var sprintWindowLoggedHours = SprintWindowHours(issues, report.StartDate, report.EndDate);
        var sprintWindowDoneLoggedHours = SprintWindowHours(issues.Where(i => i.StatusCategoryKey == "done"), report.StartDate, report.EndDate);
        var sprintWindowDoneReviewLoggedHours = SprintWindowHours(issues.Where(i =>
            i.StatusCategoryKey == "done" || string.Equals(i.Status, "In Review", StringComparison.OrdinalIgnoreCase)), report.StartDate, report.EndDate);

        var idleActiveCount = issues.Count(i => i.StatusCategoryKey == "indeterminate" && i.DaysSinceLastWorklog >= 1);
        var staleCount      = issues.Count(i => i.StatusCategoryKey == "indeterminate" && i.DaysSinceLastWorklog >= 2);

        var throughputPerDay = report.ElapsedDays <= 0 ? 0 : doneCount / (double)report.ElapsedDays;
        var neededThroughputPerDay = effectiveDaysRemaining <= 0 ? 0 : (issues.Count - doneCount) / (double)effectiveDaysRemaining;
        var throughputBehind = neededThroughputPerDay > 0 && throughputPerDay < neededThroughputPerDay * 0.9;

        var remainingPerMember = issues
            .Where(i => i.StatusCategoryKey != "done" && !string.IsNullOrWhiteSpace(i.Assignee) && i.Assignee != "Unassigned")
            .GroupBy(i => i.Assignee)
            .Select(g => (Name: g.Key, RemainingHours: g.Sum(x => x.ComputedRemainingSeconds) / 3600.0))
            .Where(x => x.RemainingHours > 0)
            .ToList();
        var avgRemainingPerMember = remainingPerMember.Count == 0 ? 0 : remainingPerMember.Average(x => x.RemainingHours);
        DeliveryHeaviestMember? heaviest = remainingPerMember.Count == 0 ? null
            : remainingPerMember
                .OrderByDescending(x => x.RemainingHours)
                .Select(x => new DeliveryHeaviestMember(x.Name, x.RemainingHours))
                .First();
        var workloadImbalancePct = remainingPerMember.Count == 0 || avgRemainingPerMember <= 0 ? 0
            : (remainingPerMember.Max(x => x.RemainingHours) - avgRemainingPerMember) / avgRemainingPerMember * 100.0;

        var openForEstimation = issues.Where(i => i.StatusCategoryKey != "done").ToList();
        var estimationCoveragePct = openForEstimation.Count == 0 ? 100
            : openForEstimation.Count(i => i.OriginalEstimateSeconds > 0) * 100.0 / openForEstimation.Count;

        var unassignedCount = issues.Count(i =>
            string.IsNullOrWhiteSpace(i.Assignee) || i.Assignee.Equals("Unassigned", StringComparison.OrdinalIgnoreCase));

        // ── Health score: weighted composite (pace 30 / capacity 25 / plan-load 15 / risk 15 / 5th 15) ──
        double pace;
        if (report.SprintProgressPct < 5 && completionPct < 5) pace = 0.5;
        else
        {
            var gap = report.SprintProgressPct - completionPct;
            pace = 1.0 - Math.Clamp(gap / 30.0, 0, 1);
        }

        double capacityFactor;
        if (totalRemainingHours <= 0) capacityFactor = 1.0;
        else if (capacityHours <= 0) capacityFactor = 0.0;
        else capacityFactor = Math.Min(1.0, capacityHours / totalRemainingHours);

        var planLoad = plannedHours <= 0 ? 0.5 : fullSprintCapacityHours <= 0 ? 0.0 : Math.Min(1.0, fullSprintCapacityHours / plannedHours);

        // AtRiskIssues is only filtered by IsExcludedFromMetrics (not the Epic/JSSUPPORT
        // exclusion) on TeamDeliveryV3 — reproduced exactly here for parity.
        var atRiskScoped = report.AtRiskIssues.Where(i => !IsExcludedFromMetrics(i)).ToList();

        double risk;
        double fifthFactor;
        string message;
        string band;
        int score;

        if (isTaskOnly)
        {
            var taskAtRisk = atRiskScoped.Count(IsDeliveryType);
            risk = 1.0 - Math.Min(1.0, taskAtRisk / (double)Math.Max(1, issues.Count) * 2.0);
            fifthFactor = 1.0 - Math.Min(1.0, staleCount / (double)Math.Max(1, inProgressCount)); // execution
        }
        else
        {
            risk = 1.0 - Math.Min(1.0, atRiskScoped.Count / (double)Math.Max(1, issues.Count) * 2.0);
            var openBugsCount = issues.Count(i => string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase) && i.StatusCategoryKey != "done");
            fifthFactor = 1.0 - Math.Min(1.0, openBugsCount / (double)Math.Max(1, issues.Count) * 2.0); // quality
        }

        if (issues.Count == 0)
        {
            score = 0;
        }
        else
        {
            var rawScore = pace * 30 + capacityFactor * 25 + planLoad * 15 + risk * 15 + fifthFactor * 15;
            score = Math.Clamp((int)Math.Round(rawScore), 0, 100);
        }

        band = score >= 75 ? "success" : score >= 50 ? "warn" : "danger";

        message = isTaskOnly
            ? score switch
            {
                >= 85 => "Task delivery excellent — pace, capacity & execution aligned.",
                >= 75 => "Healthy task delivery — minor risks only.",
                >= 60 => "Task pace on track — watch stalled items.",
                >= 40 => "Task delivery at risk — re-balance or de-scope.",
                _     => "Critical — tasks are well behind the sprint pace."
            }
            : score switch
            {
                >= 85 => "Sprint is performing exceptionally — pace, capacity and quality all aligned.",
                >= 75 => "Healthy sprint — minor risks but trajectory is solid.",
                >= 60 => "On track but watch the pacing — small adjustments will help.",
                >= 40 => "At risk — consider de-scoping or reallocating work to recover.",
                _     => "Critical — significant gap between commitment and current pace."
            };

        if (planOverCommit)
            message += $" Planned work exceeds full-sprint capacity by {(plannedHours - fullSprintCapacityHours):F0}h.";

        return new DeliveryHealthMetrics(
            Score: score,
            Band: band,
            Message: message,
            TimeElapsedPct: report.SprintProgressPct,
            CompletionPct: completionPct,
            CompletionBehind: completionBehind,
            TotalCount: issues.Count,
            DoneCount: doneCount,
            InProgressCount: inProgressCount,
            ToDoCount: toDoCount,
            DoneHours: doneHours,
            DoneAndInReviewHours: doneAndInReviewHours,
            PlannedHours: plannedHours,
            FullSprintCapacityHours: fullSprintCapacityHours,
            PlanOverCommit: planOverCommit,
            EstimatedCount: estimatedCount,
            UnestimatedCount: unestimatedCount,
            CapacityHours: capacityHours,
            RemainingHours: totalRemainingHours,
            OverCapacity: overCapacity,
            HoursLoggedPerWorkday: hoursLoggedPerWorkday,
            TotalLoggedHours: totalLoggedHours,
            ProjectedFinishHours: projectedFinishHours,
            SprintWindowLoggedHours: sprintWindowLoggedHours,
            SprintWindowDoneLoggedHours: sprintWindowDoneLoggedHours,
            SprintWindowDoneReviewLoggedHours: sprintWindowDoneReviewLoggedHours,
            IdleActiveCount: idleActiveCount,
            StaleCount: staleCount,
            ThroughputPerDay: throughputPerDay,
            NeededThroughputPerDay: neededThroughputPerDay,
            ThroughputBehind: throughputBehind,
            WorkloadImbalancePct: workloadImbalancePct,
            HeaviestMember: heaviest,
            AvgRemainingPerMember: avgRemainingPerMember,
            EstimationCoveragePct: estimationCoveragePct,
            UnassignedCount: unassignedCount
        );
    }
}
