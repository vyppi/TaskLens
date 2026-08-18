using TaskLens.Core;

namespace TaskLens.Core.Tests;

[TestClass]
public sealed class SqliteTaskRepositoryTests
{
    private string _databasePath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"tasklens-{Guid.NewGuid():N}.db");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_SeedsGenericArea()
    {
        var repository = new SqliteTaskRepository(_databasePath);

        await repository.InitializeAsync();
        var areas = await repository.GetAreasAsync();

        Assert.HasCount(1, areas);
        Assert.AreEqual("general", areas[0].Id);
        Assert.AreEqual("General", areas[0].Name);
    }

    [TestMethod]
    public async Task CreateAreaAsync_NewArea_PersistsArea()
    {
        var repository = new SqliteTaskRepository(_databasePath);
        await repository.InitializeAsync();

        await repository.CreateAreaAsync(
            new WorkArea("launch", "Product Launch", "#2563EB"));
        var areas = await repository.GetAreasAsync();

        Assert.HasCount(2, areas);
        Assert.AreEqual("Product Launch", areas[1].Name);
    }

    [TestMethod]
    public async Task SaveTaskAsync_NewAndUpdatedTask_PersistsChanges()
    {
        var repository = new SqliteTaskRepository(_databasePath);
        await repository.InitializeAsync();
        var task = CreateTask();

        await repository.SaveTaskAsync(task);
        await repository.SaveTaskAsync(task with { IsCompleted = true });
        var tasks = await repository.GetTasksAsync();

        Assert.HasCount(1, tasks);
        Assert.IsTrue(tasks[0].IsCompleted);
        Assert.AreEqual(task.Title, tasks[0].Title);
    }

    [TestMethod]
    public async Task DeleteTaskAsync_ExistingTask_RemovesTask()
    {
        var repository = new SqliteTaskRepository(_databasePath);
        await repository.InitializeAsync();
        var task = CreateTask();
        await repository.SaveTaskAsync(task);

        await repository.DeleteTaskAsync(task.Id);
        var tasks = await repository.GetTasksAsync();

        Assert.IsEmpty(tasks);
    }

    private static TaskItem CreateTask() =>
        new(
            Guid.NewGuid().ToString("N"),
            "Prepare weekly update",
            string.Empty,
            "general",
            DateTimeOffset.Now.AddDays(1),
            30,
            TaskPriority.High,
            false,
            TaskSource.Manual,
            string.Empty,
            string.Empty,
            1,
            DateTimeOffset.UtcNow);
}
