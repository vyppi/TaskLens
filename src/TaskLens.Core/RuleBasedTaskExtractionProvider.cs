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
        "will ",
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

    public string Name => "On-device quick extraction";

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
            .Select(CleanLine)
            .Where(line => line.Length >= 6)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!LooksActionable(line))
            {
                continue;
            }

            var title = NormalizeTitle(line);
            suggestions.Add(new TaskSuggestion(
                title,
                InferArea(line, areas),
                InferDueDate(line),
                InferDuration(line),
                InferPriority(line),
                line.Length <= 180 ? line : $"{line[..177]}...",
                "Detected an actionable statement in the pasted text.",
                CalculateConfidence(line)));
        }

        return Task.FromResult(new ExtractionResult(
            suggestions.Take(20).ToArray(),
            Name));
    }

    private static IEnumerable<string> SplitSentences(string line) =>
        Regex.Split(line, @"(?<=[.!?])\s+(?=[A-Z])");

    private static string CleanLine(string line) =>
        BulletPrefixRegex().Replace(line.Trim(), string.Empty).Trim();

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
        var target = line.Contains("blue badge", StringComparison.OrdinalIgnoreCase)
            ? "blue-badge"
            : line.Contains("certif", StringComparison.OrdinalIgnoreCase) ||
              line.Contains("learn", StringComparison.OrdinalIgnoreCase) ||
              line.Contains("exam", StringComparison.OrdinalIgnoreCase)
                ? "ai-certification"
                : line.Contains("manager", StringComparison.OrdinalIgnoreCase) ||
                  line.Contains("1:1", StringComparison.OrdinalIgnoreCase) ||
                  line.Contains("one-on-one", StringComparison.OrdinalIgnoreCase)
                    ? "manager"
                    : "personal";

        return areas.Any(area => area.Id == target)
            ? target
            : areas.FirstOrDefault()?.Id ?? "personal";
    }

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

    private static int InferDuration(string line)
    {
        var match = DurationRegex().Match(line);
        if (!match.Success)
        {
            return 30;
        }

        var amount = int.Parse(match.Groups["amount"].Value);
        return match.Groups["unit"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase)
            ? Math.Min(amount * 60, 480)
            : Math.Min(amount, 480);
    }

    private static TaskPriority InferPriority(string line) =>
        line.Contains("urgent", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("asap", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("blocker", StringComparison.OrdinalIgnoreCase)
            ? TaskPriority.High
            : TaskPriority.Normal;

    private static double CalculateConfidence(string line)
    {
        var score = 0.62;
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

    [GeneratedRegex(
        @"(?<amount>\d{1,3})\s*(?<unit>minutes?|mins?|hours?|hrs?)",
        RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();
}
