using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using POriginsItemEditor.App.ViewModels;

namespace POriginsItemEditor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.TryLoadLastSessionAsync();
        };
    }

    private void OnMinimapSwatchClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control)
            FlyoutBase.ShowAttachedFlyout(control);
    }

    private void OnMinimapColorPicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || DataContext is not MainWindowViewModel vm || vm.SelectedItem is not { } item)
            return;

        ushort index = border.Tag switch
        {
            ushort u => u,
            int i => (ushort)i,
            _ => ushort.MaxValue,
        };
        if (index > 215) return;

        item.MinimapColor = index;

        Control? current = border;
        while (current != null)
        {
            var flyout = FlyoutBase.GetAttachedFlyout(current);
            if (flyout != null)
            {
                flyout.Hide();
                break;
            }
            current = current.Parent as Control;
        }
    }

    private void OnCompositionSpriteDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border
            && border.DataContext is SpriteViewModel svm
            && DataContext is MainWindowViewModel vm)
        {
            vm.NavigateRightSpriteToIdCommand.Execute(svm.SpriteId);
        }
    }

    private void OnClientItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OpenClientItemEditor();
    }

    private void OnOtbItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OpenOtbItemEditor();
    }
}