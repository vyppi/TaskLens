using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using TaskLens.Core;
using TaskLens_App.Services;

namespace TaskLens_App.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly ITaskRepository _repository;
    private readonly TaskReminderService _reminderService = new();
    private ITaskExtractionProvider _extractionProvider =
        new RuleBasedTaskExtractionProvider();
    private readonly List<TaskItem> _allTasks = [];

    public MainPageViewModel()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskLens");
        _repository = new SqliteTaskRepository(
            Path.Combine(dataDirectory, "tasklens.db"));
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

    public string CaptureProviderDescription => _extractionProvider.Name;

    public bool CanDeleteSelectedArea =>
        SelectedAreaId is not null && Areas.Count > 1;

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
            _extractionProvider =
                WindowsLocalTaskExtractionProvider.IsPotentiallyAvailable()
                    ? new WindowsLocalTaskExtractionProvider()
                    : new RuleBasedTaskExtractionProvider();
            OnPropertyChanged(nameof(CaptureProviderDescription));
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
            SelectedAreaId ?? Areas.FirstOrDefault()?.Id ?? "general",
            SelectedView == "My Day" ? DateTimeOffset.Now.Date.AddHours(17) : null,
            TaskPriority.Normal,
            false,
            TaskSource.Manual,
            string.Empty,
            string.Empty,
            1,
            DateTimeOffset.UtcNow);
        await _repository.SaveTaskAsync(task);
        TryScheduleReminder(task);
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
                    ?? "General";
                Suggestions.Add(new SuggestedTaskViewModel(suggestion, areaName));
            }

            StatusText = Suggestions.Count == 0
                ? "No clear action items found. Try more explicit action language."
                : $"{Suggestions.Count} suggestions from {result.ProviderName}";
        }
        catch (System.Text.Json.JsonException exception)
        {
            StatusText = $"AI response was invalid: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            StatusText = $"AI extraction is unavailable: {exception.Message}";
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
            var task = new TaskItem(
                Guid.NewGuid().ToString("N"),
                suggestion.Title,
                string.Empty,
                suggestion.AreaId,
                suggestion.DueAt,
                suggestion.Priority,
                false,
                TaskSource.BrainDump,
                suggestion.SourceExcerpt,
                suggestion.Rationale,
                suggestion.Confidence,
                DateTimeOffset.UtcNow);
            await _repository.SaveTaskAsync(task);
            TryScheduleReminder(task);
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
        TryScheduleReminder(updated);
        await ReloadAsync();
        StatusText = isCompleted ? "Task completed" : "Task reopened";
    }

    public async Task DeleteAsync(TaskCardViewModel item)
    {
        TryCancelReminder(item.Task.Id);
        await _repository.DeleteTaskAsync(item.Task.Id);
        await ReloadAsync();
        StatusText = "Task deleted";
    }

    public async Task CreateAreaAsync(string name)
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            throw new ArgumentException("Area name is required.", nameof(name));
        }

        var area = new WorkArea(
            Guid.NewGuid().ToString("N"),
            trimmedName,
            "#2563EB");
        await _repository.CreateAreaAsync(area);
        Areas.Add(new AreaOption(area.Id, area.Name, area.Color));
        OnPropertyChanged(nameof(CanDeleteSelectedArea));
        StatusText = $"Area '{area.Name}' created";
    }

    public async Task UpdateTaskAsync(
        TaskCardViewModel item,
        string title,
        string areaId,
        DateTimeOffset? dueAt,
        TaskPriority priority)
    {
        var updated = item.Task with
        {
            Title = title.Trim(),
            AreaId = areaId,
            DueAt = dueAt,
            Priority = priority
        };
        await _repository.SaveTaskAsync(updated);
        TryScheduleReminder(updated);
        await ReloadAsync();
        StatusText = "Task updated";
    }

    public async Task MoveTaskAsync(string taskId, string areaId)
    {
        var task = _allTasks.FirstOrDefault(item => item.Id == taskId);
        if (task is null || Areas.All(area => area.Id != areaId))
        {
            return;
        }

        await _repository.SaveTaskAsync(task with { AreaId = areaId });
        await ReloadAsync();
        var areaName = Areas.First(area => area.Id == areaId).Name;
        StatusText = $"Task moved to {areaName}";
    }

    public int GetTaskCount(string areaId) =>
        _allTasks.Count(task => task.AreaId == areaId);

    public async Task DeleteAreaAsync(string areaId, string? replacementAreaId)
    {
        if (Areas.Count <= 1)
        {
            throw new InvalidOperationException("At least one area is required.");
        }

        await _repository.DeleteAreaAsync(areaId, replacementAreaId);
        var area = Areas.First(item => item.Id == areaId);
        Areas.Remove(area);
        SelectedAreaId = null;
        SelectedView = "My Day";
        IsCaptureVisible = false;
        await ReloadAsync();
        OnPropertyChanged(nameof(CanDeleteSelectedArea));
        StatusText = $"Area '{area.Name}' deleted";
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

    partial void OnSelectedAreaIdChanged(string? value) =>
        OnPropertyChanged(nameof(CanDeleteSelectedArea));

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
        if (App.NotificationRegistrationError is null)
        {
            try
            {
                _reminderService.Synchronize(_allTasks);
            }
            catch (System.Runtime.InteropServices.COMException exception)
            {
                StatusText = $"Windows reminders are unavailable: {exception.Message}";
            }
        }
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

    private void TryScheduleReminder(TaskItem task)
    {
        if (App.NotificationRegistrationError is not null)
        {
            StatusText =
                $"Windows reminders are unavailable: {App.NotificationRegistrationError}";
            return;
        }

        try
        {
            _reminderService.Schedule(task);
        }
        catch (System.Runtime.InteropServices.COMException exception)
        {
            StatusText = $"Windows reminder could not be scheduled: {exception.Message}";
        }
    }

    private void TryCancelReminder(string taskId)
    {
        if (App.NotificationRegistrationError is not null)
        {
            return;
        }

        try
        {
            _reminderService.Cancel(taskId);
        }
        catch (System.Runtime.InteropServices.COMException exception)
        {
            StatusText = $"Windows reminder could not be cancelled: {exception.Message}";
        }
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
            return $"{due}  •  {Task.Priority} priority";
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
        $"{Suggestion.Priority} priority  •  {Suggestion.Confidence:P0} confidence";

    public string Explanation => Suggestion.Rationale;

    public string SourceExcerpt => Suggestion.SourceExcerpt;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;
}
