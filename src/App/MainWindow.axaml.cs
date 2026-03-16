using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using POriginsItemEditor.App.ViewModels;

namespace POriginsItemEditor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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

        // Tag may be boxed as ushort or int depending on XAML binding
        ushort index = border.Tag switch
        {
            ushort u => u,
            int i => (ushort)i,
            _ => ushort.MaxValue,
        };
        if (index > 215) return;

        item.MinimapColor = index;

        // Walk up to find the control with the attached flyout and close it
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
}