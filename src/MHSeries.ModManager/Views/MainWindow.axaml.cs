using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HuntForge.ViewModels;

namespace HuntForge.Views;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private async void ImportClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入 Monster Hunter MOD",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("MOD 压缩包") { Patterns = ["*.zip", "*.7z", "*.rar"] },
                new FilePickerFileType("所有文件") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            await ViewModel.ImportFromPathAsync(files[0].Path.LocalPath);
        }
    }

    private async void ChooseGameClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择游戏安装目录",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            ViewModel.SetGamePath(folders[0].Path.LocalPath);
        }
    }

    private void CleanClick(object? sender, RoutedEventArgs e) =>
        (ViewModel.CleanCommand as System.Windows.Input.ICommand)?.Execute(null);

    private void GroupToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { DataContext: GroupViewModel group } toggle)
        {
            if (ViewModel.IsBusy)
            {
                toggle.IsChecked = group.AllEnabled;
                return;
            }

            _ = ViewModel.ToggleGroupAsync(group, !group.AllEnabled);
        }
    }

    private void GroupNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: GroupViewModel group } box)
        {
            ViewModel.RenameGroup(group, box.Text ?? group.Name);
        }
    }

    private void ModToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { DataContext: ModItemViewModel mod } toggle)
        {
            if (ViewModel.IsBusy)
            {
                toggle.IsChecked = mod.Enabled;
                return;
            }

            _ = ViewModel.ToggleModAsync(mod, !mod.Enabled);
        }
    }

    private void GamePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: GameViewModel game })
        {
            game.Select();
        }
    }

    private void TitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control control || control is Button || FindAncestor<Button>(control) is not null)
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            StartWindowDrag(e);
        }
    }

    private void StartWindowDrag(PointerPressedEventArgs e)
    {
        try { base.BeginMoveDrag(e); }
        catch { }
    }

    private void MinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    private static T? FindAncestor<T>(Control? control) where T : Control
    {
        var current = control?.Parent;
        while (current is not null)
        {
            if (current is T typed) return typed;
            current = current.Parent;
        }
        return null;
    }
}
