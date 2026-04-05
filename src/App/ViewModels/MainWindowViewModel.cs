using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetsAndMapEditor.OTB;
using System.Collections.ObjectModel;

namespace AssetsAndMapEditor.App.ViewModels;

using AssetsAndMapEditor.App;

public partial class MainWindowViewModel : ObservableObject
{
    private OtbData? _otbData;
    private DatData? _datData;
    private SprFile? _sprFile;
    private string? _otbPath;
    private List<ItemViewModel> _allItems = [];
    private List<ClientItemViewModel> _allClientItems = [];
    private readonly AppSettings _appSettings = AppSettings.Load();

    /// <summary>Original snapshots for reset: keyed by (Category, Id).</summary>
    private readonly Dictionary<(ThingCategory, ushort), DatThingType> _originalSnapshots = [];

    /// <summary>Clipboard for Copy/Paste of client items (DatThingType clone + source SPR).</summary>
    private DatThingType? _copiedClientItem;
    private SprFile? _copiedClientItemSprFile;

    /// <summary>Internal clipboard for sprite copy/paste (stores 32×32 RGBA pixel data).</summary>
    private byte[]? _copiedSpriteRgba;

    // ── Sessions ──
    public ObservableCollection<SessionViewModel> Sessions { get; } = [];
    private SessionViewModel? _currentSession;
    public SessionViewModel? ActiveSession
    {
        get => _currentSession;
        private set => SetProperty(ref _currentSession, value);
    }
    public bool HasMultipleSessions => Sessions.Count > 1;

    // ── Split view ──
    [ObservableProperty] private SplitMode _splitMode = SplitMode.None;
    [ObservableProperty] private SessionViewModel? _secondaryPaneSession;
    public bool IsSplit => SplitMode != SplitMode.None;
    public string SecondaryPaneLabel => SecondaryPaneSession?.Name ?? "";
    public string SecondaryPaneProtocol => SecondaryPaneSession != null && SecondaryPaneSession.ProtocolVersion > 0
        ? $"Protocol {SecondaryPaneSession.ProtocolVersion}" : "";
    public string SecondaryPaneItemCount
    {
        get
        {
            if (SecondaryPaneSession == null) return "";
            int otb = SecondaryPaneSession.AllItems.Count;
            int dat = SecondaryPaneSession.AllClientItems.Count;
            return $"OTB: {otb} items — DAT: {dat} items";
        }
    }

    partial void OnSplitModeChanged(SplitMode value)
    {
        OnPropertyChanged(nameof(IsSplit));
    }

    partial void OnSecondaryPaneSessionChanged(SessionViewModel? value)
    {
        OnPropertyChanged(nameof(SecondaryPaneLabel));
        OnPropertyChanged(nameof(SecondaryPaneProtocol));
        OnPropertyChanged(nameof(SecondaryPaneItemCount));
    }

    [RelayCommand]
    private void SplitRight(SessionViewModel? session)
    {
        if (session == null || Sessions.Count < 2) return;
        // If dragging the active session, put it in secondary and switch to another
        if (session == _currentSession)
        {
            var other = Sessions.FirstOrDefault(s => s != session);
            if (other != null) SwitchToSession(other);
        }
        SplitMode = SplitMode.Right;
        SecondaryPaneSession = session;
    }

    [RelayCommand]
    private void SplitDown(SessionViewModel? session)
    {
        if (session == null || Sessions.Count < 2) return;
        if (session == _currentSession)
        {
            var other = Sessions.FirstOrDefault(s => s != session);
            if (other != null) SwitchToSession(other);
        }
        SplitMode = SplitMode.Down;
        SecondaryPaneSession = session;
    }

    [RelayCommand]
    private void Unsplit()
    {
        SplitMode = SplitMode.None;
        SecondaryPaneSession = null;
    }

    [RelayCommand]
    public void ActivateSecondaryPane()
    {
        if (SecondaryPaneSession == null) return;
        var oldActive = _currentSession;
        SwitchToSession(SecondaryPaneSession);
        SecondaryPaneSession = oldActive;
    }

    [RelayCommand]
    private void MoveSessionToIndex(object? parameter)
    {
        if (parameter is not int[] args || args.Length < 2) return;
        int from = args[0];
        int to = args[1];
        if (from < 0 || from >= Sessions.Count || to < 0 || to >= Sessions.Count || from == to) return;
        Sessions.Move(from, to);
    }
    public string ActiveSessionLabel
    {
        get
        {
            if (_currentSession == null) return "No session";
            var proto = _datData?.ProtocolVersion ?? 0;
            return proto > 0 ? $"Protocol {proto}" : _currentSession.Name;
        }
    }

    [RelayCommand]
    private void NewSession()
    {
        var session = new SessionViewModel { Name = $"Session {Sessions.Count + 1}" };
        Sessions.Add(session);
        SwitchToSession(session);
        OnPropertyChanged(nameof(HasMultipleSessions));
        // New session = clean slate — no auto-loaded data
        StatusText = "New session — select the client folder to begin";
    }

    [RelayCommand]
    private void CloseSession()
    {
        if (_currentSession == null || Sessions.Count <= 1) return;
        CloseSessionByTab(_currentSession);
    }

    [RelayCommand]
    private void CloseSessionByTab(SessionViewModel? session)
    {
        if (session == null || Sessions.Count <= 1) return;
        int idx = Sessions.IndexOf(session);
        if (idx < 0) return;

        // If closing the secondary pane session, unsplit
        if (session == SecondaryPaneSession)
            Unsplit();

        // Save & remove
        SaveCurrentToSession(session);
        Sessions.Remove(session);
        OnPropertyChanged(nameof(HasMultipleSessions));

        // Switch to adjacent session
        if (Sessions.Count > 0)
        {
            int newIdx = Math.Min(idx, Sessions.Count - 1);
            SwitchToSession(Sessions[newIdx]);
        }
    }

    public void SwitchToSession(SessionViewModel session)
    {
        if (_currentSession == session || _isSwitchingSession) return;

        _isSwitchingSession = true;
        try
        {
            // Save current state to the outgoing session
            if (_currentSession != null)
            {
                SaveCurrentToSession(_currentSession);
                _currentSession.IsActive = false;
            }

            ActiveSession = session;
            session.IsActive = true;
            LoadFromSession(session);
            OnPropertyChanged(nameof(ActiveSessionLabel));
        }
        finally
        {
            _isSwitchingSession = false;
        }
    }

    private bool _isSwitchingSession;

    private void SaveCurrentToSession(SessionViewModel session)
    {
        session.OtbData = _otbData;
        session.DatData = _datData;
        session.SprFile = _sprFile;
        session.OtbPath = _otbPath;
        session.ClientFolderPath = ClientFolderPath;
        session.MapData = MapData;
        session.MapFilePath = MapFilePath;
        session.BrushDb = BrushDb;
        session.Palette = Palette;
        session.AllItems = new List<ItemViewModel>(_allItems);
        session.AllClientItems = new List<ClientItemViewModel>(_allClientItems);
        session.HasUnsavedChanges = HasUnsavedChanges;
        session.IsClientLoaded = IsClientLoaded;
        session.MapHasUnsavedChanges = MapHasUnsavedChanges;
        session.ProtocolVersion = _datData?.ProtocolVersion ?? 0;

        // Save UI state: selection, page, filters
        session.SelectedClientItemId = SelectedClientItem?.Id;
        session.SelectedOtbItemServerId = SelectedItem?.ServerId;
        session.ClientCurrentPage = ClientCurrentPage;
        session.OtbPanelCurrentPage = OtbPanelCurrentPage;
        session.RightSpriteCurrentPage = RightSpriteCurrentPage;
        session.ClientCategoryFilter = ClientCategoryFilter;
        session.ClientSearchText = ClientSearchText;
        session.SearchText = SearchText;

        // Save map viewport state
        if (_mapGetViewport != null)
        {
            var (vx, vy) = _mapGetViewport();
            session.MapViewX = vx;
            session.MapViewY = vy;
        }
        session.MapCurrentFloor = MapCurrentFloor;
        session.MapZoom = MapZoom;

        session.UpdateName();
    }

    /// <summary>Update the active session's name from the current loaded data.</summary>
    private void UpdateActiveSessionName()
    {
        if (_currentSession == null) return;
        _currentSession.ProtocolVersion = _datData?.ProtocolVersion ?? 0;
        _currentSession.OtbPath = _otbPath;
        _currentSession.ClientFolderPath = ClientFolderPath;
        _currentSession.UpdateName();
        OnPropertyChanged(nameof(ActiveSessionLabel));
    }

    private void LoadFromSession(SessionViewModel session)
    {
        _otbData = session.OtbData;
        _datData = session.DatData;
        _sprFile = session.SprFile;
        _otbPath = session.OtbPath;
        ClientFolderPath = session.ClientFolderPath;

        // Restore viewport BEFORE setting MapData so CenterOnMap is suppressed
        if (session.MapData != null && _mapRestoreViewport != null)
            _mapRestoreViewport(session.MapViewX, session.MapViewY);

        MapData = session.MapData;
        MapFilePath = session.MapFilePath;
        BrushDb = session.BrushDb;
        Palette = session.Palette;
        _allItems = session.AllItems ?? [];
        _allClientItems = session.AllClientItems ?? [];
        HasUnsavedChanges = session.HasUnsavedChanges;
        IsClientLoaded = session.IsClientLoaded;
        MapHasUnsavedChanges = session.MapHasUnsavedChanges;

        // Refresh computed properties
        OnPropertyChanged(nameof(ExposedDatData));
        OnPropertyChanged(nameof(ExposedSprFile));
        OnPropertyChanged(nameof(ExposedOtbData));
        OnPropertyChanged(nameof(BrushDb));
        OnPropertyChanged(nameof(MapFloors));
        OnPropertyChanged(nameof(ActiveSessionLabel));

        // Restore UI state (filters, page) BEFORE rebuilding visible collections.
        // Set backing fields directly to avoid triggering filter re-runs from property setters.
        #pragma warning disable MVVMTK0034
        _searchText = session.SearchText;
        OnPropertyChanged(nameof(SearchText));
        _clientSearchText = session.ClientSearchText;
        OnPropertyChanged(nameof(ClientSearchText));
        _clientCategoryFilter = session.ClientCategoryFilter;
        OnPropertyChanged(nameof(ClientCategoryFilter));
        #pragma warning restore MVVMTK0034

        // Clear visible collections — they will be repopulated by the filters below
        Items.Clear();
        ClientItems.Clear();
        TotalItems = 0;
        FilteredCount = 0;

        if (_otbData != null && _allItems.Count > 0)
        {
            // Items already built — just re-apply filter + restore page
            TotalItems = _allItems.Count;
            ApplyFilter();

            // Restore OTB panel page and selection
            OtbPanelCurrentPage = Math.Clamp(session.OtbPanelCurrentPage, 1, Math.Max(1, OtbPanelTotalPages));
            LoadOtbPanelPage();
            if (session.SelectedOtbItemServerId is ushort sid)
                SelectedItem = _allItems.FirstOrDefault(i => i.ServerId == sid);
        }
        else if (_otbData != null)
        {
            BuildItemList();
            ApplyFilter();
        }

        if (_datData != null && _sprFile != null && _allClientItems.Count > 0)
        {
            // Client items already built with sprites — just re-apply filter + restore page
            ApplyClientFilter();
            ClientCurrentPage = Math.Clamp(session.ClientCurrentPage, 1, Math.Max(1, ClientTotalPages));
            LoadClientPage();
            StartAnimationTimer();

            // Restore selection
            if (session.SelectedClientItemId is ushort cid)
            {
                var match = ClientItems.FirstOrDefault(c => c.Id == cid)
                         ?? _allClientItems.FirstOrDefault(c => c.Id == cid);
                if (match != null)
                    SelectedClientItem = match;
            }
        }
        else if (_datData != null && _sprFile != null)
        {
            BuildClientItemList();
        }

        // Restore sprite panel page
        if (_sprFile != null)
        {
            RightSpriteCurrentPage = Math.Clamp(session.RightSpriteCurrentPage, 1, Math.Max(1, RightSpriteTotalPages));
            LoadRightSpritePage();
        }

        MapTileCount = MapData?.Tiles.Count ?? 0;

        // Restore map viewport state
        MapCurrentFloor = session.MapCurrentFloor;
        MapZoom = session.MapZoom;

        StatusText = IsClientLoaded
            ? $"Session loaded: {_datData?.ItemCount ?? 0} items"
            : "Select the client folder to begin";
    }

    /// <summary>Creates the default session on startup.</summary>
    private void EnsureDefaultSession()
    {
        if (Sessions.Count == 0)
        {
            var session = new SessionViewModel { Name = "Default", IsActive = true };
            Sessions.Add(session);
            ActiveSession = session;
        }
    }

    // ── Cross-version transplanting ──

    [RelayCommand]
    private async Task TransplantItemAsync()
    {
        if (Sessions.Count < 2)
        {
            StatusText = "Need at least 2 sessions open to transplant.";
            return;
        }

        // Must have a selected client item in the active session
        if (SelectedClientItem == null || _datData == null || _sprFile == null)
        {
            StatusText = "Select a client item first.";
            return;
        }

        var sourceItem = SelectedClientItem;
        var sourceProtocol = _datData.ProtocolVersion;

        // Find other sessions to transplant to
        var targets = Sessions.Where(s => s != _currentSession && s.DatData != null).ToList();
        if (targets.Count == 0)
        {
            StatusText = "No other session with a loaded DAT file.";
            return;
        }

        // Use the first other session as target (for now; could show picker for 3+)
        var targetSession = targets[0];
        var targetDat = targetSession.DatData!;
        var targetProtocol = targetDat.ProtocolVersion;

        // Get the thing type from the correct category dictionary
        var sourceDict = GetCategoryDict(_datData, sourceItem.Category);
        if (!sourceDict.TryGetValue(sourceItem.Id, out var sourceThing))
        {
            StatusText = $"Client {sourceItem.Category} {sourceItem.Id} not found in DAT.";
            return;
        }

        // Build compatibility report
        var report = TransplantReport.Compare(sourceThing, sourceProtocol, targetProtocol);

        // Capture source SPR at call time (before user can switch sessions)
        var sourceSpr = _sprFile;

        // Show transplant dialog
        await ShowTransplantDialog(report, sourceThing, targetSession, targetDat, sourceSpr);
    }

    private async Task ShowTransplantDialog(TransplantReport report, DatThingType sourceThing,
        SessionViewModel targetSession, DatData targetDat, SprFile sourceSpr)
    {
        var categoryLabel = sourceThing.Category.ToString().ToLowerInvariant();

        // Build message
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Transplant {categoryLabel} {sourceThing.Id} from protocol {report.SourceProtocol} → {report.TargetProtocol}");
        sb.AppendLine();

        if (report.PreservedAttributes.Count > 0)
        {
            sb.AppendLine("✓ Preserved attributes:");
            foreach (var attr in report.PreservedAttributes)
                sb.AppendLine($"  • {attr}");
            sb.AppendLine();
        }

        if (report.IgnoredAttributes.Count > 0)
        {
            sb.AppendLine("⚠ Ignored attributes (not supported in target version):");
            foreach (var attr in report.IgnoredAttributes)
                sb.AppendLine($"  • {attr}");
            sb.AppendLine();
        }

        if (report.MissingAttributes.Count > 0)
        {
            sb.AppendLine("ℹ Missing attributes (not present in source version):");
            foreach (var attr in report.MissingAttributes)
                sb.AppendLine($"  • {attr}");
            sb.AppendLine();
        }

        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;

        if (window == null) return;

        var dialog = new Avalonia.Controls.Window
        {
            Title = "Transplant Compatibility Report",
            Width = 500,
            Height = 450,
            Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.DockPanel
            {
                Margin = new Avalonia.Thickness(16),
                Children =
                {
                    new Avalonia.Controls.StackPanel
                    {
                        [Avalonia.Controls.DockPanel.DockProperty] = Avalonia.Controls.Dock.Bottom,
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Avalonia.Thickness(0, 12, 0, 0),
                    },
                    new Avalonia.Controls.ScrollViewer
                    {
                        Content = new Avalonia.Controls.TextBlock
                        {
                            Text = sb.ToString(),
                            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
                            FontSize = 13,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        }
                    }
                }
            }
        };

        // Add buttons to bottom panel
        var buttonPanel = (Avalonia.Controls.StackPanel)((Avalonia.Controls.DockPanel)dialog.Content).Children[0];
        var confirmBtn = new Avalonia.Controls.Button
        {
            Content = "Transplant",
            Background = Avalonia.Media.Brush.Parse("#89b4fa"),
            Foreground = Avalonia.Media.Brush.Parse("#1e1e2e"),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Avalonia.Thickness(16, 8),
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(16, 8),
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        cancelBtn.Click += (_, _) => dialog.Close();
        confirmBtn.Click += (_, _) =>
        {
            // Perform the transplant: clone thing and add to correct target category
            var clone = sourceThing.Clone();
            var targetDict = GetCategoryDict(targetDat, sourceThing.Category);
            ushort baseId = sourceThing.Category == ThingCategory.Item ? (ushort)99 : (ushort)0;
            ushort newId = (ushort)(targetDict.Keys.DefaultIfEmpty(baseId).Max() + 1);
            clone.Id = newId;

            // Strip unsupported flags for downcast
            if (report.IsDowncast)
                StripUnsupportedFlags(clone, report.TargetProtocol);

            // Adapt frame groups for cross-protocol outfit migration
            AdaptFrameGroups(clone, report.SourceProtocol, report.TargetProtocol);

            // Copy sprites to target session's SPR file (BEFORE adding to DAT)
            if (targetSession.SprFile != null)
                RemapSpritesToTarget(clone, sourceSpr, targetSession.SprFile);

            targetDict[newId] = clone;

            // Rebuild target session's item list so it appears immediately when switching
            targetSession.HasUnsavedChanges = true;
            RebuildSessionClientItems(targetSession);

            StatusText = $"Transplanted {categoryLabel} {sourceThing.Id} → {newId} in {targetSession.Name}";
            AddMapLog($"Transplanted: {categoryLabel} #{sourceThing.Id} (proto {report.SourceProtocol}) → #{newId} (proto {report.TargetProtocol})");
            dialog.Close();
        };
        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(confirmBtn);

        await dialog.ShowDialog(window);
    }

    private static void StripUnsupportedFlags(DatThingType thing, int targetProtocol)
    {
        // Strip flags not present in the target version (per OB MetadataFlags).
        // The write methods already only write version-appropriate flags,
        // but stripping keeps the in-memory model clean.
        if (targetProtocol <= 986) // V5 and below: no V6-only flags
        {
            thing.IsNoMoveAnimation = false;
            thing.HasDefaultAction = false; thing.DefaultAction = 0;
            thing.IsWrappable = false;
            thing.IsUnwrappable = false;
            thing.IsTopEffect = false;
            thing.IsUsable = false;
        }
        if (targetProtocol <= 854) // V4 and below: no V5-only flags
        {
            thing.IsTranslucent = false;
            thing.IsCloth = false; thing.ClothSlot = 0;
            thing.IsMarketItem = false; thing.MarketCategory = 0; thing.MarketTradeAs = 0;
            thing.MarketShowAs = 0; thing.MarketName = string.Empty;
            thing.MarketRestrictProfession = 0; thing.MarketRestrictLevel = 0;
        }
        if (targetProtocol <= 772) // V3 and below: no V4-only flags
        {
            thing.HasCharges = false;
            thing.IsDontHide = false;
            thing.IsIgnoreLook = false;
        }
    }

    /// <summary>
    /// Adapts frame group structure when transplanting between protocols with different
    /// frame group support. Protocol &gt;= 1057 uses frame groups (idle/walking) for outfits;
    /// protocol &gt;= 1050 uses enhanced animations (frameDurations).
    /// </summary>
    private static void AdaptFrameGroups(DatThingType thing, int sourceProtocol, int targetProtocol)
    {
        if (thing.Category != ThingCategory.Outfit) return;
        if (thing.FrameGroups.Length == 0) return;

        bool sourceHasGroups = sourceProtocol >= 1057;
        bool targetHasGroups = targetProtocol >= 1057;
        bool sourceHasAnimData = sourceProtocol >= 1050;
        bool targetHasAnimData = targetProtocol >= 1050;

        // Frame group adaptation (1057 boundary)
        if (sourceHasGroups && !targetHasGroups)
        {
            // Downcast frame groups: keep only the first group
            thing.FrameGroups = [thing.FrameGroups[0]];
            thing.FrameGroups[0].Type = FrameGroupType.Default;
        }
        else if (!sourceHasGroups && targetHasGroups)
        {
            // Upcast frame groups: ensure Type = Default
            thing.FrameGroups[0].Type = FrameGroupType.Default;
        }

        // Improved animation adaptation (1050 boundary)
        if (!sourceHasAnimData && targetHasAnimData)
        {
            // Upcast: populate FrameDurations with defaults for animated groups
            foreach (var fg in thing.FrameGroups)
            {
                if (fg.Frames > 1 && (fg.FrameDurations == null || fg.FrameDurations.Length != fg.Frames))
                {
                    fg.FrameDurations = new FrameDuration[fg.Frames];
                    for (int i = 0; i < fg.Frames; i++)
                        fg.FrameDurations[i] = new FrameDuration { Minimum = 100, Maximum = 100 };
                }
            }
        }
        else if (sourceHasAnimData && !targetHasAnimData)
        {
            // Downcast: strip enhanced animation metadata
            foreach (var fg in thing.FrameGroups)
            {
                fg.AnimationMode = AnimationMode.Async;
                fg.LoopCount = 0;
                fg.StartFrame = 0;
                fg.FrameDurations = [];
            }
        }
    }

    // ── Full session merge (DAT/SPR) ──

    /// <summary>
    /// Merge things from a source session into the current (active) session.
    /// When categoryFilter is null, merges all categories; otherwise only the specified one.
    /// Detects duplicates by comparing sprite images and shows a batch preview dialog.
    /// </summary>
    public async Task MergeSessionAsync(SessionViewModel sourceSession, ThingCategory? categoryFilter = null)
    {
        if (_datData == null || _sprFile == null)
        {
            StatusText = "Current session has no DAT/SPR loaded.";
            return;
        }
        if (sourceSession.DatData == null || sourceSession.SprFile == null)
        {
            StatusText = "Source session has no DAT/SPR loaded.";
            return;
        }

        var sourceDat = sourceSession.DatData;
        var sourceSpr = sourceSession.SprFile;
        var sourceProtocol = sourceDat.ProtocolVersion;
        var targetProtocol = _datData.ProtocolVersion;

        var allCategories = new[]
        {
            (ThingCategory.Item,    sourceDat.Items,    _datData.Items),
            (ThingCategory.Outfit,  sourceDat.Outfits,  _datData.Outfits),
            (ThingCategory.Effect,  sourceDat.Effects,   _datData.Effects),
            (ThingCategory.Missile, sourceDat.Missiles,  _datData.Missiles),
        };

        var categories = categoryFilter.HasValue
            ? allCategories.Where(c => c.Item1 == categoryFilter.Value).ToArray()
            : allCategories;

        int totalSource = categories.Sum(c => c.Item2.Count);
        var filterLabel = categoryFilter?.ToString().ToLowerInvariant() ?? "all";
        StatusText = $"Analyzing {totalSource} source {filterLabel} things for duplicates…";

        // Analyze each category
        var entries = new List<TransplantEntry>();
        foreach (var (cat, sourceDict, targetDict) in categories)
        {
            if (sourceDict.Count == 0) continue;
            var targetIndex = BuildSpriteHashIndex(targetDict, _sprFile);

            foreach (var (id, sourceThing) in sourceDict.OrderBy(x => x.Key))
            {
                var report = TransplantReport.Compare(sourceThing, sourceProtocol, targetProtocol);
                var duplicateId = FindDuplicateByImage(sourceThing, sourceSpr, targetIndex);
                bool empty = IsThingEmpty(sourceThing);

                entries.Add(new TransplantEntry
                {
                    Category = cat,
                    SourceId = id,
                    SourceThing = sourceThing,
                    Report = report,
                    DuplicateTargetId = duplicateId,
                    IsEmpty = empty,
                    Action = (duplicateId.HasValue || empty) ? TransplantAction.Skip : TransplantAction.Add,
                });
            }
        }

        int dupCount = entries.Count(e => e.DuplicateTargetId.HasValue);
        int emptyCount = entries.Count(e => e.IsEmpty);
        StatusText = $"Analyzed {entries.Count} things — {dupCount} duplicates, {emptyCount} empty, {entries.Count - dupCount - emptyCount} new.";

        // Reuse the batch transplant dialog but pass source SPR for sprite remapping
        await ShowMergeDialog(entries, sourceSession, sourceSpr, sourceProtocol, targetProtocol);
    }

    /// <summary>
    /// Executes the merge: clones things from sourceSession into the active session.
    /// </summary>
    private void ExecuteMerge(
        List<TransplantEntry> entries,
        SprFile sourceSpr,
        int sourceProtocol,
        int targetProtocol)
    {
        if (_datData == null || _sprFile == null) return;

        int transplanted = 0;
        int skipped = 0;

        // Track next available ID per category
        var nextIds = new Dictionary<ThingCategory, ushort>
        {
            [ThingCategory.Item]    = (ushort)(_datData.Items.Keys.DefaultIfEmpty((ushort)99).Max() + 1),
            [ThingCategory.Outfit]  = (ushort)(_datData.Outfits.Keys.DefaultIfEmpty((ushort)0).Max() + 1),
            [ThingCategory.Effect]  = (ushort)(_datData.Effects.Keys.DefaultIfEmpty((ushort)0).Max() + 1),
            [ThingCategory.Missile] = (ushort)(_datData.Missiles.Keys.DefaultIfEmpty((ushort)0).Max() + 1),
        };

        foreach (var entry in entries)
        {
            // Duplicates and empty things are always skipped — never copied
            if (entry.Action == TransplantAction.Skip || entry.DuplicateTargetId.HasValue || entry.IsEmpty)
            {
                skipped++;
                continue;
            }

            var clone = entry.SourceThing.Clone();

            // Strip unsupported flags for downcast
            if (sourceProtocol > targetProtocol)
                StripUnsupportedFlags(clone, targetProtocol);

            // Adapt frame groups for cross-protocol outfit migration
            AdaptFrameGroups(clone, sourceProtocol, targetProtocol);

            // Copy sprites from source SPR → current session's SPR
            RemapSpritesToTarget(clone, sourceSpr, _sprFile);

            var targetDict = GetCategoryDict(_datData, entry.Category);
            clone.Id = nextIds[entry.Category]++;
            targetDict[clone.Id] = clone;
            transplanted++;

            entry.NewTargetId = clone.Id;
        }

        HasUnsavedChanges = true;

        // Rebuild client item list for current session
        if (_currentSession != null)
        {
            _currentSession.DatData = _datData;
            _currentSession.SprFile = _sprFile;
            _currentSession.HasUnsavedChanges = true;
        }
        BuildClientItemList();

        var msg = $"Merge complete: {transplanted} added, {skipped} skipped (duplicates/empty excluded).";
        StatusText = msg;
        AddMapLog(msg);
    }

    // ── Multi-item cross-version transplanting ──

    /// <summary>
    /// Transplant multiple selected things to a target session with image-based duplicate detection.
    /// </summary>
    public async Task TransplantMultipleItemsAsync(SessionViewModel targetSession)
    {
        if (_datData == null || _sprFile == null || targetSession.DatData == null)
        {
            StatusText = "Source or target session data not loaded.";
            return;
        }

        var selectedItems = SelectedClientItemsList;
        if (selectedItems.Count == 0)
        {
            StatusText = "No items selected for transplant.";
            return;
        }

        var targetDat = targetSession.DatData;
        var targetSpr = targetSession.SprFile;
        var sourceProtocol = _datData.ProtocolVersion;
        var targetProtocol = targetDat.ProtocolVersion;

        StatusText = $"Analyzing {selectedItems.Count} things for duplicates...";

        // Build per-category hash indexes for duplicate detection
        var targetIndexes = new Dictionary<ThingCategory, Dictionary<long, List<ushort>>>();

        // Analyze each selected thing
        var entries = new List<TransplantEntry>();
        foreach (var cvm in selectedItems)
        {
            var cat = cvm.Category;
            var sourceDict = GetCategoryDict(_datData, cat);
            if (!sourceDict.TryGetValue(cvm.Id, out var sourceThing))
                continue;

            // Lazily build target index per category
            if (!targetIndexes.TryGetValue(cat, out var targetIndex))
            {
                targetIndex = BuildSpriteHashIndex(GetCategoryDict(targetDat, cat), targetSpr);
                targetIndexes[cat] = targetIndex;
            }

            var report = TransplantReport.Compare(sourceThing, sourceProtocol, targetProtocol);
            var duplicateId = FindDuplicateByImage(sourceThing, _sprFile, targetIndex);
            bool empty = IsThingEmpty(sourceThing);

            entries.Add(new TransplantEntry
            {
                Category = cat,
                SourceId = cvm.Id,
                SourceThing = sourceThing,
                Report = report,
                DuplicateTargetId = duplicateId,
                IsEmpty = empty,
                Action = (duplicateId.HasValue || empty) ? TransplantAction.Skip : TransplantAction.Add,
            });
        }

        StatusText = $"Analyzed {entries.Count} things — {entries.Count(e => e.DuplicateTargetId.HasValue)} duplicates found.";

        // Capture source SPR at call time (before user can switch sessions)
        var sourceSpr = _sprFile;

        // Show the transplant preview dialog
        await ShowBatchTransplantDialog(entries, targetSession, targetDat, targetSpr, sourceSpr, sourceProtocol, targetProtocol);
    }

    /// <summary>
    /// Builds a hash index of sprites in a dictionary for fast duplicate detection.
    /// Key = sprite hash, Value = list of thing IDs with that hash.
    /// </summary>
    private static Dictionary<long, List<ushort>> BuildSpriteHashIndex(Dictionary<ushort, DatThingType> dict, SprFile? sprFile)
    {
        var index = new Dictionary<long, List<ushort>>();
        if (sprFile == null) return index;

        foreach (var (id, thing) in dict)
        {
            if (thing.FrameGroups.Length == 0) continue;

            long hash = ComputeSpriteHash(thing.FrameGroups, sprFile);
            if (!index.TryGetValue(hash, out var list))
            {
                list = [];
                index[hash] = list;
            }
            list.Add(id);
        }

        return index;
    }

    /// <summary>
    /// Computes a hash of the composed sprite data for all frame groups and all frames.
    /// Produces a unique fingerprint that considers the full visual identity of a thing.
    /// </summary>
    private static long ComputeSpriteHash(FrameGroup[] frameGroups, SprFile sprFile)
    {
        long hash = 17;

        for (int fgi = 0; fgi < frameGroups.Length; fgi++)
        {
            var fg = frameGroups[fgi];
            if (fg.SpriteIndex.Length == 0) continue;

            int w = fg.Width, h = fg.Height;
            int layers = fg.Layers;
            int frames = fg.Frames;
            int patX = fg.PatternX, patY = fg.PatternY, patZ = fg.PatternZ;

            // Mix in frame group index to distinguish idle vs walking
            hash = hash * 31 + fgi;

            for (int f = 0; f < frames; f++)
            {
                for (int pz = 0; pz < patZ; pz++)
                {
                    for (int py = 0; py < patY; py++)
                    {
                        for (int px = 0; px < patX; px++)
                        {
                            for (int l = 0; l < layers; l++)
                            {
                                for (int tw = 0; tw < w; tw++)
                                {
                                    for (int th = 0; th < h; th++)
                                    {
                                        uint sprId = fg.GetSpriteId(tw, th, l, px, py, pz, f);
                                        var rgba = sprFile.GetSpriteRgba(sprId);
                                        if (rgba == null)
                                        {
                                            hash = hash * 31 + sprId;
                                            continue;
                                        }

                                        // FNV-1a style hash over RGBA bytes
                                        for (int i = 0; i < rgba.Length; i += 16)
                                        {
                                            long v = rgba[i] | ((long)rgba[i + 1] << 8)
                                                             | ((long)rgba[i + 4] << 16)
                                                             | ((long)rgba[i + 5] << 24)
                                                             | ((long)rgba[i + 8] << 32)
                                                             | ((long)rgba[i + 9] << 40)
                                                             | ((long)rgba[i + 12] << 48)
                                                             | ((long)rgba[i + 13] << 56);
                                            hash ^= v;
                                            hash *= unchecked((long)0x100000001B3);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return hash;
    }

    /// <summary>
    /// Finds a duplicate item in the target session by comparing sprite images.
    /// Returns the target item ID if a match is found, null otherwise.
    /// </summary>
    private static ushort? FindDuplicateByImage(
        DatThingType source, SprFile sourceSpr,
        Dictionary<long, List<ushort>> targetIndex)
    {
        if (source.FrameGroups.Length == 0) return null;

        long hash = ComputeSpriteHash(source.FrameGroups, sourceSpr);
        if (targetIndex.TryGetValue(hash, out var matches) && matches.Count > 0)
            return matches[0];

        return null;
    }

    /// <summary>
    /// Shows a merge preview dialog — similar to batch transplant but for merging a full session into the current one.
    /// </summary>
    private async Task ShowMergeDialog(
        List<TransplantEntry> entries,
        SessionViewModel sourceSession,
        SprFile sourceSpr,
        int sourceProtocol,
        int targetProtocol)
    {
        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window == null) return;

        int dupCount = entries.Count(e => e.DuplicateTargetId.HasValue);
        int newCount = entries.Count - dupCount;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Merge Session: {entries.Count} things from \"{sourceSession.Name}\" (protocol {sourceProtocol}) → current session (protocol {targetProtocol})");

        // Per-category breakdown
        foreach (var cat in new[] { ThingCategory.Item, ThingCategory.Outfit, ThingCategory.Effect, ThingCategory.Missile })
        {
            int catCount = entries.Count(e => e.Category == cat);
            if (catCount > 0)
                sb.AppendLine($"    {cat}: {catCount}");
        }
        sb.AppendLine();
        int emptyCount = entries.Count(e => e.IsEmpty);
        sb.AppendLine($"  ● New (no match found): {newCount - emptyCount}");
        sb.AppendLine($"  ● Duplicates detected by sprite image: {dupCount}");
        if (emptyCount > 0)
            sb.AppendLine($"  ● Empty (no sprites): {emptyCount}");
        sb.AppendLine();

        var anyIgnored = entries.Any(e => e.Report.IgnoredAttributes.Count > 0);
        var anyMissing = entries.Any(e => e.Report.MissingAttributes.Count > 0);
        if (anyIgnored)
        {
            var ignoredSet = entries.SelectMany(e => e.Report.IgnoredAttributes).Distinct().ToList();
            sb.AppendLine("⚠ Attributes that will be stripped (not supported in target):");
            foreach (var attr in ignoredSet)
                sb.AppendLine($"    • {attr}");
            sb.AppendLine();
        }
        if (anyMissing)
        {
            var missingSet = entries.SelectMany(e => e.Report.MissingAttributes).Distinct().ToList();
            sb.AppendLine("ℹ Attributes missing in source (target supports more):");
            foreach (var attr in missingSet)
                sb.AppendLine($"    • {attr}");
            sb.AppendLine();
        }

        sb.AppendLine("Uncheck items to skip them. Duplicates and empty sprites are automatically excluded.");

        // Build item rows with sprite previews rendered from source SPR
        var itemsPanel = new Avalonia.Controls.StackPanel { Spacing = 2 };

        // Track animated entries for the dialog's animation timer
        var animatedEntries = new List<(Avalonia.Controls.Image img, DatThingType thing, int frames, int fgIndex)>();

        foreach (var entry in entries)
        {
            var rowGrid = new Avalonia.Controls.Grid
            {
                ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,38,*,Auto"),
                Height = 42,
                Margin = new Avalonia.Thickness(0, 1),
            };

            var isDuplicate = entry.DuplicateTargetId.HasValue;
            var isAutoSkip = isDuplicate || entry.IsEmpty;
            var cb = new Avalonia.Controls.CheckBox
            {
                IsChecked = !isAutoSkip && entry.Action != TransplantAction.Skip,
                IsEnabled = !isAutoSkip,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(4, 0),
            };
            var capturedEntry = entry;
            cb.IsCheckedChanged += (_, _) =>
            {
                capturedEntry.Action = cb.IsChecked == true
                    ? TransplantAction.Add
                    : TransplantAction.Skip;
            };
            Avalonia.Controls.Grid.SetColumn(cb, 0);
            rowGrid.Children.Add(cb);

            // Sprite preview (from SOURCE session's SPR)
            var spriteBorder = new Avalonia.Controls.Border
            {
                Background = Avalonia.Media.Brush.Parse("#11111b"),
                CornerRadius = new Avalonia.CornerRadius(4),
                Width = 32, Height = 32,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                ClipToBounds = true,
            };
            var bmp = ComposeThingBitmapStatic(entry.SourceThing, sourceSpr);
            Avalonia.Controls.Image? spriteImg = null;
            if (bmp != null)
            {
                spriteImg = new Avalonia.Controls.Image
                {
                    Source = bmp, Width = 32, Height = 32,
                    Stretch = Avalonia.Media.Stretch.Uniform,
                };
                Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(spriteImg, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
                spriteBorder.Child = spriteImg;

                // Register for animation — pick the frame group with most frames
                int bestFgIdx = 0;
                int bestFrames = 0;
                for (int i = 0; i < entry.SourceThing.FrameGroups.Length; i++)
                {
                    if (entry.SourceThing.FrameGroups[i].Frames > bestFrames)
                    {
                        bestFrames = entry.SourceThing.FrameGroups[i].Frames;
                        bestFgIdx = i;
                    }
                }
                if (bestFrames > 1)
                    animatedEntries.Add((spriteImg, entry.SourceThing, bestFrames, bestFgIdx));
            }
            Avalonia.Controls.Grid.SetColumn(spriteBorder, 1);
            rowGrid.Children.Add(spriteBorder);

            var textBlock = new Avalonia.Controls.TextBlock
            {
                Text = $"#{entry.SourceId}  ({entry.SourceThing.Category})",
                Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(6, 0),
            };
            Avalonia.Controls.Grid.SetColumn(textBlock, 2);
            rowGrid.Children.Add(textBlock);

            string statusText;
            string statusColor;
            if (entry.DuplicateTargetId.HasValue)
            {
                statusText = $"Duplicate → #{entry.DuplicateTargetId}";
                statusColor = "#f9e2af";
            }
            else if (entry.IsEmpty)
            {
                statusText = "Empty";
                statusColor = "#6c7086";
            }
            else
            {
                statusText = "New";
                statusColor = "#a6e3a1";
            }
            var statusBlock = new Avalonia.Controls.TextBlock
            {
                Text = statusText,
                Foreground = Avalonia.Media.Brush.Parse(statusColor),
                FontSize = 11,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(4, 0, 8, 0),
            };
            Avalonia.Controls.Grid.SetColumn(statusBlock, 3);
            rowGrid.Children.Add(statusBlock);

            var rowBorder = new Avalonia.Controls.Border
            {
                Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
                CornerRadius = new Avalonia.CornerRadius(4),
                Child = rowGrid,
            };
            itemsPanel.Children.Add(rowBorder);
        }

        var dialog = new Avalonia.Controls.Window
        {
            Title = $"Merge Session — {sourceSession.Name}",
            Width = 650,
            MinHeight = 350,
            MaxHeight = 750,
            Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.DockPanel
            {
                Margin = new Avalonia.Thickness(16),
                Children =
                {
                    new Avalonia.Controls.StackPanel
                    {
                        [Avalonia.Controls.DockPanel.DockProperty] = Avalonia.Controls.Dock.Bottom,
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Avalonia.Thickness(0, 12, 0, 0),
                    },
                    new Avalonia.Controls.TextBlock
                    {
                        [Avalonia.Controls.DockPanel.DockProperty] = Avalonia.Controls.Dock.Top,
                        Text = sb.ToString(),
                        Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
                        FontSize = 12,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(0, 0, 0, 8),
                    },
                    new Avalonia.Controls.ScrollViewer
                    {
                        Content = itemsPanel,
                    },
                }
            }
        };

        var buttonPanel = (Avalonia.Controls.StackPanel)((Avalonia.Controls.DockPanel)dialog.Content).Children[0];

        var selectAllBtn = new Avalonia.Controls.Button
        {
            Content = "Select All",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(12, 6),
            CornerRadius = new Avalonia.CornerRadius(6),
            Margin = new Avalonia.Thickness(0, 0, 8, 0),
        };
        selectAllBtn.Click += (_, _) =>
        {
            foreach (var row in itemsPanel.Children.OfType<Avalonia.Controls.Border>())
                if (row.Child is Avalonia.Controls.Grid g)
                {
                    var cb = g.Children.OfType<Avalonia.Controls.CheckBox>().FirstOrDefault();
                    if (cb?.IsEnabled == true)
                        cb.SetCurrentValue(Avalonia.Controls.CheckBox.IsCheckedProperty, (bool?)true);
                }
        };

        var deselectAllBtn = new Avalonia.Controls.Button
        {
            Content = "Deselect All",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(12, 6),
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        deselectAllBtn.Click += (_, _) =>
        {
            foreach (var row in itemsPanel.Children.OfType<Avalonia.Controls.Border>())
                if (row.Child is Avalonia.Controls.Grid g)
                    g.Children.OfType<Avalonia.Controls.CheckBox>().FirstOrDefault()?.SetCurrentValue(Avalonia.Controls.CheckBox.IsCheckedProperty, (bool?)false);
        };

        var spacer = new Avalonia.Controls.Border { Width = 1, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(16, 8),
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        var confirmBtn = new Avalonia.Controls.Button
        {
            Content = "Merge Selected",
            Background = Avalonia.Media.Brush.Parse("#cba6f7"),
            Foreground = Avalonia.Media.Brush.Parse("#1e1e2e"),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Avalonia.Thickness(16, 8),
            CornerRadius = new Avalonia.CornerRadius(6),
        };

        cancelBtn.Click += (_, _) => dialog.Close();
        confirmBtn.Click += (_, _) =>
        {
            ExecuteMerge(entries, sourceSpr, sourceProtocol, targetProtocol);
            dialog.Close();
        };

        buttonPanel.Children.Add(selectAllBtn);
        buttonPanel.Children.Add(deselectAllBtn);
        buttonPanel.Children.Add(spacer);
        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(confirmBtn);

        // Animation timer for sprite previews in the merge dialog
        DispatcherTimer? mergeAnimTimer = null;
        if (animatedEntries.Count > 0)
        {
            var animState = animatedEntries.Select(e => new { e.img, e.thing, e.frames, e.fgIndex, frame = new int[] { 0 } }).ToList();
            int tickCounter = 0;
            mergeAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            mergeAnimTimer.Tick += (_, _) =>
            {
                tickCounter++;
                foreach (var s in animState)
                {
                    int divisor = s.thing.Category switch
                    {
                        ThingCategory.Effect => 1,
                        ThingCategory.Missile => 1,
                        ThingCategory.Outfit => 3,
                        _ => 5,
                    };
                    if (tickCounter % divisor != 0) continue;
                    s.frame[0] = (s.frame[0] + 1) % s.frames;
                    var newBmp = ComposeThingBitmapStatic(s.thing, sourceSpr, s.frame[0], s.fgIndex);
                    if (newBmp != null)
                        s.img.Source = newBmp;
                }
            };
            mergeAnimTimer.Start();
        }

        dialog.Closed += (_, _) => mergeAnimTimer?.Stop();

        await dialog.ShowDialog(window);
    }

    /// <summary>
    /// Shows a batch transplant preview dialog with per-item duplicate analysis.
    /// </summary>
    private async Task ShowBatchTransplantDialog(
        List<TransplantEntry> entries,
        SessionViewModel targetSession,
        DatData targetDat,
        SprFile? targetSpr,
        SprFile sourceSpr,
        int sourceProtocol,
        int targetProtocol)
    {
        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window == null) return;

        int dupCount = entries.Count(e => e.DuplicateTargetId.HasValue);
        int newCount = entries.Count - dupCount;

        // Build the report text
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Batch Transplant: {entries.Count} things from protocol {sourceProtocol} → {targetProtocol}");
        sb.AppendLine($"Target session: {targetSession.Name}");

        // Per-category breakdown
        foreach (var cat in new[] { ThingCategory.Item, ThingCategory.Outfit, ThingCategory.Effect, ThingCategory.Missile })
        {
            int catCount = entries.Count(e => e.Category == cat);
            if (catCount > 0)
                sb.AppendLine($"    {cat}: {catCount}");
        }
        sb.AppendLine();
        sb.AppendLine($"  ● New (no match found): {newCount}");
        sb.AppendLine($"  ● Duplicates detected by image: {dupCount}");
        sb.AppendLine();

        // Attribute compatibility summary
        var anyIgnored = entries.Any(e => e.Report.IgnoredAttributes.Count > 0);
        var anyMissing = entries.Any(e => e.Report.MissingAttributes.Count > 0);
        if (anyIgnored)
        {
            var ignoredSet = entries.SelectMany(e => e.Report.IgnoredAttributes).Distinct().ToList();
            sb.AppendLine("⚠ Attributes that will be stripped (not supported in target):");
            foreach (var attr in ignoredSet)
                sb.AppendLine($"    • {attr}");
            sb.AppendLine();
        }
        if (anyMissing)
        {
            var missingSet = entries.SelectMany(e => e.Report.MissingAttributes).Distinct().ToList();
            sb.AppendLine("ℹ Attributes missing in source (target supports more):");
            foreach (var attr in missingSet)
                sb.AppendLine($"    • {attr}");
            sb.AppendLine();
        }

        // Build the item list with sprite previews
        var itemsPanel = new Avalonia.Controls.StackPanel { Spacing = 2 };

        // Track animated entries for the dialog's animation timer
        var batchAnimEntries = new List<(Avalonia.Controls.Image img, DatThingType thing, int frames, int fgIndex)>();

        foreach (var entry in entries)
        {
            var rowGrid = new Avalonia.Controls.Grid
            {
                ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,38,*,Auto"),
                Height = 42,
                Margin = new Avalonia.Thickness(0, 1),
            };

            // Checkbox to toggle action
            var isAutoSkipBatch = entry.DuplicateTargetId.HasValue || entry.IsEmpty;
            var cb = new Avalonia.Controls.CheckBox
            {
                IsChecked = !isAutoSkipBatch && entry.Action != TransplantAction.Skip,
                IsEnabled = !entry.IsEmpty,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(4, 0),
            };
            var capturedEntry = entry;
            cb.IsCheckedChanged += (_, _) =>
            {
                capturedEntry.Action = cb.IsChecked == true
                    ? (capturedEntry.DuplicateTargetId.HasValue ? TransplantAction.Replace : TransplantAction.Add)
                    : TransplantAction.Skip;
            };
            Avalonia.Controls.Grid.SetColumn(cb, 0);
            rowGrid.Children.Add(cb);

            // Sprite preview
            var spriteBorder = new Avalonia.Controls.Border
            {
                Background = Avalonia.Media.Brush.Parse("#11111b"),
                CornerRadius = new Avalonia.CornerRadius(4),
                Width = 32, Height = 32,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                ClipToBounds = true,
            };
            var bmp = ComposeThingBitmapStatic(entry.SourceThing, sourceSpr);
            if (bmp != null)
            {
                var img = new Avalonia.Controls.Image
                {
                    Source = bmp, Width = 32, Height = 32,
                    Stretch = Avalonia.Media.Stretch.Uniform,
                };
                Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(img, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
                spriteBorder.Child = img;

                int bestBatchFgIdx = 0;
                int bestBatchFrames = 0;
                for (int i = 0; i < entry.SourceThing.FrameGroups.Length; i++)
                {
                    if (entry.SourceThing.FrameGroups[i].Frames > bestBatchFrames)
                    {
                        bestBatchFrames = entry.SourceThing.FrameGroups[i].Frames;
                        bestBatchFgIdx = i;
                    }
                }
                if (bestBatchFrames > 1)
                    batchAnimEntries.Add((img, entry.SourceThing, bestBatchFrames, bestBatchFgIdx));
            }
            Avalonia.Controls.Grid.SetColumn(spriteBorder, 1);
            rowGrid.Children.Add(spriteBorder);

            // Item text
            var textBlock = new Avalonia.Controls.TextBlock
            {
                Text = $"#{entry.SourceId}  ({entry.SourceThing.Category})",
                Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(6, 0),
            };
            Avalonia.Controls.Grid.SetColumn(textBlock, 2);
            rowGrid.Children.Add(textBlock);

            // Status badge
            string statusText;
            string statusColor;
            if (entry.DuplicateTargetId.HasValue)
            {
                statusText = $"Duplicate → #{entry.DuplicateTargetId}";
                statusColor = "#f9e2af";
            }
            else if (entry.IsEmpty)
            {
                statusText = "Empty";
                statusColor = "#6c7086";
            }
            else
            {
                statusText = "New";
                statusColor = "#a6e3a1";
            }
            var statusBlock = new Avalonia.Controls.TextBlock
            {
                Text = statusText,
                Foreground = Avalonia.Media.Brush.Parse(statusColor),
                FontSize = 11,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(4, 0, 8, 0),
            };
            Avalonia.Controls.Grid.SetColumn(statusBlock, 3);
            rowGrid.Children.Add(statusBlock);

            var rowBorder = new Avalonia.Controls.Border
            {
                Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
                CornerRadius = new Avalonia.CornerRadius(4),
                Child = rowGrid,
            };
            itemsPanel.Children.Add(rowBorder);
        }

        var dialog = new Avalonia.Controls.Window
        {
            Title = "Batch Transplant Preview",
            Width = 620,
            MinHeight = 300,
            MaxHeight = 700,
            Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.DockPanel
            {
                Margin = new Avalonia.Thickness(16),
                Children =
                {
                    // Bottom buttons
                    new Avalonia.Controls.StackPanel
                    {
                        [Avalonia.Controls.DockPanel.DockProperty] = Avalonia.Controls.Dock.Bottom,
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Avalonia.Thickness(0, 12, 0, 0),
                    },
                    // Top summary
                    new Avalonia.Controls.TextBlock
                    {
                        [Avalonia.Controls.DockPanel.DockProperty] = Avalonia.Controls.Dock.Top,
                        Text = sb.ToString(),
                        Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
                        FontSize = 12,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(0, 0, 0, 8),
                    },
                    // Scrollable item list
                    new Avalonia.Controls.ScrollViewer
                    {
                        Content = itemsPanel,
                    },
                }
            }
        };

        var buttonPanel = (Avalonia.Controls.StackPanel)((Avalonia.Controls.DockPanel)dialog.Content).Children[0];

        // Select All / Deselect All buttons
        var selectAllBtn = new Avalonia.Controls.Button
        {
            Content = "Select All",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(12, 6),
            CornerRadius = new Avalonia.CornerRadius(6),
            Margin = new Avalonia.Thickness(0, 0, 8, 0),
        };
        selectAllBtn.Click += (_, _) =>
        {
            foreach (var row in itemsPanel.Children.OfType<Avalonia.Controls.Border>())
            {
                if (row.Child is Avalonia.Controls.Grid g)
                {
                    var cb = g.Children.OfType<Avalonia.Controls.CheckBox>().FirstOrDefault();
                    if (cb != null) cb.IsChecked = true;
                }
            }
        };

        var deselectAllBtn = new Avalonia.Controls.Button
        {
            Content = "Deselect All",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(12, 6),
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        deselectAllBtn.Click += (_, _) =>
        {
            foreach (var row in itemsPanel.Children.OfType<Avalonia.Controls.Border>())
            {
                if (row.Child is Avalonia.Controls.Grid g)
                {
                    var cb = g.Children.OfType<Avalonia.Controls.CheckBox>().FirstOrDefault();
                    if (cb != null) cb.IsChecked = false;
                }
            }
        };

        // Spacer
        var spacer = new Avalonia.Controls.Border { Width = 1, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(16, 8),
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        var confirmBtn = new Avalonia.Controls.Button
        {
            Content = "Transplant Selected",
            Background = Avalonia.Media.Brush.Parse("#89b4fa"),
            Foreground = Avalonia.Media.Brush.Parse("#1e1e2e"),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Avalonia.Thickness(16, 8),
            CornerRadius = new Avalonia.CornerRadius(6),
        };

        cancelBtn.Click += (_, _) => dialog.Close();
        confirmBtn.Click += (_, _) =>
        {
            ExecuteBatchTransplant(entries, targetSession, targetDat, targetSpr, sourceSpr, sourceProtocol, targetProtocol);
            dialog.Close();
        };

        buttonPanel.Children.Add(selectAllBtn);
        buttonPanel.Children.Add(deselectAllBtn);
        buttonPanel.Children.Add(spacer);
        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(confirmBtn);

        // Animation timer for sprite previews in the batch transplant dialog
        DispatcherTimer? batchAnimTimer = null;
        if (batchAnimEntries.Count > 0)
        {
            var animState = batchAnimEntries.Select(e => new { e.img, e.thing, e.frames, e.fgIndex, frame = new int[] { 0 } }).ToList();
            int tickCounter = 0;
            batchAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            batchAnimTimer.Tick += (_, _) =>
            {
                tickCounter++;
                foreach (var s in animState)
                {
                    int divisor = s.thing.Category switch
                    {
                        ThingCategory.Effect => 1,
                        ThingCategory.Missile => 1,
                        ThingCategory.Outfit => 3,
                        _ => 5,
                    };
                    if (tickCounter % divisor != 0) continue;
                    s.frame[0] = (s.frame[0] + 1) % s.frames;
                    var newBmp = ComposeThingBitmapStatic(s.thing, sourceSpr, s.frame[0], s.fgIndex);
                    if (newBmp != null)
                        s.img.Source = newBmp;
                }
            };
            batchAnimTimer.Start();
        }

        dialog.Closed += (_, _) => batchAnimTimer?.Stop();

        await dialog.ShowDialog(window);
    }

    /// <summary>
    /// Executes the batch transplant: clones things, copies sprites, adds to target DAT.
    /// </summary>
    private void ExecuteBatchTransplant(
        List<TransplantEntry> entries,
        SessionViewModel targetSession,
        DatData targetDat,
        SprFile? targetSpr,
        SprFile sourceSpr,
        int sourceProtocol,
        int targetProtocol)
    {
        int transplanted = 0;
        int replaced = 0;
        int skipped = 0;

        // Track next available ID per category
        var nextIds = new Dictionary<ThingCategory, ushort>
        {
            [ThingCategory.Item]    = (ushort)(targetDat.Items.Keys.DefaultIfEmpty((ushort)99).Max() + 1),
            [ThingCategory.Outfit]  = (ushort)(targetDat.Outfits.Keys.DefaultIfEmpty((ushort)0).Max() + 1),
            [ThingCategory.Effect]  = (ushort)(targetDat.Effects.Keys.DefaultIfEmpty((ushort)0).Max() + 1),
            [ThingCategory.Missile] = (ushort)(targetDat.Missiles.Keys.DefaultIfEmpty((ushort)0).Max() + 1),
        };

        foreach (var entry in entries)
        {
            if (entry.Action == TransplantAction.Skip)
            {
                skipped++;
                continue;
            }

            var clone = entry.SourceThing.Clone();

            // Strip unsupported flags for downcast
            if (sourceProtocol > targetProtocol)
                StripUnsupportedFlags(clone, targetProtocol);

            // Adapt frame groups for cross-protocol outfit migration
            AdaptFrameGroups(clone, sourceProtocol, targetProtocol);

            // Copy sprites to target SPR
            if (targetSpr != null)
                RemapSpritesToTarget(clone, sourceSpr, targetSpr);

            var targetDict = GetCategoryDict(targetDat, entry.Category);

            if (entry.Action == TransplantAction.Replace && entry.DuplicateTargetId.HasValue)
            {
                // Replace existing thing keeping same ID
                clone.Id = entry.DuplicateTargetId.Value;
                targetDict[clone.Id] = clone;
                replaced++;
            }
            else
            {
                // Add as new thing
                clone.Id = nextIds[entry.Category]++;
                targetDict[clone.Id] = clone;
                transplanted++;
            }

            entry.NewTargetId = clone.Id;
        }

        // Rebuild target session's AllClientItems so new things appear when switching
        targetSession.DatData = targetDat;
        targetSession.SprFile = targetSpr;
        targetSession.HasUnsavedChanges = true;
        RebuildSessionClientItems(targetSession);

        var msg = $"Batch transplant complete: {transplanted} added, {replaced} replaced, {skipped} skipped.";
        StatusText = msg;
        AddMapLog(msg);
    }

    /// <summary>
    /// Rebuilds AllClientItems for a target session using its own DAT+SPR data.
    /// Call after transplanting items so they appear when the user switches to that session.
    /// </summary>
    private static void RebuildSessionClientItems(SessionViewModel session)
    {
        if (session.DatData == null || session.SprFile == null) return;

        var list = new List<ClientItemViewModel>();

        void Add(Dictionary<ushort, DatThingType> dict)
        {
            foreach (var kvp in dict.OrderBy(x => x.Key))
            {
                var vm = new ClientItemViewModel(kvp.Value);
                vm.Sprite = ComposeThingBitmapStatic(kvp.Value, session.SprFile);
                list.Add(vm);
            }
        }

        Add(session.DatData.Items);
        Add(session.DatData.Outfits);
        Add(session.DatData.Effects);
        Add(session.DatData.Missiles);

        session.AllClientItems = list;
    }

    /// <summary>
    /// Copies sprite pixel data from source SPR to target SPR, remapping sprite IDs in the clone.
    /// </summary>
    private static void RemapSpritesToTarget(DatThingType clone, SprFile sourceSpr, SprFile targetSpr)
    {
        // Build a mapping of old sprite IDs → new sprite IDs
        var spriteMap = new Dictionary<uint, uint>();

        foreach (var fg in clone.FrameGroups)
        {
            for (int i = 0; i < fg.SpriteIndex.Length; i++)
            {
                uint oldId = fg.SpriteIndex[i];
                if (oldId == 0) continue;

                if (!spriteMap.TryGetValue(oldId, out uint newId))
                {
                    var rgba = sourceSpr.GetSpriteRgba(oldId);
                    newId = targetSpr.AddSprite(rgba);
                    spriteMap[oldId] = newId;
                }

                fg.SpriteIndex[i] = newId;
            }
        }
    }

    /// <summary>Entry representing one thing in a batch transplant operation.</summary>
    private sealed class TransplantEntry
    {
        public ThingCategory Category { get; init; }
        public ushort SourceId { get; init; }
        public DatThingType SourceThing { get; init; } = null!;
        public TransplantReport Report { get; init; } = null!;
        public ushort? DuplicateTargetId { get; init; }
        public bool IsEmpty { get; init; }
        public TransplantAction Action { get; set; }
        public ushort? NewTargetId { get; set; }
    }

    /// <summary>
    /// Returns true if the thing has no visible sprites (all sprite IDs are 0 across all frame groups).
    /// </summary>
    private static bool IsThingEmpty(DatThingType thing)
    {
        if (thing.FrameGroups.Length == 0) return true;
        foreach (var fg in thing.FrameGroups)
        {
            foreach (uint sprId in fg.SpriteIndex)
            {
                if (sprId != 0) return false;
            }
        }
        return true;
    }

    private enum TransplantAction { Skip, Add, Replace }

    // ── Workspace navigation ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsItemEditorActive))]
    [NotifyPropertyChangedFor(nameof(IsMapEditorActive))]
    private int _activeEditorIndex;

    public bool IsItemEditorActive => ActiveEditorIndex == 0;
    public bool IsMapEditorActive => ActiveEditorIndex == 1;

    [RelayCommand]
    private void SwitchToItemEditor() => ActiveEditorIndex = 0;

    [RelayCommand]
    private void SwitchToMapEditor() => ActiveEditorIndex = 1;

    // ── Map Editor ──
    [ObservableProperty] private MapData? _mapData;
    [ObservableProperty] private string _mapStatusText = "No map loaded";
    [ObservableProperty] private byte _mapCurrentFloor = 7;
    [ObservableProperty] private double _mapZoom = 1.0;
    [ObservableProperty] private string _mapHoveredTile = string.Empty;
    [ObservableProperty] private int _mapTileCount;
    [ObservableProperty] private string? _mapFilePath;
    [ObservableProperty] private WriteableBitmap? _mapMinimapBitmap;
    [ObservableProperty] private bool _mapHasUnsavedChanges;

    // ── Action log (strategic user action messages) ──
    public ObservableCollection<string> MapActionLog { get; } = [];
    private const int MaxLogEntries = 200;

    // ── Tile inspector (single selected tile) ──
    [ObservableProperty] private ObservableCollection<TileItemInfo> _selectedTileItems = [];
    [ObservableProperty] private string _selectedTileHeader = string.Empty;

    // ── Inspector detail (single item selected from list) ──
    [ObservableProperty] private TileItemInfo? _inspectorSelectedItem;
    [ObservableProperty] private ObservableCollection<InspectorSection> _inspectorDetailSections = [];

    // ── Log panel south of map ──
    [ObservableProperty] private bool _isLogPanelVisible = true;
    [ObservableProperty] private double _logFontSize = 9;
    [ObservableProperty] private double _logPanelHeight = 140;

    [RelayCommand]
    private void ToggleLogPanel() => IsLogPanelVisible = !IsLogPanelVisible;

    [RelayCommand]
    private void LogFontIncrease() => LogFontSize = Math.Min(LogFontSize + 1, 18);

    [RelayCommand]
    private void LogFontDecrease() => LogFontSize = Math.Max(LogFontSize - 1, 7);

    [RelayCommand]
    private async Task CopyLogAsync()
    {
        if (MapActionLog.Count == 0) return;
        var text = string.Join(Environment.NewLine, MapActionLog);
        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
        StatusText = "Log copied to clipboard";
    }

    [RelayCommand]
    private void SelectInspectorItem(TileItemInfo? item)
    {
        InspectorSelectedItem = item;
        InspectorDetailSections.Clear();
        if (item == null) return;

        // Auto-select the corresponding OTB item in the items list
        ushort serverId = item.ServerId;
        var otbIdx = _otbPanelFilteredItems.FindIndex(v => v.ServerId == serverId);
        if (otbIdx >= 0)
        {
            int page = otbIdx / OtbPanelItemsPerPage + 1;
            if (page != OtbPanelCurrentPage)
            {
                OtbPanelCurrentPage = page;
                LoadOtbPanelPage();
            }
            SelectedItem = _otbPanelFilteredItems[otbIdx];
        }

        var otbItem = _otbData?.Items.FirstOrDefault(o => o.ServerId == serverId);
        ushort clientId = otbItem?.ClientId ?? 0;
        DatThingType? datType = null;
        if (clientId > 0) _datData?.Items.TryGetValue(clientId, out datType);

        // ═══ Section 1: Identification ═══
        var idSection = new InspectorSection { Title = "Identification" };
        idSection.Props.Add(new("Server ID", serverId.ToString()));
        idSection.Props.Add(new("Client ID", clientId > 0 ? clientId.ToString() : "—"));
        if (otbItem != null)
        {
            idSection.Props.Add(new("Group", otbItem.Group.ToString()));
            if (!string.IsNullOrEmpty(otbItem.Name)) idSection.Props.Add(new("Name", otbItem.Name));
        }
        if (datType != null)
            idSection.Props.Add(new("Category", datType.Category.ToString()));
        InspectorDetailSections.Add(idSection);

        // ═══ Section 2: OTB Properties ═══
        if (otbItem != null)
        {
            var otbProps = new InspectorSection { Title = "OTB Properties" };
            if (otbItem.Speed > 0) otbProps.Props.Add(new("Speed", otbItem.Speed.ToString()));
            if (otbItem.TopOrder > 0) otbProps.Props.Add(new("Top Order", otbItem.TopOrder.ToString()));
            if (otbItem.LightLevel > 0 || otbItem.LightColor > 0)
                otbProps.Props.Add(new("Light", $"Level {otbItem.LightLevel}, Color {otbItem.LightColor}"));
            if (otbItem.MinimapColor > 0) otbProps.Props.Add(new("Minimap Color", otbItem.MinimapColor.ToString()));
            if (otbItem.WareId > 0) otbProps.Props.Add(new("Ware ID", otbItem.WareId.ToString()));
            if (otbItem.MaxReadWriteChars > 0) otbProps.Props.Add(new("Max Write", otbItem.MaxReadWriteChars.ToString()));
            if (otbItem.MaxReadChars > 0) otbProps.Props.Add(new("Max Read", otbItem.MaxReadChars.ToString()));

            // OTB Flags as individual pills
            if (otbItem.IsBlockSolid) otbProps.Flags.Add("Block Solid");
            if (otbItem.IsBlockProjectile) otbProps.Flags.Add("Block Projectile");
            if (otbItem.IsStackable) otbProps.Flags.Add("Stackable");
            if (otbItem.IsPickupable) otbProps.Flags.Add("Pickupable");
            if (otbItem.IsMoveable) otbProps.Flags.Add("Moveable");
            if (otbItem.IsHasHeight) otbProps.Flags.Add("Has Height");
            if (otbItem.IsUsable) otbProps.Flags.Add("Usable");
            if (otbItem.IsHangable) otbProps.Flags.Add("Hangable");
            if (otbItem.IsRotatable) otbProps.Flags.Add("Rotatable");
            if (otbItem.IsReadable) otbProps.Flags.Add("Readable");
            if (otbItem.IsAnimation) otbProps.Flags.Add("Animated");
            if (otbItem.IsFullGround) otbProps.Flags.Add("Full Ground");
            if (otbItem.IsLookThrough) otbProps.Flags.Add("Look Through");
            if (otbItem.IsForceUse) otbProps.Flags.Add("Force Use");
            if (otbProps.HasProps || otbProps.HasFlags)
                InspectorDetailSections.Add(otbProps);
        }

        // ═══ Section 3: DAT Properties ═══
        if (datType != null)
        {
            var datProps = new InspectorSection { Title = "DAT Properties" };
            if (datType.GroundSpeed > 0) datProps.Props.Add(new("Ground Speed", datType.GroundSpeed.ToString()));
            if (datType.MaxTextLength > 0) datProps.Props.Add(new("Max Text", datType.MaxTextLength.ToString()));
            if (datType.LightLevel > 0 || datType.LightColor > 0)
                datProps.Props.Add(new("Light", $"Level {datType.LightLevel}, Color {datType.LightColor}"));
            if (datType.OffsetX != 0 || datType.OffsetY != 0)
                datProps.Props.Add(new("Offset", $"X:{datType.OffsetX}  Y:{datType.OffsetY}"));
            if (datType.Elevation > 0) datProps.Props.Add(new("Elevation", datType.Elevation.ToString()));
            if (datType.MiniMapColor > 0) datProps.Props.Add(new("Minimap Color", datType.MiniMapColor.ToString()));
            if (datType.LensHelp > 0) datProps.Props.Add(new("Cursor", datType.LensHelp.ToString()));
            if (datType.ClothSlot > 0) datProps.Props.Add(new("Cloth Slot", datType.ClothSlot.ToString()));
            if (datType.DefaultAction > 0) datProps.Props.Add(new("Default Action", datType.DefaultAction.ToString()));
            if (datProps.HasProps)
                InspectorDetailSections.Add(datProps);

            // ═══ Section 4: DAT Flags (Posicionamento) ═══
            var posFlags = new InspectorSection { Title = "Positioning" };
            if (datType.IsGround) posFlags.Flags.Add("Ground");
            if (datType.IsGroundBorder) posFlags.Flags.Add("Ground Border");
            if (datType.IsOnBottom) posFlags.Flags.Add("On Bottom");
            if (datType.IsOnTop) posFlags.Flags.Add("On Top");
            if (datType.IsFullGround) posFlags.Flags.Add("Full Ground");
            if (datType.IsHangable) posFlags.Flags.Add("Hangable");
            if (datType.IsVertical) posFlags.Flags.Add("Vertical (Hook South)");
            if (datType.IsHorizontal) posFlags.Flags.Add("Horizontal (Hook East)");
            if (datType.IsLyingObject) posFlags.Flags.Add("Lying Object");
            if (datType.HasElevation) posFlags.Flags.Add("Has Elevation");
            if (datType.HasOffset) posFlags.Flags.Add("Has Offset");
            if (posFlags.HasFlags) InspectorDetailSections.Add(posFlags);

            // ═══ Section 5: DAT Flags (Bloqueio) ═══
            var blockFlags = new InspectorSection { Title = "Blocking" };
            if (datType.IsUnpassable) blockFlags.Flags.Add("Unpassable");
            if (datType.IsBlockMissile) blockFlags.Flags.Add("Block Missile");
            if (datType.IsBlockPathfind) blockFlags.Flags.Add("Block Pathfind");
            if (datType.IsUnmoveable) blockFlags.Flags.Add("Unmoveable");
            if (blockFlags.HasFlags) InspectorDetailSections.Add(blockFlags);

            // ═══ Section 6: DAT Flags (Interaction) ═══
            var interFlags = new InspectorSection { Title = "Interaction" };
            if (datType.IsContainer) interFlags.Flags.Add("Container");
            if (datType.IsStackable) interFlags.Flags.Add("Stackable");
            if (datType.IsPickupable) interFlags.Flags.Add("Pickupable");
            if (datType.IsRotatable) interFlags.Flags.Add("Rotatable");
            if (datType.IsForceUse) interFlags.Flags.Add("Force Use");
            if (datType.IsMultiUse) interFlags.Flags.Add("Multi-Use");
            if (datType.IsUsable) interFlags.Flags.Add("Usable");
            if (datType.IsWritable) interFlags.Flags.Add("Writable");
            if (datType.IsWritableOnce) interFlags.Flags.Add("Writable Once");
            if (datType.IsFluidContainer) interFlags.Flags.Add("Fluid Container");
            if (datType.IsFluid) interFlags.Flags.Add("Fluid");
            if (interFlags.HasFlags) InspectorDetailSections.Add(interFlags);

            // ═══ Section 7: DAT Flags (Visual) ═══
            var visFlags = new InspectorSection { Title = "Visual" };
            if (datType.HasLight) visFlags.Flags.Add("Has Light");
            if (datType.IsTranslucent) visFlags.Flags.Add("Translucent");
            if (datType.IsDontHide) visFlags.Flags.Add("Don't Hide");
            if (datType.IsAnimateAlways) visFlags.Flags.Add("Animate Always");
            if (datType.IsNoMoveAnimation) visFlags.Flags.Add("No Move Animation");
            if (datType.IsMiniMap) visFlags.Flags.Add("Minimap");
            if (datType.IsIgnoreLook) visFlags.Flags.Add("Ignore Look");
            if (datType.IsTopEffect) visFlags.Flags.Add("Top Effect");
            if (visFlags.HasFlags) InspectorDetailSections.Add(visFlags);

            // ═══ Section 8: DAT Flags (Outros) ═══
            var otherFlags = new InspectorSection { Title = "Other" };
            if (datType.IsCloth) otherFlags.Flags.Add("Cloth");
            if (datType.IsWrappable) otherFlags.Flags.Add("Wrappable");
            if (datType.IsUnwrappable) otherFlags.Flags.Add("Unwrappable");
            if (datType.IsMarketItem) otherFlags.Flags.Add("Market Item");
            if (otherFlags.HasFlags) InspectorDetailSections.Add(otherFlags);

            // ═══ Section 9: Market ═══
            if (datType.MarketCategory > 0 || !string.IsNullOrEmpty(datType.MarketName))
            {
                var marketSection = new InspectorSection { Title = "Market" };
                if (!string.IsNullOrEmpty(datType.MarketName)) marketSection.Props.Add(new("Name", datType.MarketName));
                if (datType.MarketCategory > 0) marketSection.Props.Add(new("Category", datType.MarketCategory.ToString()));
                if (datType.MarketTradeAs > 0) marketSection.Props.Add(new("Trade As", datType.MarketTradeAs.ToString()));
                if (datType.MarketShowAs > 0) marketSection.Props.Add(new("Show As", datType.MarketShowAs.ToString()));
                if (datType.MarketRestrictProfession > 0) marketSection.Props.Add(new("Profession", datType.MarketRestrictProfession.ToString()));
                if (datType.MarketRestrictLevel > 0) marketSection.Props.Add(new("Min. Level", datType.MarketRestrictLevel.ToString()));
                InspectorDetailSections.Add(marketSection);
            }

            // ═══ Section 10: Sprite Data ═══
            if (datType.FrameGroups.Length > 0)
            {
                var sprSection = new InspectorSection { Title = "Sprites" };
                for (int i = 0; i < datType.FrameGroups.Length; i++)
                {
                    var fg = datType.FrameGroups[i];
                    sprSection.Props.Add(new("Group", fg.Type.ToString()));
                    sprSection.Props.Add(new("Size", $"{fg.Width}×{fg.Height}"));
                    sprSection.Props.Add(new("Layers", fg.Layers.ToString()));
                    sprSection.Props.Add(new("Pattern", $"{fg.PatternX}×{fg.PatternY}×{fg.PatternZ}"));
                    sprSection.Props.Add(new("Frames", fg.Frames.ToString()));
                    sprSection.Props.Add(new("Total Sprites", fg.SpriteIndex.Length.ToString()));
                    if (fg.IsAnimation)
                    {
                        sprSection.Props.Add(new("Anim. Mode", fg.AnimationMode.ToString()));
                        sprSection.Props.Add(new("Loop", fg.LoopCount == 0 ? "Infinite" : fg.LoopCount.ToString()));
                    }
                }
                InspectorDetailSections.Add(sprSection);
            }
        }

        // If not found in either
        if (otbItem == null && datType == null)
        {
            var missing = new InspectorSection { Title = "Warning" };
            missing.Props.Add(new("Status", "Item not found in OTB or DAT"));
            InspectorDetailSections.Add(missing);
        }
    }

    [RelayCommand]
    private void ClearInspectorDetail()
    {
        InspectorSelectedItem = null;
        InspectorDetailSections.Clear();
    }

    public DatData? ExposedDatData => _datData;
    public SprFile? ExposedSprFile => _sprFile;
    public OtbData? ExposedOtbData => _otbData;
    public BrushDatabase? BrushDb { get; private set; }
    public BrushCatalog? BrushCatalog { get; private set; }

    [RelayCommand]
    private void OpenBrushEditor()
    {
        if (BrushCatalog == null)
        {
            AddMapLog("No brush catalog loaded. Load a client first.");
            return;
        }
        try
        {
            AddMapLog($"Opening brush editor: {BrushCatalog.Grounds.Count} grounds, {BrushCatalog.Walls.Count} walls, {BrushCatalog.Doodads.Count} doodads, {BrushCatalog.Creatures.Count} creatures");
            var vm = new BrushEditorViewModel(this);
            vm.Initialize(BrushCatalog);
            AddMapLog($"Brush editor initialized: {vm.FilteredBrushes.Count} brushes shown");
            var win = new BrushEditorWindow(vm);
            if (Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is not null)
            {
                win.Show(desktop.MainWindow);
            }
            else
            {
                win.Show();
            }
        }
        catch (Exception ex)
        {
            AddMapLog($"Brush editor error: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ── Unmapped Client Items Finder ──────────────────────────────────────

    [RelayCommand]
    private async Task FindUnmappedClientItemsAsync()
    {
        if (_datData == null || _otbData == null)
        {
            StatusText = "Load both DAT and OTB first.";
            return;
        }

        // Build set of client IDs that already have OTB entries
        var mappedClientIds = new HashSet<ushort>();
        foreach (var otb in _otbData.Items)
            mappedClientIds.Add(otb.ClientId);

        // Find DAT items with no OTB mapping
        var unmapped = new List<DatThingType>();
        foreach (var kvp in _datData.Items.OrderBy(x => x.Key))
        {
            if (!mappedClientIds.Contains(kvp.Key))
                unmapped.Add(kvp.Value);
        }

        if (unmapped.Count == 0)
        {
            StatusText = "All client items already have OTB entries.";
            return;
        }

        StatusText = $"Found {unmapped.Count} unmapped client item(s).";

        // Next server ID
        ushort nextServerId = (ushort)(_otbData.Items.Count > 0
            ? _otbData.Items.Max(i => i.ServerId) + 1
            : 100);

        // Build preview entries
        var entries = new List<UnmappedItemEntry>();
        foreach (var dat in unmapped)
        {
            var group = InferOtbGroupFromDat(dat);
            var flags = InferOtbFlagsFromDat(dat);
            entries.Add(new UnmappedItemEntry
            {
                DatThing = dat,
                Include = true,
                PreviewServerId = nextServerId++,
                PreviewGroup = group,
                PreviewFlags = flags,
            });
        }

        await ShowUnmappedItemsDialog(entries);
    }

    private static OtbGroup InferOtbGroupFromDat(DatThingType dat)
    {
        if (dat.IsGround) return OtbGroup.Ground;
        if (dat.IsContainer) return OtbGroup.Container;
        if (dat.IsFluidContainer) return OtbGroup.Splash;
        if (dat.IsFluid) return OtbGroup.Fluid;
        if (dat.IsWritable || dat.IsWritableOnce) return OtbGroup.Writable;
        if (dat.IsStackable) return OtbGroup.Ammunition;
        if (dat.IsPickupable) return OtbGroup.Armor;
        return OtbGroup.None;
    }

    private static OtbFlags InferOtbFlagsFromDat(DatThingType dat)
    {
        OtbFlags flags = OtbFlags.None;
        if (dat.IsStackable) flags |= OtbFlags.Stackable;
        if (dat.IsPickupable) flags |= OtbFlags.Pickupable;
        if (!dat.IsUnmoveable) flags |= OtbFlags.Moveable;
        if (dat.IsUnpassable) flags |= OtbFlags.BlockSolid;
        if (dat.IsBlockMissile) flags |= OtbFlags.BlockProjectile;
        if (dat.IsBlockPathfind) flags |= OtbFlags.BlockPathFind;
        if (dat.HasElevation) flags |= OtbFlags.HasHeight;
        if (dat.IsUsable || dat.IsMultiUse) flags |= OtbFlags.Usable;
        if (dat.IsHangable) flags |= OtbFlags.Hangable;
        if (dat.IsRotatable) flags |= OtbFlags.Rotatable;
        if (dat.IsWritable || dat.IsWritableOnce) flags |= OtbFlags.Readable;
        if (dat.IsForceUse) flags |= OtbFlags.ForceUse;
        if (dat.IsFullGround) flags |= OtbFlags.FullGround;
        if (dat.IsVertical) flags |= OtbFlags.Vertical;
        if (dat.IsHorizontal) flags |= OtbFlags.Horizontal;
        return flags;
    }

    private async Task ShowUnmappedItemsDialog(List<UnmappedItemEntry> entries)
    {
        if (Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;

        var window = desktop.MainWindow;

        // ── Summary row ──
        var summaryText = new Avalonia.Controls.TextBlock
        {
            Text = $"{entries.Count} client items have no OTB mapping. Select which to create:",
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            FontSize = 14,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
        };

        // ── Select All / None buttons ──
        var selectAllBtn = new Avalonia.Controls.Button
        {
            Content = "Select All",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(12, 4),
            CornerRadius = new Avalonia.CornerRadius(4),
            Margin = new Avalonia.Thickness(0, 0, 6, 0),
        };
        var selectNoneBtn = new Avalonia.Controls.Button
        {
            Content = "Select None",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(12, 4),
            CornerRadius = new Avalonia.CornerRadius(4),
        };
        var selBtnPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
        };
        selBtnPanel.Children.Add(selectAllBtn);
        selBtnPanel.Children.Add(selectNoneBtn);

        // ── Header row ──
        var headerGrid = new Avalonia.Controls.Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("30,50,Auto,60,*,80"),
            Margin = new Avalonia.Thickness(0, 0, 0, 4),
        };
        string[] headers = ["", "Sprite", "Client ID", "Server ID", "Group", "Flags"];
        for (int i = 0; i < headers.Length; i++)
        {
            var tb = new Avalonia.Controls.TextBlock
            {
                Text = headers[i],
                Foreground = Avalonia.Media.Brush.Parse("#585b70"),
                FontSize = 11,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(4, 0),
            };
            Avalonia.Controls.Grid.SetColumn(tb, i);
            headerGrid.Children.Add(tb);
        }

        // ── Scrollable item list ──
        var listStack = new Avalonia.Controls.StackPanel { Spacing = 2 };
        var checkboxes = new List<Avalonia.Controls.CheckBox>();

        foreach (var entry in entries)
        {
            var rowGrid = new Avalonia.Controls.Grid
            {
                ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("30,50,Auto,60,*,80"),
                Background = Avalonia.Media.Brush.Parse("#181825"),
                MinHeight = 36,
            };

            // Checkbox
            var cb = new Avalonia.Controls.CheckBox
            {
                IsChecked = entry.Include,
                Margin = new Avalonia.Thickness(4, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            var capturedEntry = entry;
            cb.IsCheckedChanged += (_, _) => capturedEntry.Include = cb.IsChecked == true;
            checkboxes.Add(cb);
            Avalonia.Controls.Grid.SetColumn(cb, 0);
            rowGrid.Children.Add(cb);

            // Sprite preview
            var spriteBorder = new Avalonia.Controls.Border
            {
                Background = Avalonia.Media.Brush.Parse("#11111b"),
                CornerRadius = new Avalonia.CornerRadius(4),
                Width = 32, Height = 32,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                ClipToBounds = true,
                Margin = new Avalonia.Thickness(4, 2),
            };
            if (_sprFile != null)
            {
                var bmp = ComposeThingBitmapStatic(entry.DatThing, _sprFile);
                if (bmp != null)
                {
                    var img = new Avalonia.Controls.Image
                    {
                        Source = bmp, Width = 32, Height = 32,
                        Stretch = Avalonia.Media.Stretch.Uniform,
                    };
                    Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(img, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
                    spriteBorder.Child = img;
                }
            }
            Avalonia.Controls.Grid.SetColumn(spriteBorder, 1);
            rowGrid.Children.Add(spriteBorder);

            // Client ID
            var clientIdTb = new Avalonia.Controls.TextBlock
            {
                Text = entry.DatThing.Id.ToString(),
                Foreground = Avalonia.Media.Brush.Parse("#f9e2af"),
                FontSize = 13,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(8, 0),
            };
            Avalonia.Controls.Grid.SetColumn(clientIdTb, 2);
            rowGrid.Children.Add(clientIdTb);

            // Preview Server ID
            var serverIdTb = new Avalonia.Controls.TextBlock
            {
                Text = entry.PreviewServerId.ToString(),
                Foreground = Avalonia.Media.Brush.Parse("#a6e3a1"),
                FontSize = 13,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(8, 0),
            };
            Avalonia.Controls.Grid.SetColumn(serverIdTb, 3);
            rowGrid.Children.Add(serverIdTb);

            // Group
            var groupTb = new Avalonia.Controls.TextBlock
            {
                Text = entry.PreviewGroup.ToString(),
                Foreground = Avalonia.Media.Brush.Parse("#89b4fa"),
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(8, 0),
            };
            Avalonia.Controls.Grid.SetColumn(groupTb, 4);
            rowGrid.Children.Add(groupTb);

            // Flag count
            var flagCount = CountSetFlags(entry.PreviewFlags);
            var flagTb = new Avalonia.Controls.TextBlock
            {
                Text = flagCount > 0 ? $"{flagCount} flags" : "—",
                Foreground = Avalonia.Media.Brush.Parse("#585b70"),
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(8, 0),
            };
            Avalonia.Controls.ToolTip.SetTip(flagTb, FormatOtbFlags(entry.PreviewFlags));
            Avalonia.Controls.Grid.SetColumn(flagTb, 5);
            rowGrid.Children.Add(flagTb);

            listStack.Children.Add(rowGrid);
        }

        selectAllBtn.Click += (_, _) =>
        {
            foreach (var cb in checkboxes) cb.IsChecked = true;
        };
        selectNoneBtn.Click += (_, _) =>
        {
            foreach (var cb in checkboxes) cb.IsChecked = false;
        };

        var scrollViewer = new Avalonia.Controls.ScrollViewer
        {
            Content = listStack,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        // ── Bottom buttons ──
        var countLabel = new Avalonia.Controls.TextBlock
        {
            Foreground = Avalonia.Media.Brush.Parse("#a6adc8"),
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        void UpdateCount()
        {
            var sel = entries.Count(e => e.Include);
            countLabel.Text = $"{sel} of {entries.Count} selected";
        }
        UpdateCount();
        foreach (var cb in checkboxes)
            cb.IsCheckedChanged += (_, _) => UpdateCount();

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(16, 6),
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        var createBtn = new Avalonia.Controls.Button
        {
            Content = "Create Selected",
            Background = Avalonia.Media.Brush.Parse("#a6e3a1"),
            Foreground = Avalonia.Media.Brush.Parse("#1e1e2e"),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Avalonia.Thickness(16, 6),
            CornerRadius = new Avalonia.CornerRadius(6),
        };

        var btnPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
        };
        btnPanel.Children.Add(countLabel);
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(createBtn);

        // ── Layout ──
        var mainStack = new Avalonia.Controls.DockPanel { Margin = new Avalonia.Thickness(16) };
        Avalonia.Controls.DockPanel.SetDock(summaryText, Avalonia.Controls.Dock.Top);
        Avalonia.Controls.DockPanel.SetDock(selBtnPanel, Avalonia.Controls.Dock.Top);
        Avalonia.Controls.DockPanel.SetDock(headerGrid, Avalonia.Controls.Dock.Top);
        Avalonia.Controls.DockPanel.SetDock(btnPanel, Avalonia.Controls.Dock.Bottom);
        mainStack.Children.Add(summaryText);
        mainStack.Children.Add(selBtnPanel);
        mainStack.Children.Add(headerGrid);
        mainStack.Children.Add(btnPanel);
        mainStack.Children.Add(scrollViewer);

        var dialog = new Avalonia.Controls.Window
        {
            Title = "Unmapped Client Items",
            Width = 640, Height = 520,
            MinWidth = 500, MinHeight = 350,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
            Content = mainStack,
        };

        cancelBtn.Click += (_, _) => dialog.Close();
        createBtn.Click += (_, _) =>
        {
            var selected = entries.Where(e => e.Include).ToList();
            if (selected.Count == 0) { dialog.Close(); return; }

            CreateOtbItemsFromUnmapped(selected);
            dialog.Close();
        };

        await dialog.ShowDialog(window);
    }

    private void CreateOtbItemsFromUnmapped(List<UnmappedItemEntry> selected)
    {
        if (_otbData == null) return;

        ushort nextServerId = (ushort)(_otbData.Items.Count > 0
            ? _otbData.Items.Max(i => i.ServerId) + 1
            : 100);

        int created = 0;
        foreach (var entry in selected)
        {
            var dat = entry.DatThing;
            var newItem = new OtbItem
            {
                ServerId = nextServerId++,
                ClientId = dat.Id,
                Group = entry.PreviewGroup,
                Flags = entry.PreviewFlags,
                Speed = dat.GroundSpeed,
                LightLevel = dat.LightLevel,
                LightColor = dat.LightColor,
                MinimapColor = dat.MiniMapColor,
                MaxReadWriteChars = dat.MaxTextLength,
                Name = dat.MarketName.Length > 0 ? dat.MarketName : null,
            };

            if (dat.FrameGroups.Length > 0 && dat.FrameGroups[0].Frames > 1)
                newItem.IsAnimation = true;

            _otbData.Items.Add(newItem);

            var vm = new ItemViewModel(newItem, this);
            _allItems.Add(vm);
            created++;
        }

        TotalItems = _allItems.Count;
        CrossReferenceDat();
        LoadAllSprites();
        ApplyFilter();
        HasUnsavedChanges = true;
        StatusText = $"Created {created} OTB item(s) from unmapped client items. Save to persist.";
    }

    private static int CountSetFlags(OtbFlags flags)
    {
        int count = 0;
        for (var val = (uint)flags; val != 0; val &= val - 1)
            count++;
        return count;
    }

    private static string FormatOtbFlags(OtbFlags flags)
    {
        if (flags == OtbFlags.None) return "None";
        var parts = new List<string>();
        foreach (OtbFlags f in Enum.GetValues(typeof(OtbFlags)))
        {
            if (f != OtbFlags.None && flags.HasFlag(f))
                parts.Add(f.ToString());
        }
        return string.Join(", ", parts);
    }

    // ── Duplicate Finder (pixel-based) ────────────────────────────────

    [RelayCommand]
    private async Task FindDuplicateItemsAsync()
    {
        if (_datData == null || _sprFile == null)
        {
            StatusText = "Load DAT and SPR first.";
            return;
        }

        IsLoading = true;
        StatusText = "Scanning for duplicate items (pixel comparison)…";

        var sprFile = _sprFile;
        var items = _datData.Items.Values.ToList();

        // Build pixel hashes on background thread
        var groups = await Task.Run(() =>
        {
            var hashToItems = new Dictionary<string, List<DatThingType>>();
            foreach (var thing in items)
            {
                if (thing.FrameGroups.Length == 0) continue;
                var hash = ComputeThingPixelHash(thing, sprFile);
                if (hash == null) continue; // fully transparent, skip

                if (!hashToItems.TryGetValue(hash, out var list))
                {
                    list = [];
                    hashToItems[hash] = list;
                }
                list.Add(thing);
            }
            // Keep only groups with 2+ items
            return hashToItems.Values.Where(g => g.Count > 1).OrderByDescending(g => g.Count).ToList();
        });

        IsLoading = false;

        if (groups.Count == 0)
        {
            StatusText = "No duplicate items found.";
            return;
        }

        int totalDuplicates = groups.Sum(g => g.Count - 1);
        StatusText = $"Found {groups.Count} duplicate group(s) ({totalDuplicates} redundant items).";
        await ShowDuplicateItemsDialog(groups);
    }

    private static string? ComputeThingPixelHash(DatThingType thing, SprFile sprFile)
    {
        // Hash all sprite pixels across all frame groups
        using var hasher = System.Security.Cryptography.SHA256.Create();
        bool hasAnyPixels = false;

        foreach (var fg in thing.FrameGroups)
        {
            foreach (var spriteId in fg.SpriteIndex)
            {
                if (spriteId == 0) continue;
                var rgba = sprFile.GetSpriteRgba(spriteId);
                if (rgba != null)
                {
                    hasher.TransformBlock(rgba, 0, rgba.Length, null, 0);
                    hasAnyPixels = true;
                }
            }
        }

        if (!hasAnyPixels) return null;

        hasher.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(hasher.Hash!);
    }

    private async Task ShowDuplicateItemsDialog(List<List<DatThingType>> groups)
    {
        if (Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;

        var window = desktop.MainWindow;

        var summaryText = new Avalonia.Controls.TextBlock
        {
            Text = $"{groups.Count} duplicate group(s) found. Items in each group have identical pixel data.",
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            FontSize = 14,
            Margin = new Avalonia.Thickness(0, 0, 0, 12),
        };

        var listStack = new Avalonia.Controls.StackPanel { Spacing = 8 };

        foreach (var group in groups)
        {
            // Group header
            var groupBorder = new Avalonia.Controls.Border
            {
                Background = Avalonia.Media.Brush.Parse("#181825"),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(10, 8),
            };

            var groupStack = new Avalonia.Controls.StackPanel { Spacing = 4 };

            var headerTb = new Avalonia.Controls.TextBlock
            {
                Text = $"Duplicate group — {group.Count} items with identical sprites",
                Foreground = Avalonia.Media.Brush.Parse("#cba6f7"),
                FontSize = 13,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Margin = new Avalonia.Thickness(0, 0, 0, 4),
            };
            groupStack.Children.Add(headerTb);

            var itemsPanel = new Avalonia.Controls.WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            foreach (var thing in group)
            {
                var itemPanel = new Avalonia.Controls.StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Vertical,
                    Margin = new Avalonia.Thickness(4),
                    Width = 60,
                };

                var spriteBorder = new Avalonia.Controls.Border
                {
                    Background = Avalonia.Media.Brush.Parse("#11111b"),
                    CornerRadius = new Avalonia.CornerRadius(4),
                    Width = 40, Height = 40,
                    ClipToBounds = true,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                };
                if (_sprFile != null)
                {
                    var bmp = ComposeThingBitmapStatic(thing, _sprFile);
                    if (bmp != null)
                    {
                        var img = new Avalonia.Controls.Image
                        {
                            Source = bmp, Width = 40, Height = 40,
                            Stretch = Avalonia.Media.Stretch.Uniform,
                        };
                        Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(img, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
                        spriteBorder.Child = img;
                    }
                }
                itemPanel.Children.Add(spriteBorder);

                var idTb = new Avalonia.Controls.TextBlock
                {
                    Text = $"ID {thing.Id}",
                    Foreground = Avalonia.Media.Brush.Parse("#f9e2af"),
                    FontSize = 11,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                };
                itemPanel.Children.Add(idTb);

                // Check if has OTB mapping
                bool hasOtb = _otbData?.Items.Any(o => o.ClientId == thing.Id) == true;
                var otbTb = new Avalonia.Controls.TextBlock
                {
                    Text = hasOtb ? "Has OTB" : "No OTB",
                    Foreground = Avalonia.Media.Brush.Parse(hasOtb ? "#a6e3a1" : "#585b70"),
                    FontSize = 10,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                };
                itemPanel.Children.Add(otbTb);

                itemsPanel.Children.Add(itemPanel);
            }
            groupStack.Children.Add(itemsPanel);
            groupBorder.Child = groupStack;
            listStack.Children.Add(groupBorder);
        }

        var scrollViewer = new Avalonia.Controls.ScrollViewer
        {
            Content = listStack,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        var closeBtn = new Avalonia.Controls.Button
        {
            Content = "Close",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(16, 6),
            CornerRadius = new Avalonia.CornerRadius(6),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };

        var mainDock = new Avalonia.Controls.DockPanel { Margin = new Avalonia.Thickness(16) };
        Avalonia.Controls.DockPanel.SetDock(summaryText, Avalonia.Controls.Dock.Top);
        Avalonia.Controls.DockPanel.SetDock(closeBtn, Avalonia.Controls.Dock.Bottom);
        mainDock.Children.Add(summaryText);
        mainDock.Children.Add(closeBtn);
        mainDock.Children.Add(scrollViewer);

        var dialog = new Avalonia.Controls.Window
        {
            Title = "Duplicate Items (Pixel Comparison)",
            Width = 600, Height = 500,
            MinWidth = 400, MinHeight = 300,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
            Content = mainDock,
        };

        closeBtn.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(window);
    }

    // ── Compact Sprites (fill empty slots) ─────────────────────────────

    [RelayCommand]
    private async Task CompactSpritesAsync()
    {
        if (_datData == null || _sprFile == null)
        {
            StatusText = "Load DAT and SPR first.";
            return;
        }

        IsLoading = true;
        StatusText = "Analyzing sprite usage…";

        var datData = _datData;
        var sprFile = _sprFile;
        var otbData = _otbData;

        var (emptySlots, usedSpriteIds, totalSprites) = await Task.Run(() =>
        {
            // Collect all sprite IDs referenced by any item/outfit/effect/missile
            var used = new HashSet<uint>();
            void CollectRefs(Dictionary<ushort, DatThingType> dict)
            {
                foreach (var thing in dict.Values)
                    foreach (var fg in thing.FrameGroups)
                        foreach (var id in fg.SpriteIndex)
                            if (id != 0) used.Add(id);
            }
            CollectRefs(datData.Items);
            CollectRefs(datData.Outfits);
            CollectRefs(datData.Effects);
            CollectRefs(datData.Missiles);

            // Find empty slots: no pixels AND not referenced
            var empty = new List<uint>();
            for (uint id = 1; id <= sprFile.SpriteCount; id++)
            {
                if (used.Contains(id)) continue;
                var rgba = sprFile.GetSpriteRgba(id);
                if (rgba == null) { empty.Add(id); continue; }
                // Check if all pixels are transparent
                bool allTransparent = true;
                for (int i = 3; i < rgba.Length; i += 4)
                {
                    if (rgba[i] != 0) { allTransparent = false; break; }
                }
                if (allTransparent) empty.Add(id);
            }

            return (empty, used, sprFile.SpriteCount);
        });

        IsLoading = false;

        if (emptySlots.Count == 0)
        {
            StatusText = "No empty sprite slots found. SPR is already compact.";
            return;
        }

        StatusText = $"Found {emptySlots.Count} empty sprite slot(s) out of {totalSprites} total.";
        await ShowCompactSpritesDialog(emptySlots, usedSpriteIds, totalSprites);
    }

    private async Task ShowCompactSpritesDialog(List<uint> emptySlots, HashSet<uint> usedSpriteIds, uint totalSprites)
    {
        if (Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;

        var window = desktop.MainWindow;

        var summaryText = new Avalonia.Controls.TextBlock
        {
            Text = $"Found {emptySlots.Count} empty sprite slot(s) out of {totalSprites}.\n\n" +
                   "Compacting will shift sprites to fill empty slots and update all references " +
                   "in DAT items and OTB client mappings.\n\n" +
                   "This operation is staged — click Save All (Ctrl+S) to persist.",
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            FontSize = 14,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 12),
        };

        var previewTb = new Avalonia.Controls.TextBlock
        {
            Text = $"After compacting: {totalSprites - (uint)emptySlots.Count} sprites (saving {emptySlots.Count} slots)",
            Foreground = Avalonia.Media.Brush.Parse("#a6e3a1"),
            FontSize = 14,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 0, 0, 12),
        };

        var btnPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
        };

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(16, 6),
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        var compactBtn = new Avalonia.Controls.Button
        {
            Content = "Compact Now",
            Background = Avalonia.Media.Brush.Parse("#a6e3a1"),
            Foreground = Avalonia.Media.Brush.Parse("#1e1e2e"),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Padding = new Avalonia.Thickness(16, 6),
            CornerRadius = new Avalonia.CornerRadius(6),
        };
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(compactBtn);

        var mainDock = new Avalonia.Controls.DockPanel { Margin = new Avalonia.Thickness(20, 16) };
        Avalonia.Controls.DockPanel.SetDock(summaryText, Avalonia.Controls.Dock.Top);
        Avalonia.Controls.DockPanel.SetDock(previewTb, Avalonia.Controls.Dock.Top);
        Avalonia.Controls.DockPanel.SetDock(btnPanel, Avalonia.Controls.Dock.Bottom);
        mainDock.Children.Add(summaryText);
        mainDock.Children.Add(previewTb);
        mainDock.Children.Add(btnPanel);
        mainDock.Children.Add(new Avalonia.Controls.Panel()); // filler

        var dialog = new Avalonia.Controls.Window
        {
            Title = "Compact Sprites",
            Width = 480, Height = 260,
            CanResize = false,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
            Content = mainDock,
        };

        cancelBtn.Click += (_, _) => dialog.Close();
        compactBtn.Click += async (_, _) =>
        {
            dialog.Close();
            await ExecuteCompactSprites(emptySlots);
        };

        await dialog.ShowDialog(window);
    }

    private async Task ExecuteCompactSprites(List<uint> emptySlots)
    {
        if (_datData == null || _sprFile == null) return;

        IsLoading = true;
        StatusText = "Compacting sprites…";

        var datData = _datData;
        var sprFile = _sprFile;
        var otbData = _otbData;

        // Build the old→new mapping on background thread
        var remapTable = await Task.Run(() =>
        {
            var emptySet = new HashSet<uint>(emptySlots);

            // Build sorted list of all sprite IDs (1..count), identify which are empty
            // We walk from lowest empty slot, and fill it with next non-empty sprite from the end.
            var sortedEmpty = new SortedSet<uint>(emptySlots);
            var remap = new Dictionary<uint, uint>(); // old → new

            // Strategy: walk from end, for each non-empty sprite that's after our first empty slot,
            // remap it to the first available empty slot.
            uint newCount = sprFile.SpriteCount;
            foreach (var emptyId in sortedEmpty)
            {
                if (emptyId >= newCount) break;

                // Find last non-empty sprite
                while (newCount > emptyId && emptySet.Contains(newCount))
                    newCount--;

                if (newCount <= emptyId) break;

                // Move sprite at 'newCount' → 'emptyId'
                remap[newCount] = emptyId;
                emptySet.Add(newCount);
                emptySet.Remove(emptyId);
                newCount--;
            }

            return remap;
        });

        if (remapTable.Count == 0)
        {
            IsLoading = false;
            StatusText = "No sprites to compact.";
            return;
        }

        // Apply remapping
        await Task.Run(() =>
        {
            // 1. Move sprite pixel data
            foreach (var (oldId, newId) in remapTable)
            {
                var pixels = sprFile.GetSpriteRgba(oldId);
                sprFile.SetSpriteRgba(newId, pixels);
                sprFile.SetSpriteRgba(oldId, null); // blank old slot
            }

            // 2. Trim trailing empty sprites
            // After compaction, the effective count is max non-empty ID
            // RemoveSprite handles trailing trimming

            // 3. Update all DAT sprite references
            void RemapRefs(Dictionary<ushort, DatThingType> dict)
            {
                foreach (var thing in dict.Values)
                {
                    foreach (var fg in thing.FrameGroups)
                    {
                        var idx = fg.SpriteIndex;
                        for (int i = 0; i < idx.Length; i++)
                        {
                            if (idx[i] != 0 && remapTable.TryGetValue(idx[i], out var newId))
                                idx[i] = newId;
                        }
                    }
                }
            }
            RemapRefs(datData.Items);
            RemapRefs(datData.Outfits);
            RemapRefs(datData.Effects);
            RemapRefs(datData.Missiles);

            // 4. Update OTB client IDs if needed (OTB maps by clientId, not spriteId)
            // OTB doesn't reference sprite IDs directly, no change needed.
        });

        // Rebuild UI
        HasUnsavedChanges = true;

        BuildClientItemList();
        if (_otbData != null)
        {
            CrossReferenceDat();
            LoadAllSprites();
            ApplyFilter();
        }

        IsLoading = false;
        StatusText = $"Compacted {remapTable.Count} sprite(s). Save to persist changes.";
    }

    // ── Compare DAT ↔ OTB ───────────────────────────────────────────────

    [RelayCommand]
    private async Task CompareDatOtbAsync()
    {
        if (_otbData == null || _datData == null)
        {
            StatusText = "Load both OTB and DAT files first.";
            return;
        }

        StatusText = "Comparing DAT ↔ OTB properties…";
        var divergent = await Task.Run(() => DatOtbComparer.FindDivergentItems(_otbData, _datData));

        if (divergent.Count == 0)
        {
            StatusText = "All OTB items match their DAT counterparts — no differences found.";
            return;
        }

        StatusText = $"Found {divergent.Count} divergent item(s). Review and apply…";

        var dialog = new DatOtbSyncWindow(divergent, _datData, _sprFile);

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            await dialog.ShowDialog(desktop.MainWindow);
        }

        if (dialog.Result == null)
        {
            StatusText = "DAT ↔ OTB compare cancelled.";
            return;
        }

        var r = dialog.Result;
        if (r.OtbPropertiesChanged > 0 || r.DatPropertiesChanged > 0)
        {
            HasUnsavedChanges = true;
            var parts = new List<string>();
            if (r.OtbPropertiesChanged > 0) parts.Add($"{r.OtbPropertiesChanged} → OTB");
            if (r.DatPropertiesChanged > 0) parts.Add($"{r.DatPropertiesChanged} → DAT");
            StatusText = $"Applied {string.Join(", ", parts)} across {r.ItemsAffected} item(s). Save to persist.";
        }
        else
        {
            StatusText = "No changes applied.";
        }
    }

    public byte[] MapFloors => MapData?.GetFloors() ?? [7];

    partial void OnMapCurrentFloorChanged(byte value)
    {
        OnPropertyChanged(nameof(MapFloors));
    }

    [RelayCommand]
    private async Task OpenMapAsync()
    {
        var path = await FileDialogHelper.OpenFileAsync("Open OTBM Map",
            [("OTBM Files", "*.otbm"), ("All Files", "*")]);
        if (path == null) return;
        await LoadMapFromPath(path);
    }

    private async Task LoadMapFromPath(string path)
    {
        try
        {
            MapData = OtbmFile.Load(path);
            MapFilePath = path;
            var mapDir = Path.GetDirectoryName(path) ?? ".";

            // Load external spawn file
            if (!string.IsNullOrEmpty(MapData.SpawnFile))
            {
                var spawnPath = Path.Combine(mapDir, MapData.SpawnFile);
                var spawns = SpawnHouseXml.LoadSpawns(spawnPath);
                MapData.Spawns.Clear();
                MapData.Spawns.AddRange(spawns);
                AddMapLog($"Spawns loaded: {spawns.Count} zones from {MapData.SpawnFile}");
            }

            // Load external house file
            if (!string.IsNullOrEmpty(MapData.HouseFile))
            {
                var housePath = Path.Combine(mapDir, MapData.HouseFile);
                var houses = SpawnHouseXml.LoadHouses(housePath);
                MapData.Houses.Clear();
                MapData.Houses.AddRange(houses);
                AddMapLog($"Houses loaded: {houses.Count} houses from {MapData.HouseFile}");
            }

            MapTileCount = MapData.Tiles.Count;
            MapStatusText = $"Map loaded: {MapTileCount:N0} tiles, {MapData.Towns.Count} towns, {MapData.Spawns.Count} spawns, {MapData.Houses.Count} houses — {Path.GetFileName(path)}";
            MapHasUnsavedChanges = false;
            AddMapLog($"Map opened: {Path.GetFileName(path)} ({MapTileCount:N0} tiles)");
            OnPropertyChanged(nameof(MapFloors));
            OnPropertyChanged(nameof(ExposedDatData));
            OnPropertyChanged(nameof(ExposedSprFile));
            OnPropertyChanged(nameof(ExposedOtbData));

            // Auto-load creature database
            LoadCreatureDatabaseAuto();

            // Auto-select floor 7 if available
            var floors = MapData.GetFloors();
            if (floors.Length > 0)
                MapCurrentFloor = floors.Contains((byte)7) ? (byte)7 : floors[0];
        }
        catch (Exception ex)
        {
            MapStatusText = $"Error: {ex.Message}";
        }
    }

    // ── Save map ──

    [RelayCommand]
    public void SaveMap()
    {
        if (MapData == null || string.IsNullOrEmpty(MapFilePath)) return;
        try
        {
            OtbmFile.Save(MapFilePath, MapData);
            var mapDir = Path.GetDirectoryName(MapFilePath) ?? ".";

            // Save external spawn file
            if (!string.IsNullOrEmpty(MapData.SpawnFile))
            {
                var spawnPath = Path.Combine(mapDir, MapData.SpawnFile);
                SpawnHouseXml.SaveSpawns(spawnPath, MapData.Spawns);
                AddMapLog($"Spawns saved: {MapData.Spawns.Count} zones");
            }

            // Save external house file
            if (!string.IsNullOrEmpty(MapData.HouseFile))
            {
                var housePath = Path.Combine(mapDir, MapData.HouseFile);
                SpawnHouseXml.SaveHouses(housePath, MapData.Houses);
                AddMapLog($"Houses saved: {MapData.Houses.Count} houses");
            }

            MapTileCount = MapData.Tiles.Count;
            MapHasUnsavedChanges = false;
            MapStatusText = $"Map saved: {MapTileCount:N0} tiles — {Path.GetFileName(MapFilePath)}";
            AddMapLog("Map saved successfully");
        }
        catch (Exception ex)
        {
            MapStatusText = $"Save error: {ex.Message}";
            AddMapLog($"SAVE ERROR: {ex.Message}");
        }
    }

    /// <summary>Called by MapCanvasControl.MapEdited event.</summary>
    public void MarkMapDirty()
    {
        MapHasUnsavedChanges = true;
        MapTileCount = MapData?.Tiles.Count ?? 0;
    }

    // ── Spawn/House management ──

    /// <summary>Deactivates all spawn/house brush modes.</summary>
    public void DeactivateSpawnHouseBrushes()
    {
        IsSpawnBrushActive = false;
        IsCreatureBrushActive = false;
        IsHouseBrushActive = false;
        IsHouseExitBrushActive = false;
    }

    /// <summary>Activates spawn brush mode (exclusive with other brushes).</summary>
    public void ActivateSpawnBrush()
    {
        BrushServerId = 0;
        BrushItemIds = null;
        ActiveZoneBrush = 0;
        IsBorderRemoverActive = false;
        IsCreatureBrushActive = false;
        IsHouseBrushActive = false;
        IsHouseExitBrushActive = false;
        IsSpawnBrushActive = true;
    }

    /// <summary>Activates creature brush mode (exclusive with other brushes).</summary>
    public void ActivateCreatureBrush()
    {
        BrushServerId = 0;
        BrushItemIds = null;
        ActiveZoneBrush = 0;
        IsBorderRemoverActive = false;
        IsSpawnBrushActive = false;
        IsHouseBrushActive = false;
        IsHouseExitBrushActive = false;
        IsCreatureBrushActive = true;
    }

    /// <summary>Activates house brush mode (exclusive with other brushes).</summary>
    public void ActivateHouseBrush()
    {
        BrushServerId = 0;
        BrushItemIds = null;
        ActiveZoneBrush = 0;
        IsBorderRemoverActive = false;
        IsSpawnBrushActive = false;
        IsCreatureBrushActive = false;
        IsHouseExitBrushActive = false;
        IsHouseBrushActive = true;
    }

    /// <summary>Activates house exit brush mode (exclusive with other brushes).</summary>
    public void ActivateHouseExitBrush()
    {
        BrushServerId = 0;
        BrushItemIds = null;
        ActiveZoneBrush = 0;
        IsBorderRemoverActive = false;
        IsSpawnBrushActive = false;
        IsCreatureBrushActive = false;
        IsHouseBrushActive = false;
        IsHouseExitBrushActive = true;
    }

    /// <summary>Creates a new house and selects it.</summary>
    public MapHouse? AddNewHouse(uint townId)
    {
        if (MapData == null) return null;

        uint newId = 1;
        var usedIds = new HashSet<uint>(MapData.Houses.Select(h => h.Id));
        while (usedIds.Contains(newId)) newId++;

        var house = new MapHouse
        {
            Id = newId,
            Name = $"Unnamed House #{newId}",
            TownId = townId,
        };
        MapData.Houses.Add(house);
        SelectedHouseId = newId;
        MarkMapDirty();
        AddMapLog($"House #{newId} created (Town {townId})");
        return house;
    }

    /// <summary>Removes a house and clears all tiles belonging to it.</summary>
    public void RemoveHouse(uint houseId)
    {
        if (MapData == null) return;
        var house = MapData.Houses.FirstOrDefault(h => h.Id == houseId);
        if (house == null) return;

        // Clear house from tiles
        foreach (var tile in MapData.Tiles.Values)
        {
            if (tile.HouseId == houseId)
            {
                tile.HouseId = 0;
                // Remove PZ flag
                tile.Flags &= ~1u; // TILESTATE_PROTECTIONZONE = 0x01
            }
        }

        MapData.Houses.Remove(house);
        if (SelectedHouseId == houseId) SelectedHouseId = 0;
        MarkMapDirty();
        AddMapLog($"House #{houseId} removed");
    }

    /// <summary>Gets a list of houses filtered by selected town.</summary>
    public List<MapHouse> GetFilteredHouses()
    {
        if (MapData == null) return [];
        if (SelectedHouseTownFilter == 0)
            return MapData.Houses.OrderBy(h => h.Id).ToList();
        return MapData.Houses.Where(h => h.TownId == SelectedHouseTownFilter).OrderBy(h => h.Id).ToList();
    }

    /// <summary>Creates a new spawn at the specified position.</summary>
    public MapSpawn AddNewSpawn(ushort x, ushort y, byte z, int radius)
    {
        var spawn = new MapSpawn { CenterX = x, CenterY = y, CenterZ = z, Radius = radius };
        MapData?.Spawns.Add(spawn);
        MarkMapDirty();
        AddMapLog($"Spawn created at ({x}, {y}, {z}) r={radius}");
        return spawn;
    }

    /// <summary>Removes a spawn.</summary>
    public void RemoveSpawn(MapSpawn spawn)
    {
        if (MapData == null) return;
        MapData.Spawns.Remove(spawn);
        MarkMapDirty();
        AddMapLog($"Spawn removed at ({spawn.CenterX}, {spawn.CenterY}, {spawn.CenterZ})");
    }

    /// <summary>Finds the spawn covering a given position.</summary>
    public MapSpawn? FindSpawnAt(ushort x, ushort y, byte z)
    {
        if (MapData == null) return null;
        foreach (var spawn in MapData.Spawns)
        {
            if (spawn.CenterZ != z) continue;
            int dx = Math.Abs(x - spawn.CenterX);
            int dy = Math.Abs(y - spawn.CenterY);
            if (dx <= spawn.Radius && dy <= spawn.Radius)
                return spawn;
        }
        return null;
    }

    /// <summary>Called by MapCanvasControl.ActionLogged event.</summary>
    public void AddMapLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        MapActionLog.Insert(0, $"[{timestamp}] {message}");
        while (MapActionLog.Count > MaxLogEntries)
            MapActionLog.RemoveAt(MapActionLog.Count - 1);
    }

    // ── Map Properties & Operations ──

    [RelayCommand]
    private async Task OpenMapPropertiesAsync()
    {
        if (MapData == null) return;

        var dialog = new MapPropertiesDialog
        {
            MapDescription = MapData.Description,
            MapWidth = MapData.Width,
            MapHeight = MapData.Height,
            MapHouseFile = MapData.HouseFile,
            MapSpawnFile = MapData.SpawnFile,
            OtbmVersion = MapData.Version.ToString(),
            OtbVersion = $"{MapData.OtbMajorVersion}.{MapData.OtbMinorVersion}",
            TileCount = MapData.Tiles.Count,
        };

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            await dialog.ShowDialog(desktop.MainWindow);
        }
        else return;

        if (dialog.Result == null) return;

        var r = dialog.Result;
        bool changed = false;

        if (MapData.Description != r.Description)
        { MapData.Description = r.Description; changed = true; }
        if (MapData.HouseFile != r.HouseFile)
        { MapData.HouseFile = r.HouseFile; changed = true; }
        if (MapData.SpawnFile != r.SpawnFile)
        { MapData.SpawnFile = r.SpawnFile; changed = true; }

        bool resized = MapData.Width != r.Width || MapData.Height != r.Height;
        if (resized)
        {
            int removed = 0;
            var toRemove = new List<MapPosition>();
            foreach (var pos in MapData.Tiles.Keys)
            {
                if (pos.X >= r.Width || pos.Y >= r.Height)
                    toRemove.Add(pos);
            }
            foreach (var pos in toRemove)
            {
                MapData.Tiles.Remove(pos);
                removed++;
            }

            MapData.Width = r.Width;
            MapData.Height = r.Height;
            changed = true;

            MapTileCount = MapData.Tiles.Count;
            AddMapLog($"Map resized to {r.Width}×{r.Height} — {removed} tile(s) removed");
        }

        if (changed)
        {
            MarkMapDirty();
            MapStatusText = $"Map properties updated — {MapData.Tiles.Count:N0} tiles";
            if (!resized) AddMapLog("Map properties updated");
        }
    }

    [RelayCommand]
    private async Task ShowMapStatisticsAsync()
    {
        if (MapData == null) return;

        var dialog = new MapStatisticsDialog();
        dialog.Populate(MapData);

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            await dialog.ShowDialog(desktop.MainWindow);
        }
        else
        {
            dialog.Show();
        }
    }

    [RelayCommand]
    private void MapCleanupInvalidItems()
    {
        if (MapData == null || _otbData == null) return;

        var validIds = new HashSet<ushort>(_otbData.Items.Select(i => i.ServerId));
        int removedItems = 0;
        var emptyTiles = new List<MapPosition>();

        foreach (var (pos, tile) in MapData.Tiles)
        {
            int before = tile.Items.Count;
            tile.Items.RemoveAll(item => item.Id > 0 && !validIds.Contains(item.Id));
            removedItems += before - tile.Items.Count;

            if (tile.Items.Count == 0)
                emptyTiles.Add(pos);
        }

        foreach (var pos in emptyTiles)
            MapData.Tiles.Remove(pos);

        if (removedItems > 0 || emptyTiles.Count > 0)
        {
            MarkMapDirty();
            MapTileCount = MapData.Tiles.Count;
            var msg = $"Cleanup: removed {removedItems} invalid item(s), {emptyTiles.Count} empty tile(s)";
            MapStatusText = msg;
            AddMapLog(msg);
        }
        else
        {
            AddMapLog("Cleanup: no invalid items found");
            MapStatusText = "No invalid items found";
        }
    }

    [RelayCommand]
    private async Task MapRemoveItemsByIdAsync()
    {
        if (MapData == null) return;

        // Build a simple input dialog inline
        var tcs = new TaskCompletionSource<ushort?>();
        var input = new Avalonia.Controls.TextBox
        {
            Watermark = "Item Server ID (e.g. 2148)",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            BorderBrush = Avalonia.Media.Brush.Parse("#45475a"),
            CornerRadius = new Avalonia.CornerRadius(4),
            Padding = new Avalonia.Thickness(6, 4),
            FontSize = 12,
        };
        var okBtn = new Avalonia.Controls.Button
        {
            Content = "Remove", Background = Avalonia.Media.Brush.Parse("#f38ba8"),
            Foreground = Avalonia.Media.Brush.Parse("#1e1e2e"), FontWeight = FontWeight.SemiBold,
            CornerRadius = new Avalonia.CornerRadius(4), Padding = new Avalonia.Thickness(14, 6),
        };
        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel", Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            CornerRadius = new Avalonia.CornerRadius(4), Padding = new Avalonia.Thickness(14, 6),
        };

        var btnPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
        };
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);

        var panel = new Avalonia.Controls.StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(20) };
        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "Enter the Server ID of the item to remove from the entire map:",
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"), FontSize = 12,
        });
        panel.Children.Add(input);
        panel.Children.Add(btnPanel);

        var dlg = new Avalonia.Controls.Window
        {
            Title = "Remove Items by ID",
            Width = 380, SizeToContent = Avalonia.Controls.SizeToContent.Height,
            CanResize = false, WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
            Content = panel,
        };

        okBtn.Click += (_, _) =>
        {
            if (ushort.TryParse(input.Text?.Trim(), out var id) && id > 0)
            { tcs.TrySetResult(id); dlg.Close(); }
        };
        cancelBtn.Click += (_, _) => { tcs.TrySetResult(null); dlg.Close(); };
        dlg.Closing += (_, _) => tcs.TrySetResult(null);

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            await dlg.ShowDialog(desktop.MainWindow);
        }
        else return;

        var targetId = await tcs.Task;
        if (targetId == null) return;

        int removed = RemoveItemIdFromMap(MapData, targetId.Value);
        if (removed > 0)
        {
            MarkMapDirty();
            MapTileCount = MapData.Tiles.Count;
            var msg = $"Removed {removed} instance(s) of item #{targetId.Value}";
            MapStatusText = msg;
            AddMapLog(msg);
        }
        else
        {
            AddMapLog($"Item #{targetId.Value} not found on map");
            MapStatusText = $"Item #{targetId.Value} not found";
        }
    }

    private static int RemoveItemIdFromMap(MapData map, ushort id)
    {
        int removed = 0;
        var emptyTiles = new List<MapPosition>();

        foreach (var (pos, tile) in map.Tiles)
        {
            int before = tile.Items.Count;
            tile.Items.RemoveAll(i => i.Id == id);
            removed += before - tile.Items.Count;

            // Also remove from container contents
            foreach (var item in tile.Items)
                removed += RemoveItemIdFromContents(item.Contents, id);

            if (tile.Items.Count == 0)
                emptyTiles.Add(pos);
        }

        foreach (var pos in emptyTiles)
            map.Tiles.Remove(pos);

        return removed;
    }

    private static int RemoveItemIdFromContents(List<MapItem> contents, ushort id)
    {
        int removed = 0;
        int before = contents.Count;
        contents.RemoveAll(i => i.Id == id);
        removed += before - contents.Count;

        foreach (var item in contents)
            removed += RemoveItemIdFromContents(item.Contents, id);

        return removed;
    }

    [RelayCommand]
    private void MapRemoveEmptyTiles()
    {
        if (MapData == null) return;

        var toRemove = new List<MapPosition>();
        foreach (var (pos, tile) in MapData.Tiles)
        {
            if (tile.Items.Count == 0)
                toRemove.Add(pos);
        }

        foreach (var pos in toRemove)
            MapData.Tiles.Remove(pos);

        if (toRemove.Count > 0)
        {
            MarkMapDirty();
            MapTileCount = MapData.Tiles.Count;
            var msg = $"Removed {toRemove.Count} empty tile(s)";
            MapStatusText = msg;
            AddMapLog(msg);
        }
        else
        {
            AddMapLog("No empty tiles found");
            MapStatusText = "No empty tiles found";
        }
    }

    /// <summary>Called by MapCanvasControl.SelectedTileChanged event.</summary>
    public void OnSelectedTileChanged(MapPosition? pos)
    {
        SelectedTileItems.Clear();
        SelectedTileHeader = string.Empty;
        // Clear detail view when tile changes
        InspectorSelectedItem = null;
        InspectorDetailSections.Clear();

        if (pos == null || MapData == null) return;
        if (!MapData.Tiles.TryGetValue(pos.Value, out var tile)) return;

        SelectedTileHeader = $"Tile ({pos.Value.X}, {pos.Value.Y}, {pos.Value.Z}) — {tile.Items.Count} items";
        if (tile.Flags != 0)
            SelectedTileHeader += $" | Flags: 0x{tile.Flags:X}";
        if (tile.HouseId > 0)
            SelectedTileHeader += $" | House: {tile.HouseId}";

        for (int i = 0; i < tile.Items.Count; i++)
        {
            var item = tile.Items[i];
            var info = new TileItemInfo
            {
                Index = i,
                ServerId = item.Id,
                Label = i == 0 ? "Ground" : $"Item #{i}"
            };

            // Resolve name from OTB if available
            if (_otbData != null)
            {
                var otbItem = _otbData.Items.FirstOrDefault(o => o.ServerId == item.Id);
                if (otbItem != null && !string.IsNullOrEmpty(otbItem.Name))
                    info.Name = otbItem.Name;
            }

            if (item.Count != 0) info.Details.Add($"Count: {item.Count}");
            if (item.ActionId != 0) info.Details.Add($"ActionId: {item.ActionId}");
            if (item.UniqueId != 0) info.Details.Add($"UniqueId: {item.UniqueId}");
            if (item.Text != null) info.Details.Add($"Text: \"{item.Text}\"");
            if (item.TeleportDestination is { } dest)
                info.Details.Add($"Teleport: ({dest.X}, {dest.Y}, {dest.Z})");
            if (item.DepotId != 0) info.Details.Add($"DepotId: {item.DepotId}");
            if (item.DoorId != 0) info.Details.Add($"DoorId: {item.DoorId}");
            if (item.Contents.Count > 0) info.Details.Add($"Contents: {item.Contents.Count} items");

            SelectedTileItems.Add(info);
        }

        // Auto-select detail when tile has only one item
        if (SelectedTileItems.Count == 1)
            SelectInspectorItem(SelectedTileItems[0]);
    }

    // ── Map floor/zoom/goto commands ──
    [ObservableProperty] private int _mapGoToX;
    [ObservableProperty] private int _mapGoToY;
    [ObservableProperty] private int _mapGoToZ = 7;

    [RelayCommand]
    private void MapFloorUp()
    {
        if (MapCurrentFloor > 0) MapCurrentFloor = (byte)(MapCurrentFloor - 1);
    }

    [RelayCommand]
    private void MapFloorDown()
    {
        if (MapCurrentFloor < 15) MapCurrentFloor = (byte)(MapCurrentFloor + 1);
    }

    [RelayCommand]
    private void MapZoomIn() => MapZoom = Math.Min(MapZoom * 1.25, 4.0);

    [RelayCommand]
    private void MapZoomOut() => MapZoom = Math.Max(MapZoom * 0.8, 0.125);

    [RelayCommand]
    private void MapCenter() => _mapCenterRequested?.Invoke();

    [RelayCommand]
    private void MapGoTo() => _mapGoToRequested?.Invoke((ushort)MapGoToX, (ushort)MapGoToY, (byte)MapGoToZ);

    // Events for the View to hook into (for MapCanvasControl interaction)
    internal Action? _mapCenterRequested;
    internal Action<ushort, ushort, byte>? _mapGoToRequested;
    internal Action? _mapSpriteCacheInvalidated;
    internal Func<(double, double)>? _mapGetViewport;
    internal Action<double, double>? _mapRestoreViewport;

    // ── View menu toggles (bound to MapCanvasControl properties) ──
    [ObservableProperty] private bool _viewShowAllFloors = true;
    [ObservableProperty] private bool _viewShowAnimation = true;
    [ObservableProperty] private bool _viewShowLights = true;
    [ObservableProperty] private bool _viewShowGrid;
    [ObservableProperty] private bool _viewShowShade = true;
    [ObservableProperty] private bool _viewShowAsMinimap;
    [ObservableProperty] private bool _viewGhostItems;
    [ObservableProperty] private bool _viewGhostHigherFloors;
    [ObservableProperty] private bool _viewGhostLowerFloors;
    [ObservableProperty] private bool _viewShowSpecial = true;
    [ObservableProperty] private bool _viewShowZones = true;
    [ObservableProperty] private bool _viewShowHouses = true;
    [ObservableProperty] private bool _viewShowSpawns = true;
    [ObservableProperty] private bool _viewShowWaypoints = true;
    [ObservableProperty] private bool _viewShowTowns;
    [ObservableProperty] private bool _viewShowPathing;
    [ObservableProperty] private bool _viewHighlightItems;
    [ObservableProperty] private bool _viewShowTooltips = true;
    [ObservableProperty] private bool _viewShowIngameBox;

    // ── Palette ──
    [ObservableProperty] private PaletteViewModel? _palette;

    // ── Brush (selected item for map placement) ──
    [ObservableProperty] private ushort _brushServerId;
    [ObservableProperty] private int _brushSize;       // 0=1x1, 1=3x3, 2=5x5, 3=7x7, 4=9x9, 5=11x11, 6=13x13
    [ObservableProperty] private bool _brushCircle;    // false=square, true=circle
    [ObservableProperty] private int _activeZoneBrush; // 0=none, 1=PZ, 2=NoLogout, 4=NoPvP, 8=PvPZone
    [ObservableProperty] private bool _isBorderRemoverActive; // border remover tool
    [ObservableProperty] private IList<ushort>? _brushItemIds; // custom brush: random pick from this list
    [ObservableProperty] private bool _useAutomagic = true; // Border Automagic toggle (A key)

    // ── Spawn brush ──
    [ObservableProperty] private bool _isSpawnBrushActive;
    [ObservableProperty] private bool _isCreatureBrushActive;
    [ObservableProperty] private int _spawnBrushRadius = 5;
    [ObservableProperty] private int _creatureSpawnTime = 60;
    [ObservableProperty] private string _selectedCreatureName = string.Empty;
    [ObservableProperty] private bool _selectedCreatureIsNpc;

    // ── House brush ──
    [ObservableProperty] private bool _isHouseBrushActive;
    [ObservableProperty] private bool _isHouseExitBrushActive;
    [ObservableProperty] private uint _selectedHouseId;
    [ObservableProperty] private uint _selectedHouseTownFilter; // 0 = all towns

    // ── Creature database ──
    private List<CreatureEntry> _allCreatures = [];
    [ObservableProperty] private string _creatureSearchText = string.Empty;
    [ObservableProperty] private string _creatureFilter = "All"; // All, Monsters, NPCs
    public ObservableCollection<CreatureEntry> FilteredCreatures { get; } = [];

    partial void OnCreatureSearchTextChanged(string value) => RefreshFilteredCreatures();
    partial void OnCreatureFilterChanged(string value) => RefreshFilteredCreatures();

    public void LoadCreatureDatabase(string xmlPath)
    {
        _allCreatures = CreatureDatabase.LoadFromXml(xmlPath);
        RefreshFilteredCreatures();
        AddMapLog($"Creatures loaded: {_allCreatures.Count} entries from {Path.GetFileName(xmlPath)}");
    }

    public void LoadCreatureDatabaseAuto()
    {
        var xmlPath = CreatureDatabase.FindCreaturesXml(MapFilePath, ClientFolderPath);
        if (xmlPath != null)
            LoadCreatureDatabase(xmlPath);
    }

    public void RefreshFilteredCreatures()
    {
        FilteredCreatures.Clear();
        var search = CreatureSearchText.Trim();
        foreach (var c in _allCreatures)
        {
            if (CreatureFilter == "Monsters" && c.IsNpc) continue;
            if (CreatureFilter == "NPCs" && !c.IsNpc) continue;
            if (!string.IsNullOrEmpty(search) &&
                c.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            FilteredCreatures.Add(c);
        }
    }

    public void SelectCreature(CreatureEntry entry)
    {
        SelectedCreatureName = entry.Name;
        SelectedCreatureIsNpc = entry.IsNpc;
        if (!IsCreatureBrushActive)
            ActivateCreatureBrush();
    }

    [ObservableProperty] private string _statusText = "Select the client folder to begin";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ItemViewModel? _selectedItem;
    [ObservableProperty] private bool _showMismatchesOnly;
    [ObservableProperty] private int _totalItems;
    [ObservableProperty] private int _mismatchCount;
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _clientFolderPath;
    [ObservableProperty] private bool _isClientLoaded;
    [ObservableProperty] private bool _showDeprecatedOnly;

    // ── OTB item editing (double-click to open) ──
    [ObservableProperty] private bool _isOtbItemEditing;

    // ── OTB panel (right column) ──
    [ObservableProperty] private string _otbPanelSearchText = string.Empty;
    [ObservableProperty] private int _otbPanelCurrentPage = 1;
    [ObservableProperty] private int _otbPanelTotalPages = 1;
    private const int OtbPanelItemsPerPage = 100;
    private List<ItemViewModel> _otbPanelFilteredItems = [];
    public ObservableCollection<ItemViewModel> OtbPanelItems { get; } = [];
    private bool _suppressOtbClientSync;

    // ── Animation timer for client item list ──
    // Object Builder durations: Items=500ms, Outfits=300ms, Effects=100ms, Missiles=100ms
    private DispatcherTimer? _animationTimer;
    private int _animTickCounter;

    private void StartAnimationTimer()
    {
        if (_animationTimer != null) return;
        _animTickCounter = 0;
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void StopAnimationTimer()
    {
        if (_animationTimer == null) return;
        _animationTimer.Stop();
        _animationTimer.Tick -= OnAnimationTick;
        _animationTimer = null;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_sprFile == null) return;
        _animTickCounter++;

        // Animate client item list
        foreach (var item in ClientItems)
        {
            if (item.Frames <= 1) continue;
            int divisor = item.Category switch
            {
                ThingCategory.Effect => 1,
                ThingCategory.Missile => 1,
                ThingCategory.Outfit => 3,
                _ => 5
            };
            if (_animTickCounter % divisor != 0) continue;
            item.AnimFrame = (item.AnimFrame + 1) % item.Frames;
            item.Sprite = ComposeThingBitmap(item.ThingType, item.AnimFrame);
        }

        // Animate OTB panel items (every 5 ticks = 500ms like items)
        if (_animTickCounter % 5 == 0)
        {
            foreach (var vm in OtbPanelItems)
            {
                if (vm.DatThingType == null) continue;
                var fg = vm.DatThingType.FrameGroups;
                if (fg.Length == 0 || fg[0].Frames <= 1) continue;
                vm.AnimFrame = (vm.AnimFrame + 1) % fg[0].Frames;
                vm.Sprite = ComposeThingBitmap(vm.DatThingType, vm.AnimFrame);
            }
        }

        // Animate catalog items (every 5 ticks = 500ms)
        if (_animTickCounter % 5 == 0 && Palette != null)
        {
            var items = Palette.IsViewingSubCollection ? Palette.DisplayedItems : Palette.CatalogResults;
            foreach (var pvm in items)
            {
                var thing = Palette.GetThingForPaletteItem(pvm);
                if (thing == null) continue;
                var fg = thing.FrameGroups;
                if (fg.Length == 0 || fg[0].Frames <= 1) continue;
                pvm.AnimFrame = (pvm.AnimFrame + 1) % fg[0].Frames;
                pvm.Sprite = ComposeThingBitmap(thing, pvm.AnimFrame);
            }
        }
    }

    // ── Right-side sprite panel (always visible when client loaded) ──
    [ObservableProperty] private int _rightSpriteCurrentPage = 1;
    [ObservableProperty] private int _rightSpriteTotalPages = 1;
    private const int RightSpritesPerPage = 100;
    public ObservableCollection<SpriteViewModel> RightSprites { get; } = [];

    // ── Client items (DAT view) ──
    [ObservableProperty] private string _clientSearchText = string.Empty;
    [ObservableProperty] private ClientItemViewModel? _selectedClientItem;
    [ObservableProperty] private bool _isClientItemEditing;
    [ObservableProperty] private int _clientFilteredCount;
    [ObservableProperty] private string _clientCategoryFilter = "All";
    [ObservableProperty] private string _clientNavigateId = string.Empty;
    [ObservableProperty] private int _clientCurrentPage = 1;
    [ObservableProperty] private int _clientTotalPages = 1;
    private const int ClientItemsPerPage = 100;
    private List<ClientItemViewModel> _clientFilteredItems = [];
    public string[] ClientCategoryOptions { get; } = ["All", "Item", "Outfit", "Effect", "Missile", "Mismatch"];
    public ObservableCollection<ClientItemViewModel> ClientItems { get; } = [];

    /// <summary>Multi-selection list, bridged from code-behind.</summary>
    public List<ClientItemViewModel> SelectedClientItemsList { get; set; } = [];

    /// <summary>Multi-selection list for OTB items, bridged from code-behind.</summary>
    public List<ItemViewModel> SelectedOtbItemsList { get; set; } = [];

    // ── Client item view options ──
    [ObservableProperty] private bool _showCropSize;
    [ObservableProperty] private bool _showGrid;
    [ObservableProperty] private bool _showAllFrames;
    [ObservableProperty] private bool _isPlayingAnimation;

    public ObservableCollection<SpriteViewModel> FilmstripFrames { get; } = [];
    public bool HasAnimation => (CurrentFrameGroup?.Frames ?? 1) > 1;
    public bool HasMultipleGroups => (_currentCompositionThing?.FrameGroups.Length ?? 1) > 1;
    public bool HasMultipleLayers => (CurrentFrameGroup?.Layers ?? 1) > 1;
    public int AnimationFrameMax => Math.Max(0, (CurrentFrameGroup?.Frames ?? 1) - 1);
    public string AnimationLoopLabel
    {
        get
        {
            var fg = CurrentFrameGroup;
            if (fg == null || fg.Frames <= 1) return "";
            return fg.LoopCount switch
            {
                -1 => "Pingpong",
                0 => "Loop",
                _ => $"Loop ×{fg.LoopCount}"
            };
        }
    }

    partial void OnShowAllFramesChanged(bool value) => BuildFilmstrip();

    partial void OnIsPlayingAnimationChanged(bool value)
    {
        if (value) StartCompositionAnimTimer();
        else StopCompositionAnimTimer();
    }

    // ── Composition grid layout ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompositionPreviewWidth))]
    [NotifyPropertyChangedFor(nameof(CropOverlaySize))]
    private int _compositionGridColumns = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompositionPreviewHeight))]
    [NotifyPropertyChangedFor(nameof(CropOverlaySize))]
    private int _compositionGridRows = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CropOverlaySize))]
    private int _compositionExactSize = 32;

    [ObservableProperty] private SpriteViewModel? _selectedRightSprite;

    private const double CompositionCellSize = 64.0;
    private const double CompositionCellGap = 2.0;

    public double CompositionPreviewWidth => CompositionGridColumns * (CompositionCellSize + CompositionCellGap);
    public double CompositionPreviewHeight => CompositionGridRows * (CompositionCellSize + CompositionCellGap);
    public double CropOverlaySize => Math.Min(CompositionExactSize * 2.0, Math.Min(CompositionPreviewWidth, CompositionPreviewHeight));

    private static readonly ISolidColorBrush GridVisibleBrush = new SolidColorBrush(Color.Parse("#45475a"));
    private static readonly ISolidColorBrush GridHiddenBrush = new SolidColorBrush(Color.Parse("#11111b"));
    public ISolidColorBrush CompositionGridBrush => ShowGrid ? GridVisibleBrush : GridHiddenBrush;

    partial void OnShowGridChanged(bool value) => OnPropertyChanged(nameof(CompositionGridBrush));

    // ── Composition animation timer (plays animation in preview) ──
    private DispatcherTimer? _compositionAnimTimer;
    private int _compositionAnimDirection = 1;

    private void StartCompositionAnimTimer()
    {
        StopCompositionAnimTimer();
        var fg = CurrentFrameGroup;
        if (fg == null || fg.Frames <= 1) { IsPlayingAnimation = false; return; }

        _compositionAnimDirection = 1;
        int interval = GetFrameDurationMs(fg, 0);
        _compositionAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        _compositionAnimTimer.Tick += OnCompositionAnimTick;
        _compositionAnimTimer.Start();
    }

    private void StopCompositionAnimTimer()
    {
        if (_compositionAnimTimer == null) return;
        _compositionAnimTimer.Stop();
        _compositionAnimTimer.Tick -= OnCompositionAnimTick;
        _compositionAnimTimer = null;
    }

    private void OnCompositionAnimTick(object? sender, EventArgs e)
    {
        var fg = CurrentFrameGroup;
        if (fg == null || fg.Frames <= 1) { StopCompositionAnimTimer(); IsPlayingAnimation = false; return; }

        int nextFrame = CompositionFrame + _compositionAnimDirection;

        if (fg.LoopCount == -1) // Pingpong
        {
            if (nextFrame >= fg.Frames) { _compositionAnimDirection = -1; nextFrame = fg.Frames - 2; }
            if (nextFrame < 0) { _compositionAnimDirection = 1; nextFrame = 1; }
            nextFrame = Math.Clamp(nextFrame, 0, fg.Frames - 1);
        }
        else // Normal loop
        {
            if (nextFrame >= fg.Frames) nextFrame = 0;
        }

        CompositionFrame = nextFrame;

        // Update interval for next frame's duration
        if (_compositionAnimTimer != null)
            _compositionAnimTimer.Interval = TimeSpan.FromMilliseconds(GetFrameDurationMs(fg, CompositionFrame));
    }

    private static int GetFrameDurationMs(FrameGroup fg, int frame)
    {
        if (fg.FrameDurations != null && frame >= 0 && frame < fg.FrameDurations.Length)
        {
            var dur = fg.FrameDurations[frame];
            int ms = (int)((dur.Minimum + dur.Maximum) / 2);
            return Math.Max(50, ms);
        }
        return 200;
    }

    [RelayCommand]
    private void PreviousAnimationFrame()
    {
        var fg = CurrentFrameGroup;
        if (fg == null) return;
        CompositionFrame = CompositionFrame > 0 ? CompositionFrame - 1 : fg.Frames - 1;
    }

    [RelayCommand]
    private void NextAnimationFrame()
    {
        var fg = CurrentFrameGroup;
        if (fg == null) return;
        CompositionFrame = CompositionFrame < fg.Frames - 1 ? CompositionFrame + 1 : 0;
    }

    private void BuildFilmstrip()
    {
        FilmstripFrames.Clear();
        if (!ShowAllFrames) return;

        var thing = _currentCompositionThing;
        if (thing == null || _sprFile == null) return;

        var fg = CurrentFrameGroup;
        if (fg == null || fg.Frames <= 1) return;

        int layer = Math.Clamp(CompositionLayer, 0, Math.Max(0, fg.Layers - 1));
        int px = Math.Clamp(CompositionPatternX, 0, Math.Max(0, fg.PatternX - 1));
        int py = Math.Clamp(CompositionPatternY, 0, Math.Max(0, fg.PatternY - 1));
        int pz = Math.Clamp(CompositionPatternZ, 0, Math.Max(0, fg.PatternZ - 1));

        for (int f = 0; f < fg.Frames; f++)
        {
            var bitmap = ComposeFramePreview(fg, f, layer, px, py, pz);
            FilmstripFrames.Add(new SpriteViewModel { SpriteId = (uint)f, Bitmap = bitmap });
        }
    }

    private WriteableBitmap? ComposeFramePreview(FrameGroup fg, int frame, int layer, int px, int py, int pz)
    {
        if (_sprFile == null) return null;
        int w = fg.Width, h = fg.Height;
        if (w == 0 || h == 0) return null;

        if (w == 1 && h == 1)
        {
            uint sprId = fg.GetSpriteId(0, 0, layer, px, py, pz, frame);
            return LoadSpriteBitmap(sprId);
        }

        int bmpW = w * 32, bmpH = h * 32;
        var pixels = new byte[bmpW * bmpH * 4];

        for (int tw = 0; tw < w; tw++)
        {
            for (int th = 0; th < h; th++)
            {
                uint sprId = fg.GetSpriteId(tw, th, layer, px, py, pz, frame);
                var rgba = _sprFile.GetSpriteRgba(sprId);
                if (rgba == null) continue;
                int destX = (w - 1 - tw) * 32;
                int destY = (h - 1 - th) * 32;
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        int srcIdx = (y * 32 + x) * 4;
                        byte a = rgba[srcIdx + 3];
                        if (a == 0) continue;
                        int dstIdx = ((destY + y) * bmpW + destX + x) * 4;
                        pixels[dstIdx] = rgba[srcIdx];
                        pixels[dstIdx + 1] = rgba[srcIdx + 1];
                        pixels[dstIdx + 2] = rgba[srcIdx + 2];
                        pixels[dstIdx + 3] = a;
                    }
                }
            }
        }

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(bmpW, bmpH), new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
            using (var fb = bitmap.Lock())
                Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
            return bitmap;
        }
        catch { return null; }
    }

    // ── Sprite composition (for selected item/client item) ──
    [ObservableProperty] private int _compositionFrameGroupIndex;
    [ObservableProperty] private int _compositionFrame;
    [ObservableProperty] private int _compositionLayer;
    [ObservableProperty] private int _compositionPatternX;
    [ObservableProperty] private int _compositionPatternY;
    [ObservableProperty] private int _compositionPatternZ;
    public ObservableCollection<SpriteViewModel> CompositionSprites { get; } = [];
    [ObservableProperty] private SpriteViewModel? _selectedCompositionSprite;

    partial void OnSelectedCompositionSpriteChanged(SpriteViewModel? value)
    {
        if (value != null && value.SpriteId > 0)
            NavigateRightSpriteToId(value.SpriteId);
    }

    // ── Category visibility helpers (for conditional controls like OB) ──
    public bool IsItemSelected => SelectedClientItem?.ThingType.Category == ThingCategory.Item;
    public bool IsOutfitSelected => SelectedClientItem?.ThingType.Category == ThingCategory.Outfit;
    public bool IsEffectSelected => SelectedClientItem?.ThingType.Category == ThingCategory.Effect;
    public bool IsMissileSelected => SelectedClientItem?.ThingType.Category == ThingCategory.Missile;
    public bool IsNotMissileSelected => SelectedClientItem != null && SelectedClientItem.ThingType.Category != ThingCategory.Missile;

    // ── OB-style navigation visibility ──
    // Outfits/missiles use per-pattern navigation; items/effects show all patterns at once
    public bool IsOutfitOrMissile => IsOutfitSelected || IsMissileSelected;
    public bool ShowPatternZNav => (CurrentFrameGroup?.PatternZ ?? 1) > 1;

    // Direction label for outfits (N/E/S/W based on PatternX index)
    public string DirectionLabel => CompositionPatternX switch
    {
        0 => "↑ N",
        1 => "→ E",
        2 => "↓ S",
        3 => "← W",
        _ => $"Dir {CompositionPatternX}"
    };

    // ── OB-style direction commands (set PatternX to specific direction) ──
    [RelayCommand] private void SetDirectionNorth() => CompositionPatternX = 0;
    [RelayCommand] private void SetDirectionEast() => CompositionPatternX = 1;
    [RelayCommand] private void SetDirectionSouth() => CompositionPatternX = 2;
    [RelayCommand] private void SetDirectionWest() => CompositionPatternX = 3;

    // ── Texture property bindings (read/write to current FrameGroup) ──
    public int CompositionFrameGroupCount => _currentCompositionThing?.FrameGroups.Length ?? 1;

    public int CompositionWidth
    {
        get => CurrentFrameGroup?.Width ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.Width = (byte)value; fg.UpdateSpriteCount(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionHeight
    {
        get => CurrentFrameGroup?.Height ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.Height = (byte)value; fg.UpdateSpriteCount(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionCropSize
    {
        get => CurrentFrameGroup?.ExactSize ?? 32;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 64) { fg.ExactSize = (byte)value; OnPropertyChanged(); ReloadComposition(); } }
    }
    public int CompositionLayerCount
    {
        get => CurrentFrameGroup?.Layers ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.Layers = (byte)value; fg.UpdateSpriteCount(); ClampNavigationIndices(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionPatternXCount
    {
        get => CurrentFrameGroup?.PatternX ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.PatternX = (byte)value; fg.UpdateSpriteCount(); ClampNavigationIndices(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionPatternYCount
    {
        get => CurrentFrameGroup?.PatternY ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.PatternY = (byte)value; fg.UpdateSpriteCount(); ClampNavigationIndices(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionPatternZCount
    {
        get => CurrentFrameGroup?.PatternZ ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 8) { fg.PatternZ = (byte)value; fg.UpdateSpriteCount(); ClampNavigationIndices(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }
    public int CompositionFrameCount
    {
        get => CurrentFrameGroup?.Frames ?? 1;
        set { var fg = CurrentFrameGroup; if (fg != null && value >= 1 && value <= 255) { fg.Frames = (byte)value; fg.UpdateSpriteCount(); ClampNavigationIndices(); OnPropertyChanged(); NotifyAllCompositionLabels(); ReloadComposition(); } }
    }

    // ── Appearance labels ──
    public string CompositionFrameGroupName => CompositionFrameGroupIndex == 0 ? "Idle" : "Walking";
    public bool IsIdleGroup { get => CompositionFrameGroupIndex == 0; set { if (value) CompositionFrameGroupIndex = 0; } }
    public bool IsWalkingGroup { get => CompositionFrameGroupIndex == 1; set { if (value) CompositionFrameGroupIndex = 1; } }
    public string CompositionFrameLabel => $"{CompositionFrame}/{Math.Max(0, (CurrentFrameGroup?.Frames ?? 1) - 1)}";
    public string CompositionLayerLabel => $"{CompositionLayer}/{Math.Max(0, (CurrentFrameGroup?.Layers ?? 1) - 1)}";
    public string CompositionPatternXLabel => $"{CompositionPatternX}/{Math.Max(0, (CurrentFrameGroup?.PatternX ?? 1) - 1)}";
    public string CompositionPatternYLabel => $"{CompositionPatternY}/{Math.Max(0, (CurrentFrameGroup?.PatternY ?? 1) - 1)}";

    private FrameGroup? CurrentFrameGroup
    {
        get
        {
            var thing = _currentCompositionThing;
            if (thing == null || thing.FrameGroups.Length == 0) return null;
            int idx = Math.Clamp(CompositionFrameGroupIndex, 0, thing.FrameGroups.Length - 1);
            return thing.FrameGroups[idx];
        }
    }

    /// <summary>
    /// Clamps navigation indices to valid range after a structural property changes
    /// (e.g. reducing PatternX from 4 to 2 when CompositionPatternX was 3).
    /// </summary>
    private void ClampNavigationIndices()
    {
        var fg = CurrentFrameGroup;
        if (fg == null) return;
        if (CompositionFrame >= fg.Frames) CompositionFrame = fg.Frames - 1;
        if (CompositionLayer >= fg.Layers) CompositionLayer = fg.Layers - 1;
        if (CompositionPatternX >= fg.PatternX) CompositionPatternX = fg.PatternX - 1;
        if (CompositionPatternY >= fg.PatternY) CompositionPatternY = fg.PatternY - 1;
        if (CompositionPatternZ >= fg.PatternZ) CompositionPatternZ = fg.PatternZ - 1;
    }

    private void NotifyAllCompositionLabels()
    {
        OnPropertyChanged(nameof(CompositionFrameGroupCount));
        OnPropertyChanged(nameof(CompositionWidth));
        OnPropertyChanged(nameof(CompositionHeight));
        OnPropertyChanged(nameof(CompositionCropSize));
        OnPropertyChanged(nameof(CompositionLayerCount));
        OnPropertyChanged(nameof(CompositionPatternXCount));
        OnPropertyChanged(nameof(CompositionPatternYCount));
        OnPropertyChanged(nameof(CompositionPatternZCount));
        OnPropertyChanged(nameof(CompositionFrameCount));
        OnPropertyChanged(nameof(CompositionFrameGroupName));
        OnPropertyChanged(nameof(IsIdleGroup));
        OnPropertyChanged(nameof(IsWalkingGroup));
        OnPropertyChanged(nameof(CompositionFrameLabel));
        OnPropertyChanged(nameof(CompositionLayerLabel));
        OnPropertyChanged(nameof(CompositionPatternXLabel));
        OnPropertyChanged(nameof(CompositionPatternYLabel));
        OnPropertyChanged(nameof(HasAnimation));
        OnPropertyChanged(nameof(HasMultipleGroups));
        OnPropertyChanged(nameof(HasMultipleLayers));
        OnPropertyChanged(nameof(AnimationFrameMax));
        OnPropertyChanged(nameof(AnimationLoopLabel));
        OnPropertyChanged(nameof(IsOutfitOrMissile));
        OnPropertyChanged(nameof(ShowPatternZNav));
        OnPropertyChanged(nameof(DirectionLabel));
    }

    // ── Pattern spin commands (modify FrameGroup values) ──
    [RelayCommand] private void IncrementFrameGroupCount() { } // TODO: structural change — adding/removing frame groups
    [RelayCommand] private void DecrementFrameGroupCount() { }
    [RelayCommand] private void IncrementWidth() { if (CompositionWidth < 8) CompositionWidth++; }
    [RelayCommand] private void DecrementWidth() { if (CompositionWidth > 1) CompositionWidth--; }
    [RelayCommand] private void IncrementHeight() { if (CompositionHeight < 8) CompositionHeight++; }
    [RelayCommand] private void DecrementHeight() { if (CompositionHeight > 1) CompositionHeight--; }
    [RelayCommand] private void IncrementCropSize() { if (CompositionCropSize < 64) CompositionCropSize++; }
    [RelayCommand] private void DecrementCropSize() { if (CompositionCropSize > 1) CompositionCropSize--; }
    [RelayCommand] private void IncrementLayerCount() { if (CompositionLayerCount < 8) CompositionLayerCount++; }
    [RelayCommand] private void DecrementLayerCount() { if (CompositionLayerCount > 1) CompositionLayerCount--; }
    [RelayCommand] private void IncrementPatternXCount() { if (CompositionPatternXCount < 8) CompositionPatternXCount++; }
    [RelayCommand] private void DecrementPatternXCount() { if (CompositionPatternXCount > 1) CompositionPatternXCount--; }
    [RelayCommand] private void IncrementPatternYCount() { if (CompositionPatternYCount < 8) CompositionPatternYCount++; }
    [RelayCommand] private void DecrementPatternYCount() { if (CompositionPatternYCount > 1) CompositionPatternYCount--; }
    [RelayCommand] private void IncrementPatternZCount() { if (CompositionPatternZCount < 8) CompositionPatternZCount++; }
    [RelayCommand] private void DecrementPatternZCount() { if (CompositionPatternZCount > 1) CompositionPatternZCount--; }
    [RelayCommand] private void IncrementFrameCount() { if (CompositionFrameCount < 255) CompositionFrameCount++; }
    [RelayCommand] private void DecrementFrameCount() { if (CompositionFrameCount > 1) CompositionFrameCount--; }

    // ── Appearance spin commands ──
    [RelayCommand]
    private void IncrementFrameGroupIndex()
    {
        var thing = _currentCompositionThing;
        if (thing != null && CompositionFrameGroupIndex < thing.FrameGroups.Length - 1)
            CompositionFrameGroupIndex++;
    }
    [RelayCommand]
    private void DecrementFrameGroupIndex()
    {
        if (CompositionFrameGroupIndex > 0) CompositionFrameGroupIndex--;
    }
    [RelayCommand]
    private void IncrementFrame()
    {
        var fg = CurrentFrameGroup;
        if (fg != null && CompositionFrame < fg.Frames - 1) CompositionFrame++;
    }
    [RelayCommand]
    private void DecrementFrame()
    {
        if (CompositionFrame > 0) CompositionFrame--;
    }
    [RelayCommand]
    private void IncrementLayer()
    {
        var fg = CurrentFrameGroup;
        if (fg != null && CompositionLayer < fg.Layers - 1) CompositionLayer++;
    }
    [RelayCommand]
    private void DecrementLayer()
    {
        if (CompositionLayer > 0) CompositionLayer--;
    }
    [RelayCommand]
    private void IncrementPatternX()
    {
        var fg = CurrentFrameGroup;
        if (fg != null && CompositionPatternX < fg.PatternX - 1) CompositionPatternX++;
    }
    [RelayCommand]
    private void DecrementPatternX()
    {
        if (CompositionPatternX > 0) CompositionPatternX--;
    }
    [RelayCommand]
    private void IncrementPatternY()
    {
        var fg = CurrentFrameGroup;
        if (fg != null && CompositionPatternY < fg.PatternY - 1) CompositionPatternY++;
    }
    [RelayCommand]
    private void DecrementPatternY()
    {
        if (CompositionPatternY > 0) CompositionPatternY--;
    }
    [RelayCommand]
    private void IncrementPatternZ()
    {
        var fg = CurrentFrameGroup;
        if (fg != null && CompositionPatternZ < fg.PatternZ - 1) CompositionPatternZ++;
    }
    [RelayCommand]
    private void DecrementPatternZ()
    {
        if (CompositionPatternZ > 0) CompositionPatternZ--;
    }

    // ── Dropdown options for ComboBoxes ──
    public OtbGroup[] GroupOptions { get; } = [OtbGroup.None, OtbGroup.Ground, OtbGroup.Container, OtbGroup.Splash, OtbGroup.Fluid, OtbGroup.Deprecated];
    public string[] StackOrderOptions { get; } = ["None", "Border", "Bottom", "Top"];
    public MinimapColorEntry[] MinimapPalette => TibiaColors.Palette;

    public ObservableCollection<ItemViewModel> Items { get; } = [];

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowMismatchesOnlyChanged(bool value) => ApplyFilter();
    partial void OnShowDeprecatedOnlyChanged(bool value) => ApplyFilter();
    partial void OnClientSearchTextChanged(string value) { ClientCurrentPage = 1; ApplyClientFilter(); }
    partial void OnClientCategoryFilterChanged(string value) { ClientCurrentPage = 1; ApplyClientFilter(); }
    partial void OnOtbPanelSearchTextChanged(string value) { OtbPanelCurrentPage = 1; ApplyOtbPanelFilter(); }

    partial void OnSelectedItemChanged(ItemViewModel? value)
    {
        // Link OTB → Client: auto-select matching client item
        if (value != null && !_suppressOtbClientSync)
        {
            _suppressOtbClientSync = true;
            SelectClientItemByClientId(value.ClientId);
            _suppressOtbClientSync = false;
        }
    }

    public void OpenOtbItemEditor()
    {
        if (SelectedItem == null) return;
        if (SelectedItem.DatThingType != null)
            LoadComposition(SelectedItem.DatThingType);
        IsOtbItemEditing = true;
    }

    partial void OnCompositionFrameChanged(int value) { OnPropertyChanged(nameof(CompositionFrameLabel)); ReloadCompositionGridOnly(); }
    partial void OnCompositionLayerChanged(int value) { OnPropertyChanged(nameof(CompositionLayerLabel)); ReloadComposition(); }
    partial void OnCompositionPatternXChanged(int value) { OnPropertyChanged(nameof(CompositionPatternXLabel)); OnPropertyChanged(nameof(DirectionLabel)); ReloadComposition(); }
    partial void OnCompositionPatternYChanged(int value) { OnPropertyChanged(nameof(CompositionPatternYLabel)); ReloadComposition(); }
    partial void OnCompositionPatternZChanged(int value) => ReloadComposition();
    partial void OnCompositionFrameGroupIndexChanged(int value) { NotifyAllCompositionLabels(); ReloadComposition(); }

    [RelayCommand]
    private async Task SelectClientFolderAsync()
    {
        var window = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window == null) return;

        var dialog = new OpenClientDialog();
        await dialog.ShowDialog(window);

        if (dialog.Result is not { } result) return;
        await LoadClientFromResult(result);
    }

    private void SetupDatDiagLog()
    {
        DatFile.DiagLog = msg => AddMapLog(msg);
    }

    /// <summary>Load client from the dialog result (protocol-aware).</summary>
    private async Task LoadClientFromResult(OpenClientResult result)
    {
        try
        {
            SetupDatDiagLog();
            IsLoading = true;
            StatusText = "Loading client files…";

            _sprFile?.Dispose();
            var (datData, sprFile) = await Task.Run(() =>
            {
                var d = DatFile.Load(result.DatPath, result.ProtocolVersion,
                    result.Extended, result.ImprovedAnimations, result.FrameGroups);
                var s = SprFile.Load(result.SprPath, d.Extended, result.Transparency);
                return (d, s);
            });
            _datData = datData;
            _sprFile = sprFile;
            ClientFolderPath = result.FolderPath;
            IsClientLoaded = true;
            var features = new List<string>();
            if (_datData.Extended) features.Add("Ext");
            if (_datData.ImprovedAnimations) features.Add("Anim");
            if (_datData.FrameGroups) features.Add("FG");
            var featureStr = features.Count > 0 ? $" [{string.Join("+", features)}]" : "";
            StatusText = $"Client loaded: {_datData.ItemCount} items, {_sprFile.SpriteCount} sprites — v{_datData.ProtocolVersion}{featureStr}";

            OnPropertyChanged(nameof(ExposedDatData));
            OnPropertyChanged(nameof(ExposedSprFile));
            _appSettings.LastClientFolderPath = result.FolderPath;
            _appSettings.Save();

            InitRightSpriteList();
            BuildClientItemList();

            if (_otbData != null)
            {
                CrossReferenceDat();
                LoadAllSprites();
                ApplyFilter();
                InitializePalette();
            }

            UpdateActiveSessionName();
        }
        catch (Exception ex)
        {
            StatusText = $"Client load error: {ex.Message}";
            AddMapLog($"[LOAD-RESULT] {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Load client from a folder path (auto-detect protocol). Used by TryLoadLastSessionAsync.</summary>
    private async Task LoadClientFromFolder(string folderPath)
    {
        var (datPath, sprPath) = FindClientFiles(folderPath);
        if (datPath == null || sprPath == null)
        {
            StatusText = "Tibia.dat/Tibia.spr not found in selected folder";
            return;
        }

        try
        {
            SetupDatDiagLog();
            IsLoading = true;
            StatusText = "Loading client files…";

            _sprFile?.Dispose();
            var (datData, sprFile) = await Task.Run(() =>
            {
                var d = DatFile.Load(datPath);
                var s = SprFile.Load(sprPath, d.Extended);
                return (d, s);
            });
            _datData = datData;
            _sprFile = sprFile;
            ClientFolderPath = folderPath;
            IsClientLoaded = true;
            var features2 = new List<string>();
            if (_datData.Extended) features2.Add("Ext");
            if (_datData.ImprovedAnimations) features2.Add("Anim");
            if (_datData.FrameGroups) features2.Add("FG");
            var featureStr2 = features2.Count > 0 ? $" [{string.Join("+", features2)}]" : "";
            StatusText = $"Client loaded: {_datData.ItemCount} items, {_sprFile.SpriteCount} sprites — v{_datData.ProtocolVersion}{featureStr2}";

            OnPropertyChanged(nameof(ExposedDatData));
            OnPropertyChanged(nameof(ExposedSprFile));
            _appSettings.LastClientFolderPath = folderPath;
            _appSettings.Save();

            InitRightSpriteList();
            BuildClientItemList();

            if (_otbData != null)
            {
                CrossReferenceDat();
                LoadAllSprites();
                ApplyFilter();
                InitializePalette();
            }

            UpdateActiveSessionName();
        }
        catch (Exception ex)
        {
            StatusText = $"Client load error: {ex.Message}";
            AddMapLog($"[LOAD-FOLDER] {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenOtbAsync()
    {
        var path = await FileDialogHelper.OpenFileAsync("Open items.otb", [("OTB Files", "*.otb"), ("All Files", "*")]);
        if (path == null) return;
        await LoadOtbFromPath(path);
    }

    private async Task LoadOtbFromPath(string path)
    {
        try
        {
            IsLoading = true;
            StatusText = "Loading OTB…";

            var otbData = await Task.Run(() => OtbFile.Load(path));
            _otbData = otbData;
            _otbPath = path;
            BuildItemList();
            StatusText = $"OTB loaded: {_otbData.Items.Count} items — {Path.GetFileName(path)}";
            HasUnsavedChanges = false;

            // Notify map editor
            OnPropertyChanged(nameof(ExposedOtbData));

            _appSettings.LastOtbPath = path;
            _appSettings.Save();

            // Initialize palette if client already loaded
            if (_datData != null && _sprFile != null)
                InitializePalette();

            UpdateActiveSessionName();
        }
        catch (Exception ex)
        {
            StatusText = $"OTB load error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task TryLoadLastSessionAsync()
    {
        // Restore View menu toggles
        RestoreViewSettings();

        EnsureDefaultSession();

        // Restore sessions from previous app run
        if (_appSettings.Sessions.Count > 0)
        {
            await RestoreSessionsFromSettings();
        }
    }

    private void RestoreViewSettings()
    {
        var vs = _appSettings.ViewSettings;
        if (vs.Count == 0) return;

        if (vs.TryGetValue(nameof(ViewShowAllFloors), out var v)) ViewShowAllFloors = v;
        if (vs.TryGetValue(nameof(ViewShowAnimation), out v)) ViewShowAnimation = v;
        if (vs.TryGetValue(nameof(ViewShowLights), out v)) ViewShowLights = v;
        if (vs.TryGetValue(nameof(ViewShowGrid), out v)) ViewShowGrid = v;
        if (vs.TryGetValue(nameof(ViewShowShade), out v)) ViewShowShade = v;
        if (vs.TryGetValue(nameof(ViewShowAsMinimap), out v)) ViewShowAsMinimap = v;
        if (vs.TryGetValue(nameof(ViewGhostItems), out v)) ViewGhostItems = v;
        if (vs.TryGetValue(nameof(ViewGhostHigherFloors), out v)) ViewGhostHigherFloors = v;
        if (vs.TryGetValue(nameof(ViewGhostLowerFloors), out v)) ViewGhostLowerFloors = v;
        if (vs.TryGetValue(nameof(ViewShowSpecial), out v)) ViewShowSpecial = v;
        if (vs.TryGetValue(nameof(ViewShowZones), out v)) ViewShowZones = v;
        if (vs.TryGetValue(nameof(ViewShowHouses), out v)) ViewShowHouses = v;
        if (vs.TryGetValue(nameof(ViewShowSpawns), out v)) ViewShowSpawns = v;
        if (vs.TryGetValue(nameof(ViewShowWaypoints), out v)) ViewShowWaypoints = v;
        if (vs.TryGetValue(nameof(ViewShowTowns), out v)) ViewShowTowns = v;
        if (vs.TryGetValue(nameof(ViewShowPathing), out v)) ViewShowPathing = v;
        if (vs.TryGetValue(nameof(ViewHighlightItems), out v)) ViewHighlightItems = v;
        if (vs.TryGetValue(nameof(ViewShowTooltips), out v)) ViewShowTooltips = v;
        if (vs.TryGetValue(nameof(ViewShowIngameBox), out v)) ViewShowIngameBox = v;
    }

    /// <summary>Save all open sessions to AppSettings for persistence across app restarts.</summary>
    public void SaveSessionsToSettings()
    {
        // Make sure the current session is up-to-date
        if (_currentSession != null)
            SaveCurrentToSession(_currentSession);

        _appSettings.Sessions.Clear();
        foreach (var session in Sessions)
        {
            _appSettings.Sessions.Add(new SavedSession
            {
                ClientFolderPath = session.ClientFolderPath,
                OtbPath = session.OtbPath,
                MapFilePath = session.MapFilePath,
                ProtocolVersion = session.ProtocolVersion,
                IsActive = session.IsActive,
                MapViewX = session.MapViewX,
                MapViewY = session.MapViewY,
                MapCurrentFloor = session.MapCurrentFloor,
                MapZoom = session.MapZoom,
            });
        }

        // Persist View menu toggles
        _appSettings.ViewSettings = new Dictionary<string, bool>
        {
            [nameof(ViewShowAllFloors)] = ViewShowAllFloors,
            [nameof(ViewShowAnimation)] = ViewShowAnimation,
            [nameof(ViewShowLights)] = ViewShowLights,
            [nameof(ViewShowGrid)] = ViewShowGrid,
            [nameof(ViewShowShade)] = ViewShowShade,
            [nameof(ViewShowAsMinimap)] = ViewShowAsMinimap,
            [nameof(ViewGhostItems)] = ViewGhostItems,
            [nameof(ViewGhostHigherFloors)] = ViewGhostHigherFloors,
            [nameof(ViewGhostLowerFloors)] = ViewGhostLowerFloors,
            [nameof(ViewShowSpecial)] = ViewShowSpecial,
            [nameof(ViewShowZones)] = ViewShowZones,
            [nameof(ViewShowHouses)] = ViewShowHouses,
            [nameof(ViewShowSpawns)] = ViewShowSpawns,
            [nameof(ViewShowWaypoints)] = ViewShowWaypoints,
            [nameof(ViewShowTowns)] = ViewShowTowns,
            [nameof(ViewShowPathing)] = ViewShowPathing,
            [nameof(ViewHighlightItems)] = ViewHighlightItems,
            [nameof(ViewShowTooltips)] = ViewShowTooltips,
            [nameof(ViewShowIngameBox)] = ViewShowIngameBox,
        };

        _appSettings.Save();
    }

    /// <summary>Restore sessions saved from a previous app run.</summary>
    private async Task RestoreSessionsFromSettings()
    {
        var saved = _appSettings.Sessions;
        if (saved.Count == 0) return;

        // Remove the default empty session created by EnsureDefaultSession
        if (Sessions.Count == 1 && Sessions[0].ClientFolderPath == null && Sessions[0].OtbPath == null)
            Sessions.Clear();

        SessionViewModel? activeSession = null;

        foreach (var s in saved)
        {
            var session = new SessionViewModel
            {
                ProtocolVersion = s.ProtocolVersion,
                ClientFolderPath = s.ClientFolderPath,
                OtbPath = s.OtbPath,
                MapFilePath = s.MapFilePath,
                MapViewX = s.MapViewX,
                MapViewY = s.MapViewY,
                MapCurrentFloor = s.MapCurrentFloor,
                MapZoom = s.MapZoom,
            };
            session.UpdateName();
            Sessions.Add(session);

            if (s.IsActive)
                activeSession = session;
        }

        if (Sessions.Count == 0)
        {
            EnsureDefaultSession();
            return;
        }

        OnPropertyChanged(nameof(HasMultipleSessions));

        // Activate the session that was active when the app closed
        activeSession ??= Sessions[0];
        ActiveSession = activeSession;
        activeSession.IsActive = true;

        // Load files for the active session
        await LoadSessionFiles(activeSession);

        // Load files for other sessions in background (don't block UI)
        foreach (var session in Sessions)
        {
            if (session == activeSession) continue;
            await LoadSessionFiles(session);
        }
    }

    /// <summary>Load DAT/SPR/OTB files for a session from its saved paths.</summary>
    private async Task LoadSessionFiles(SessionViewModel session)
    {
        bool isActive = session == _currentSession;

        try
        {
            // Load client files
            if (!string.IsNullOrEmpty(session.ClientFolderPath))
            {
                var (datPath, sprPath) = FindClientFiles(session.ClientFolderPath);
                if (datPath != null && sprPath != null)
                {
                    SetupDatDiagLog();
                    var datData = DatFile.Load(datPath);
                    var sprFile = SprFile.Load(sprPath, datData.Extended);
                    session.DatData = datData;
                    session.SprFile = sprFile;
                    session.IsClientLoaded = true;
                    session.ProtocolVersion = datData.ProtocolVersion;
                }
            }

            // Load OTB
            if (!string.IsNullOrEmpty(session.OtbPath) && File.Exists(session.OtbPath))
            {
                session.OtbData = OtbFile.Load(session.OtbPath);
            }

            // Load Map
            if (!string.IsNullOrEmpty(session.MapFilePath) && File.Exists(session.MapFilePath))
            {
                session.MapData = OtbmFile.Load(session.MapFilePath);
            }

            session.UpdateName();

            // If this is the active session, propagate data to the view model
            if (isActive)
            {
                _datData = session.DatData;
                _sprFile = session.SprFile;
                _otbData = session.OtbData;
                _otbPath = session.OtbPath;
                ClientFolderPath = session.ClientFolderPath;
                IsClientLoaded = session.IsClientLoaded;

                OnPropertyChanged(nameof(ExposedDatData));
                OnPropertyChanged(nameof(ExposedSprFile));
                OnPropertyChanged(nameof(ExposedOtbData));

                if (_sprFile != null)
                    InitRightSpriteList();
                if (_datData != null && _sprFile != null)
                    BuildClientItemList();
                if (_otbData != null)
                {
                    BuildItemList();
                    ApplyFilter();
                    if (_datData != null && _sprFile != null)
                        InitializePalette();
                }

                // Restore map for the active session
                if (session.MapData != null)
                {
                    if (_mapRestoreViewport != null)
                        _mapRestoreViewport(session.MapViewX, session.MapViewY);
                    MapData = session.MapData;
                    MapFilePath = session.MapFilePath;
                    MapTileCount = MapData.Tiles.Count;
                    MapHasUnsavedChanges = false;
                    MapCurrentFloor = session.MapCurrentFloor;
                    MapZoom = session.MapZoom;
                    OnPropertyChanged(nameof(MapFloors));
                }

                StatusText = IsClientLoaded
                    ? $"Restored: {_datData?.ItemCount ?? 0} items — v{_datData?.ProtocolVersion}"
                    : "Select the client folder to begin";
            }
        }
        catch (Exception ex)
        {
            if (isActive)
                StatusText = $"Restore error: {ex.Message}";
        }
    }

    // ── Palette initialization ──

    private void InitializePalette()
    {
        Palette ??= new PaletteViewModel(this);
        Palette.Initialize(_otbData, _datData, _sprFile);
        LoadBrushDatabase();
    }

    private void LoadBrushDatabase()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var brushDir = Path.Combine(baseDir, "data", "brushes");

        // Load OTB autobordering system
        try
        {
            var bordersPath = Path.Combine(brushDir, "borders.xml");
            var groundsPath = Path.Combine(brushDir, "grounds.xml");
            if (File.Exists(bordersPath) && File.Exists(groundsPath))
            {
                BrushDb = BrushDatabase.Load(bordersPath, groundsPath);
                OnPropertyChanged(nameof(BrushDb));
                AddMapLog($"Brush system loaded: {BrushDb.GroundBrushes.Count} ground brushes, {BrushDb.AutoBorders.Count} borders");
            }
        }
        catch (Exception ex)
        {
            AddMapLog($"Brush system error: {ex.Message}");
        }

        // Load full catalog (all brush types + tilesets)
        try
        {
            if (Directory.Exists(brushDir))
            {
                BrushCatalog = BrushXmlLoader.LoadFromDirectory(brushDir);
                OnPropertyChanged(nameof(BrushCatalog));
                AddMapLog($"Brush catalog: {BrushCatalog.Grounds.Count} grounds, {BrushCatalog.Walls.Count} walls, {BrushCatalog.Doodads.Count} doodads, {BrushCatalog.Creatures.Count} creatures, {BrushCatalog.Tilesets.Count} tilesets");
                Palette?.LoadFromBrushCatalog(BrushCatalog);
            }
        }
        catch (Exception ex)
        {
            AddMapLog($"Brush catalog error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        var saved = new List<string>();
        IsLoading = true;
        StatusText = "Saving…";

        try
        {
            // Apply model changes on UI thread before background save
            if (_otbData != null && _otbPath != null)
            {
                foreach (var vm in _allItems)
                    vm.ApplyToModel();
            }

            // Capture references for background work
            var otbData = _otbData;
            var otbPath = _otbPath;
            var datData = _datData;
            var sprFile = _sprFile;
            var mapData = MapData;
            var mapPath = MapFilePath;
            var clientFolder = ClientFolderPath;
            string? datFilePath = null;
            string? sprFilePath = null;

            if (clientFolder != null)
            {
                var (dp, sp) = FindClientFiles(clientFolder);
                datFilePath = dp;
                sprFilePath = sp;
            }

            await Task.Run(() =>
            {
                if (otbData != null && otbPath != null)
                {
                    OtbFile.Save(otbPath, otbData);
                    saved.Add("OTB");
                }

                if (datData != null && datFilePath != null)
                {
                    DatFile.Save(datFilePath, datData);
                    saved.Add("DAT");
                }

                if (sprFile != null && sprFilePath != null && sprFile.HasChanges)
                {
                    var tempPath = sprFilePath + ".tmp";
                    sprFile.Save(tempPath);
                    sprFile.Dispose();
                    File.Move(tempPath, sprFilePath, overwrite: true);
                    saved.Add("SPR");
                }

                if (mapData != null && !string.IsNullOrEmpty(mapPath))
                {
                    OtbmFile.Save(mapPath, mapData);
                    saved.Add("Map");
                }
            });

            // UI-thread updates after background save
            if (saved.Count > 0)
                HasUnsavedChanges = false;

            // Reload SPR if it was saved (needs re-open for memory-mapped access)
            if (saved.Contains("SPR") && sprFilePath != null && datData != null)
            {
                _sprFile = SprFile.Load(sprFilePath, datData.Extended);
                if (_currentSession != null)
                    _currentSession.SprFile = _sprFile;
                OnPropertyChanged(nameof(ExposedSprFile));
            }

            if (mapData != null && !string.IsNullOrEmpty(mapPath))
            {
                MapTileCount = mapData.Tiles.Count;
                MapHasUnsavedChanges = false;
            }

            StatusText = saved.Count > 0
                ? $"Saved: {string.Join(", ", saved)}"
                : "Nothing to save";
        }
        catch (Exception ex)
        {
            StatusText = $"Save error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveOtbAsync()
    {
        if (_otbData == null || _otbPath == null) return;

        try
        {
            foreach (var vm in _allItems)
                vm.ApplyToModel();

            OtbFile.Save(_otbPath, _otbData);
            SaveClientFilesIfLoaded();
            HasUnsavedChanges = false;
            StatusText = $"Saved: {Path.GetFileName(_otbPath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveOtbAsAsync()
    {
        if (_otbData == null) return;

        var path = await FileDialogHelper.SaveFileAsync("Save items.otb as...", [("OTB Files", "*.otb")]);
        if (path == null) return;

        try
        {
            foreach (var vm in _allItems)
                vm.ApplyToModel();

            OtbFile.Save(path, _otbData);
            SaveClientFilesIfLoaded();
            _otbPath = path;
            HasUnsavedChanges = false;
            StatusText = $"Saved as: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save error: {ex.Message}";
        }
    }

    private void SaveClientFilesIfLoaded()
    {
        if (_datData == null || ClientFolderPath == null) return;
        var (datPath, sprPath) = FindClientFiles(ClientFolderPath);
        if (datPath == null) return;
        DatFile.Save(datPath, _datData);

        // Save SPR if it has new/modified sprites (transplants, edits, etc.)
        if (_sprFile != null && sprPath != null && _sprFile.HasChanges)
        {
            var tempPath = sprPath + ".tmp";
            _sprFile.Save(tempPath);
            _sprFile.Dispose();
            File.Move(tempPath, sprPath, overwrite: true);
            _sprFile = SprFile.Load(sprPath, _datData.Extended);

            if (_currentSession != null)
                _currentSession.SprFile = _sprFile;

            OnPropertyChanged(nameof(ExposedSprFile));
        }
    }

    [RelayCommand]
    private void FixAllMismatches()
    {
        int fixed_ = 0;
        foreach (var vm in _allItems)
        {
            if (vm.HasMismatch && vm.DatAnimPhases > 1 && !vm.IsAnimation)
            {
                vm.IsAnimation = true;
                fixed_++;
            }
            else if (vm.HasMismatch && vm.DatAnimPhases <= 1 && vm.IsAnimation)
            {
                vm.IsAnimation = false;
                fixed_++;
            }
        }

        if (fixed_ > 0)
        {
            HasUnsavedChanges = true;
            CrossReferenceDat();
            StatusText = $"{fixed_} mismatches fixed automatically";
        }
    }

    public void MarkDirty() => HasUnsavedChanges = true;

    /// <summary>Create a new OTB server item from the currently selected DAT client item.</summary>
    [RelayCommand]
    private void CreateServerItemFromClient()
    {
        if (_otbData == null || SelectedClientItem == null) return;
        var dat = SelectedClientItem.ThingType;
        if (dat.Category != ThingCategory.Item) { StatusText = "Only Item-category things can be added to OTB"; return; }

        // Derive next server ID
        ushort nextServerId = (ushort)(_otbData.Items.Count > 0
            ? _otbData.Items.Max(i => i.ServerId) + 1
            : 100);

        // Derive OTB group from DAT flags
        OtbGroup group = OtbGroup.None;
        if (dat.IsGround) group = OtbGroup.Ground;
        else if (dat.IsContainer) group = OtbGroup.Container;
        else if (dat.IsFluidContainer) group = OtbGroup.Splash;
        else if (dat.IsFluid) group = OtbGroup.Fluid;
        else if (dat.IsWritable || dat.IsWritableOnce) group = OtbGroup.Writable;
        else if (dat.IsStackable) group = OtbGroup.Ammunition;
        else if (dat.IsPickupable) group = OtbGroup.Armor;

        // Build OTB flags from DAT flags
        OtbFlags flags = OtbFlags.None;
        if (dat.IsStackable) flags |= OtbFlags.Stackable;
        if (dat.IsPickupable) flags |= OtbFlags.Pickupable;
        if (!dat.IsUnmoveable) flags |= OtbFlags.Moveable;
        if (dat.IsUnpassable) flags |= OtbFlags.BlockSolid;
        if (dat.IsBlockMissile) flags |= OtbFlags.BlockProjectile;
        if (dat.IsBlockPathfind) flags |= OtbFlags.BlockPathFind;
        if (dat.HasElevation) flags |= OtbFlags.HasHeight;
        if (dat.IsUsable || dat.IsMultiUse) flags |= OtbFlags.Usable;
        if (dat.IsHangable) flags |= OtbFlags.Hangable;
        if (dat.IsRotatable) flags |= OtbFlags.Rotatable;
        if (dat.IsWritable || dat.IsWritableOnce) flags |= OtbFlags.Readable;
        if (dat.IsForceUse) flags |= OtbFlags.ForceUse;
        if (dat.IsFullGround) flags |= OtbFlags.FullGround;
        if (dat.IsVertical) flags |= OtbFlags.Vertical;
        if (dat.IsHorizontal) flags |= OtbFlags.Horizontal;

        var newItem = new OtbItem
        {
            ServerId = nextServerId,
            ClientId = dat.Id,
            Group = group,
            Flags = flags,
            Speed = dat.GroundSpeed,
            LightLevel = dat.LightLevel,
            LightColor = dat.LightColor,
            MinimapColor = dat.MiniMapColor,
            MaxReadWriteChars = dat.MaxTextLength,
            Name = dat.MarketName.Length > 0 ? dat.MarketName : null,
        };

        // Check animation phases
        if (dat.FrameGroups.Length > 0)
        {
            var fg = dat.FrameGroups[0];
            if (fg.Frames > 1) newItem.IsAnimation = true;
        }

        _otbData.Items.Add(newItem);
        var vm = new ItemViewModel(newItem, this);
        _allItems.Add(vm);
        TotalItems = _allItems.Count;
        CrossReferenceDat();
        LoadAllSprites();
        ApplyFilter();
        HasUnsavedChanges = true;
        SelectedItem = vm;
        StatusText = $"Created server item {nextServerId} from client {dat.Id} (group: {group})";
    }

    /// <summary>
    /// Creates OTB items from the current multi-selection (or single selection).
    /// Called from the context menu.
    /// </summary>
    public void CreateServerItemsFromSelection()
    {
        if (_otbData == null) return;

        var items = SelectedClientItemsList.Count > 0
            ? SelectedClientItemsList.ToList()
            : (SelectedClientItem != null ? new List<ClientItemViewModel> { SelectedClientItem } : []);

        if (items.Count == 0)
        {
            StatusText = "No items selected.";
            return;
        }

        int created = 0;
        foreach (var cvm in items)
        {
            // Temporarily set SelectedClientItem so CreateServerItemFromClient works
            SelectedClientItem = cvm;
            if (cvm.Category != ThingCategory.Item) continue;
            CreateServerItemFromClient();
            created++;
        }

        if (created == 0)
            StatusText = "No Item-category things in selection (only Items can become OTB items).";
        else
            StatusText = $"Created {created} OTB item(s) from selection.";
    }

    private void BuildItemList()
    {
        _allItems.Clear();
        Items.Clear();

        if (_otbData == null) return;

        foreach (var item in _otbData.Items)
        {
            var vm = new ItemViewModel(item, this);
            _allItems.Add(vm);
        }

        TotalItems = _allItems.Count;
        CrossReferenceDat();
        LoadAllSprites();
        ApplyFilter();
    }

    private void CrossReferenceDat()
    {
        int mismatches = 0;
        foreach (var vm in _allItems)
        {
            if (_datData != null && _datData.Items.TryGetValue(vm.ClientId, out var datThing))
            {
                // Sum animation phases across all frame groups
                int totalPhases = 0;
                foreach (var fg in datThing.FrameGroups)
                    totalPhases += fg.Frames;

                vm.DatAnimPhases = totalPhases;
                vm.DatAnimateAlways = datThing.IsAnimateAlways;
                vm.FirstSpriteId = datThing.FirstSpriteId;
                vm.DatThingType = datThing;
                vm.HasMismatch = (totalPhases > 1) != vm.IsAnimation;
            }
            else
            {
                vm.DatAnimPhases = 0;
                vm.DatAnimateAlways = false;
                vm.FirstSpriteId = 0;
                vm.DatThingType = null;
                vm.HasMismatch = false;
            }

            if (vm.HasMismatch) mismatches++;
        }
        MismatchCount = mismatches;

        // Propagate mismatch info to client items
        var mismatchClientIds = new HashSet<ushort>();
        foreach (var vm in _allItems)
            if (vm.HasMismatch) mismatchClientIds.Add(vm.ClientId);
        foreach (var cvm in _allClientItems)
            cvm.HasOtbMismatch = mismatchClientIds.Contains(cvm.Id);
    }

    private void LoadAllSprites()
    {
        if (_sprFile == null) return;

        foreach (var vm in _allItems)
        {
            if (vm.DatThingType != null)
            {
                vm.Sprite = ComposeThingBitmap(vm.DatThingType);
                continue;
            }
            if (vm.FirstSpriteId == 0)
            {
                vm.Sprite = null;
                continue;
            }
            vm.Sprite = LoadSpriteBitmap(vm.FirstSpriteId);
        }
    }

    private static (string? datPath, string? sprPath) FindClientFiles(string folder)
    {
        // Search in the folder and common subfolders
        string[] searchPaths = [
            folder,
            Path.Combine(folder, "data", "things"),
        ];

        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath)) continue;

            // Check directly
            var dat = Path.Combine(basePath, "Tibia.dat");
            var spr = Path.Combine(basePath, "Tibia.spr");
            if (File.Exists(dat) && File.Exists(spr))
                return (dat, spr);

            // Check subdirectories (e.g., 1098/)
            foreach (var subDir in Directory.GetDirectories(basePath))
            {
                dat = Path.Combine(subDir, "Tibia.dat");
                spr = Path.Combine(subDir, "Tibia.spr");
                if (File.Exists(dat) && File.Exists(spr))
                    return (dat, spr);
            }
        }

        return (null, null);
    }

    private void ApplyFilter()
    {
        Items.Clear();

        var search = SearchText.Trim();
        bool hasSearch = !string.IsNullOrEmpty(search);
        ushort numericId = 0;
        bool isNumericSearch = hasSearch && ushort.TryParse(search, out numericId);

        foreach (var vm in _allItems)
        {
            // When "Deprecated Only" is on, show only deprecated items
            // By default hide deprecated unless the filter is active
            if (ShowDeprecatedOnly && !vm.IsDeprecated) continue;
            if (!ShowDeprecatedOnly && vm.IsDeprecated) continue;
            if (ShowMismatchesOnly && !vm.HasMismatch) continue;

            if (hasSearch)
            {
                if (isNumericSearch)
                {
                    if (vm.ServerId != numericId && vm.ClientId != numericId) continue;
                }
                else if (search.Equals("mismatch", StringComparison.OrdinalIgnoreCase))
                {
                    if (!vm.HasMismatch) continue;
                }
                else
                {
                    if (!vm.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase)
                        && !vm.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
            }

            Items.Add(vm);
        }

        FilteredCount = Items.Count;
        ApplyOtbPanelFilter();
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── OTB Panel (right column) ──
    // ══════════════════════════════════════════════════════════════════════

    private void ApplyOtbPanelFilter()
    {
        _otbPanelFilteredItems.Clear();
        OtbPanelItems.Clear();

        var search = OtbPanelSearchText.Trim();
        bool hasSearch = !string.IsNullOrEmpty(search);
        ushort numericId = 0;
        bool isNumericSearch = hasSearch && ushort.TryParse(search, out numericId);

        foreach (var vm in _allItems)
        {
            if (vm.IsDeprecated) continue;
            if (hasSearch)
            {
                if (isNumericSearch)
                {
                    if (vm.ServerId != numericId && vm.ClientId != numericId) continue;
                }
                else
                {
                    if (!vm.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                        && !vm.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
            }
            _otbPanelFilteredItems.Add(vm);
        }

        OtbPanelTotalPages = Math.Max(1, (_otbPanelFilteredItems.Count + OtbPanelItemsPerPage - 1) / OtbPanelItemsPerPage);
        if (OtbPanelCurrentPage > OtbPanelTotalPages) OtbPanelCurrentPage = OtbPanelTotalPages;
        LoadOtbPanelPage();
    }

    private void LoadOtbPanelPage()
    {
        OtbPanelItems.Clear();
        int start = (OtbPanelCurrentPage - 1) * OtbPanelItemsPerPage;
        int end = Math.Min(start + OtbPanelItemsPerPage, _otbPanelFilteredItems.Count);
        for (int i = start; i < end; i++)
            OtbPanelItems.Add(_otbPanelFilteredItems[i]);
    }

    [RelayCommand] private void OtbPanelFirstPage() { OtbPanelCurrentPage = 1; LoadOtbPanelPage(); }
    [RelayCommand] private void OtbPanelPrevPage() { if (OtbPanelCurrentPage > 1) { OtbPanelCurrentPage--; LoadOtbPanelPage(); } }
    [RelayCommand] private void OtbPanelNextPage() { if (OtbPanelCurrentPage < OtbPanelTotalPages) { OtbPanelCurrentPage++; LoadOtbPanelPage(); } }
    [RelayCommand] private void OtbPanelLastPage() { OtbPanelCurrentPage = OtbPanelTotalPages; LoadOtbPanelPage(); }

    [RelayCommand]
    private void RemoveOtbItems()
    {
        if (_otbData == null) return;

        var toRemove = SelectedOtbItemsList.Count > 0
            ? SelectedOtbItemsList.ToList()
            : (SelectedItem != null ? new List<ItemViewModel> { SelectedItem } : []);

        if (toRemove.Count == 0) { StatusText = "No OTB items selected."; return; }

        // Find the index of the earliest selected item to select the one before it
        int earliestIdx = int.MaxValue;
        foreach (var item in toRemove)
        {
            int idx = _allItems.IndexOf(item);
            if (idx >= 0 && idx < earliestIdx) earliestIdx = idx;
        }

        int removed = 0;
        foreach (var item in toRemove)
        {
            if (_otbData.Items.Remove(item.Model))
            {
                _allItems.Remove(item);
                removed++;
            }
        }

        if (removed == 0) { StatusText = "No OTB items were removed."; return; }

        TotalItems = _allItems.Count;

        // Select nearest item before the deleted ones
        int selectIdx = Math.Max(0, earliestIdx - 1);
        var nextSelection = selectIdx < _allItems.Count ? _allItems[selectIdx] : null;

        SelectedItem = null;
        IsOtbItemEditing = false;
        ApplyOtbPanelFilter();
        ApplyFilter();

        if (nextSelection != null)
        {
            int filteredIdx = _otbPanelFilteredItems.IndexOf(nextSelection);
            if (filteredIdx >= 0)
            {
                int page = filteredIdx / OtbPanelItemsPerPage + 1;
                OtbPanelCurrentPage = Math.Clamp(page, 1, OtbPanelTotalPages);
                LoadOtbPanelPage();
                SelectedItem = nextSelection;
            }
        }

        HasUnsavedChanges = true;
        var label = removed == 1 ? "Removed 1 OTB item" : $"Removed {removed} OTB items";
        StatusText = label;
        AddMapLog(label);
    }

    /// <summary>Find and select the OTB item matching a clientId, navigating to the correct page.</summary>
    private void SelectOtbItemByClientId(ushort clientId)
    {
        int idx = _otbPanelFilteredItems.FindIndex(v => v.ClientId == clientId);
        if (idx < 0) return;
        int page = idx / OtbPanelItemsPerPage + 1;
        if (page != OtbPanelCurrentPage)
        {
            OtbPanelCurrentPage = page;
            LoadOtbPanelPage();
        }
        SelectedItem = _otbPanelFilteredItems[idx];
    }

    /// <summary>Find and select the client item matching a clientId, navigating to the correct page.</summary>
    private void SelectClientItemByClientId(ushort clientId)
    {
        var cat = ClientCategoryFilter;
        var source = cat == "All" ? _allClientItems :
            _allClientItems.Where(c => c.CategoryName.Equals(cat, StringComparison.OrdinalIgnoreCase)).ToList();
        int idx = source.FindIndex(c => c.Id == clientId);
        if (idx < 0) return;
        int page = idx / ClientItemsPerPage + 1;
        if (page != ClientCurrentPage)
        {
            ClientCurrentPage = page;
            // Rebuild filtered items for the page
            _clientFilteredItems = source;
            ClientTotalPages = Math.Max(1, (_clientFilteredItems.Count + ClientItemsPerPage - 1) / ClientItemsPerPage);
            LoadClientPage();
        }
        SelectedClientItem = source[idx];
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Client Items (DAT view) ──
    // ══════════════════════════════════════════════════════════════════════

    private void BuildClientItemList()
    {
        _allClientItems.Clear();
        ClientItems.Clear();
        if (_datData == null) return;

        void AddCategory(Dictionary<ushort, DatThingType> dict)
        {
            foreach (var kvp in dict.OrderBy(x => x.Key))
            {
                var vm = new ClientItemViewModel(kvp.Value);
                vm.Sprite = ComposeThingBitmap(kvp.Value);
                _allClientItems.Add(vm);
            }
        }

        AddCategory(_datData.Items);
        AddCategory(_datData.Outfits);
        AddCategory(_datData.Effects);
        AddCategory(_datData.Missiles);

        ApplyClientFilter();
        StartAnimationTimer();
    }

    private void ApplyClientFilter()
    {
        _clientFilteredItems.Clear();
        ClientItems.Clear();

        var search = ClientSearchText.Trim();
        bool hasSearch = !string.IsNullOrEmpty(search);
        ushort numericId = 0;
        bool isNumericSearch = hasSearch && ushort.TryParse(search, out numericId);

        bool filterMismatch = ClientCategoryFilter == "Mismatch";

        foreach (var vm in _allClientItems)
        {
            if (filterMismatch)
            {
                if (!vm.HasOtbMismatch) continue;
            }
            else if (ClientCategoryFilter != "All")
            {
                if (!vm.CategoryName.Equals(ClientCategoryFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            if (hasSearch)
            {
                if (isNumericSearch)
                {
                    if (vm.Id != numericId) continue;
                }
                else if (search.Equals("mismatch", StringComparison.OrdinalIgnoreCase))
                {
                    if (!vm.HasOtbMismatch) continue;
                }
                else
                {
                    if (!vm.CategoryName.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
            }

            _clientFilteredItems.Add(vm);
        }

        ClientFilteredCount = _clientFilteredItems.Count;
        ClientTotalPages = Math.Max(1, (_clientFilteredItems.Count + ClientItemsPerPage - 1) / ClientItemsPerPage);
        ClientCurrentPage = Math.Clamp(ClientCurrentPage, 1, ClientTotalPages);
        LoadClientPage();
    }

    private void LoadClientPage()
    {
        ClientItems.Clear();
        int start = (ClientCurrentPage - 1) * ClientItemsPerPage;
        int end = Math.Min(start + ClientItemsPerPage, _clientFilteredItems.Count);
        for (int i = start; i < end; i++)
            ClientItems.Add(_clientFilteredItems[i]);

        if (ClientItems.Count > 0 && (SelectedClientItem == null || !ClientItems.Contains(SelectedClientItem)))
            SelectedClientItem = ClientItems[0];
    }

    partial void OnSelectedClientItemChanged(ClientItemViewModel? value)
    {
        if (value != null)
        {
            _suppressNavigateSync = true;
            ClientNavigateId = value.Id.ToString();
            _suppressNavigateSync = false;

            // Link Client → OTB: auto-select matching OTB item
            if (!_suppressOtbClientSync)
            {
                _suppressOtbClientSync = true;
                SelectOtbItemByClientId(value.Id);
                _suppressOtbClientSync = false;
            }
        }
        else
        {
            IsClientItemEditing = false;
            CompositionSprites.Clear();
        }

        OnPropertyChanged(nameof(IsItemSelected));
        OnPropertyChanged(nameof(IsOutfitSelected));
        OnPropertyChanged(nameof(IsEffectSelected));
        OnPropertyChanged(nameof(IsMissileSelected));
        OnPropertyChanged(nameof(IsNotMissileSelected));
    }

    public void OpenClientItemEditor()
    {
        if (SelectedClientItem == null) return;
        LoadComposition(SelectedClientItem.ThingType);
        IsClientItemEditing = true;
        // Also show the linked OTB detail panel if an OTB item is linked
        IsOtbItemEditing = SelectedItem != null;
    }

    /// <summary>
    /// Navigate to the selected client item's page, resetting filters so all items are visible.
    /// Then open the item editor.
    /// </summary>
    public void NavigateToClientItem()
    {
        if (SelectedClientItem == null) return;
        var targetId = SelectedClientItem.Id;

        // Set filter to the item's category so it stays visible
        _suppressNavigateSync = true;
        ClientSearchText = string.Empty;
        ClientCategoryFilter = SelectedClientItem.CategoryName;
        _suppressNavigateSync = false;

        // Rebuild filtered list with category filter
        ApplyClientFilter();

        // Find the item in the full list and navigate to its page
        int idx = _clientFilteredItems.FindIndex(c => c.Id == targetId);
        if (idx >= 0)
        {
            int page = idx / ClientItemsPerPage + 1;
            ClientCurrentPage = Math.Clamp(page, 1, ClientTotalPages);
            LoadClientPage();
            SelectedClientItem = _clientFilteredItems[idx];
        }

        // Open the editor for this item
        OpenClientItemEditor();
    }

    private bool _suppressNavigateSync;

    /// <summary>Navigate to a specific client item by ID and category after a replace operation.</summary>
    private void NavigateToClientItemById(ushort id, ThingCategory category)
    {
        int idx = _clientFilteredItems.FindIndex(c => c.Id == id && c.Category == category);
        if (idx >= 0)
        {
            int page = idx / ClientItemsPerPage + 1;
            ClientCurrentPage = Math.Clamp(page, 1, ClientTotalPages);
            LoadClientPage();
            SelectedClientItem = _clientFilteredItems[idx];
        }
    }

    partial void OnClientNavigateIdChanged(string value)
    {
        if (_suppressNavigateSync) return;
        if (ushort.TryParse(value.Trim(), out var targetId))
        {
            // Find in filtered items and navigate to correct page
            int idx = _clientFilteredItems.FindIndex(c => c.Id == targetId);
            if (idx >= 0)
            {
                int page = idx / ClientItemsPerPage + 1;
                if (page != ClientCurrentPage)
                {
                    ClientCurrentPage = page;
                    LoadClientPage();
                }
                SelectedClientItem = _clientFilteredItems[idx];
            }
        }
    }

    // ── Client item page navigation ──

    [RelayCommand]
    private void ClientFirstPage()
    {
        ClientCurrentPage = 1;
        LoadClientPage();
    }

    [RelayCommand]
    private void ClientPrevPage()
    {
        if (ClientCurrentPage > 1) { ClientCurrentPage--; LoadClientPage(); }
    }

    [RelayCommand]
    private void ClientNextPage()
    {
        if (ClientCurrentPage < ClientTotalPages) { ClientCurrentPage++; LoadClientPage(); }
    }

    [RelayCommand]
    private void ClientLastPage()
    {
        ClientCurrentPage = ClientTotalPages;
        LoadClientPage();
    }

    [RelayCommand]
    private void ClientPrevItem()
    {
        if (SelectedClientItem == null) return;
        int idx = _clientFilteredItems.IndexOf(SelectedClientItem);
        if (idx <= 0) return;
        idx--;
        int page = idx / ClientItemsPerPage + 1;
        if (page != ClientCurrentPage)
        {
            ClientCurrentPage = page;
            LoadClientPage();
        }
        SelectedClientItem = _clientFilteredItems[idx];
    }

    [RelayCommand]
    private void ClientNextItem()
    {
        if (SelectedClientItem == null) return;
        int idx = _clientFilteredItems.IndexOf(SelectedClientItem);
        if (idx < 0 || idx >= _clientFilteredItems.Count - 1) return;
        idx++;
        int page = idx / ClientItemsPerPage + 1;
        if (page != ClientCurrentPage)
        {
            ClientCurrentPage = page;
            LoadClientPage();
        }
        SelectedClientItem = _clientFilteredItems[idx];
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Right-side Sprite Panel ──
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void RightSpriteFirstPage()
    {
        RightSpriteCurrentPage = 1;
        LoadRightSpritePage();
    }

    [RelayCommand]
    private void RightSpritePrevPage()
    {
        if (RightSpriteCurrentPage > 1) { RightSpriteCurrentPage--; LoadRightSpritePage(); }
    }

    [RelayCommand]
    private void RightSpriteNextPage()
    {
        if (RightSpriteCurrentPage < RightSpriteTotalPages) { RightSpriteCurrentPage++; LoadRightSpritePage(); }
    }

    [RelayCommand]
    private void RightSpriteLastPage()
    {
        RightSpriteCurrentPage = RightSpriteTotalPages;
        LoadRightSpritePage();
    }

    private void InitRightSpriteList()
    {
        if (_sprFile == null) return;
        RightSpriteTotalPages = Math.Max(1, ((int)_sprFile.SpriteCount + RightSpritesPerPage - 1) / RightSpritesPerPage);
        RightSpriteCurrentPage = 1;
        LoadRightSpritePage();
    }

    private void LoadRightSpritePage()
    {
        RightSprites.Clear();
        if (_sprFile == null) return;

        int total = (int)_sprFile.SpriteCount;
        int startId = (RightSpriteCurrentPage - 1) * RightSpritesPerPage + 1;
        int endId = Math.Min(startId + RightSpritesPerPage - 1, total);

        for (int id = startId; id <= endId; id++)
        {
            var vm = new SpriteViewModel { SpriteId = (uint)id };
            vm.Bitmap = LoadSpriteBitmap((uint)id);
            RightSprites.Add(vm);
        }
    }

    private DatThingType? _currentCompositionThing;

    private void LoadComposition(DatThingType thing)
    {
        _currentCompositionThing = thing;

        // Snapshot original state for reset (only if not already stored)
        var key = (thing.Category, thing.Id);
        if (!_originalSnapshots.ContainsKey(key))
            _originalSnapshots[key] = thing.Clone();

        StopCompositionAnimTimer();
        IsPlayingAnimation = false;
        CompositionFrameGroupIndex = 0;
        CompositionFrame = 0;
        CompositionLayer = 0;
        CompositionPatternX = 0;
        CompositionPatternY = 0;
        CompositionPatternZ = 0;
        NotifyAllCompositionLabels();
        BuildCompositionGrid();
        BuildFilmstrip();
    }

    private void ReloadComposition()
    {
        if (_currentCompositionThing != null)
        {
            BuildCompositionGrid();
            if (ShowAllFrames) BuildFilmstrip();
        }
    }

    private void ReloadCompositionGridOnly()
    {
        if (_currentCompositionThing != null)
            BuildCompositionGrid();
    }

    private void BuildCompositionGrid()
    {
        CompositionSprites.Clear();

        var thing = _currentCompositionThing;
        if (thing == null || thing.FrameGroups.Length == 0)
        {
            CompositionGridColumns = 1;
            CompositionGridRows = 1;
            CompositionExactSize = 32;
            return;
        }

        int fgIdx = Math.Clamp(CompositionFrameGroupIndex, 0, Math.Max(0, thing.FrameGroups.Length - 1));
        var fg = thing.FrameGroups[fgIdx];

        if (fg.Width == 0 || fg.Height == 0)
        {
            CompositionGridColumns = 1;
            CompositionGridRows = 1;
            CompositionExactSize = 32;
            return;
        }

        int frame = Math.Clamp(CompositionFrame, 0, Math.Max(0, fg.Frames - 1));
        int layer = Math.Clamp(CompositionLayer, 0, Math.Max(0, fg.Layers - 1));
        int pz = Math.Clamp(CompositionPatternZ, 0, Math.Max(0, fg.PatternZ - 1));

        // OB behavior: items/effects show ALL patterns at once; outfits/missiles show one pattern
        bool showAllPatterns = thing.Category != ThingCategory.Outfit && thing.Category != ThingCategory.Missile;
        int pxCount = showAllPatterns ? fg.PatternX : 1;
        int pyCount = showAllPatterns ? fg.PatternY : 1;
        int pxStart = showAllPatterns ? 0 : Math.Clamp(CompositionPatternX, 0, Math.Max(0, fg.PatternX - 1));
        int pyStart = showAllPatterns ? 0 : Math.Clamp(CompositionPatternY, 0, Math.Max(0, fg.PatternY - 1));

        CompositionGridColumns = fg.Width * pxCount;
        CompositionGridRows = fg.Height * pyCount;
        CompositionExactSize = fg.ExactSize;

        // Build grid: for each pattern cell (py, px), render W×H sprites
        // Tibia coordinate system: w=0,h=0 is bottom-right; we render top-left to bottom-right
        for (int patY = 0; patY < pyCount; patY++)
        {
            for (int row = 0; row < fg.Height; row++)
            {
                for (int patX = 0; patX < pxCount; patX++)
                {
                    for (int col = 0; col < fg.Width; col++)
                    {
                        int px = pxStart + patX;
                        int py = pyStart + patY;
                        int flatIdx = fg.GetFlatIndex(fg.Width - 1 - col, fg.Height - 1 - row, layer, px, py, pz, frame);
                        uint spriteId = flatIdx >= 0 ? fg.SpriteIndex[flatIdx] : 0;
                        var svm = new SpriteViewModel { SpriteId = spriteId, SlotIndex = flatIdx };
                        svm.Bitmap = LoadSpriteBitmap(spriteId);
                        CompositionSprites.Add(svm);
                    }
                }
            }
        }
    }

    [RelayCommand]
    private void NavigateRightSpriteToId(uint spriteId)
    {
        if (_sprFile == null || spriteId == 0) return;

        int page = (int)((spriteId - 1) / RightSpritesPerPage) + 1;
        if (page != RightSpriteCurrentPage)
        {
            RightSpriteCurrentPage = page;
            LoadRightSpritePage();
        }

        SelectedRightSprite = RightSprites.FirstOrDefault(s => s.SpriteId == spriteId);
    }

    /// <summary>Assign a sprite to a composition slot (used by drag-drop from sprite list).</summary>
    public void AssignSpriteToSlot(SpriteViewModel targetSlot, uint newSpriteId)
    {
        if (targetSlot.SlotIndex < 0) return;
        var thing = _currentCompositionThing;
        if (thing == null || thing.FrameGroups.Length == 0) return;

        int fgIdx = Math.Clamp(CompositionFrameGroupIndex, 0, Math.Max(0, thing.FrameGroups.Length - 1));
        var fg = thing.FrameGroups[fgIdx];

        fg.SetSpriteId(targetSlot.SlotIndex, newSpriteId);

        // Update the cell in-place
        targetSlot.SpriteId = newSpriteId;
        targetSlot.Bitmap = LoadSpriteBitmap(newSpriteId);

        StatusText = $"Set slot {targetSlot.SlotIndex} → sprite {newSpriteId}";
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Reset thing to original state ──
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private void ResetThing()
    {
        var thing = _currentCompositionThing;
        if (thing == null || SelectedClientItem == null) return;

        var key = (thing.Category, thing.Id);
        if (!_originalSnapshots.TryGetValue(key, out var original)) return;

        // Restore from snapshot
        var restored = original.Clone();
        SelectedClientItem.ThingType.FrameGroups = restored.FrameGroups;
        CopyThingFlags(restored, SelectedClientItem.ThingType);

        // Re-snapshot (so another reset restores to original, not to the clone)
        _originalSnapshots[key] = original;

        // Reload
        StopCompositionAnimTimer();
        IsPlayingAnimation = false;
        CompositionFrameGroupIndex = 0;
        CompositionFrame = 0;
        CompositionLayer = 0;
        CompositionPatternX = 0;
        CompositionPatternY = 0;
        CompositionPatternZ = 0;
        NotifyAllCompositionLabels();
        OnPropertyChanged(nameof(IsItemSelected));
        OnPropertyChanged(nameof(IsOutfitSelected));
        OnPropertyChanged(nameof(IsEffectSelected));
        OnPropertyChanged(nameof(IsMissileSelected));
        OnPropertyChanged(nameof(IsNotMissileSelected));
        ReloadComposition();
        StatusText = $"Reset item {thing.Id} to original state";
    }

    private static void CopyThingFlags(DatThingType src, DatThingType dst)
    {
        dst.IsGround = src.IsGround; dst.GroundSpeed = src.GroundSpeed;
        dst.IsGroundBorder = src.IsGroundBorder; dst.IsOnBottom = src.IsOnBottom; dst.IsOnTop = src.IsOnTop;
        dst.IsContainer = src.IsContainer; dst.IsStackable = src.IsStackable;
        dst.IsForceUse = src.IsForceUse; dst.IsMultiUse = src.IsMultiUse;
        dst.IsWritable = src.IsWritable; dst.IsWritableOnce = src.IsWritableOnce;
        dst.MaxTextLength = src.MaxTextLength;
        dst.IsFluidContainer = src.IsFluidContainer; dst.IsFluid = src.IsFluid;
        dst.IsUnpassable = src.IsUnpassable; dst.IsUnmoveable = src.IsUnmoveable;
        dst.IsBlockMissile = src.IsBlockMissile; dst.IsBlockPathfind = src.IsBlockPathfind;
        dst.IsNoMoveAnimation = src.IsNoMoveAnimation; dst.IsPickupable = src.IsPickupable;
        dst.IsHangable = src.IsHangable; dst.IsVertical = src.IsVertical; dst.IsHorizontal = src.IsHorizontal;
        dst.IsRotatable = src.IsRotatable;
        dst.HasLight = src.HasLight; dst.LightLevel = src.LightLevel; dst.LightColor = src.LightColor;
        dst.IsDontHide = src.IsDontHide; dst.IsTranslucent = src.IsTranslucent;
        dst.HasOffset = src.HasOffset; dst.OffsetX = src.OffsetX; dst.OffsetY = src.OffsetY;
        dst.HasElevation = src.HasElevation; dst.Elevation = src.Elevation;
        dst.IsLyingObject = src.IsLyingObject; dst.IsAnimateAlways = src.IsAnimateAlways;
        dst.IsMiniMap = src.IsMiniMap; dst.MiniMapColor = src.MiniMapColor;
        dst.IsLensHelp = src.IsLensHelp; dst.LensHelp = src.LensHelp;
        dst.IsFullGround = src.IsFullGround; dst.IsIgnoreLook = src.IsIgnoreLook;
        dst.IsCloth = src.IsCloth; dst.ClothSlot = src.ClothSlot;
        dst.IsMarketItem = src.IsMarketItem;
        dst.MarketCategory = src.MarketCategory; dst.MarketTradeAs = src.MarketTradeAs;
        dst.MarketShowAs = src.MarketShowAs; dst.MarketName = src.MarketName;
        dst.MarketRestrictProfession = src.MarketRestrictProfession; dst.MarketRestrictLevel = src.MarketRestrictLevel;
        dst.HasDefaultAction = src.HasDefaultAction; dst.DefaultAction = src.DefaultAction;
        dst.IsWrappable = src.IsWrappable; dst.IsUnwrappable = src.IsUnwrappable;
        dst.IsTopEffect = src.IsTopEffect; dst.IsUsable = src.IsUsable;
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Thing CRUD Commands ──
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task ReplaceThingAsync()
    {
        if (SelectedClientItem == null || _datData == null) return;
        StatusText = "Replace: not yet implemented — DAT write support needed";
    }

    [RelayCommand]
    private async Task ImportThingAsync()
    {
        if (_datData == null || _sprFile == null)
        {
            StatusText = "Load a client first before importing";
            return;
        }

        var dialog = new ImportThingDialog();

        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            await dialog.ShowDialog(desktop.MainWindow);
        }
        else
        {
            dialog.Show();
            return;
        }

        if (dialog.Result == null) return;
        var r = dialog.Result;

        try
        {
            // 1. Copy sprites from source to our SPR, building old→new ID map
            var spriteIdMap = new Dictionary<uint, uint>();
            foreach (var (oldId, rgba) in r.SpriteData)
            {
                var newId = _sprFile.AddSprite(rgba);
                spriteIdMap[oldId] = newId;
            }

            // 2. Remap sprite indices in the cloned thing
            var thing = r.Thing;
            foreach (var fg in thing.FrameGroups)
            {
                if (fg.SpriteIndex == null) continue;
                for (int i = 0; i < fg.SpriteIndex.Length; i++)
                {
                    var old = fg.SpriteIndex[i];
                    if (old != 0 && spriteIdMap.TryGetValue(old, out var newId))
                        fg.SpriteIndex[i] = newId;
                }
            }

            // 3. Assign new ID and add to the current DAT
            var dict = GetDatDictForCategory(r.Category);
            ushort newThingId = dict.Count > 0 ? (ushort)(dict.Keys.Max() + 1) : (ushort)100;
            thing.Id = newThingId;
            thing.Category = r.Category;
            dict[newThingId] = thing;

            // 4. Rebuild UI lists
            RefreshSpritePanel();
            BuildClientItemList();
            HasUnsavedChanges = true;

            StatusText = $"Imported {r.Category} #{newThingId} with {r.SpriteData.Count} sprite(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Import error: {ex.Message}";
        }
    }

    private Dictionary<ushort, DatThingType> GetDatDictForCategory(ThingCategory category)
    {
        return GetCategoryDict(_datData!, category);
    }

    private static Dictionary<ushort, DatThingType> GetCategoryDict(DatData data, ThingCategory category)
    {
        return category switch
        {
            ThingCategory.Outfit => data.Outfits,
            ThingCategory.Effect => data.Effects,
            ThingCategory.Missile => data.Missiles,
            _ => data.Items,
        };
    }

    [RelayCommand]
    private async Task ExportThingAsync()
    {
        if (SelectedClientItem == null || _sprFile == null) return;

        var thing = SelectedClientItem.ThingType;
        var dialog = new ExportDialog
        {
            SuggestedFileName = $"{thing.Category.ToString().ToLowerInvariant()}_{thing.Id}",
            SuggestedClientVersion = (ushort)(_datData?.ProtocolVersion ?? 854),
        };

        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            await dialog.ShowDialog(desktop.MainWindow);
        }
        else
        {
            dialog.Show();
            return;
        }

        if (dialog.Result == null) return;
        var r = dialog.Result;

        try
        {
            if (r.Format == ExportFormat.Obd)
            {
                // Export as .obd (Object Builder Data)
                var filePath = Path.Combine(r.OutputFolder, r.FileName + ".obd");
                var obdBytes = ObdCodec.Encode(thing, r.ClientVersion,
                    sprId => _sprFile.GetSpriteRgba(sprId), r.ObdVersion);
                File.WriteAllBytes(filePath, obdBytes);
                StatusText = $"Exported OBD: {Path.GetFileName(filePath)}";
            }
            else
            {
                var ext = r.Format switch { ExportFormat.Bmp => ".bmp", ExportFormat.Jpg => ".jpg", _ => ".png" };
                var filePath = Path.Combine(r.OutputFolder, r.FileName + ext);
                var pixels = ComposeThingBitmapRgba(thing, r.TransparentBackground);
                if (pixels == null) { StatusText = "Nothing to export"; return; }

                var fg = thing.FrameGroups[0];
                int w = fg.Width * 32;
                int h = fg.Height * 32;
                SaveImageToFile(filePath, pixels, w, h, r.Format, r.JpegQuality, r.TransparentBackground);
                StatusText = $"Exported: {Path.GetFileName(filePath)}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Export error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void EditThing()
    {
        OpenClientItemEditor();
    }

    [RelayCommand]
    private void DuplicateThing()
    {
        if (SelectedClientItem == null || _datData == null) return;
        StatusText = "Duplicate: not yet implemented — DAT write support needed";
    }

    [RelayCommand]
    private void NewThing()
    {
        if (_datData == null) return;
        StatusText = "New: not yet implemented — DAT write support needed";
    }

    // ════════════════════════════════════════════════════════════
    //  Copy / Replace client item
    // ════════════════════════════════════════════════════════════

    /// <summary>Copy the currently selected client item to the internal clipboard.</summary>
    public void CopyClientItem()
    {
        if (SelectedClientItem == null || _sprFile == null)
        {
            StatusText = "Select an item to copy.";
            return;
        }

        _copiedClientItem = SelectedClientItem.ThingType.Clone();
        _copiedClientItemSprFile = _sprFile;
        StatusText = $"Copied client item #{SelectedClientItem.Id} to clipboard.";
    }

    public bool HasCopiedClientItem => _copiedClientItem != null;

    /// <summary>Replace the selected client item's data with the clipboard clone.</summary>
    public void ReplaceClientItemFromClipboard()
    {
        if (SelectedClientItem == null || _datData == null || _sprFile == null)
        {
            StatusText = "Select a target item first.";
            return;
        }
        if (_copiedClientItem == null || _copiedClientItemSprFile == null)
        {
            StatusText = "Clipboard is empty — copy an item first.";
            return;
        }

        var target = SelectedClientItem;
        var clone = _copiedClientItem.Clone();

        // Remap sprites from the source SPR into the current SPR
        if (_copiedClientItemSprFile != _sprFile)
            RemapSpritesToTarget(clone, _copiedClientItemSprFile, _sprFile);

        // Keep the target's ID
        clone.Id = target.Id;
        clone.Category = target.Category;

        // Replace in the DAT dictionary
        var dict = GetDatDictForCategory(target.Category);
        dict[target.Id] = clone;

        // Replace the VM entry in the master list
        int idx = _allClientItems.FindIndex(c => c.Id == target.Id && c.Category == target.Category);
        if (idx >= 0)
        {
            var newVm = new ClientItemViewModel(clone) { Sprite = ComposeThingBitmap(clone) };
            _allClientItems[idx] = newVm;
        }

        HasUnsavedChanges = true;
        ApplyClientFilter();
        NavigateToClientItemById(target.Id, target.Category);
        StatusText = $"Replaced client item #{target.Id} with clipboard data.";
    }

    /// <summary>Replace the selected client item from an .obd (Object Builder Data) file.</summary>
    public async Task ReplaceClientItemFromObdAsync()
    {
        if (SelectedClientItem == null || _datData == null || _sprFile == null)
        {
            StatusText = "Select a target item first.";
            return;
        }

        var obdPath = await FileDialogHelper.OpenFileAsync(
            "Open OBD file",
            [("Object Builder Data", "*.obd")]);
        if (obdPath == null) return;

        ObdCodec.ObdData obd;
        try
        {
            var bytes = File.ReadAllBytes(obdPath);
            obd = ObdCodec.Decode(bytes);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load OBD file: {ex.Message}";
            return;
        }

        var target = SelectedClientItem;
        var clone = obd.Thing.Clone();

        // Import sprites from OBD into our SPR file
        foreach (var fg in clone.FrameGroups)
        {
            for (int i = 0; i < fg.SpriteIndex.Length; i++)
            {
                var oldId = fg.SpriteIndex[i];
                if (oldId == 0) continue;
                if (obd.Sprites.TryGetValue(oldId, out var rgba))
                {
                    var newId = _sprFile.AddSprite(rgba);
                    fg.SpriteIndex[i] = newId;
                }
            }
        }

        clone.Id = target.Id;
        clone.Category = target.Category;

        var dict = GetDatDictForCategory(target.Category);
        dict[target.Id] = clone;

        int idx = _allClientItems.FindIndex(c => c.Id == target.Id && c.Category == target.Category);
        if (idx >= 0)
        {
            var newVm = new ClientItemViewModel(clone) { Sprite = ComposeThingBitmap(clone) };
            _allClientItems[idx] = newVm;
        }

        HasUnsavedChanges = true;
        ApplyClientFilter();
        NavigateToClientItemById(target.Id, target.Category);
        StatusText = $"Replaced client item #{target.Id} from OBD file.";
    }

    [RelayCommand]
    private void RemoveThing()
    {
        if (_datData == null) return;

        // Use multi-selection if available, otherwise fall back to single selection
        var toRemove = SelectedClientItemsList.Count > 0
            ? SelectedClientItemsList.ToList()
            : (SelectedClientItem != null ? [SelectedClientItem] : []);

        if (toRemove.Count == 0)
        {
            StatusText = "No items selected for removal.";
            return;
        }

        // Find the index of the earliest selected item to select the one before it after deletion
        int earliestIdx = int.MaxValue;
        foreach (var item in toRemove)
        {
            int idx = _allClientItems.FindIndex(c => c.Id == item.Id && c.Category == item.Category);
            if (idx >= 0 && idx < earliestIdx)
                earliestIdx = idx;
        }

        // Remove all selected items
        int removed = 0;
        foreach (var item in toRemove)
        {
            var thing = item.ThingType;
            var dict = GetDatDictForCategory(thing.Category);
            if (dict.Remove(thing.Id))
            {
                _allClientItems.RemoveAll(c => c.Id == thing.Id && c.Category == thing.Category);
                removed++;
            }
        }

        if (removed == 0)
        {
            StatusText = "No items were removed.";
            return;
        }

        // Select the item just before the earliest deleted one
        int selectIdx = Math.Max(0, earliestIdx - 1);
        var nextSelection = selectIdx < _allClientItems.Count ? _allClientItems[selectIdx] : null;

        SelectedClientItem = null;
        IsClientItemEditing = false;
        ApplyClientFilter();

        // Navigate to the page containing the selection target
        if (nextSelection != null)
        {
            int filteredIdx = _clientFilteredItems.IndexOf(nextSelection);
            if (filteredIdx >= 0)
            {
                int page = filteredIdx / ClientItemsPerPage + 1;
                ClientCurrentPage = Math.Clamp(page, 1, ClientTotalPages);
                LoadClientPage();
                SelectedClientItem = nextSelection;
            }
        }

        HasUnsavedChanges = true;
        var label = removed == 1 ? $"Removed 1 item" : $"Removed {removed} items";
        StatusText = label;
        AddMapLog(label);
    }

    // ══════════════════════════════════════════════════════════════════════
    // ── Sprite CRUD Commands ──
    // ══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task ReplaceSpriteAsync()
    {
        if (SelectedRightSprite == null || _sprFile == null) return;

        var path = await FileDialogHelper.OpenFileAsync("Replace Sprite (32×32 PNG)", [("PNG Image", "*.png"), ("All Files", "*")]);
        if (path == null) return;

        try
        {
            var rgba = LoadPngAsRgba32(path);
            if (rgba == null) { StatusText = "Invalid image — must be 32×32 PNG"; return; }

            _sprFile.SetSpriteRgba(SelectedRightSprite.SpriteId, rgba);
            SelectedRightSprite.Bitmap = LoadSpriteBitmap(SelectedRightSprite.SpriteId);
            InvalidateSpriteCache();
            Palette?.RefreshSprites();
            LoadAllSprites();
            StatusText = $"Replaced sprite {SelectedRightSprite.SpriteId}";
        }
        catch (Exception ex) { StatusText = $"Replace sprite error: {ex.Message}"; }
    }

    [RelayCommand]
    private void PasteSprite()
    {
        if (_copiedSpriteRgba == null || _sprFile == null || SelectedRightSprite == null) return;

        _sprFile.SetSpriteRgba(SelectedRightSprite.SpriteId, (byte[])_copiedSpriteRgba.Clone());
        SelectedRightSprite.Bitmap = LoadSpriteBitmap(SelectedRightSprite.SpriteId);
        RefreshAfterSpriteEdit(SelectedRightSprite.SpriteId);
        StatusText = $"Pasted sprite → {SelectedRightSprite.SpriteId}";
    }

    [RelayCommand]
    private async Task ImportSpriteAsync()
    {
        if (_sprFile == null) return;

        var path = await FileDialogHelper.OpenFileAsync("Import Sprite (32×32 PNG)", [("PNG Image", "*.png"), ("All Files", "*")]);
        if (path == null) return;

        try
        {
            var rgba = LoadPngAsRgba32(path);
            if (rgba == null) { StatusText = "Invalid image — must be 32×32 PNG"; return; }

            var newId = _sprFile.AddSprite(rgba);
            RefreshSpritePanel();
            StatusText = $"Imported sprite as ID {newId}";
        }
        catch (Exception ex) { StatusText = $"Import sprite error: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task ExportSpriteAsync()
    {
        if (SelectedRightSprite == null || _sprFile == null) return;

        var spriteId = SelectedRightSprite.SpriteId;
        var dialog = new ExportDialog
        {
            SuggestedFileName = $"sprite_{spriteId}",
            SpriteOnly = true,
        };

        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            await dialog.ShowDialog(desktop.MainWindow);
        }
        else
        {
            dialog.Show();
            return;
        }

        if (dialog.Result == null) return;
        var r = dialog.Result;

        try
        {
            var ext = r.Format switch { ExportFormat.Bmp => ".bmp", ExportFormat.Jpg => ".jpg", _ => ".png" };
            var filePath = Path.Combine(r.OutputFolder, r.FileName + ext);
            var rgba = _sprFile.GetSpriteRgba(spriteId);
            if (rgba == null) { StatusText = "Nothing to export"; return; }

            if (!r.TransparentBackground)
                ApplyMagentaBackground(rgba, 32, 32);

            SaveImageToFile(filePath, rgba, 32, 32, r.Format, r.JpegQuality, r.TransparentBackground);
            StatusText = $"Exported sprite {spriteId}: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export sprite error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DuplicateSprite()
    {
        if (SelectedRightSprite == null || _sprFile == null) return;

        var rgba = _sprFile.GetSpriteRgba(SelectedRightSprite.SpriteId);
        var newId = _sprFile.AddSprite(rgba);
        RefreshSpritePanel();
        StatusText = $"Duplicated sprite {SelectedRightSprite.SpriteId} → new ID {newId}";
    }

    [RelayCommand]
    private async Task CopySpriteToClipboardAsync()
    {
        if (SelectedRightSprite == null || _sprFile == null) return;

        var rgba = _sprFile.GetSpriteRgba(SelectedRightSprite.SpriteId);
        if (rgba == null) { StatusText = "Sprite has no data"; return; }

        _copiedSpriteRgba = (byte[])rgba.Clone();
        StatusText = $"Sprite {SelectedRightSprite.SpriteId} copied";

        // Best-effort: also copy to system clipboard as image
        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(32, 32),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul);

            using (var fb = bitmap.Lock())
                Marshal.Copy(rgba, 0, fb.Address, rgba.Length);

            var clipboard = (Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow?.Clipboard;

            if (clipboard != null)
                await Avalonia.Input.Platform.ClipboardExtensions.SetBitmapAsync(clipboard, bitmap);
        }
        catch { /* system clipboard is optional */ }
    }

    [RelayCommand]
    private void CopyCompositionSprite(SpriteViewModel? source)
    {
        if (source == null || source.SpriteId == 0 || _sprFile == null) return;
        var rgba = _sprFile.GetSpriteRgba(source.SpriteId);
        if (rgba == null) { StatusText = "Sprite has no data"; return; }
        _copiedSpriteRgba = (byte[])rgba.Clone();
        StatusText = $"Copied sprite {source.SpriteId} from composition";
    }

    [RelayCommand]
    private void PasteSpriteToSlot(SpriteViewModel? target)
    {
        if (_copiedSpriteRgba == null || _sprFile == null) return;

        if (target == null || target.SlotIndex < 0)
        {
            StatusText = "Select a composition slot to paste into";
            return;
        }

        // Write the copied pixels into the sprite at this slot
        if (target.SpriteId == 0)
        {
            // Slot has no sprite — create a new one
            var newId = _sprFile.AddSprite((byte[])_copiedSpriteRgba.Clone());
            AssignSpriteToSlot(target, newId);
        }
        else
        {
            _sprFile.SetSpriteRgba(target.SpriteId, (byte[])_copiedSpriteRgba.Clone());
            target.Bitmap = LoadSpriteBitmap(target.SpriteId);
        }
        RefreshAfterSpriteEdit(target.SpriteId);
        StatusText = $"Pasted sprite → slot {target.SlotIndex}";
    }

    [RelayCommand]
    private void NewSprite()
    {
        if (_sprFile == null) return;
        var newId = _sprFile.AddSprite(null);
        RefreshSpritePanel();
        StatusText = $"New blank sprite ID {newId}";
    }

    [RelayCommand]
    private void RemoveSprite()
    {
        if (SelectedRightSprite == null || _sprFile == null) return;

        var id = SelectedRightSprite.SpriteId;
        _sprFile.RemoveSprite(id);
        InvalidateSpriteCache();
        Palette?.RefreshSprites();
        LoadAllSprites();
        RefreshSpritePanel();
        StatusText = id == _sprFile.SpriteCount + 1
            ? $"Removed sprite {id} (last — count decremented)"
            : $"Blanked sprite {id} (not last)";
    }

    [RelayCommand]
    private async Task SaveSprAsync()
    {
        if (_sprFile == null) return;
        var path = await FileDialogHelper.SaveFileAsync("Save SPR file", [("SPR Files", "*.spr")]);
        if (path == null) return;

        try
        {
            _sprFile.Save(path);
            StatusText = $"Saved SPR: {Path.GetFileName(path)} ({_sprFile.SpriteCount} sprites)";
        }
        catch (Exception ex) { StatusText = $"Save SPR error: {ex.Message}"; }
    }

    /// <summary>Load a 32×32 PNG and return its raw RGBA bytes, or null if invalid.</summary>
    private static byte[]? LoadPngAsRgba32(string path)
    {
        using var skBmp = SkiaSharp.SKBitmap.Decode(path);
        if (skBmp == null || skBmp.Width != 32 || skBmp.Height != 32)
            return null;

        // Ensure Rgba8888 format
        SkiaSharp.SKBitmap? temp = null;
        try
        {
            var source = skBmp;
            if (skBmp.ColorType != SkiaSharp.SKColorType.Rgba8888)
            {
                temp = skBmp.Copy(SkiaSharp.SKColorType.Rgba8888);
                if (temp == null) return null;
                source = temp;
            }

            var src = source.GetPixelSpan();
            var rgba = new byte[32 * 32 * 4];
            src[..(32 * 32 * 4)].CopyTo(rgba);
            return rgba;
        }
        finally { temp?.Dispose(); }
    }

    /// <summary>Refresh the sprite panel after adding/removing sprites.</summary>
    private void RefreshSpritePanel()
    {
        if (_sprFile == null) return;
        RightSpriteTotalPages = Math.Max(1, ((int)_sprFile.SpriteCount + RightSpritesPerPage - 1) / RightSpritesPerPage);
        if (RightSpriteCurrentPage > RightSpriteTotalPages)
            RightSpriteCurrentPage = RightSpriteTotalPages;
        LoadRightSpritePage();
    }

    /// <summary>Invalidate palette sprite cache and reload compositions after sprite mutation.</summary>
    private void InvalidateSpriteCache()
    {
        Palette?.ClearSpriteCache();

        // Refresh client item thumbnails that may use the modified sprite
        if (_sprFile != null)
        {
            foreach (var vm in _allClientItems)
                vm.Sprite = ComposeThingBitmap(vm.ThingType);
        }

        // Reload composition grid if a thing is being edited
        ReloadComposition();

        // Clear map canvas sprite caches so it re-fetches fresh data
        _mapSpriteCacheInvalidated?.Invoke();

        // Reload the OTB items preview if something is selected
        if (SelectedItem != null)
            OnPropertyChanged(nameof(SelectedItem));
    }

    /// <summary>Lightweight refresh after editing a single sprite — only updates items that reference it.</summary>
    private void RefreshAfterSpriteEdit(uint spriteId)
    {
        Palette?.ClearSpriteCache();

        // Only recompose client items whose frame groups reference this sprite
        if (_sprFile != null)
        {
            foreach (var vm in _allClientItems)
            {
                foreach (var fg in vm.ThingType.FrameGroups)
                {
                    if (Array.IndexOf(fg.SpriteIndex, spriteId) >= 0)
                    {
                        vm.Sprite = ComposeThingBitmap(vm.ThingType);
                        break;
                    }
                }
            }
        }

        ReloadComposition();
        _mapSpriteCacheInvalidated?.Invoke();

        if (SelectedItem != null)
            OnPropertyChanged(nameof(SelectedItem));
    }

    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Load a single sprite to a WriteableBitmap.</summary>
    internal WriteableBitmap? LoadSpriteBitmap(uint spriteId)
    {
        if (_sprFile == null || spriteId == 0) return null;

        try
        {
            var rgba = _sprFile.GetSpriteRgba(spriteId);
            if (rgba == null) return null;

            var bitmap = new WriteableBitmap(
                new PixelSize(32, 32),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul);

            using (var fb = bitmap.Lock())
            {
                Marshal.Copy(rgba, 0, fb.Address, rgba.Length);
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Compose all W×H tiles of a ThingType into a single bitmap (like Object Builder).
    /// Uses frame 0, all layers, outfits show south-facing (patternX=2 if available).
    /// </summary>
    internal WriteableBitmap? ComposeThingBitmap(DatThingType thing, int frame = 0)
    {
        if (_sprFile == null || thing.FrameGroups.Length == 0) return null;

        var fg = thing.FrameGroups[0];
        int w = fg.Width;
        int h = fg.Height;
        if (w == 0 || h == 0) return null;

        int clampedFrame = Math.Clamp(frame, 0, Math.Max(0, fg.Frames - 1));

        // Single 1×1 item — use simple path
        if (w == 1 && h == 1 && fg.Layers == 1)
        {
            int px = 0;
            // Outfits: show south-facing (patternX index 2 if available)
            if (thing.Category == ThingCategory.Outfit && fg.PatternX > 2)
                px = 2;
            uint sprId = fg.GetSpriteId(0, 0, 0, px, 0, 0, clampedFrame);
            return LoadSpriteBitmap(sprId);
        }

        int bmpW = w * 32;
        int bmpH = h * 32;
        var pixels = new byte[bmpW * bmpH * 4];

        int layers = fg.Layers;
        int patX = 0;
        if (thing.Category == ThingCategory.Outfit)
        {
            layers = 1; // Only base layer for outfits in list view
            if (fg.PatternX > 2) patX = 2; // South-facing
        }

        for (int l = 0; l < layers; l++)
        {
            for (int tw = 0; tw < w; tw++)
            {
                for (int th = 0; th < h; th++)
                {
                    uint sprId = fg.GetSpriteId(tw, th, l, patX, 0, 0, clampedFrame);
                    var rgba = _sprFile.GetSpriteRgba(sprId);
                    if (rgba == null) continue;

                    // Place with inverted coords (Object Builder style)
                    int destX = (w - 1 - tw) * 32;
                    int destY = (h - 1 - th) * 32;

                    for (int y = 0; y < 32; y++)
                    {
                        for (int x = 0; x < 32; x++)
                        {
                            int srcIdx = (y * 32 + x) * 4;
                            byte a = rgba[srcIdx + 3];
                            if (a == 0) continue; // Skip transparent

                            int dstIdx = ((destY + y) * bmpW + destX + x) * 4;
                            pixels[dstIdx] = rgba[srcIdx];
                            pixels[dstIdx + 1] = rgba[srcIdx + 1];
                            pixels[dstIdx + 2] = rgba[srcIdx + 2];
                            pixels[dstIdx + 3] = a;
                        }
                    }
                }
            }
        }

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(bmpW, bmpH),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul);

            using (var fb = bitmap.Lock())
                Marshal.Copy(pixels, 0, fb.Address, pixels.Length);

            return bitmap;
        }
        catch { return null; }
    }

    /// <summary>
    /// Static version of ComposeThingBitmap that takes an explicit SprFile.
    /// Used for composing sprites in a target session context (e.g. after transplant).
    /// </summary>
    internal static WriteableBitmap? ComposeThingBitmapStatic(DatThingType thing, SprFile sprFile, int frame = 0, int frameGroupIndex = 0)
    {
        if (thing.FrameGroups.Length == 0) return null;

        int fgIdx = Math.Clamp(frameGroupIndex, 0, thing.FrameGroups.Length - 1);
        var fg = thing.FrameGroups[fgIdx];
        int w = fg.Width;
        int h = fg.Height;
        if (w == 0 || h == 0) return null;

        int clampedFrame = Math.Clamp(frame, 0, Math.Max(0, fg.Frames - 1));

        // Single 1×1 item
        if (w == 1 && h == 1 && fg.Layers == 1)
        {
            int px = 0;
            if (thing.Category == ThingCategory.Outfit && fg.PatternX > 2)
                px = 2;
            uint sprId = fg.GetSpriteId(0, 0, 0, px, 0, 0, clampedFrame);
            var rgba = sprFile.GetSpriteRgba(sprId);
            if (rgba == null) return null;
            try
            {
                var bmp = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
                using (var fb = bmp.Lock())
                    Marshal.Copy(rgba, 0, fb.Address, rgba.Length);
                return bmp;
            }
            catch { return null; }
        }

        int bmpW = w * 32;
        int bmpH = h * 32;
        var pixels = new byte[bmpW * bmpH * 4];

        int layers = fg.Layers;
        int patX = 0;
        if (thing.Category == ThingCategory.Outfit)
        {
            layers = 1;
            if (fg.PatternX > 2) patX = 2;
        }

        for (int l = 0; l < layers; l++)
        {
            for (int tw = 0; tw < w; tw++)
            {
                for (int th = 0; th < h; th++)
                {
                    uint sprId = fg.GetSpriteId(tw, th, l, patX, 0, 0, clampedFrame);
                    var rgba = sprFile.GetSpriteRgba(sprId);
                    if (rgba == null) continue;

                    int destX = (w - 1 - tw) * 32;
                    int destY = (h - 1 - th) * 32;

                    for (int y = 0; y < 32; y++)
                    {
                        for (int x = 0; x < 32; x++)
                        {
                            int srcIdx = (y * 32 + x) * 4;
                            byte a = rgba[srcIdx + 3];
                            if (a == 0) continue;
                            int dstIdx = ((destY + y) * bmpW + destX + x) * 4;
                            pixels[dstIdx] = rgba[srcIdx];
                            pixels[dstIdx + 1] = rgba[srcIdx + 1];
                            pixels[dstIdx + 2] = rgba[srcIdx + 2];
                            pixels[dstIdx + 3] = a;
                        }
                    }
                }
            }
        }

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(bmpW, bmpH), new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
            using (var fb = bitmap.Lock())
                Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
            return bitmap;
        }
        catch { return null; }
    }

    /// <summary>Save a WriteableBitmap as PNG using Avalonia's rendering.</summary>
    private static void SaveWriteableBitmapAsPng(WriteableBitmap wb, Stream stream)
    {
        var renderTarget = new Avalonia.Media.Imaging.RenderTargetBitmap(wb.PixelSize);
        using (var ctx = renderTarget.CreateDrawingContext())
        {
            ctx.DrawImage(wb, new Rect(0, 0, wb.PixelSize.Width, wb.PixelSize.Height));
        }
        renderTarget.Save(stream);
    }

    /// <summary>Compose all W×H tiles as raw RGBA, optionally with magenta background for non-transparent.</summary>
    private byte[]? ComposeThingBitmapRgba(DatThingType thing, bool transparent)
    {
        if (_sprFile == null || thing.FrameGroups.Length == 0) return null;

        var fg = thing.FrameGroups[0];
        int w = fg.Width;
        int h = fg.Height;
        if (w == 0 || h == 0) return null;

        int bmpW = w * 32;
        int bmpH = h * 32;
        var pixels = new byte[bmpW * bmpH * 4];

        // Fill background
        if (!transparent)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0xFF;     // R = 255 (magenta)
                pixels[i + 1] = 0x00; // G = 0
                pixels[i + 2] = 0xFF; // B = 255
                pixels[i + 3] = 0xFF; // A = 255
            }
        }

        int layers = fg.Layers;
        int patX = 0;
        if (thing.Category == ThingCategory.Outfit)
        {
            layers = 1;
            if (fg.PatternX > 2) patX = 2;
        }

        for (int l = 0; l < layers; l++)
        {
            for (int tw = 0; tw < w; tw++)
            {
                for (int th = 0; th < h; th++)
                {
                    uint sprId = fg.GetSpriteId(tw, th, l, patX, 0, 0, 0);
                    var rgba = _sprFile.GetSpriteRgba(sprId);
                    if (rgba == null) continue;

                    int destX = (w - 1 - tw) * 32;
                    int destY = (h - 1 - th) * 32;

                    for (int y = 0; y < 32; y++)
                    {
                        for (int x = 0; x < 32; x++)
                        {
                            int srcIdx = (y * 32 + x) * 4;
                            byte a = rgba[srcIdx + 3];
                            if (a == 0) continue;

                            int dstIdx = ((destY + y) * bmpW + destX + x) * 4;
                            pixels[dstIdx] = rgba[srcIdx];
                            pixels[dstIdx + 1] = rgba[srcIdx + 1];
                            pixels[dstIdx + 2] = rgba[srcIdx + 2];
                            pixels[dstIdx + 3] = a;
                        }
                    }
                }
            }
        }

        return pixels;
    }

    /// <summary>Replace transparent pixels with magenta background.</summary>
    private static void ApplyMagentaBackground(byte[] rgba, int w, int h)
    {
        for (int i = 0; i < w * h * 4; i += 4)
        {
            if (rgba[i + 3] == 0)
            {
                rgba[i] = 0xFF;
                rgba[i + 1] = 0x00;
                rgba[i + 2] = 0xFF;
                rgba[i + 3] = 0xFF;
            }
        }
    }

    /// <summary>Save raw RGBA pixel data to an image file in the specified format.</summary>
    private static void SaveImageToFile(string path, byte[] rgba, int width, int height,
                                         ExportFormat format, int jpegQuality, bool hasAlpha)
    {
        using var skBmp = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
        var ptr = skBmp.GetPixels();
        Marshal.Copy(rgba, 0, ptr, rgba.Length);

        var encFormat = format switch
        {
            ExportFormat.Bmp => SkiaSharp.SKEncodedImageFormat.Bmp,
            ExportFormat.Jpg => SkiaSharp.SKEncodedImageFormat.Jpeg,
            _ => SkiaSharp.SKEncodedImageFormat.Png,
        };

        int quality = format == ExportFormat.Jpg ? jpegQuality : 100;

        using var image = SkiaSharp.SKImage.FromBitmap(skBmp);
        using var data = image.Encode(encFormat, quality);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}

/// <summary>Display model for a single item in the tile inspector.</summary>
public sealed class TileItemInfo
{
    public int Index { get; set; }
    public ushort ServerId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Name { get; set; }
    public List<string> Details { get; } = [];

    public string DisplayName => Name != null ? $"{Label}: {Name} (id:{ServerId})" : $"{Label}: id:{ServerId}";
    public string DetailsText => Details.Count > 0 ? string.Join(" · ", Details) : string.Empty;
    public bool HasDetails => Details.Count > 0;
}

/// <summary>A named section in the inspector detail view (e.g. "OTB · Identification").</summary>
public sealed class InspectorSection
{
    public string Title { get; set; } = string.Empty;
    public List<InspectorProp> Props { get; } = [];
    public List<string> Flags { get; } = [];
    public bool HasFlags => Flags.Count > 0;
    public bool HasProps => Props.Count > 0;
}

/// <summary>A single property row (key → value).</summary>
public sealed record InspectorProp(string Key, string Value);

/// <summary>Preview entry for the Unmapped Client Items dialog.</summary>
public sealed class UnmappedItemEntry
{
    public required DatThingType DatThing { get; init; }
    public bool Include { get; set; }
    public ushort PreviewServerId { get; set; }
    public OtbGroup PreviewGroup { get; set; }
    public OtbFlags PreviewFlags { get; set; }
}
