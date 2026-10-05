using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using RemoteHub.Controls;
using RemoteHub.Models;
using RemoteHub.Rdp;
using RemoteHub.Services;

namespace RemoteHub.Views;

public partial class SessionView : UserControl, IDisposable
{
    private readonly MainWindow _shell;
    private readonly DispatcherTimer _resizeTimer;
    private readonly Stopwatch _connectionClock = new();
    private WindowsFormsHost? _host;
    private RdpActiveX? _rdp;
    private SshTerminalView? _terminal;
    private VncDesktopView? _vnc;
    private readonly Func<SshTerminalView, Task>? _terminalStarter;
    private readonly Func<VncDesktopView, Task>? _vncStarter;
    private bool _started;
    private bool _disposed;
    private bool _requestedDisconnect;
    private bool _authenticated;
    private bool _updatingScrollSpeed;
    public ConnectionProfile Profile { get; }
    public bool IsLive => Profile.IsSessionActive;
    public bool IsReady => Profile.IsPreview || (Profile.Kind switch { ConnectionKind.Ssh => _terminal?.IsReady == true, ConnectionKind.Vnc => _vnc?.IsReady == true, _ => _authenticated });
    public bool IsFocus { get; private set; }
    public bool IsFitToWindow { get; private set; }
    public RdpActiveX? RdpControl => _rdp;
    internal SshTerminalView? TerminalControl => _terminal;
    internal VncDesktopView? VncControl => _vnc;
    public event EventHandler? StateChanged;

    public SessionView(MainWindow shell, ConnectionProfile profile, Func<SshTerminalView, Task>? terminalStarter = null, Func<VncDesktopView, Task>? vncStarter = null)
    {
        InitializeComponent(); _shell = shell; Profile = profile;
        SessionStatus.DataContext = profile;
        _terminalStarter = terminalStarter;
        _vncStarter = vncStarter;
        Subtitle.Text = profile.IsPreview ? $"{profile.Name} - local design preview, no network connection." : profile.Name;
        EndpointText.Text = profile.IsPreview ? "Design preview" : $"{profile.Protocol}  ·  {profile.Endpoint}";
        SshClipboardTools.Visibility = profile.Kind == ConnectionKind.Ssh ? Visibility.Visible : Visibility.Collapsed;
        VncTools.Visibility = profile.Kind == ConnectionKind.Vnc ? Visibility.Visible : Visibility.Collapsed;
        MacShortcut.IsEnabled = !profile.VncViewOnly;
        UpdateScrollSpeedControl();
        FullScreenButton.IsEnabled = IsReady;
        FitToWindowButton.IsEnabled = IsReady;
        FullScreenButton.ToolTip = "Available after the terminal opens or desktop sign-in completes. Ctrl+Alt+Home returns to the manager.";
        SessionActionButton.Content = profile.IsPreview ? "Close preview" : "Close session";
        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _resizeTimer.Tick += (_, _) => { _resizeTimer.Stop(); ResizeRemote(); };
        DesktopContainer.SizeChanged += (_, _) => { if (_authenticated) { _resizeTimer.Stop(); _resizeTimer.Start(); } };
        Loaded += (_, _) => { if (!_started) { _started = true; Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Start)); } };
    }
    private void Start()
    {
        if (_disposed) return;
        if (Profile.IsPreview) { BuildPreviewDesktop(); Profile.State = SessionState.Preview; SessionStatus.Text = "Design preview only - no remote input or connection"; StateChanged?.Invoke(this, EventArgs.Empty); return; }
        if (Profile.Kind == ConnectionKind.Ssh) { StartSsh(); return; }
        if (Profile.Kind == ConnectionKind.Vnc) { StartVnc(); return; }
        try
        {
            DisposeRdp();
            PlaceholderTitle.Text = "Connecting to your computer";
            PlaceholderMessage.Text = "Windows will ask for credentials. Your remote desktop is rendered directly by Microsoft's RDP component.";
            ReconnectButton.Visibility = Visibility.Collapsed;
            _host = new WindowsFormsHost();
            _rdp = new RdpActiveX();
            ((ISupportInitialize)_rdp).BeginInit();
            _host.Child = _rdp;
            ((ISupportInitialize)_rdp).EndInit();
            _rdp.Notification += RdpNotification;
            RdpHostSlot.Content = _host;
            Placeholder.Visibility = Visibility.Collapsed;
            RdpHostSlot.UpdateLayout();
            _rdp.CreateControl();
            var dpi = VisualTreeHelper.GetDpi(this);
            _rdp.Configure(Profile, (int)(Math.Max(800, DesktopContainer.ActualWidth) * dpi.DpiScaleX),
                (int)(Math.Max(600, DesktopContainer.ActualHeight) * dpi.DpiScaleY));
            _requestedDisconnect = false; _authenticated = false;
            _connectionClock.Restart();
            SetState(SessionState.Connecting, "Connecting directly with Microsoft RDP...");
            AppLog.Write("connect-requested", Profile.Id);
            _rdp.Connect();
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            AppLog.Write("connect-start-failed", Profile.Id, new { type = error.GetType().Name, error.HResult, error.Message });
            ShowEnded("Could not start the connection", error.Message, true);
        }
    }
    private async void StartVnc()
    {
        DisposeVnc();
        var viewer = new VncDesktopView(Profile.Id);
        _vnc = viewer;
        viewer.Started += VncStarted;
        viewer.Ended += VncEnded;
        RdpHostSlot.Content = viewer; Placeholder.Visibility = Visibility.Collapsed;
        NetworkStatus.Text = Profile.VncViewOnly ? "View only · VNC transport is not encrypted" : "VNC transport is not encrypted";
        SetState(SessionState.Connecting, "Connecting to Screen Sharing...");
        try { await (_vncStarter is null ? viewer.StartAsync(Profile) : _vncStarter(viewer)); }
        catch (OperationCanceledException) when (_disposed || !ReferenceEquals(_vnc, viewer) || viewer.IsClosed) { }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Net.Sockets.SocketException or COMException or
            InvalidOperationException or Win32Exception or TimeoutException or UnauthorizedAccessException or
            Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            if (_disposed || !ReferenceEquals(_vnc, viewer)) return;
            AppLog.Write("vnc-start-failed", Profile.Id, new { type = error.GetType().Name });
            DisposeVnc();
            ShowEnded("Could not open VNC", "Check the Mac's Screen Sharing settings, address, and network. " + error.Message, true);
        }
    }
    private void VncStarted()
    {
        Profile.LastConnected = DateTimeOffset.Now;
        SetState(SessionState.Connected, Profile.VncViewOnly ? "Connected · View only" : "Connected · macOS / VNC desktop");
        _shell.SaveWorkspace();
    }
    private void VncEnded(bool failed, string message)
    {
        var viewer = _vnc;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed || !ReferenceEquals(_vnc, viewer)) return;
            DisposeVnc(); ShowEnded(failed ? "VNC connection failed" : "VNC disconnected", message, failed);
        }));
    }
    private void DisposeVnc()
    {
        if (_vnc is null) return;
        _vnc.Started -= VncStarted; _vnc.Ended -= VncEnded;
        _vnc.Dispose(); _vnc = null; RdpHostSlot.Content = null;
    }
    private void MacShortcutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MacShortcut?.SelectedItem is not ComboBoxItem { Tag: string action }) return;
        _vnc?.SendMacShortcut(action); MacShortcut.SelectedIndex = 0;
    }
    private void UpdateScrollSpeedControl()
    {
        _updatingScrollSpeed = true;
        VncScrollSpeed.SelectedItem = Profile.VncScrollSpeed;
        VncScrollSpeed.IsEnabled = Profile.Kind == ConnectionKind.Vnc && !Profile.VncViewOnly && _vnc?.IsReady == true;
        _updatingScrollSpeed = false;
    }
    private void VncScrollSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingScrollSpeed && VncScrollSpeed?.SelectedItem is int speed) SetVncScrollSpeed(speed);
    }
    internal void SetVncScrollSpeed(int speed)
    {
        if (speed is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(speed));
        if (_disposed || !VncScrollSpeed.IsEnabled) { UpdateScrollSpeedControl(); return; }
        int previous = Profile.VncScrollSpeed;
        if (speed == previous) return;
        Profile.VncScrollSpeed = speed;
        if (!_shell.SaveWorkspace()) Profile.VncScrollSpeed = previous;
        else
        {
            _vnc?.SetScrollSpeed(speed);
            _shell.SetStatus($"Scroll speed saved: {speed}× for {Profile.Name}.");
        }
        UpdateScrollSpeedControl();
        _shell.RefreshFocusOverlays();
    }
    private async void StartSsh()
    {
        DisposeTerminal();
        var terminal = new SshTerminalView(Profile.Id);
        _terminal = terminal;
        terminal.Ended += SshEnded;
        terminal.Started += SshStarted;
        RdpHostSlot.Content = terminal;
        Placeholder.Visibility = Visibility.Collapsed;
        SshReconnectButton.Visibility = Visibility.Collapsed;
        SetState(SessionState.Connecting, "Opening the local SSH terminal...");
        try { await (_terminalStarter is null ? terminal.StartAsync(Profile) : _terminalStarter(terminal)); }
        catch (OperationCanceledException) when (_disposed || !ReferenceEquals(_terminal, terminal) || Profile.State == SessionState.Failed) { }
        catch (Exception error) when (error is IOException or COMException or InvalidOperationException or Win32Exception or TimeoutException or
            UnauthorizedAccessException or Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException)
        {
            if (_disposed || !ReferenceEquals(_terminal, terminal)) return;
            AppLog.Write("ssh-start-failed", Profile.Id, new { type = error.GetType().Name, error.Message });
            DisposeTerminal(); RdpHostSlot.Content = null;
            ShowEnded("Could not open SSH", error.Message + "\nSSH requires Windows OpenSSH Client and Microsoft Edge WebView2 Runtime.", true);
        }
    }
    private void SshStarted()
    {
        Profile.LastOpened = DateTimeOffset.Now;
        SetState(SessionState.TerminalOpen, "SSH terminal open · Sign-in and host verification are handled in the terminal");
        _shell.SaveWorkspace();
    }
    private void SshEnded(int code, string? error)
    {
        if (_disposed) return;
        if (_shell.ActiveSession == this) _shell.ExitFocus();
        SshReconnectButton.Visibility = Visibility.Visible;
        SetState(code == 0 ? SessionState.Disconnected : SessionState.Failed,
            error ?? (code == 0 ? "SSH session ended · Terminal output is retained until reconnecting" : $"SSH exited with code {code} · See terminal output"));
        if (_terminal?.IsReady != true) ShowEnded("SSH terminal ended", error ?? "The terminal renderer is unavailable.", code != 0);
    }
    private void DisposeTerminal()
    {
        if (_terminal is null) return;
        _terminal.Ended -= SshEnded; _terminal.Started -= SshStarted;
        _terminal.Dispose(); _terminal = null;
    }
    private void RdpNotification(object? sender, RdpNotification notification)
    {
        // COM callbacks must finish before changing visibility or disposing the ActiveX host.
        Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(sender, _rdp)) ApplyNotification(notification); }));
    }
    private void ApplyNotification(RdpNotification notification)
    {
        if (_disposed) return;
        AppLog.Write("rdp-" + notification.Kind, Profile.Id, new { elapsedMs = _connectionClock.ElapsedMilliseconds, notification.Code });
        switch (notification.Kind)
        {
            case "Connecting": SetState(SessionState.Connecting, "Connecting directly..."); break;
            case "Connected": SetState(SessionState.Authenticating, $"Transport connected in {_connectionClock.Elapsed.TotalSeconds:0.00}s. Completing sign-in..."); break;
            case "LoginComplete":
            case "AutoReconnected":
                _authenticated = true; Profile.LastConnected = DateTimeOffset.Now;
                SetState(SessionState.Connected, $"Connected  ·  Sign-in complete in {_connectionClock.Elapsed.TotalSeconds:0.00}s");
                _shell.SaveWorkspace(); _resizeTimer.Start(); _rdp?.FocusRemote(); break;
            case "Disconnected":
                ShowEnded(_requestedDisconnect ? "You're safely disconnected." : "Connection ended",
                    _requestedDisconnect ? "Your connection is saved. Disconnecting does not sign out of the remote computer." :
                    notification.Message ?? $"Microsoft RDP disconnected with code {notification.Code}.",
                    !_requestedDisconnect && notification.Code is not 1 and not 2);
                break;
            case "FatalError": ShowEnded("The RDP connection encountered an error", $"Microsoft RDP error {notification.Code}. Check the server, network, and Windows event logs.", true); break;
            case "Reconnecting": SetState(SessionState.Reconnecting, $"Reconnecting... attempt {notification.Code}"); break;
            case "Warning": _shell.SetStatus(notification.Message ?? $"RDP warning {notification.Code}"); break;
            case "NetworkStatus": NetworkStatus.Text = notification.Message; break;
            case "LogonError": SessionStatus.Text = $"Windows reported sign-in status {notification.Code}. Check the Windows sign-in prompt."; break;
            case "ServiceMessage": _shell.ShowMessage("Remote server message", notification.Message ?? "The server sent an empty message."); break;
            case "EnterFullScreen": if (!_shell.IsInFocus) _shell.EnterFocus(this); break;
            case "LeaveFullScreen": if (_shell.ActiveSession == this) _shell.ExitFocus(); break;
            case "Minimize": if (_shell.ActiveSession == this) _shell.MinimizeSession(); break;
            case "FocusReleased": _shell.ShowFocusControls(); break;
            case "AuthenticationDialog": _shell.HideFocusOverlays(); break;
            case "AuthenticationDialogClosed": _shell.RefreshFocusOverlays(); break;
        }
    }
    private void SetState(SessionState state, string text)
    {
        Profile.State = state; SessionStatus.Text = text;
        if (state is SessionState.Disconnected or SessionState.Failed) { IsFitToWindow = false; ApplySessionLayout(); }
        UpdateScrollSpeedControl();
        FullScreenButton.IsEnabled = IsReady;
        FitToWindowButton.IsEnabled = IsReady;
        SessionActionButton.Content = IsLive ? "Disconnect" : "Close session";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void ShowEnded(string title, string message, bool failed)
    {
        _authenticated = false; _resizeTimer.Stop();
        if (_shell.ActiveSession == this) _shell.ExitFocus();
        if (_host is not null) _host.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Visible;
        PlaceholderTitle.Text = title; PlaceholderMessage.Text = message;
        ReconnectButton.Visibility = Visibility.Visible;
        SetState(failed ? SessionState.Failed : SessionState.Disconnected, failed ? "Connection failed - see details above" : "Disconnected");
        _shell.SetStatus(message);
    }
    private void ResizeRemote()
    {
        if (!_authenticated || _rdp is null || !IsVisible) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        _rdp.ResizeDesktop((int)(DesktopContainer.ActualWidth * dpi.DpiScaleX), (int)(DesktopContainer.ActualHeight * dpi.DpiScaleY),
            (uint)Math.Round(dpi.DpiScaleX * 100));
    }
    public void SetFocus(bool enabled)
    {
        IsFocus = enabled;
        ApplySessionLayout();
    }
    internal void SetFitToWindow(bool enabled)
    {
        IsFitToWindow = enabled;
        ApplySessionLayout();
    }
    private void ApplySessionLayout()
    {
        bool expanded = IsFocus || IsFitToWindow;
        LayoutRoot.Margin = expanded ? new Thickness(0) : new Thickness(28, 27, 28, 23);
        DesktopFrame.BorderThickness = expanded ? new Thickness(0) : new Thickness(1, 0, 1, 0);
        HeadingPanel.Visibility = ToolbarPanel.Visibility = FooterPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        HeaderRow.Height = ToolbarRow.Height = FooterRow.Height = expanded ? new GridLength(0) : GridLength.Auto;
        if (_authenticated) { _resizeTimer.Stop(); _resizeTimer.Start(); }
    }
    public void FocusRemote() { if (Profile.IsPreview) Focus(); else if (Profile.Kind == ConnectionKind.Ssh) _terminal?.FocusTerminal(); else if (Profile.Kind == ConnectionKind.Vnc) _vnc?.FocusDesktop(); else _rdp?.FocusRemote(); }
    public void Disconnect()
    {
        if (Profile.IsPreview) { _shell.ExitFocus(); _shell.ShowHome(); return; }
        if (!IsLive) return;
        if (Profile.Kind == ConnectionKind.Vnc)
        {
            DisposeVnc(); ShowEnded("VNC disconnected", "The remote desktop remains signed in. Reconnect when you are ready.", false); return;
        }
        if (Profile.Kind == ConnectionKind.Ssh)
        {
            DisposeTerminal(); RdpHostSlot.Content = null;
            ShowEnded("SSH disconnected", "The SSH shell has closed. Use a server-side session manager for commands that must survive disconnection.", false);
            return;
        }
        try { _requestedDisconnect = true; _rdp?.Disconnect(); }
        catch (COMException error) { AppLog.Write("disconnect-failed", Profile.Id, new { error.HResult }); _shell.ShowMessage("Could not disconnect", error.Message); }
    }
    private void FullscreenClicked(object sender, RoutedEventArgs e) => _shell.EnterFocus(this);
    private void FitToWindowClicked(object sender, RoutedEventArgs e) => _shell.SetFitToWindow(this, true);
    private void ConnectionsClicked(object sender, RoutedEventArgs e) => _shell.ShowConnections();
    private void DisconnectClicked(object sender, RoutedEventArgs e) => _shell.ConfirmDisconnect(this);
    private void ReconnectClicked(object sender, RoutedEventArgs e) => Start();
    private async void CopyTerminalClicked(object sender, RoutedEventArgs e) { if (_terminal is { } terminal) await terminal.CopySelectionAsync(); }
    private async void PasteTerminalClicked(object sender, RoutedEventArgs e) { if (_terminal is { } terminal) await terminal.PasteClipboardAsync(); }

    private void BuildPreviewDesktop()
    {
        Placeholder.Visibility = Visibility.Collapsed;
        var root = new Grid();
        root.SetResourceReference(BackgroundProperty, "Surface");
        root.RowDefinitions.Add(new() { Height = new(42) }); root.RowDefinitions.Add(new() { Height = new(45) });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = new(44) });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        tabs.SetResourceReference(BackgroundProperty, "Subtle");
        foreach (string title in new[] { "Project files", "Design workspace" })
        {
            var button = new Button { Content = title + "    ×",
                BorderThickness = new(0), Margin = new(9, 6, 0, 0), Padding = new(18, 8, 18, 8), FontSize = 11 };
            button.SetResourceReference(BackgroundProperty, title == "Project files" ? "Surface" : "Transparent");
            button.Click += (_, _) => _shell.SetStatus($"Preview tab selected: {title}. No toolbar obstructs these tabs.");
            tabs.Children.Add(button);
        }
        root.Children.Add(tabs);
        var addressText = new TextBlock { Text = "⌂    workspace.example / projects / website-refresh    ·    Local design preview", FontSize = 11 };
        addressText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var address = new Border { Padding = new(24, 13, 24, 13), Child = addressText };
        address.SetResourceReference(Border.BackgroundProperty, "Subtle");
        Grid.SetRow(address, 1); root.Children.Add(address);
        var page = new StackPanel { Margin = new(45, 38, 45, 25) };
        page.Children.Add(new TextBlock { Text = "Workspace", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 30) });
        var breadcrumb = new TextBlock { Text = "My files   ›   Design   ›   Website refresh", FontSize = 11, Margin = new(0, 0, 0, 20) };
        breadcrumb.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        page.Children.Add(breadcrumb);
        page.Children.Add(new TextBlock { Text = "Website refresh", FontSize = 28, FontWeight = FontWeights.SemiBold });
        var description = new TextBlock { Text = "Everything for the next chapter. All in one place.", FontSize = 12, Margin = new(0, 10, 0, 28) };
        description.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        page.Children.Add(description);
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        foreach (var item in new[] { ("Visual exploration.fig", "Blue"), ("Brand direction.pdf", "Sand"), ("Homepage wireframe.fig", "Mint") })
        {
            var content = new StackPanel();
            content.Children.Add(new Wallpaper { Tone = item.Item2, Height = 180 });
            content.Children.Add(new TextBlock { Text = item.Item1, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new(16, 16, 16, 8) });
            var caption = new TextBlock { Text = "Illustrative content", FontSize = 10, Margin = new(16, 0, 16, 16) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            content.Children.Add(caption);
            var card = new Border { Child = content, BorderThickness = new(1),
                CornerRadius = new(10), ClipToBounds = true, Margin = new(0, 0, 18, 0) };
            card.SetResourceReference(Border.BackgroundProperty, "Surface");
            card.SetResourceReference(Border.BorderBrushProperty, "Line");
            cards.Children.Add(card);
        }
        page.Children.Add(cards);
        foreach (string filename in new[] { "Project brief.docx", "Content outline.docx", "Launch checklist.xlsx" })
        {
            var label = new TextBlock { Text = filename, FontSize = 12 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            var row = new Border { BorderThickness = new(0, 0, 0, 1), Padding = new(12, 17, 12, 17), Child = label };
            row.SetResourceReference(Border.BorderBrushProperty, "Line");
            page.Children.Add(row);
        }
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        var taskbarText = new TextBlock { Text = "■     Search     ▣    ●    ■", FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        taskbarText.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        var taskbar = new Border { Child = taskbarText };
        taskbar.SetResourceReference(Border.BackgroundProperty, "Subtle");
        Grid.SetRow(taskbar, 3); root.Children.Add(taskbar);
        RdpHostSlot.Content = root;
    }
    private void DisposeRdp()
    {
        if (_rdp is not null) _rdp.Notification -= RdpNotification;
        RdpHostSlot.Content = null;
        _host?.Dispose(); _host = null; _rdp = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _resizeTimer.Stop();
        DisposeTerminal();
        DisposeVnc();
        DisposeRdp();
    }
}
