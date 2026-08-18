using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskLens.Core;

public sealed class OpenAiCompatibleTaskExtractionProvider(
    HttpClient httpClient,
    Uri endpoint,
    string apiKey,
    string model) : ITaskExtractionProvider
{
    public string Name => "Cloud AI";

    public async Task<ExtractionResult> ExtractAsync(
        string content,
        IReadOnlyList<WorkArea> areas,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (endpoint.Host.Contains("azure", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("api-key", apiKey);
        }
        else
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var areaList = string.Join(", ", areas.Select(area => $"{area.Id}: {area.Name}"));
        request.Content = JsonContent.Create(new
        {
            model,
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content =
                        """
                        Extract only concrete action items. Return JSON with a "tasks" array.
                        Each task must contain: title, areaId, dueAt (ISO 8601 or null),
                        estimatedMinutes (5-480), priority (Low, Normal, or High),
                        sourceExcerpt, rationale, and confidence (0-1).
                        Do not invent commitments. Prefer null due dates over guessing.
                        """
                },
                new
                {
                    role = "user",
                    content = $"Available areas: {areaList}\n\nContent:\n{content}"
                }
            }
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<ChatEnvelope>(
            cancellationToken: cancellationToken)
            ?? throw new JsonException("The AI provider returned an empty response.");
        var json = envelope.Choices.FirstOrDefault()?.Message.Content
            ?? throw new JsonException("The AI provider returned no task content.");
        var payload = JsonSerializer.Deserialize<AiTaskPayload>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("The AI provider returned invalid task JSON.");

        var validAreas = areas.Select(area => area.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fallbackArea = areas.FirstOrDefault()?.Id ?? "personal";
        var tasks = payload.Tasks
            .Where(task => !string.IsNullOrWhiteSpace(task.Title))
            .Take(20)
            .Select(task => new TaskSuggestion(
                task.Title.Trim(),
                validAreas.Contains(task.AreaId) ? task.AreaId : fallbackArea,
                task.DueAt,
                Math.Clamp(task.EstimatedMinutes, 5, 480),
                Enum.TryParse<TaskPriority>(task.Priority, true, out var priority)
                    ? priority
                    : TaskPriority.Normal,
                task.SourceExcerpt ?? string.Empty,
                task.Rationale ?? "Extracted by the configured AI provider.",
                Math.Clamp(task.Confidence, 0, 1)))
            .ToArray();

        return new ExtractionResult(tasks, Name);
    }

    private sealed record ChatEnvelope(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice> Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage Message);

    private sealed record ChatMessage(
        [property: JsonPropertyName("content")] string Content);

    private sealed record AiTaskPayload(
        [property: JsonPropertyName("tasks")] IReadOnlyList<AiTask> Tasks);

    private sealed record AiTask(
        string Title,
        string AreaId,
        DateTimeOffset? DueAt,
        int EstimatedMinutes,
        string Priority,
        string? SourceExcerpt,
        string? Rationale,
        double Confidence);
}
