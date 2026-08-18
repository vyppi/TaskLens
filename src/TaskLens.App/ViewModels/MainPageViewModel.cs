using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using TaskLens.Core;

namespace TaskLens_App.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly ITaskRepository _repository;
    private readonly ITaskExtractionProvider _extractionProvider;
    private readonly List<TaskItem> _allTasks = [];

    public MainPageViewModel()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskLens");
        _repository = new SqliteTaskRepository(
            Path.Combine(dataDirectory, "tasklens.db"));
        _extractionProvider = CreateExtractionProvider();
    }

    public ObservableCollection<AreaOption> Areas { get; } = [];

    public ObservableCollection<TaskCardViewModel> VisibleTasks { get; } = [];

    public ObservableCollection<SuggestedTaskViewModel> Suggestions { get; } = [];

    [ObservableProperty]
    public partial string QuickTaskTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CaptureText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedView { get; set; } = "My Day";

    [ObservableProperty]
    public partial string? SelectedAreaId { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsCaptureVisible { get; set; }

    public Visibility CaptureVisibility =>
        IsCaptureVisible ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TaskListVisibility =>
        IsCaptureVisible ? Visibility.Collapsed : Visibility.Visible;

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await _repository.InitializeAsync();
            Areas.Clear();
            foreach (var area in await _repository.GetAreasAsync())
            {
                Areas.Add(new AreaOption(area.Id, area.Name, area.Color));
            }

            await ReloadAsync();
            StatusText = $"{_extractionProvider.Name} ready";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddTaskAsync()
    {
        var title = QuickTaskTitle.Trim();
        if (title.Length == 0)
        {
            StatusText = "Enter a task first.";
            return;
        }

        var task = new TaskItem(
            Guid.NewGuid().ToString("N"),
            title,
            string.Empty,
            SelectedAreaId ?? Areas.FirstOrDefault()?.Id ?? "personal",
            SelectedView == "My Day" ? DateTimeOffset.Now.Date.AddHours(17) : null,
            30,
            TaskPriority.Normal,
            false,
            TaskSource.Manual,
            string.Empty,
            string.Empty,
            1,
            DateTimeOffset.UtcNow);
        await _repository.SaveTaskAsync(task);
        QuickTaskTitle = string.Empty;
        await ReloadAsync();
        StatusText = "Task added";
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (string.IsNullOrWhiteSpace(CaptureText))
        {
            StatusText = "Paste a transcript or brain dump first.";
            return;
        }

        IsBusy = true;
        Suggestions.Clear();
        try
        {
            var areas = Areas
                .Select(area => new WorkArea(area.Id, area.Name, area.Color))
                .ToArray();
            var result = await _extractionProvider.ExtractAsync(CaptureText, areas);
            foreach (var suggestion in result.Suggestions)
            {
                var areaName = Areas.FirstOrDefault(area => area.Id == suggestion.AreaId)?.Name
                    ?? "Personal";
                Suggestions.Add(new SuggestedTaskViewModel(suggestion, areaName));
            }

            StatusText = Suggestions.Count == 0
                ? "No clear action items found. Try more explicit action language."
                : $"{Suggestions.Count} suggestions from {result.ProviderName}";
        }
        catch (HttpRequestException exception)
        {
            StatusText = $"Cloud AI request failed: {exception.Message}";
        }
        catch (System.Text.Json.JsonException exception)
        {
            StatusText = $"Cloud AI response was invalid: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AcceptSuggestionsAsync()
    {
        var selected = Suggestions.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            StatusText = "Select at least one suggestion.";
            return;
        }

        foreach (var item in selected)
        {
            var suggestion = item.Suggestion;
            await _repository.SaveTaskAsync(new TaskItem(
                Guid.NewGuid().ToString("N"),
                suggestion.Title,
                string.Empty,
                suggestion.AreaId,
                suggestion.DueAt,
                suggestion.EstimatedMinutes,
                suggestion.Priority,
                false,
                TaskSource.BrainDump,
                suggestion.SourceExcerpt,
                suggestion.Rationale,
                suggestion.Confidence,
                DateTimeOffset.UtcNow));
        }

        Suggestions.Clear();
        CaptureText = string.Empty;
        IsCaptureVisible = false;
        await ReloadAsync();
        StatusText = $"{selected.Length} reviewed tasks created";
    }

    public async Task SetCompletedAsync(TaskCardViewModel item, bool isCompleted)
    {
        var updated = item.Task with { IsCompleted = isCompleted };
        await _repository.SaveTaskAsync(updated);
        await ReloadAsync();
        StatusText = isCompleted ? "Task completed" : "Task reopened";
    }

    public async Task DeleteAsync(TaskCardViewModel item)
    {
        await _repository.DeleteTaskAsync(item.Task.Id);
        await ReloadAsync();
        StatusText = "Task deleted";
    }

    public void SelectView(string view)
    {
        SelectedView = view;
        SelectedAreaId = null;
        IsCaptureVisible = view == "AI Capture";
        ApplyFilter();
    }

    partial void OnIsCaptureVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(CaptureVisibility));
        OnPropertyChanged(nameof(TaskListVisibility));
    }

    public void SelectArea(string areaId)
    {
        SelectedAreaId = areaId;
        SelectedView = Areas.First(area => area.Id == areaId).Name;
        IsCaptureVisible = false;
        ApplyFilter();
    }

    private async Task ReloadAsync()
    {
        _allTasks.Clear();
        _allTasks.AddRange(await _repository.GetTasksAsync());
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var now = DateTimeOffset.Now;
        IEnumerable<TaskItem> filtered = _allTasks;

        if (SelectedAreaId is not null)
        {
            filtered = filtered.Where(task => task.AreaId == SelectedAreaId);
        }
        else
        {
            filtered = SelectedView switch
            {
                "My Day" => filtered.Where(task =>
                    !task.IsCompleted &&
                    task.DueAt is not null &&
                    task.DueAt.Value.Date <= now.Date),
                "Inbox" => filtered.Where(task =>
                    !task.IsCompleted && task.DueAt is null),
                "Upcoming" => filtered.Where(task =>
                    !task.IsCompleted &&
                    task.DueAt is not null &&
                    task.DueAt.Value.Date > now.Date),
                "Completed" => filtered.Where(task => task.IsCompleted),
                _ => filtered.Where(task => !task.IsCompleted)
            };
        }

        VisibleTasks.Clear();
        foreach (var task in filtered)
        {
            var areaName = Areas.FirstOrDefault(area => area.Id == task.AreaId)?.Name
                ?? task.AreaId;
            VisibleTasks.Add(new TaskCardViewModel(task, areaName));
        }
    }

    private static ITaskExtractionProvider CreateExtractionProvider()
    {
        var endpoint = Environment.GetEnvironmentVariable("TASKLENS_AI_ENDPOINT");
        var apiKey = Environment.GetEnvironmentVariable("TASKLENS_AI_API_KEY");
        var model = Environment.GetEnvironmentVariable("TASKLENS_AI_MODEL");
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(apiKey) &&
            !string.IsNullOrWhiteSpace(model))
        {
            return new OpenAiCompatibleTaskExtractionProvider(
                new HttpClient { Timeout = TimeSpan.FromSeconds(60) },
                uri,
                apiKey,
                model);
        }

        return new RuleBasedTaskExtractionProvider();
    }
}

public sealed record AreaOption(string Id, string Name, string Color);

public sealed class TaskCardViewModel(TaskItem task, string areaName)
{
    public TaskItem Task { get; } = task;

    public string Title => Task.Title;

    public string AreaName => areaName;

    public bool IsCompleted => Task.IsCompleted;

    public string Metadata
    {
        get
        {
            var due = Task.DueAt is null
                ? "No due date"
                : Task.DueAt.Value.Date == DateTimeOffset.Now.Date
                    ? "Today"
                    : Task.DueAt.Value.ToString("ddd, MMM d");
            return $"{due}  •  {Task.EstimatedMinutes} min  •  {Task.Priority}";
        }
    }

    public string SourceDetail => string.IsNullOrWhiteSpace(Task.SourceExcerpt)
        ? string.Empty
        : $"Source: {Task.SourceExcerpt}";
}

public partial class SuggestedTaskViewModel(
    TaskSuggestion suggestion,
    string areaName) : ObservableObject
{
    public TaskSuggestion Suggestion { get; } = suggestion;

    public string Title => Suggestion.Title;

    public string AreaName => areaName;

    public string Metadata =>
        $"{Suggestion.EstimatedMinutes} min  •  {Suggestion.Priority}  •  {Suggestion.Confidence:P0} confidence";

    public string Explanation => Suggestion.Rationale;

    public string SourceExcerpt => Suggestion.SourceExcerpt;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;
}
