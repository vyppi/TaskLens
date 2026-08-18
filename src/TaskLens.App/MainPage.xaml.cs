using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TaskLens.Core;
using TaskLens_App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace TaskLens_App;

public sealed partial class MainPage : Page
{
    private bool _initialized;

    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ViewModel.InitializeAsync();
        foreach (var area in ViewModel.Areas)
        {
            AddAreaNavigationItem(area);
        }

        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    private void Navigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        if (tag.StartsWith("area:", StringComparison.Ordinal))
        {
            ViewModel.SelectArea(tag["area:".Length..]);
        }
        else
        {
            ViewModel.SelectView(tag);
        }
    }

    private async void TaskCompletion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox &&
            checkBox.Tag is TaskCardViewModel task)
        {
            await ViewModel.SetCompletedAsync(task, checkBox.IsChecked == true);
        }
    }

    private async void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is TaskCardViewModel task)
        {
            await ViewModel.DeleteAsync(task);
        }
    }

    private async void AddArea_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "Example: Product launch",
            MaxLength = 60
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Create an area",
            Content = nameBox,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            string.IsNullOrWhiteSpace(nameBox.Text))
        {
            return;
        }

        try
        {
            await ViewModel.CreateAreaAsync(nameBox.Text);
            AddAreaNavigationItem(ViewModel.Areas[^1]);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            ViewModel.StatusText = "An area with that name already exists.";
        }
    }

    private async void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TaskCardViewModel task })
        {
            return;
        }

        var titleBox = new TextBox { Text = task.Task.Title, Header = "Task" };
        var areaBox = new ComboBox
        {
            Header = "Area",
            ItemsSource = ViewModel.Areas,
            DisplayMemberPath = nameof(AreaOption.Name),
            SelectedValuePath = nameof(AreaOption.Id),
            SelectedValue = task.Task.AreaId,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var dueDate = new CalendarDatePicker
        {
            Header = "Due date (Windows reminder at 9:00 AM)",
            Date = task.Task.DueAt,
            PlaceholderText = "No completion date"
        };
        var priority = new ComboBox
        {
            Header = "Priority",
            ItemsSource = Enum.GetValues<TaskPriority>(),
            SelectedItem = task.Task.Priority,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var priorityHelp = new TextBlock
        {
            Text = "High priority tasks sort ahead of Normal and Low tasks with the same due date.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12
        };
        var fields = new StackPanel { Spacing = 12 };
        fields.Children.Add(titleBox);
        fields.Children.Add(areaBox);
        fields.Children.Add(dueDate);
        fields.Children.Add(priority);
        fields.Children.Add(priorityHelp);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Edit task",
            Content = fields,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            string.IsNullOrWhiteSpace(titleBox.Text) ||
            areaBox.SelectedValue is not string areaId ||
            priority.SelectedItem is not TaskPriority selectedPriority)
        {
            return;
        }

        await ViewModel.UpdateTaskAsync(
            task,
            titleBox.Text,
            areaId,
            dueDate.Date,
            selectedPriority);
    }

    private async void DeleteArea_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAreaId is not string areaId)
        {
            return;
        }

        var area = ViewModel.Areas.First(item => item.Id == areaId);
        var taskCount = ViewModel.GetTaskCount(areaId);
        var replacements = ViewModel.Areas.Where(item => item.Id != areaId).ToArray();
        var replacementBox = new ComboBox
        {
            Header = "Move tasks to",
            ItemsSource = replacements,
            DisplayMemberPath = nameof(AreaOption.Name),
            SelectedValuePath = nameof(AreaOption.Id),
            SelectedIndex = replacements.Length > 0 ? 0 : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = taskCount > 0 ? Visibility.Visible : Visibility.Collapsed
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = taskCount == 0
                ? $"Delete '{area.Name}'?"
                : $"'{area.Name}' contains {taskCount} task(s). They will be moved before the area is deleted.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(replacementBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete area",
            Content = content,
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var replacementAreaId = taskCount > 0
            ? replacementBox.SelectedValue as string
            : null;
        if (taskCount > 0 && replacementAreaId is null)
        {
            ViewModel.StatusText = "Choose where the area's tasks should move.";
            return;
        }

        await ViewModel.DeleteAreaAsync(areaId, replacementAreaId);
        var navigationItem = Navigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => Equals(item.Tag, $"area:{areaId}"));
        if (navigationItem is not null)
        {
            Navigation.MenuItems.Remove(navigationItem);
        }

        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    private void TaskList_DragItemsStarting(
        object sender,
        DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is TaskCardViewModel task)
        {
            e.Data.SetText(task.Task.Id);
            e.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private void Area_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = $"Move to {((NavigationViewItem)sender).Content}";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void Area_Drop(object sender, DragEventArgs e)
    {
        if (sender is not NavigationViewItem { Tag: string tag } ||
            !tag.StartsWith("area:", StringComparison.Ordinal) ||
            !e.DataView.Contains(StandardDataFormats.Text))
        {
            return;
        }

        var taskId = await e.DataView.GetTextAsync();
        await ViewModel.MoveTaskAsync(taskId, tag["area:".Length..]);
    }

    private void AddAreaNavigationItem(AreaOption area)
    {
        var item = new NavigationViewItem
        {
            Content = area.Name,
            Tag = $"area:{area.Id}",
            Icon = new SymbolIcon(Symbol.Folder),
            AllowDrop = true
        };
        item.DragOver += Area_DragOver;
        item.Drop += Area_Drop;
        Navigation.MenuItems.Add(item);
    }
}
