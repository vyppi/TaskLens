using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskLens_App.ViewModels;

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
            Navigation.MenuItems.Add(new NavigationViewItem
            {
                Content = area.Name,
                Tag = $"area:{area.Id}",
                Icon = new SymbolIcon(Symbol.Folder)
            });
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
}
