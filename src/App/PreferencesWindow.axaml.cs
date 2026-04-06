using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AssetsAndMapEditor.App;

public partial class PreferencesWindow : Window
{
    public int ItemsPerPage { get; private set; }
    public bool Saved { get; private set; }

    public PreferencesWindow() : this(100) { }

    public PreferencesWindow(int currentItemsPerPage)
    {
        InitializeComponent();
        ItemsPerPage = currentItemsPerPage;
        ItemsPerPageInput.Value = currentItemsPerPage;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        ItemsPerPage = (int)(ItemsPerPageInput.Value ?? 100);
        Saved = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
