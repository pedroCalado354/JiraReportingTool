using System.ComponentModel.DataAnnotations;

// ─────────────────────────────────────────────────────────────────────────────
// Sprint Plan persistence models
//
// Three tables hang off SprintPlanHeader via FK → cascade-delete:
//   SprintPlanAllocation — one row per (task, member, day)
//   SprintPlanCustomTask — non-Jira tasks created on the board
//   SprintPlanHoliday    — bank holiday dates
//   SprintPlanTimeOff    — individual member time-off days
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Top-level sprint plan record.</summary>
public class SprintPlanHeader
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = "";

    public DateOnly SprintStart { get; set; }
    public DateOnly SprintEnd   { get; set; }

    /// <summary>Comma-separated epic keys — re-loaded from Jira cache when the plan is opened.</summary>
    [MaxLength(2000)]
    public string LoadedEpicKeys { get; set; } = "";

    /// <summary>Comma-separated individual Jira issue keys loaded directly (not via an epic) — re-fetched when the plan is opened.</summary>
    [MaxLength(4000)]
    public string LoadedTaskKeys { get; set; } = "";

    /// <summary>Comma-separated support-bug epic keys — bugs from these epics populate the Support Bug Time Logged column.</summary>
    [MaxLength(2000)]
    public string SupportBugEpicKeys { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<SprintPlanAllocation> Allocations { get; set; } = new();
    public List<SprintPlanCustomTask> CustomTasks  { get; set; } = new();
    public List<SprintPlanHoliday>   Holidays      { get; set; } = new();
    public List<SprintPlanTimeOff>   TimeOffs      { get; set; } = new();
}

/// <summary>Per-day task allocation within a sprint plan.</summary>
public class SprintPlanAllocation
{
    public int Id           { get; set; }
    public int SprintPlanId { get; set; }

    /// <summary>Positive = Jira task ID; negative = custom task local ID.</summary>
    public int TaskId { get; set; }

    [MaxLength(500)]
    public string TaskLabel { get; set; } = "";   // cached display label

    [MaxLength(50)]
    public string EpicKey { get; set; } = "";

    public Guid MemberId { get; set; }

    [MaxLength(100)]
    public string MemberName { get; set; } = "";  // stable fallback for matching on load

    public DateOnly Date           { get; set; }
    public decimal  HoursAllocated { get; set; }
    public Guid     GroupId        { get; set; }
}

/// <summary>Custom (non-Jira) task saved with a sprint plan.</summary>
public class SprintPlanCustomTask
{
    public int Id           { get; set; }
    public int SprintPlanId { get; set; }

    /// <summary>Negative integer — local ID used in the page's in-memory dictionary.</summary>
    public int LocalId { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(100)]
    public string Category { get; set; } = "";

    public decimal Hours { get; set; }

    [MaxLength(20)]
    public string Color { get; set; } = "";

    /// <summary>Jira epic key for "Feature Bug" tasks — drives the bug-time report. Empty otherwise.</summary>
    [MaxLength(50)]
    public string EpicKey { get; set; } = "";
}

/// <summary>Bank holiday date associated with a sprint plan.</summary>
public class SprintPlanHoliday
{
    public int      Id           { get; set; }
    public int      SprintPlanId { get; set; }
    public DateOnly Date         { get; set; }
}

/// <summary>Member-specific time-off day associated with a sprint plan.</summary>
public class SprintPlanTimeOff
{
    public int      Id           { get; set; }
    public int      SprintPlanId { get; set; }
    public Guid     MemberId     { get; set; }
    public DateOnly Date         { get; set; }
}

/// <summary>Point-in-time snapshot of a sprint plan, created on each save of an existing plan.</summary>
public class SprintPlanVersion
{
    public int      Id            { get; set; }
    public int      SprintPlanId  { get; set; }
    public int      VersionNumber { get; set; }
    public DateTime SavedAt       { get; set; } = DateTime.UtcNow;

    [MaxLength(200)]
    public string Label    { get; set; } = "";

    /// <summary>JSON-serialized SprintPlanHeader (data-only, no circular nav properties).</summary>
    public string DataJson { get; set; } = "";
}

// ── Sprint / Epic configuration — drives default inputs on Delivery & Support Bugs pages ──

/// <summary>
/// One row per sprint. Stores the Jira sprint ID, support-bugs epic key, and dates.
/// The row whose date range contains today is treated as the "current" config and
/// auto-filled into the Delivery and Support Bugs dashboards on load.
/// </summary>
public class SprintConfig
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";

    public int SprintId { get; set; }

    [MaxLength(50)]
    public string EpicKey { get; set; } = "";

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate   { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A team member available for sprint allocation. Editable via the Team Roster admin page
/// (/team-roster) so onboarding/offboarding and capacity changes no longer require a code change.
/// </summary>
public class RosterMember
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string Team { get; set; } = "";

    /// <summary>Daily capacity in hours (default 6). Lower for part-timers or heavy-meeting roles.</summary>
    public decimal HoursPerDay { get; set; } = 6m;

    /// <summary>Inactive members are hidden from the planning board but kept for historical plans.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Controls display order within a team on the board.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Jira accountId (e.g. "712020:e9a577e7-…"). When set, worklog/assignee matching uses
    /// this exact id instead of the fragile display-name heuristic. Most reliable identifier.
    /// </summary>
    [MaxLength(100)]
    public string? JiraAccountId { get; set; }

    /// <summary>
    /// Jira account email. Used to match worklog authors when no accountId is set and Jira
    /// exposes the author's email. Easier to populate than accountId, but privacy-dependent.
    /// </summary>
    [MaxLength(200)]
    public string? JiraEmail { get; set; }

    /// <summary>
    /// Per-page inclusion flags — layered on top of <see cref="Active"/>. An inactive member is
    /// hidden everywhere regardless of these; an active member can still be selectively excluded
    /// from a given page's roster-driven capacity/board membership (e.g. a new hire counted for
    /// Sprint Planning capacity before they're ramped up enough to show on the exec dashboards).
    /// </summary>
    public bool IncludeInDeliveryReports { get; set; } = true;

    /// <summary>Counts toward roster-driven capacity on Delivery — Command Center (/delivery-v3).</summary>
    public bool IncludeInCommandCenter { get; set; } = true;

    /// <summary>Appears as a plannable resource/row and counts toward capacity on Sprint Planning (/sprint-planning).</summary>
    public bool IncludeInSprintPlanning { get; set; } = true;
}

/// <summary>
/// A custom-task category available on the Sprint Planning board, managed on the
/// Sprint Planning Config page. It defines only the category + colour; the board
/// supplies the per-task name, hours, and (for Feature Bug) the epic key.
/// </summary>
public class CustomTaskTemplate
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Category { get; set; } = "Meeting";

    [MaxLength(20)]
    public string Color { get; set; } = "#64748b";

    /// <summary>Inactive categories are hidden from the board picker but kept for reference.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Controls display order in the picker and config list.</summary>
    public int SortOrder { get; set; }
}

/// <summary>A bank/public holiday shared across all sprint plans (managed on the Team Roster page).</summary>
public class SharedHoliday
{
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";
}

/// <summary>
/// A vacation/time-off range for a roster member, managed on the Team Vacations page.
/// Reduces that member's contribution to the sprint capacity shown on the Team Delivery
/// dashboards for whatever weekdays overlap the current sprint window.
/// </summary>
public class RosterVacation
{
    public int Id { get; set; }

    public int RosterMemberId { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    [MaxLength(200)]
    public string? Note { get; set; }
}

/// <summary>Audit log entry created each time a task assignment is removed from the calendar during a sprint.</summary>
public class SprintPlanRemovalLog
{
    public int  Id           { get; set; }
    public int  SprintPlanId { get; set; }

    /// <summary>GroupId of the removed allocation set — links back to the original block.</summary>
    public Guid GroupId { get; set; }

    [MaxLength(500)]
    public string TaskLabel  { get; set; } = "";

    [MaxLength(100)]
    public string MemberName { get; set; } = "";

    [MaxLength(1000)]
    public string Reason     { get; set; } = "";

    public DateTime RemovedAt { get; set; } = DateTime.UtcNow;
}
