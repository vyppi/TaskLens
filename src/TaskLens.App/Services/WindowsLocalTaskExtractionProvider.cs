using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskLens.Core;

namespace TaskLens_App.Services;

public sealed class WindowsLocalTaskExtractionProvider : ITaskExtractionProvider
{
    private LanguageModel? _model;

    public string Name => "Windows local AI (Copilot+ PC)";

    public static bool IsPotentiallyAvailable()
    {
        var state = LanguageModel.GetReadyState();
        return state is AIFeatureReadyState.Ready or AIFeatureReadyState.NotReady;
    }

    public async Task<ExtractionResult> ExtractAsync(
        string content,
        IReadOnlyList<WorkArea> areas,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        cancellationToken.ThrowIfCancellationRequested();

        _model ??= await CreateModelAsync();
        var validAreas = areas.Select(area => area.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fallbackArea = areas.FirstOrDefault()?.Id ?? "general";
        var areaList = string.Join(", ", areas.Select(area => $"{area.Id}: {area.Name}"));

        var prompt =
            $"""
            Extract concrete action items from the content below.
            Do not turn general discussion, decisions, or background facts into tasks.
            Keep source excerpts faithful to the input.
            Infer a due date only when the content provides one.
            Available areas: {areaList}

            Content:
            {content}
            """;
        var result = await _model.GenerateStructuredJsonResponseAsync(
            prompt,
            JsonSchema,
            new LanguageModelOptions
            {
                Temperature = 0.1F,
                TopP = 0.9F
            });

        if (result.Status != GenerateStructuredJsonResponseStatus.Complete)
        {
            throw new InvalidOperationException(
                $"Windows local AI could not complete extraction: {result.Status}.");
        }

        var payload = JsonSerializer.Deserialize<LocalTaskPayload>(
            result.Text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("Windows local AI returned invalid task JSON.");
        var suggestions = payload.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Title))
            .Take(20)
            .Select(task => new TaskSuggestion(
                task.Title.Trim(),
                validAreas.Contains(task.AreaId) ? task.AreaId : fallbackArea,
                task.DueAt,
                Enum.TryParse<TaskPriority>(task.Priority, true, out var priority)
                    ? priority
                    : TaskPriority.Normal,
                task.SourceExcerpt ?? string.Empty,
                task.Rationale ?? "Extracted by the Windows local language model.",
                Math.Clamp(task.Confidence, 0, 1)))
            .ToArray();

        return new ExtractionResult(suggestions, Name);
    }

    private static async Task<LanguageModel> CreateModelAsync()
    {
        if (LanguageModel.GetReadyState() == AIFeatureReadyState.NotReady)
        {
            var readiness = await LanguageModel.EnsureReadyAsync();
            if (readiness.Status != AIFeatureReadyResultState.Success)
            {
                throw new InvalidOperationException(
                    $"Windows local AI is not ready: {readiness.ErrorDisplayText}");
            }
        }

        if (LanguageModel.GetReadyState() != AIFeatureReadyState.Ready)
        {
            throw new InvalidOperationException(
                $"Windows local AI is unavailable: {LanguageModel.GetReadyState()}.");
        }

        return await LanguageModel.CreateAsync();
    }

    private const string JsonSchema =
        """
        {
          "type": "object",
          "properties": {
            "tasks": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "areaId": { "type": "string" },
                  "dueAt": { "type": ["string", "null"], "format": "date-time" },
                  "priority": { "type": "string", "enum": ["Low", "Normal", "High"] },
                  "sourceExcerpt": { "type": "string" },
                  "rationale": { "type": "string" },
                  "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
                },
                "required": [
                  "title", "areaId", "dueAt", "priority",
                  "sourceExcerpt", "rationale", "confidence"
                ],
                "additionalProperties": false
              }
            }
          },
          "required": ["tasks"],
          "additionalProperties": false
        }
        """;

    private sealed record LocalTaskPayload(
        [property: JsonPropertyName("tasks")] IReadOnlyList<LocalTask> Tasks);

    private sealed record LocalTask(
        string Title,
        string AreaId,
        DateTimeOffset? DueAt,
        string Priority,
        string? SourceExcerpt,
        string? Rationale,
        double Confidence);
}
