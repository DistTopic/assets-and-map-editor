using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace POriginsItemEditor.App.ViewModels;

public static class FileDialogHelper
{
    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    public static async Task<string?> OpenFileAsync(string title, (string Name, string Pattern)[] filters)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var storageProvider = window.StorageProvider;
        var fileTypes = filters.Select(f => new FilePickerFileType(f.Name) { Patterns = [f.Pattern] }).ToList();

        var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = fileTypes,
            AllowMultiple = false,
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> SaveFileAsync(string title, (string Name, string Pattern)[] filters)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var storageProvider = window.StorageProvider;
        var fileTypes = filters.Select(f => new FilePickerFileType(f.Name) { Patterns = [f.Pattern] }).ToList();

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            FileTypeChoices = fileTypes,
        });

        return result?.TryGetLocalPath();
    }

    public static async Task<string?> OpenFolderAsync(string title)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var storageProvider = window.StorageProvider;
        var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}
