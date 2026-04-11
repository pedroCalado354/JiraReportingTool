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
}
