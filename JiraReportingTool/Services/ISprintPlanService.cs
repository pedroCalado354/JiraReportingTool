public interface ISprintPlanService
{
    Task<List<SprintPlanHeader>> GetAllAsync();
    Task<SprintPlanHeader?>      GetAsync(int id);

    /// <summary>
    /// Create (Id == 0) or full-replace (Id > 0) a sprint plan.
    /// Child rows are always deleted and re-inserted on update.
    /// </summary>
    Task<SprintPlanHeader> SaveAsync(SprintPlanHeader plan);

    /// <summary>
    /// Upsert keyed by <see cref="SprintPlanHeader.Name"/>.
    /// If a plan with the same name already exists, it is updated in-place.
    /// Otherwise a new plan is created.
    /// </summary>
    Task<SprintPlanHeader> UpsertByNameAsync(SprintPlanHeader plan);

    Task DeleteAsync(int id);

    /// <summary>Returns the plan's last-updated timestamp (header only) for concurrency checks, or null if it no longer exists.</summary>
    Task<DateTime?> GetUpdatedAtAsync(int id);

    /// <summary>Returns all version snapshots for a plan, newest first.</summary>
    Task<List<SprintPlanVersion>> GetVersionsAsync(int planId);

    /// <summary>Persists a single removal-log entry for an in-progress plan.</summary>
    Task<SprintPlanRemovalLog> LogRemovalAsync(SprintPlanRemovalLog log);

    /// <summary>Returns all removal log entries for a plan, newest first.</summary>
    Task<List<SprintPlanRemovalLog>> GetRemovalLogsAsync(int planId);
}
