using TaskLens.Core;

namespace TaskLens.Core.Tests;

[TestClass]
public sealed class RuleBasedTaskExtractionProviderTests
{
    private static readonly WorkArea[] Areas =
    [
        new("blue-badge", "Project Blue Badge", "#2563EB"),
        new("ai-certification", "AI Certification", "#7C3AED"),
        new("manager", "Manager", "#DB2777"),
        new("personal", "Personal", "#059669")
    ];

    [TestMethod]
    public async Task ExtractAsync_TranscriptActions_ReturnsMappedSuggestions()
    {
        var provider = new RuleBasedTaskExtractionProvider();

        var result = await provider.ExtractAsync(
            """
            Alex: Action item: send the Blue Badge status update by Friday.
            I need to schedule my manager 1:1 tomorrow for 30 minutes.
            This paragraph is only background information.
            """,
            Areas);

        Assert.HasCount(2, result.Suggestions);
        Assert.AreEqual("blue-badge", result.Suggestions[0].AreaId);
        Assert.AreEqual("manager", result.Suggestions[1].AreaId);
        Assert.AreEqual(30, result.Suggestions[1].EstimatedMinutes);
        Assert.IsNotNull(result.Suggestions[1].DueAt);
    }

    [TestMethod]
    public async Task ExtractAsync_UrgentTask_SetsHighPriority()
    {
        var provider = new RuleBasedTaskExtractionProvider();

        var result = await provider.ExtractAsync(
            "TODO: finish the AI certification practice exam ASAP.",
            Areas);

        Assert.HasCount(1, result.Suggestions);
        Assert.AreEqual(TaskPriority.High, result.Suggestions[0].Priority);
        Assert.AreEqual("ai-certification", result.Suggestions[0].AreaId);
    }

    [TestMethod]
    public async Task ExtractAsync_NoActions_ReturnsEmptyCollection()
    {
        var provider = new RuleBasedTaskExtractionProvider();

        var result = await provider.ExtractAsync(
            "The meeting covered quarterly results and general team updates.",
            Areas);

        Assert.IsEmpty(result.Suggestions);
    }
}
