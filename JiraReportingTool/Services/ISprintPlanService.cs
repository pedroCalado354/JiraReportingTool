public interface ISprintPlanService
{
    Task<List<SprintPlanHeader>> GetAllAsync();
    Task<SprintPlanHeader?>      GetAsync(int id);

    /// <summary>
    /// Create (Id == 0) or full-replace (Id > 0) a sprint plan.
    /// Child rows are always deleted and re-inserted on update to keep logic simple.
    /// </summary>
    Task<SprintPlanHeader> SaveAsync(SprintPlanHeader plan);

    Task DeleteAsync(int id);
}
