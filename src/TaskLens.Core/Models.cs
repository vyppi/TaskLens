namespace TaskLens.Core;

public enum TaskPriority
{
    Low,
    Normal,
    High
}

public enum TaskSource
{
    Manual,
    BrainDump,
    Transcript,
    Email,
    Teams
}

public sealed record WorkArea(string Id, string Name, string Color);

public sealed record TaskItem(
    string Id,
    string Title,
    string Notes,
    string AreaId,
    DateTimeOffset? DueAt,
    TaskPriority Priority,
    bool IsCompleted,
    TaskSource Source,
    string SourceExcerpt,
    string Rationale,
    double Confidence,
    DateTimeOffset CreatedAt);

public sealed record TaskSuggestion(
    string Title,
    string AreaId,
    DateTimeOffset? DueAt,
    TaskPriority Priority,
    string SourceExcerpt,
    string Rationale,
    double Confidence);

public sealed record ExtractionResult(
    IReadOnlyList<TaskSuggestion> Suggestions,
    string ProviderName);
