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
    // and the exclusions above. Exposed on a raw issue list too (not just a SprintReport's
    // Issues) so callers that fetch issues outside of a single sprint's report — e.g. a
    // per-product, date-ranged fetch — can apply the same delivery-scoping rule.
    public static List<SprintIssue> FilterActive(IEnumerable<SprintIssue> issues) => issues
        .Where(i => !string.Equals(i.IssueType, "Epic", StringComparison.OrdinalIgnoreCase)
                 && !i.Key.Contains("JSSUPPORT", StringComparison.OrdinalIgnoreCase)
                 && !IsExcludedFromMetrics(i))
        .ToList();

    public static List<SprintIssue> ActiveIssues(SprintReport report) => FilterActive(report.Issues);

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
    // "Bug" here means Bug or Redesign work (a Redesign is corrective/rework, not new build);
    // "Feature / Task" is Task issue type only — anything else (e.g. Story) counts in neither
    // bucket rather than being folded into "Feature".
    public sealed record BugFeatureSplit(int BugCount, int FeatureCount, double BugHours, double FeatureHours, double BugPct);

    public static bool IsBugLike(SprintIssue i) =>
        string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(i.IssueType, "Redesign", StringComparison.OrdinalIgnoreCase);

    public static BugFeatureSplit ComputeBugFeatureSplit(SprintReport report, List<SprintIssue> issues)
    {
        var bugs = issues.Where(IsBugLike).ToList();
        var features = issues.Where(IsDeliveryType).ToList();
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
        var bugs = issues.Where(IsBugLike).ToList();
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

    // ── Issues by epic: which epics are generating the load, closed this sprint vs still open.
    // Generic over whatever issue population the caller passes in (bugs, features, a JSSUPPORT-
    // linked subset, ...) — not bug-specific despite living next to the bug metrics below.
    public sealed record EpicBreakdownRow(
        string EpicKey, string EpicName, int ClosedCount, int OpenCount, double HoursLogged,
        double ClosedHoursLogged, double OpenHoursLogged)
    {
        // Average hours logged (sprint window) per issue in each bucket — how much effort
        // closed issues typically took to conclude vs how much open ones have absorbed so far.
        public double AvgEffortClosed => ClosedCount == 0 ? 0 : Math.Round(ClosedHoursLogged / ClosedCount, 1);
        public double AvgEffortOpen   => OpenCount == 0   ? 0 : Math.Round(OpenHoursLogged / OpenCount, 1);
    }

    public static List<EpicBreakdownRow> ComputeIssuesByEpic(SprintReport report, List<SprintIssue> issues) => issues
        .GroupBy(i => (i.EpicKey, EpicName: string.IsNullOrEmpty(i.EpicName) ? "No Epic" : i.EpicName))
        .Select(g =>
        {
            var closed = g.Where(i => i.StatusCategoryKey == "done").ToList();
            var open   = g.Where(i => i.StatusCategoryKey != "done").ToList();
            return new EpicBreakdownRow(
                EpicKey: g.Key.EpicKey,
                EpicName: g.Key.EpicName,
                ClosedCount: closed.Count,
                OpenCount: open.Count,
                HoursLogged: SprintWindowHours(g, report.StartDate, report.EndDate),
                ClosedHoursLogged: SprintWindowHours(closed, report.StartDate, report.EndDate),
                OpenHoursLogged: SprintWindowHours(open, report.StartDate, report.EndDate));
        })
        .OrderByDescending(r => r.ClosedCount + r.OpenCount)
        .ToList();

    // ── Estimation accuracy: planned (original estimate) vs actual (all-time logged) hours per
    // issue — one point per issue, feeds the Estimation Accuracy scatter. A point above the
    // y = x diagonal means the issue is taking (or took) more effort than estimated.
    public sealed record EstimationPoint(string Key, double PlannedHours, double ActualHours);

    public static List<EstimationPoint> ComputeEstimationPoints(List<SprintIssue> issues) => issues
        .Where(i => i.OriginalEstimateSeconds > 0 || i.TimeSpentSeconds > 0)
        .Select(i => new EstimationPoint(i.Key, i.OriginalEstimateSeconds / 3600.0, i.TimeSpentSeconds / 3600.0))
        .ToList();

    // Daily throughput (issues resolved that day) — feeds the Velocity KPI's sparkline. An issue
    // whose done status has no resolutiondate can't be placed on a specific day, so it's simply
    // omitted from the trend (it still counts in the Velocity KPI's own totals elsewhere).
    public static List<double> ComputeVelocitySparkline(SprintReport report, List<SprintIssue> issues)
    {
        var start = report.StartDate?.Date;
        var end = report.EndDate?.Date;
        if (start is null || end is null) return [];
        var cap = end.Value < DateTime.Today ? end.Value : DateTime.Today;

        var doneByDay = issues
            .Where(i => i.StatusCategoryKey == "done" && i.ResolutionDate.HasValue)
            .GroupBy(i => i.ResolutionDate!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var points = new List<double>();
        for (var d = start.Value; d <= cap; d = d.AddDays(1))
            points.Add(doneByDay.TryGetValue(d, out var c) ? c : 0);
        return points;
    }

    // Average hours logged (sprint window) per closed issue — how much effort it typically
    // takes to conclude one (e.g. a closed bug/redesign).
    public static double AvgHoursToConclude(SprintReport report, List<SprintIssue> issues)
    {
        var closed = issues.Where(i => i.StatusCategoryKey == "done").ToList();
        if (closed.Count == 0) return 0;
        return Math.Round(SprintWindowHours(closed, report.StartDate, report.EndDate) / closed.Count, 1);
    }

    // Average hours logged (sprint window) per still-open issue — how much effort is already
    // sunk into the ones that haven't concluded yet, as a counterpart to AvgHoursToConclude.
    public static double AvgHoursOngoing(SprintReport report, List<SprintIssue> issues)
    {
        var open = issues.Where(i => i.StatusCategoryKey != "done").ToList();
        if (open.Count == 0) return 0;
        return Math.Round(SprintWindowHours(open, report.StartDate, report.EndDate) / open.Count, 1);
    }

    public sealed record PriorityCount(string Priority, int Count);

    public static List<PriorityCount> ComputePriorityBreakdown(List<SprintIssue> issues) => issues
        .GroupBy(i => string.IsNullOrWhiteSpace(i.Priority) ? "Unspecified" : i.Priority)
        .Select(g => new PriorityCount(g.Key, g.Count()))
        .OrderByDescending(p => p.Count)
        .ToList();

    public sealed record BugTypeSplit(int BugTypeCount, int RedesignTypeCount, int BugTypeClosedCount, int RedesignTypeClosedCount);

    public static BugTypeSplit ComputeBugVsRedesignSplit(List<SprintIssue> bugs) => new(
        BugTypeCount: bugs.Count(i => string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase)),
        RedesignTypeCount: bugs.Count(i => string.Equals(i.IssueType, "Redesign", StringComparison.OrdinalIgnoreCase)),
        BugTypeClosedCount: bugs.Count(i => string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase) && i.StatusCategoryKey == "done"),
        RedesignTypeClosedCount: bugs.Count(i => string.Equals(i.IssueType, "Redesign", StringComparison.OrdinalIgnoreCase) && i.StatusCategoryKey == "done"));

    // ── Origin-row summary for the Bug Origin donut (JSSUPPORT-linked / Feature-found):
    // how many of this origin's bugs are closed, and how much time went into each issue type.
    public sealed record OriginTypeBreakdown(int ClosedCount, double BugHours, double RedesignHours);

    public static OriginTypeBreakdown ComputeOriginTypeBreakdown(SprintReport report, List<SprintIssue> issues) => new(
        ClosedCount: issues.Count(i => i.StatusCategoryKey == "done"),
        BugHours: SprintWindowHours(issues.Where(i => string.Equals(i.IssueType, "Bug", StringComparison.OrdinalIgnoreCase)), report.StartDate, report.EndDate),
        RedesignHours: SprintWindowHours(issues.Where(i => string.Equals(i.IssueType, "Redesign", StringComparison.OrdinalIgnoreCase)), report.StartDate, report.EndDate));

    // ── Scope added: work added to the sprint after it started (Created on/after sprint start) ──
    public sealed record ScopeAddedTypeCount(string IssueType, int Count);

    public sealed record ScopeAddedStats(
        int AddedCount, int TotalCount, double PctOfSprintScope,
        double AddedScopeHours, double TimeLoggedHours,
        List<ScopeAddedTypeCount> TypeBreakdown);

    public static ScopeAddedStats ComputeScopeAddedStats(SprintReport report, List<SprintIssue> scopedIssues)
    {
        var start = report.StartDate?.Date;
        var added = start is null ? [] : scopedIssues.Where(i => i.Created.HasValue && i.Created.Value.Date >= start.Value).ToList();
        var typeBreakdown = added
            .GroupBy(i => string.IsNullOrWhiteSpace(i.IssueType) ? "Unspecified" : i.IssueType)
            .Select(g => new ScopeAddedTypeCount(g.Key, g.Count()))
            .OrderByDescending(t => t.Count)
            .ToList();

        return new ScopeAddedStats(
            AddedCount: added.Count,
            TotalCount: scopedIssues.Count,
            PctOfSprintScope: scopedIssues.Count == 0 ? 0 : Math.Round(added.Count * 100.0 / scopedIssues.Count, 0),
            AddedScopeHours: added.Sum(i => i.OriginalEstimateSeconds) / 3600.0,
            TimeLoggedHours: SprintWindowHours(added, report.StartDate, report.EndDate),
            TypeBreakdown: typeBreakdown);
    }

    // ── Epic breadth: how many epics the team actively logged time on this sprint, what share
    // of everyone who logged time worked on at least one of them (vs. only non-epic work like
    // support tickets, meetings, or admin), and how spread out individuals are across epics —
    // Focused (1 epic), Spread (2 epics), Critical (3+ epics — heavy context-switching).
    public sealed record EpicBreadthStats(
        int EpicCount, int ContributorsOnEpics, int TotalContributors, double PctOfContributors,
        int FocusedContributors, int SpreadContributors, int CriticalContributors,
        double PctFocused, double PctSpread, double PctCritical, double AvgEpicsPerContributor);

    public static EpicBreadthStats ComputeEpicBreadthStats(SprintReport report, List<SprintIssue> issues)
    {
        bool LoggedInWindow(WorklogEntry w) =>
            (report.StartDate == null || w.Started.Date >= report.StartDate.Value.Date) &&
            (report.EndDate   == null || w.Started.Date <= report.EndDate.Value.Date);

        var epicIssues = issues.Where(i => !string.IsNullOrEmpty(i.EpicKey) && i.Worklogs.Any(LoggedInWindow)).ToList();
        var epicCount = epicIssues.Select(i => i.EpicKey).Distinct().Count();

        static int DistinctAuthors(IEnumerable<SprintIssue> src, Func<WorklogEntry, bool> filter) => src
            .SelectMany(i => i.Worklogs.Where(filter))
            .Select(w => w.Author)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var contributorsOnEpics = DistinctAuthors(epicIssues, LoggedInWindow);
        var totalContributors = DistinctAuthors(issues, LoggedInWindow);

        // Distinct epic count per contributor who touched at least one epic — measures focus
        // (one epic all sprint) vs context-switching (spread across several).
        var epicsPerContributor = epicIssues
            .SelectMany(i => i.Worklogs.Where(LoggedInWindow).Select(w => (w.Author, i.EpicKey)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Author))
            .GroupBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Select(x => x.EpicKey).Distinct().Count())
            .ToList();

        var focusedContributors  = epicsPerContributor.Count(c => c == 1);
        var spreadContributors   = epicsPerContributor.Count(c => c == 2);
        var criticalContributors = epicsPerContributor.Count(c => c >= 3);
        var avgEpicsPerContributor = epicsPerContributor.Count == 0 ? 0 : epicsPerContributor.Average();

        return new EpicBreadthStats(
            EpicCount: epicCount,
            ContributorsOnEpics: contributorsOnEpics,
            TotalContributors: totalContributors,
            PctOfContributors: totalContributors == 0 ? 0 : Math.Round(contributorsOnEpics * 100.0 / totalContributors, 0),
            FocusedContributors: focusedContributors,
            SpreadContributors: spreadContributors,
            CriticalContributors: criticalContributors,
            PctFocused: totalContributors == 0 ? 0 : Math.Round(focusedContributors * 100.0 / totalContributors, 0),
            PctSpread: totalContributors == 0 ? 0 : Math.Round(spreadContributors * 100.0 / totalContributors, 0),
            PctCritical: totalContributors == 0 ? 0 : Math.Round(criticalContributors * 100.0 / totalContributors, 0),
            AvgEpicsPerContributor: Math.Round(avgEpicsPerContributor, 1));
    }

    // ── Time series: day-by-day burndown (remaining estimate) and hours logged per day ──
    // Scope-based burndown: remaining = total estimated scope minus the estimate of whatever
    // has actually resolved by that day. Deliberately NOT "total estimate minus cumulative hours
    // logged" — that conflates hours worked with scope completed, so heavy logging on unfinished
    // or unestimated issues (a common case — see Estimation Coverage) drains the pool to zero
    // long before the backlog is actually cleared, producing a burndown that craters in the
    // first couple of days no matter how much work is actually left open.
    public static List<(DateTime Date, double RemainingHours)> ComputeBurndown(SprintReport report, List<SprintIssue> scopedIssues)
    {
        var start = report.StartDate?.Date;
        var end = report.EndDate?.Date;
        if (start is null || end is null) return [];

        var totalEstimateHours = scopedIssues.Sum(i => i.OriginalEstimateSeconds) / 3600.0;
        var useCountFallback = totalEstimateHours == 0;

        var cap = end.Value < DateTime.Today ? end.Value : DateTime.Today;

        // An issue with StatusCategoryKey == "done" but no resolutiondate (rare, workflow-
        // dependent) can only be confirmed done as of "today" — treat it as still remaining on
        // every earlier plotted day rather than guessing when it actually closed.
        bool IsDoneByDay(SprintIssue i, DateTime d) => i.StatusCategoryKey == "done" &&
            (i.ResolutionDate.HasValue ? i.ResolutionDate.Value.Date <= d : d >= cap);

        // Per-issue remaining as of day d: estimate minus whatever had been logged against it
        // by that day, clamped at zero — same convention as SprintIssue.ComputedRemainingSeconds
        // (the "Remaining" KPI), just tracked day-by-day instead of only for "today". This is
        // what makes the chart's last point line up with that KPI: a still-open issue with
        // partial logged hours counts as partially done here too, not fully remaining.
        double RemainingFor(SprintIssue i, DateTime d)
        {
            if (IsDoneByDay(i, d)) return 0;
            if (useCountFallback) return 1;
            if (i.OriginalEstimateSeconds == 0) return 0; // unestimated — 0 remaining, matching ComputedRemainingSeconds
            var loggedByDay = i.Worklogs.Where(w => w.Started.Date <= d).Sum(w => (long)w.TimeSpentSeconds) / 3600.0;
            return Math.Max(0, i.OriginalEstimateSeconds / 3600.0 - loggedByDay);
        }

        var points = new List<(DateTime, double)>();
        for (var d = start.Value; d <= cap; d = d.AddDays(1))
            points.Add((d, scopedIssues.Sum(i => RemainingFor(i, d))));
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

    // Count-based burndown: how many issues (not hours) are still open as of each day —
    // complements ComputeBurndown's hours view for teams that want to see tasks draining down
    // regardless of how unevenly estimated they are.
    public static List<(DateTime Date, int RemainingCount)> ComputeTaskCountBurndown(SprintReport report, List<SprintIssue> scopedIssues)
    {
        var start = report.StartDate?.Date;
        var end = report.EndDate?.Date;
        if (start is null || end is null) return [];

        var cap = end.Value < DateTime.Today ? end.Value : DateTime.Today;

        bool IsDoneByDay(SprintIssue i, DateTime d) => i.StatusCategoryKey == "done" &&
            (i.ResolutionDate.HasValue ? i.ResolutionDate.Value.Date <= d : d >= cap);

        var points = new List<(DateTime, int)>();
        for (var d = start.Value; d <= cap; d = d.AddDays(1))
            points.Add((d, scopedIssues.Count(i => !IsDoneByDay(i, d))));
        return points;
    }

    // Ideal task-count burndown: straight-line trajectory from total scoped issue count (sprint
    // start) to zero (sprint end) — mirrors ComputeIdealBurndown but in issue counts.
    public static List<(DateTime Date, double IdealRemainingCount)> ComputeIdealTaskCountBurndown(SprintReport report, List<SprintIssue> scopedIssues)
    {
        var start = report.StartDate?.Date;
        var end = report.EndDate?.Date;
        if (start is null || end is null) return [];

        var totalCount = scopedIssues.Count;
        var totalDays = (end.Value - start.Value).Days;
        var points = new List<(DateTime, double)>();
        for (var d = start.Value; d <= end.Value; d = d.AddDays(1))
        {
            var elapsedDays = (d - start.Value).Days;
            var idealRemaining = totalDays <= 0 ? 0 : totalCount * (1 - (double)elapsedDays / totalDays);
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

    // Same worklog population as ComputeHoursPerDay, grouped by worklog author instead of by
    // day — feeds the "hours logged per person" drilldown chart.
    public static List<(string Person, double Hours)> ComputeHoursPerPerson(SprintReport report, List<SprintIssue> scopedIssues)
    {
        var start = report.StartDate?.Date;
        var end = report.EndDate?.Date;

        var worklogs = scopedIssues.SelectMany(i => i.Worklogs).AsEnumerable();
        if (start is not null && end is not null)
            worklogs = worklogs.Where(w => w.Started.Date >= start.Value && w.Started.Date <= end.Value);

        return worklogs
            .Where(w => !string.IsNullOrWhiteSpace(w.Author))
            .GroupBy(w => w.Author)
            .Select(g => (Person: g.Key, Hours: g.Sum(w => w.TimeSpentSeconds) / 3600.0))
            .OrderByDescending(x => x.Hours)
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
