namespace TaskLens.Core;

public interface ITaskRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkArea>> GetAreasAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskItem>> GetTasksAsync(
        CancellationToken cancellationToken = default);

    Task SaveTaskAsync(
        TaskItem task,
        CancellationToken cancellationToken = default);

    Task DeleteTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default);
}
