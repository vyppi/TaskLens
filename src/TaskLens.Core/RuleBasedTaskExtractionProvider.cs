using System.Text.RegularExpressions;

namespace TaskLens.Core;

public sealed partial class RuleBasedTaskExtractionProvider : ITaskExtractionProvider
{
    private static readonly string[] ActionSignals =
    [
        "action item",
        "todo",
        "to-do",
        "need to",
        "needs to",
        "should ",
        "i'll ",
        "we'll ",
        "will ",
        "can you",
        "could you",
        "please ",
        "follow up",
        "send ",
        "schedule ",
        "review ",
        "prepare ",
        "complete ",
        "update ",
        "create ",
        "check ",
        "draft ",
        "finish "
    ];

    public string Name => "Offline quick extraction (rules, not a language model)";

    public Task<ExtractionResult> ExtractAsync(
        string content,
        IReadOnlyList<WorkArea> areas,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var suggestions = new List<TaskSuggestion>();
        var lines = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitSentences)
            .Select(line => new CaptureLine(CleanLine(line), IsListItem(line)))
            .Where(item => item.Text.Length >= 6)
            .DistinctBy(item => item.Text, StringComparer.OrdinalIgnoreCase);

        foreach (var item in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.IsListItem && !LooksActionable(item.Text))
            {
                continue;
            }

            var line = item.Text;
            var title = NormalizeTitle(line);
            suggestions.Add(new TaskSuggestion(
                title,
                InferArea(line, areas),
                InferDueDate(line),
                InferPriority(line),
                line.Length <= 180 ? line : $"{line[..177]}...",
                item.IsListItem
                    ? "Detected a checklist or bullet item."
                    : "Detected action-oriented wording in the pasted text.",
                CalculateConfidence(line, item.IsListItem)));
        }

        return Task.FromResult(new ExtractionResult(
            suggestions.Take(20).ToArray(),
            Name));
    }

    private static IEnumerable<string> SplitSentences(string line) =>
        Regex.Split(line, @"(?<=[.!?])\s+(?=[A-Z])");

    private static string CleanLine(string line) =>
        BulletPrefixRegex().Replace(line.Trim(), string.Empty).Trim();

    private static bool IsListItem(string line) =>
        BulletPrefixRegex().IsMatch(line);

    private static bool LooksActionable(string line) =>
        ActionSignals.Any(signal =>
            line.Contains(signal, StringComparison.OrdinalIgnoreCase)) ||
        CheckboxRegex().IsMatch(line);

    private static string NormalizeTitle(string line)
    {
        var title = SpeakerPrefixRegex().Replace(line, string.Empty);
        title = ActionPrefixRegex().Replace(title, string.Empty);
        title = title.Trim(' ', '.', ':', '-', '–');

        if (title.Length == 0)
        {
            return line;
        }

        return char.ToUpperInvariant(title[0]) + title[1..];
    }

    private static string InferArea(string line, IReadOnlyList<WorkArea> areas)
    {
        var bestMatch = areas
            .Select(area => new
            {
                Area = area,
                Score = AreaTokens(area.Name)
                    .Count(token => line.Contains(token, StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(match => match.Score)
            .FirstOrDefault();

        return bestMatch is { Score: > 0 }
            ? bestMatch.Area.Id
            : areas.FirstOrDefault()?.Id ?? "general";
    }

    private static IEnumerable<string> AreaTokens(string areaName) =>
        Regex.Split(areaName, @"[^\p{L}\p{N}]+")
            .Where(token => token.Length >= 3);

    private static DateTimeOffset? InferDueDate(string line)
    {
        var now = DateTimeOffset.Now;
        if (line.Contains("today", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("asap", StringComparison.OrdinalIgnoreCase))
        {
            return now.Date.AddHours(17);
        }

        if (line.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            return now.Date.AddDays(1).AddHours(17);
        }

        if (line.Contains("next week", StringComparison.OrdinalIgnoreCase))
        {
            return now.Date.AddDays(7).AddHours(17);
        }

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            if (!line.Contains(day.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var daysAhead = ((int)day - (int)now.DayOfWeek + 7) % 7;
            daysAhead = daysAhead == 0 ? 7 : daysAhead;
            return now.Date.AddDays(daysAhead).AddHours(17);
        }

        return null;
    }

    private static TaskPriority InferPriority(string line) =>
        line.Contains("urgent", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("asap", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("blocker", StringComparison.OrdinalIgnoreCase)
            ? TaskPriority.High
            : TaskPriority.Normal;

    private static double CalculateConfidence(string line, bool isListItem)
    {
        var score = isListItem ? 0.7 : 0.58;
        if (line.Contains("action item", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("todo", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.2;
        }

        if (InferDueDate(line) is not null)
        {
            score += 0.08;
        }

        return Math.Min(score, 0.95);
    }

    private sealed record CaptureLine(string Text, bool IsListItem);

    [GeneratedRegex(@"^\s*(?:[-*•]|\d+[.)]|\[[ xX]\])\s*")]
    private static partial Regex BulletPrefixRegex();

    [GeneratedRegex(@"^\s*\[[ xX]\]")]
    private static partial Regex CheckboxRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z .'-]{0,40}:\s*")]
    private static partial Regex SpeakerPrefixRegex();

    [GeneratedRegex(
        @"^(?:action item|todo|to-do|we need to|i need to|please|can you|could you)\s*[:\-]?\s*",
        RegexOptions.IgnoreCase)]
    private static partial Regex ActionPrefixRegex();

}
