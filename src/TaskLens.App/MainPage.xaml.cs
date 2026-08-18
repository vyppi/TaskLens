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
            Header = "Complete by",
            Date = task.Task.DueAt,
            PlaceholderText = "No completion date"
        };
        var duration = new NumberBox
        {
            Header = "Estimated minutes",
            Minimum = 5,
            Maximum = 480,
            SmallChange = 5,
            Value = task.Task.EstimatedMinutes
        };
        var priority = new ComboBox
        {
            Header = "Priority",
            ItemsSource = Enum.GetValues<TaskPriority>(),
            SelectedItem = task.Task.Priority,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var fields = new StackPanel { Spacing = 12 };
        fields.Children.Add(titleBox);
        fields.Children.Add(areaBox);
        fields.Children.Add(dueDate);
        fields.Children.Add(duration);
        fields.Children.Add(priority);

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
            double.IsNaN(duration.Value) ? 30 : (int)duration.Value,
            selectedPriority);
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
