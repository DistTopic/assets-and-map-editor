using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AssetsAndMapEditor.App.ViewModels;

namespace AssetsAndMapEditor.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load();
            var hasHistory = settings.History.Count > 0 || settings.Sessions.Count > 0;

            if (hasHistory)
            {
                // Migrate legacy Sessions into a single History entry if History is empty
                if (settings.History.Count == 0 && settings.Sessions.Count > 0)
                {
                    settings.History.Add(new SessionHistoryEntry
                    {
                        ClosedAt = DateTime.UtcNow,
                        DisplayName = BuildHistoryName(settings.Sessions),
                        Tabs = new(settings.Sessions),
                        ViewSettings = new(settings.ViewSettings),
                    });
                    settings.Save();
                }

                var welcome = new WelcomeWindow(settings.History);
                desktop.MainWindow = welcome;

                welcome.Closed += (_, _) =>
                {
                    var result = welcome.Result;
                    if (result == null)
                    {
                        // User closed the window without choosing — exit
                        desktop.Shutdown();
                        return;
                    }

                    var vm = new MainWindowViewModel();
                    var mainWindow = new MainWindow { DataContext = vm };
                    desktop.MainWindow = mainWindow;

                    switch (result.Action)
                    {
                        case WelcomeAction.RestoreHistory:
                            vm.PendingHistoryRestore = result.HistoryEntry;
                            break;
                        case WelcomeAction.OpenFiles:
                            vm.PendingOpenFiles = true;
                            break;
                        // NewSession: just open MainWindow with default empty session
                    }

                    mainWindow.Show();
                };
            }
            else
            {
                // No history — go straight to editor
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel()
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string BuildHistoryName(List<SavedSession> sessions)
    {
        var maps = sessions
            .Where(s => !string.IsNullOrEmpty(s.MapFilePath))
            .Select(s => System.IO.Path.GetFileName(s.MapFilePath))
            .ToList();
        if (maps.Count > 0) return string.Join(", ", maps.Take(3));

        var protocols = sessions
            .Where(s => s.ProtocolVersion > 0)
            .Select(s => $"v{s.ProtocolVersion}")
            .Distinct()
            .ToList();
        if (protocols.Count > 0) return string.Join(", ", protocols);

        return $"{sessions.Count} tab{(sessions.Count != 1 ? "s" : "")}";
    }
}