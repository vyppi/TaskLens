using TaskLens.Core;

namespace TaskLens.Core.Tests;

[TestClass]
public sealed class RuleBasedTaskExtractionProviderTests
{
    private static readonly WorkArea[] Areas =
    [
        new("launch", "Product Launch", "#2563EB"),
        new("learning", "Learning", "#7C3AED"),
        new("people", "People Management", "#DB2777"),
        new("general", "General", "#059669")
    ];

    [TestMethod]
    public async Task ExtractAsync_TranscriptActions_ReturnsMappedSuggestions()
    {
        var provider = new RuleBasedTaskExtractionProvider();

        var result = await provider.ExtractAsync(
            """
            Alex: Action item: send the Product Launch status update by Friday.
            I need to schedule the People Management review tomorrow for 30 minutes.
            This paragraph is only background information.
            """,
            Areas);

        Assert.HasCount(2, result.Suggestions);
        Assert.AreEqual("launch", result.Suggestions[0].AreaId);
        Assert.AreEqual("people", result.Suggestions[1].AreaId);
        Assert.AreEqual(30, result.Suggestions[1].EstimatedMinutes);
        Assert.IsNotNull(result.Suggestions[1].DueAt);
    }

    [TestMethod]
    public async Task ExtractAsync_UrgentTask_SetsHighPriority()
    {
        var provider = new RuleBasedTaskExtractionProvider();

        var result = await provider.ExtractAsync(
            "TODO: finish the Learning practice exam ASAP.",
            Areas);

        Assert.HasCount(1, result.Suggestions);
        Assert.AreEqual(TaskPriority.High, result.Suggestions[0].Priority);
        Assert.AreEqual("learning", result.Suggestions[0].AreaId);
    }

    [TestMethod]
    public async Task ExtractAsync_BulletedBrainDump_CapturesEachBullet()
    {
        var provider = new RuleBasedTaskExtractionProvider();

        var result = await provider.ExtractAsync(
            """
            - Book the customer review
            - Draft launch notes
            Background context without an action.
            """,
            Areas);

        Assert.HasCount(2, result.Suggestions);
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
