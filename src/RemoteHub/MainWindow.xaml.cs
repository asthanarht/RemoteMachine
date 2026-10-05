using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Shell;
using RemoteHub.Focus;
using RemoteHub.Models;
using RemoteHub.Native;
using RemoteHub.Services;
using RemoteHub.Views;

namespace RemoteHub;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly WorkspaceStore _store;
    private readonly WorkspaceData _workspace;
    private readonly Dictionary<Guid, SessionView> _sessions = [];
    private readonly DispatcherTimer _foregroundTimer;
    private FocusHotkeys? _hotkeys;
    private WindowWorkArea? _workArea;
    private EdgeHandleWindow? _handle;
    private QuickControlsWindow? _quick;
    private FocusPanelWindow? _panel;
    private SessionView? _activeSession;
    private bool _invisible;
    private bool _closing;
    private bool _hotkeyConflictReported;
    private Rect _restoreBounds;
    private WindowState _restoreState;
    private WindowChrome? _restoreChrome;
    private Size _restoreMinimum;
    private readonly bool _noPersistence;
    public bool IsDesignPreview { get; }
    public bool IsInFocus { get; private set; }
    public ObservableCollection<ConnectionProfile> Profiles { get; }
    public ObservableCollection<ConnectionProfile> WorkSessions { get; } = [];
    public ObservableCollection<ConnectionProfile> HomeSessions { get; } = [];
    public ObservableCollection<ConnectionProfile> CloudSessions { get; } = [];
    public IEnumerable<ConnectionProfile> FavoriteConnections => Profiles.Where(p => p.Favorite).Take(3);
    public IEnumerable<ConnectionProfile> RecentConnections => Profiles.Where(p => p.LastUsed.HasValue || p.IsPreview).OrderByDescending(p => p.LastUsed).Take(4);
    public int ConnectionCount => Profiles.Count;
    public int FavoriteCount => Profiles.Count(p => p.Favorite);
    public int WorkCount => Profiles.Count(p => p.Group == "Work");
    public int HomeCount => Profiles.Count(p => p.Group == "Home");
    public int CloudCount => Profiles.Count(p => p.Group == "Cloud");
    public WindowsAccount CurrentWindowsAccount { get; }
    public string DisplayName => IsDesignPreview ? "Alex Morgan" : CurrentWindowsAccount.UserName;
    public string UserInitials => IsDesignPreview ? "AM" : CurrentWindowsAccount.Initials;
    public string WorkspaceInitial => new System.Globalization.StringInfo(UserInitials).SubstringByTextElements(0, 1);
    public string WorkspaceName => (IsDesignPreview ? "Alex" : CurrentWindowsAccount.UserName) + "'s workspace";
    public string WorkspaceLabel => IsDesignPreview ? "Design preview" : "Local workspace";
    public string WindowsAccountLabel => IsDesignPreview ? "Illustrative account" : "Signed in to Windows";
    public string WindowsAccountTooltip => IsDesignPreview ? "Fictional account for this design preview." :
        $"Windows account: {CurrentWindowsAccount.QualifiedName}\nThis is your local Windows sign-in, not an Azure or remote-session sign-in. Passwords and PINs are never read or displayed.";
    public string Greeting => (DateTime.Now.Hour < 12 ? "Good morning, " : DateTime.Now.Hour < 18 ? "Good afternoon, " : "Good evening, ") + (IsDesignPreview ? "Alex" : CurrentWindowsAccount.UserName) + ".";
    public bool HasActiveSessions => _sessions.Values.Any(s => s.IsLive);
    public string ActiveSessionSummary
    {
        get
        {
            int count = _sessions.Values.Count(s => s.IsLive);
            return count > 0 ? $"{count} active {(count == 1 ? "session" : "sessions")}" :
                _activeSession?.Profile.IsPreview == true ? "Design preview open" : "No active sessions";
        }
    }
    private SessionView? SidebarSession => _activeSession?.IsLive == true ? _activeSession :
        _sessions.Values.FirstOrDefault(s => s.IsLive) ?? (_activeSession?.Profile.IsPreview == true ? _activeSession : null);
    public string ActiveSessionName => SidebarSession?.Profile.Name ?? "Connect when you're ready";
    public event PropertyChangedEventHandler? PropertyChanged;
    public SessionView? ActiveSession => _activeSession;

    public MainWindow(bool designPreview = false, bool noPersistence = false)
    {
        IsDesignPreview = designPreview; _noPersistence = noPersistence;
        CurrentWindowsAccount = designPreview ? new("alex.morgan", "CONTOSO") : WindowsAccount.ReadCurrent();
        _store = new();
        _workspace = designPreview || noPersistence ? new() : _store.Load();
        Profiles = new(designPreview ? WorkspaceStore.PreviewConnections() : _workspace.Connections);
        Profiles.CollectionChanged += (_, _) => NotifyData();
        InitializeComponent(); DataContext = this;
        AccountPopup.CustomPopupPlacementCallback = static (popupSize, targetSize, _) =>
            [new(new Point(targetSize.Width - popupSize.Width, targetSize.Height + 6), PopupPrimaryAxis.Horizontal)];
        _foregroundTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _foregroundTimer.Tick += (_, _) => TrackForeground();
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        StateChanged += (_, _) =>
        {
            if (IsInFocus)
            {
                if (WindowState == WindowState.Minimized) { HideFocusOverlays(); _hotkeys?.Unregister(); }
                else Dispatcher.BeginInvoke(new Action(RefreshFocusOverlays));
            }
            else WindowBorder.CornerRadius = new(WindowState == WindowState.Maximized ? 0 : 10);
        };
        Activated += (_, _) => { if (IsInFocus) Dispatcher.BeginInvoke(new Action(RefreshFocusOverlays)); };
        Deactivated += (_, _) => { if (IsInFocus) Dispatcher.BeginInvoke(new Action(TrackForeground)); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control && !IsInFocus)
            {
                e.Handled = true; ShowConnections(); ((ConnectionsView)PageContent.Content).FocusSearch();
            }
        };
        ShowHome();
    }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        App.Theme.Attach(this);
        _workArea = new WindowWorkArea(this, () => IsInFocus);
        var hwnd = new WindowInteropHelper(this).Handle;
        var workArea = NativeMethods.MonitorWorkArea(hwnd);
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96d;
        MinWidth = Math.Min(MinWidth, workArea.Width / scale);
        MinHeight = Math.Min(MinHeight, workArea.Height / scale);
        Width = Math.Min(Width, workArea.Width / scale);
        Height = Math.Min(Height, workArea.Height / scale);
        _hotkeys = new(this);
        _hotkeys.ControlsRequested += (_, _) => { if (IsInFocus && IsOurForeground()) ShowFocusControls(true); };
        _hotkeys.ExitRequested += (_, _) => { if (IsInFocus && IsOurForeground()) ExitFocus(); };
        _hotkeys.DisplayChanged += (_, _) =>
        {
            if (!IsInFocus) return;
            Dispatcher.BeginInvoke(new Action(() => { PositionFullscreen(); RefreshFocusOverlays(); }));
        };
        _handle = new(this); _handle.Invoked += (_, _) => { Activate(); RefreshFocusOverlays(); ShowFocusControls(); };
        _handle.Docked += (edge, position) => { _workspace.DockEdge = edge; _workspace.DockPosition = position; SaveWorkspace(); RefreshFocusOverlays(); };
        _quick = new(this); _quick.ActionRequested += FocusAction;
        _panel = new(this); _panel.ActionRequested += FocusAction;
        _panel.ScrollSpeedRequested += speed => _activeSession?.SetVncScrollSpeed(speed);
        _panel.EdgeRequested += edge => { _workspace.DockEdge = edge; SaveWorkspace(); RefreshFocusOverlays(); PositionOverlay(_panel, false); };
        _panel.InvisibleRequested += invisible =>
        {
            if (invisible && _hotkeys?.Available != true) { _invisible = false; RefreshFocusOverlays(); return; }
            bool previous = _workspace.HideFocusHandle;
            _workspace.HideFocusHandle = invisible;
            if (!SaveWorkspace()) _workspace.HideFocusHandle = previous;
            else _invisible = invisible;
            RefreshFocusOverlays();
        };
    }
    private void NotifyData()
    {
        UpdateLiveSessionGroups();
        foreach (string property in new[] { nameof(FavoriteConnections), nameof(RecentConnections), nameof(ConnectionCount), nameof(FavoriteCount),
            nameof(WorkCount), nameof(HomeCount), nameof(CloudCount), nameof(ActiveSessionSummary), nameof(ActiveSessionName), nameof(HasActiveSessions) })
            PropertyChanged?.Invoke(this, new(property));
    }
    private void UpdateLiveSessionGroups()
    {
        foreach (var (group, items) in new[] { ("Work", WorkSessions), ("Home", HomeSessions), ("Cloud", CloudSessions) })
        {
            var live = _sessions.Values.Where(session => session.IsLive && session.Profile.Group == group)
                .Select(session => session.Profile).OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ThenBy(profile => profile.Id).ToArray();
            foreach (var profile in items.Where(profile => !live.Contains(profile)).ToArray()) items.Remove(profile);
            for (int index = 0; index < live.Length; index++)
            {
                int existing = items.IndexOf(live[index]);
                if (existing < 0) items.Insert(index, live[index]);
                else if (existing != index) items.Move(existing, index);
            }
        }
    }
    public void ShowHome() { ShowPage(new HomeView(this)); SelectNav(HomeNav); }
    public void ShowSettings() { ShowPage(new SettingsView()); SelectNav(SettingsNav); }
    public void ShowConnections(string filter = "All", string query = "") { ShowPage(new ConnectionsView(this, filter, query)); SelectConnectionFilter(filter); }
    public void SelectConnectionFilter(string filter) => SelectNav(filter switch
    {
        "Favorites" => FavoritesNav, "Work" => WorkGroupNav, "Home" => HomeGroupNav, "Cloud" => CloudGroupNav, _ => ConnectionsNav
    });
    public void ShowAdd(ConnectionProfile? profile = null)
    {
        if (IsDesignPreview) { ShowMessage("Design preview", "This window uses fictional connections. Launch the app normally to save your own real connections."); return; }
        if (profile is not null && _sessions.TryGetValue(profile.Id, out var session) && session.IsLive)
        { ShowMessage("Connection is active", "Disconnect this session before editing its settings."); return; }
        ShowPage(new AddConnectionView(this, profile)); SelectNav(null);
    }
    public void ImportRdp()
    {
        if (IsDesignPreview) { ShowMessage("Design preview", "Open the app normally to import real connections."); return; }
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import a standard PC connection", Filter = "Remote Desktop connections (*.rdp)|*.rdp", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try { ShowPage(new AddConnectionView(this, import: RdpFile.Read(dialog.FileName))); SelectNav(null); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { AppLog.Write("rdp-import-rejected", details: new { type = error.GetType().Name }); ShowMessage("Could not import this RDP file", error.Message); }
    }
    public void ExportRdp(ConnectionProfile profile)
    {
        if (profile.Kind != ConnectionKind.Rdp || profile.IsPreview) { ShowMessage("RDP export", "Only real RDP connections can be exported."); return; }
        string name = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export RDP connection", Filter = "Remote Desktop connections (*.rdp)|*.rdp",
            DefaultExt = ".rdp", AddExtension = true, FileName = name + ".rdp", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try { RdpFile.Write(dialog.FileName, profile); SetStatus("RDP file exported. Passwords and keys are not included."); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { AppLog.Write("rdp-export-failed", profile.Id, new { type = error.GetType().Name }); ShowMessage("Could not export the RDP file", error.Message); }
    }
    private void ShowPage(UserControl page)
    {
        AccountPopup.IsOpen = false;
        ExitFocus(); SessionsContainer.Visibility = Visibility.Collapsed;
        PageContent.Visibility = Visibility.Visible; PageContent.Content = page;
        UpdateSessionChrome();
    }
    private void SelectNav(Control? selected)
    {
        foreach (var button in new Control[] { HomeNav, ConnectionsNav, FavoritesNav, WorkGroupNav, HomeGroupNav, CloudGroupNav, SettingsNav })
        {
            button.SetResourceReference(BackgroundProperty, ReferenceEquals(button, selected) ? "AccentSoft" : "Transparent");
            button.SetResourceReference(ForegroundProperty, ReferenceEquals(button, selected) ? "Accent" : "Muted");
            button.BorderThickness = new Thickness(1);
            button.SetResourceReference(BorderBrushProperty, ReferenceEquals(button, selected) ? "SelectionIndicator" : "Transparent");
            button.FontWeight = ReferenceEquals(button, selected) ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
    public void OpenConnection(ConnectionProfile profile) => OpenConnection(profile, null);
    internal void OpenConnection(ConnectionProfile profile, Func<SshTerminalView, Task>? terminalStarter, Func<VncDesktopView, Task>? vncStarter = null)
    {
        AccountPopup.IsOpen = false;
        ExitFocus();
        if (!_sessions.TryGetValue(profile.Id, out var session))
        {
            session = new SessionView(this, profile, terminalStarter, vncStarter);
            session.StateChanged += (_, _) =>
            {
                NotifyData(); UpdateSessionChrome();
                if (ReferenceEquals(session, _activeSession) && session.Profile.State == SessionState.Connecting) ExpandSessionGroup(session.Profile);
                if (PageContent.Content is ConnectionsView library) library.Refresh();
            };
            _sessions.Add(profile.Id, session); SessionsContainer.Children.Add(session);
        }
        foreach (var item in _sessions.Values) item.Visibility = item == session ? Visibility.Visible : Visibility.Collapsed;
        _activeSession = session;
        PageContent.Visibility = Visibility.Collapsed; SessionsContainer.Visibility = Visibility.Visible;
        if (session.IsLive) ExpandSessionGroup(profile);
        SelectNav(null); NotifyData(); UpdateSessionChrome(); session.FocusRemote();
    }
    private void ExpandSessionGroup(ConnectionProfile profile)
    {
        var group = profile.Group switch { "Work" => WorkGroupNav, "Home" => HomeGroupNav, "Cloud" => CloudGroupNav, _ => null };
        if (group is not null) group.IsExpanded = true;
    }
    public bool SaveConnection(ConnectionProfile profile, ConnectionProfile? original)
    {
        if (original is not null)
        {
            int index = Profiles.IndexOf(original); Profiles[index] = profile;
            if (!SaveWorkspace()) { Profiles[index] = original; return false; }
            if (_sessions.Remove(original.Id, out var old)) { SessionsContainer.Children.Remove(old); old.Dispose(); if (_activeSession == old) _activeSession = null; }
        }
        else
        {
            Profiles.Add(profile);
            if (!SaveWorkspace()) { Profiles.Remove(profile); return false; }
        }
        NotifyData(); SetStatus("Connection saved. No connection has been made."); return true;
    }
    public void ToggleFavorite(ConnectionProfile profile)
    {
        profile.Favorite = !profile.Favorite;
        if (!SaveWorkspace()) profile.Favorite = !profile.Favorite;
        NotifyData();
    }
    public void DeleteConnection(ConnectionProfile profile)
    {
        if (IsDesignPreview) { ShowMessage("Design preview", "Sample connections cannot be deleted."); return; }
        if (_sessions.TryGetValue(profile.Id, out var session) && session.IsLive) { ShowMessage("Connection is active", "Disconnect the session before removing its saved connection."); return; }
        if (MessageBox.Show(this, $"Remove \"{profile.Name}\" from this workspace? This does not change the remote computer.", "Remove connection", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int index = Profiles.IndexOf(profile); Profiles.Remove(profile);
        if (!SaveWorkspace()) { Profiles.Insert(index, profile); return; }
        if (_sessions.Remove(profile.Id, out session)) { SessionsContainer.Children.Remove(session); session.Dispose(); if (_activeSession == session) _activeSession = null; }
        NotifyData();
    }
    public bool SaveWorkspace()
    {
        if (IsDesignPreview || _noPersistence) return true;
        _workspace.Connections = Profiles.ToList();
        try { _store.Save(_workspace); return true; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { AppLog.Write("workspace-save-failed", details: new { error.Message }); ShowMessage("Could not save your workspace", error.Message + "\nYour existing workspace has not been replaced."); return false; }
    }
    public void SetStatus(string text) => StatusText.Text = text;
    public void ShowMessage(string title, string message) => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void SetFitToWindow(SessionView session, bool enabled)
    {
        if (enabled && !session.IsReady)
        { SetStatus("Wait for the terminal to open or complete desktop sign-in before using Fit to window."); return; }
        if (_activeSession != session) OpenConnection(session.Profile);
        session.SetFitToWindow(enabled);
        UpdateSessionChrome();
        session.FocusRemote();
        SetStatus(enabled ? "Fit to window: use Exit fit view in the title bar to restore session controls." : "Session layout restored. The connection is still open.");
    }
    private void UpdateSessionChrome()
    {
        foreach (var session in _sessions.Values)
            session.Profile.IsCurrentSession = SessionsContainer.Visibility == Visibility.Visible && ReferenceEquals(session, _activeSession) && session.IsLive;
        bool fit = !IsInFocus && SessionsContainer.Visibility == Visibility.Visible && _activeSession?.IsFitToWindow == true;
        ExitFitViewButton.Visibility = fit ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceCaption.Visibility = fit ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ExitFitViewClicked(object sender, RoutedEventArgs e)
    {
        if (_activeSession is { } session) SetFitToWindow(session, false);
    }
    public void EnterFocus(SessionView session)
    {
        if (IsInFocus) return;
        if (!session.IsReady) { SetStatus("Wait for the terminal to open or complete desktop sign-in before entering Focus mode."); return; }
        AccountPopup.IsOpen = false;
        if (_activeSession != session) OpenConnection(session.Profile);
        _restoreState = WindowState; _restoreBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _restoreChrome = WindowChrome.GetWindowChrome(this);
        _restoreMinimum = new(MinWidth, MinHeight);
        IsInFocus = true; _invisible = _workspace.HideFocusHandle; _hotkeyConflictReported = false;
        UpdateSessionChrome();
        TitleRow.Height = StatusRow.Height = new(0); SidebarColumn.Width = new(0);
        TitleChrome.Visibility = StatusChrome.Visibility = SidebarChrome.Visibility = Visibility.Collapsed;
        WindowBorder.BorderThickness = new(0); WindowBorder.CornerRadius = new(0);
        WindowChrome.SetWindowChrome(this, null);
        MinWidth = MinHeight = 0;
        ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Normal;
        session.SetFocus(true);
        PositionFullscreen();
        _foregroundTimer.Start();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            RefreshFocusOverlays(); session.FocusRemote();
            SetStatus("Focus mode: Ctrl+Alt+Space opens controls; Ctrl+Alt+Home returns to the manager.");
        }));
    }
    private void PositionFullscreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var bounds = NativeMethods.MonitorBounds(hwnd);
        NativeMethods.Position(hwnd, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }
    public void ExitFocus()
    {
        if (!IsInFocus) return;
        IsInFocus = false; _foregroundTimer.Stop(); HideFocusOverlays(); _hotkeys?.Unregister();
        _activeSession?.SetFocus(false);
        TitleRow.Height = new(39); StatusRow.Height = new(29); SidebarColumn.Width = new(221);
        TitleChrome.Visibility = StatusChrome.Visibility = SidebarChrome.Visibility = Visibility.Visible;
        WindowBorder.BorderThickness = new(1); WindowBorder.CornerRadius = new(10);
        ResizeMode = ResizeMode.CanResize; WindowChrome.SetWindowChrome(this, _restoreChrome);
        MinWidth = _restoreMinimum.Width; MinHeight = _restoreMinimum.Height;
        WindowState = WindowState.Normal;
        Left = _restoreBounds.Left; Top = _restoreBounds.Top; Width = _restoreBounds.Width; Height = _restoreBounds.Height;
        WindowState = _restoreState;
        UpdateSessionChrome();
        _activeSession?.FocusRemote();
    }
    public void MinimizeSession()
    {
        HideFocusOverlays(); _hotkeys?.Unregister();
        WindowState = WindowState.Minimized;
    }
    private void CloseControlPopups() { _quick?.Hide(); _panel?.Hide(); }
    public void HideFocusOverlays() { _handle?.Hide(); CloseControlPopups(); }
    private bool IsOurForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var owner = new WindowInteropHelper(this).Handle;
        return foreground != IntPtr.Zero && (foreground == owner ||
            (_quick is not null && foreground == new WindowInteropHelper(_quick).Handle) ||
            (_panel is not null && foreground == new WindowInteropHelper(_panel).Handle) ||
            NativeMethods.GetAncestor(foreground, 3) == owner);
    }
    private void TrackForeground()
    {
        if (!IsInFocus || WindowState == WindowState.Minimized) return;
        if (!IsOurForeground()) { CloseControlPopups(); _hotkeys?.Unregister(); return; }
        var foreground = NativeMethods.GetForegroundWindow();
        bool controlsForeground = (_quick is not null && foreground == new WindowInteropHelper(_quick).Handle) ||
            (_panel is not null && foreground == new WindowInteropHelper(_panel).Handle);
        if (!controlsForeground && (_quick?.IsVisible == true || _panel?.IsVisible == true)) CloseControlPopups();
        if (_hotkeys?.Available != true && !_hotkeyConflictReported) RefreshFocusOverlays();
        if (!_invisible && _handle?.IsVisible == false) RefreshFocusOverlays();
    }
    public void RefreshFocusOverlays()
    {
        if (!IsInFocus || WindowState == WindowState.Minimized || _activeSession is null) return;
        bool foreground = IsOurForeground();
        bool available = _hotkeys?.Available == true || (foreground && _hotkeys?.Register() == true);
        if (foreground && !available)
        {
            _invisible = false;
            if (!_hotkeyConflictReported)
            {
                _hotkeyConflictReported = true;
                SetStatus("A Focus shortcut is used by another app. Invisible mode is disabled; the edge handle remains available.");
                AppLog.Write("invisible-mode-disabled-hotkey-conflict");
            }
        }
        var bounds = NativeMethods.MonitorBounds(new WindowInteropHelper(this).Handle);
        if (_invisible) _handle?.Hide(); else _handle?.Place(bounds, _workspace.DockEdge, _workspace.DockPosition);
        _panel?.Update(_activeSession.Profile, _workspace.DockEdge, _invisible, available);
    }
    public void ShowFocusControls(bool atCursor = false)
    {
        if (!IsInFocus || _quick is null) return;
        if (_quick.IsVisible || _panel?.IsVisible == true) { CloseControlPopups(); _activeSession?.FocusRemote(); return; }
        PositionOverlay(_quick, atCursor);
        FocusOverlay(_quick);
    }
    private void FocusOverlay(Window overlay)
    {
        // Activation alone does not release mouse capture held by the native RDP host.
        Mouse.Capture(null);
        if (NativeMethods.GetCapture() != IntPtr.Zero && !NativeMethods.ReleaseCapture())
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The remote desktop could not release input to the session controls.");
        bool activated = overlay.Activate();
        bool focused = overlay.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        AppLog.Write("focus-controls-opened", _activeSession?.Profile.Id, new
        {
            overlay = overlay.GetType().Name, activated, focused,
            foreground = NativeMethods.GetForegroundWindow().ToInt64(),
            focus = NativeMethods.GetFocus().ToInt64(), capture = NativeMethods.GetCapture().ToInt64()
        });
        if (!activated || !focused) SetStatus("Windows could not focus the session controls. Ctrl+Alt+Home returns to windowed view.");
    }
    private void PositionOverlay(Window overlay, bool atCursor)
    {
        var hwnd = new WindowInteropHelper(this).Handle; var bounds = NativeMethods.MonitorBounds(hwnd);
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96d;
        overlay.MaxHeight = Math.Max(180, bounds.Height / scale - 24);
        if (!overlay.IsVisible) overlay.Show();
        overlay.UpdateLayout();
        int width = (int)Math.Ceiling(overlay.ActualWidth * scale), height = (int)Math.Ceiling(overlay.ActualHeight * scale);
        int margin = (int)(12 * scale), gap = (int)(32 * scale);
        int alongX = bounds.Left + (int)(bounds.Width * _workspace.DockPosition), alongY = bounds.Top + (int)(bounds.Height * _workspace.DockPosition);
        int x = _workspace.DockEdge switch { DockEdge.Left => bounds.Left + gap, DockEdge.Right => bounds.Right - width - gap, _ => alongX - width / 2 };
        int y = _workspace.DockEdge switch { DockEdge.Top => bounds.Top + gap, DockEdge.Bottom => bounds.Bottom - height - gap, _ => alongY - height / 2 };
        if (atCursor && NativeMethods.GetCursorPos(out var cursor)) { x = cursor.X + margin; y = cursor.Y + margin; }
        x = Math.Clamp(x, bounds.Left + margin, Math.Max(bounds.Left + margin, bounds.Right - width - margin));
        y = Math.Clamp(y, bounds.Top + margin, Math.Max(bounds.Top + margin, bounds.Bottom - height - margin));
        NativeMethods.Position(new WindowInteropHelper(overlay).Handle, x, y, width, height);
    }
    private void FocusAction(string action)
    {
        AppLog.Write("focus-action", _activeSession?.Profile.Id, new { action });
        switch (action)
        {
            case "Minimize": MinimizeSession(); break;
            case "Windowed": ExitFocus(); break;
            case "Disconnect": if (_activeSession is { } session) ConfirmDisconnect(session); break;
            case "More":
                _quick?.Hide();
                if (_panel is not null && _activeSession is not null)
                {
                    _panel.Update(_activeSession.Profile, _workspace.DockEdge, _invisible, _hotkeys?.Available == true);
                    PositionOverlay(_panel, false); FocusOverlay(_panel);
                }
                break;
            case "Close": CloseControlPopups(); Activate(); _activeSession?.FocusRemote(); break;
        }
    }
    public void ConfirmDisconnect(SessionView session)
    {
        if (session.Profile.IsPreview) { ExitFocus(); ShowHome(); return; }
        if (!session.IsLive) { ShowConnections(); return; }
        HideFocusOverlays();
        string consequence = session.Profile.Kind == ConnectionKind.Ssh ? "This closes the SSH shell. Remote commands may stop unless they are managed by tools such as tmux or screen." :
            "This does not sign out or shut down the remote computer. Session retention depends on its server policies.";
        if (MessageBox.Show(this, $"Disconnect from \"{session.Profile.Name}\"?\n\n{consequence}", "Disconnect session", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
        { ExitFocus(); session.Disconnect(); }
        else RefreshFocusOverlays();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        var live = _sessions.Values.Where(s => s.IsLive).ToList();
        if (live.Count > 0 && MessageBox.Show(this, $"Close the app and disconnect {live.Count} active session(s)?\n\nRDP and VNC desktops will not be signed out. SSH shells will close, and their commands may stop.", "Close RemoteMachine", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        { e.Cancel = true; return; }
        _closing = true; AccountPopup.IsOpen = false; ExitFocus(); _foregroundTimer.Stop(); _hotkeys?.Dispose(); _workArea?.Dispose();
        _handle?.Close(); _quick?.Close(); _panel?.Close();
        foreach (var session in _sessions.Values) { if (session.IsLive) session.Disconnect(); session.Dispose(); }
    }
    private void HomeClicked(object sender, RoutedEventArgs e) => ShowHome();
    private void SettingsClicked(object sender, RoutedEventArgs e) => ShowSettings();
    private void AccountClicked(object sender, RoutedEventArgs e)
    {
        AccountPopup.IsOpen = !AccountPopup.IsOpen;
        if (AccountPopup.IsOpen) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (AccountPopup.IsOpen) AccountSettingsButton.Focus();
        }));
    }
    private void AccountPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true; AccountPopup.IsOpen = false; AccountButton.Focus();
    }
    private void ConnectionsClicked(object sender, RoutedEventArgs e) => ShowConnections();
    private void FavoritesClicked(object sender, RoutedEventArgs e) => ShowConnections("Favorites");
    private void GroupLibraryClicked(object sender, RoutedEventArgs e) => ShowConnections((string)((Button)sender).Tag);
    private void ResumeSessionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConnectionProfile profile } && _sessions.TryGetValue(profile.Id, out var session) && session.IsLive) OpenConnection(session.Profile);
        else { NotifyData(); SetStatus("That session has ended. Use View all connections to reconnect it."); }
    }
    private void ActiveSessionClicked(object sender, RoutedEventArgs e) { if (SidebarSession is { } session) OpenConnection(session.Profile); else ShowConnections(); }
    private void MinimizeClicked(object sender, RoutedEventArgs e) => MinimizeSession();
    private void MaximizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
    private void GuideClicked(object sender, RoutedEventArgs e)
    {
        ShowMessage("RemoteMachine",
            "APPEARANCE\nOpen Settings to choose System, Light, or Dark. Your choice is saved locally. Your Windows account is available from the avatar at the top right.\n\nFOCUS MODE\nClick the accent-colored handle for Minimize, Windowed, Disconnect, and More. Drag it to any edge.\n\nCtrl+Alt+Space: show controls at the pointer.\nCtrl+Alt+Home: leave Focus mode.\nEscape remains available to the remote app.\n\nPERFORMANCE\nDirect Microsoft RDP rendering, native bandwidth detection, and hardware decoding requested. No browser, proxy, or frame copying in the RDP path. Windows system shortcuts stay local so Alt+Tab and recovery remain available.\n\nVNC / MAC\nEnable Screen Sharing on the Mac and allow your Mac account. Connect to its hostname or IP, usually on port 5900. VNC screen and input are not encrypted: use only a trusted network, VPN, or existing SSH tunnel. Never expose port 5900 to the internet. Mac shortcuts operate on the remote Mac; Windows clipboard synchronization and file transfer are not included.\n\nPRIVACY\nConnection metadata is saved locally. Windows handles RDP credentials; OpenSSH handles SSH authentication. VNC asks for credentials per connection and does not save passwords. RDP clipboard sharing is opt-in. No telemetry.\n\nDiagnostics:\n" + AppLog.LogPath + (AppLog.LastWriteError is { } error ? "\nLog write error: " + error : ""));
    }
}
