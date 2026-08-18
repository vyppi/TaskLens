namespace TaskLens.Core;

public interface ITaskExtractionProvider
{
    string Name { get; }

    Task<ExtractionResult> ExtractAsync(
        string content,
        IReadOnlyList<WorkArea> areas,
        CancellationToken cancellationToken = default);
}
