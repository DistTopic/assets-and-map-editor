using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AssetsAndMapEditor.App;

/// <summary>
/// Result of the WelcomeWindow: which action the user chose.
/// </summary>
public enum WelcomeAction { NewSession, OpenFiles, RestoreHistory }

public class WelcomeResult
{
    public WelcomeAction Action { get; init; }
    /// <summary>The history entry to restore (only for RestoreHistory).</summary>
    public SessionHistoryEntry? HistoryEntry { get; init; }
}

/// <summary>Display model for a history entry in the list.</summary>
public class HistoryDisplayItem
{
    public string DisplayName { get; init; } = "";
    public string Detail { get; init; } = "";
    public string TimeAgo { get; init; } = "";
    public SessionHistoryEntry Entry { get; init; } = null!;
}

public partial class WelcomeWindow : Window
{
    public WelcomeResult? Result { get; private set; }

    public WelcomeWindow()
    {
        InitializeComponent();
    }

    public WelcomeWindow(List<SessionHistoryEntry> history) : this()
    {
        var items = history
            .OrderByDescending(h => h.ClosedAt)
            .Take(20)
            .Select(h => new HistoryDisplayItem
            {
                DisplayName = !string.IsNullOrEmpty(h.DisplayName) ? h.DisplayName : "Untitled Session",
                Detail = BuildDetail(h),
                TimeAgo = FormatTimeAgo(h.ClosedAt),
                Entry = h,
            })
            .ToList();

        if (items.Count == 0)
        {
            EmptyState.IsVisible = true;
            SessionScroller.IsVisible = false;
        }
        else
        {
            EmptyState.IsVisible = false;
            SessionScroller.IsVisible = true;
            SessionList.ItemsSource = items;
        }
    }

    private void OnNewSessionClick(object? sender, RoutedEventArgs e)
    {
        Result = new WelcomeResult { Action = WelcomeAction.NewSession };
        Close();
    }

    private void OnOpenFilesClick(object? sender, RoutedEventArgs e)
    {
        Result = new WelcomeResult { Action = WelcomeAction.OpenFiles };
        Close();
    }

    private void OnHistoryEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is HistoryDisplayItem item)
        {
            Result = new WelcomeResult
            {
                Action = WelcomeAction.RestoreHistory,
                HistoryEntry = item.Entry,
            };
            Close();
        }
    }

    private static string BuildDetail(SessionHistoryEntry entry)
    {
        var parts = new List<string>();
        var tabCount = entry.Tabs.Count;
        if (tabCount > 0)
            parts.Add($"{tabCount} tab{(tabCount != 1 ? "s" : "")}");

        var maps = entry.Tabs
            .Where(t => !string.IsNullOrEmpty(t.MapFilePath))
            .Select(t => Path.GetFileName(t.MapFilePath))
            .ToList();
        if (maps.Count > 0)
            parts.Add(string.Join(", ", maps.Take(3)));

        var protocols = entry.Tabs
            .Where(t => t.ProtocolVersion > 0)
            .Select(t => $"v{t.ProtocolVersion}")
            .Distinct()
            .ToList();
        if (protocols.Count > 0)
            parts.Add(string.Join(", ", protocols));

        return parts.Count > 0 ? string.Join(" · ", parts) : "Empty session";
    }

    private static string FormatTimeAgo(DateTime closedAt)
    {
        var diff = DateTime.UtcNow - closedAt;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}w ago";
        return closedAt.ToString("MMM dd");
    }
}
