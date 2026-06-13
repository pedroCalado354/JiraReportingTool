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
