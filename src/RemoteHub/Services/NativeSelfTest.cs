using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using RemoteHub.Focus;
using RemoteHub.Models;
using RemoteHub.Native;
using RemoteHub.Rdp;
using RemoteHub.Themes;
using RemoteHub.Views;

namespace RemoteHub.Services;

internal static class NativeSelfTest
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativeMethods.Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    public static async Task<int> Run(MainWindow window, bool interactive = false)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "self-test-results.json");
        var checks = new List<string>();
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks.Add(description);
        }
        try
        {
            await Task.Delay(200);
            if (interactive)
            {
                window.Title = "Click this window to start the local controls test";
                window.SetStatus("Local input test: activate this window, then leave the mouse and keyboard untouched.");
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (NativeMethods.GetForegroundWindow() != new WindowInteropHelper(window).Handle && wait.Elapsed < TimeSpan.FromSeconds(30))
                    await Task.Delay(200);
                Check(NativeMethods.GetForegroundWindow() == new WindowInteropHelper(window).Handle,
                    "Interactive tests require this app in the foreground. Minimize any covering Remote Desktop window and rerun --interaction-test. No input has been injected.");
                window.Title = "RemoteMachine - local controls test";
            }
            Snapshot(window, "native-empty");
            BrandingSelfTest.Run(window, Check);
            Check(((TextBlock)window.FindName("WindowsAccountName")).Text == WindowsAccount.ReadCurrent().QualifiedName,
                "The shell displays the current Windows account without requesting credentials");
            Check(!window.RecentConnections.Any() && !window.HasActiveSessions, "An empty workspace does not imply recent or active connections");
            if (!interactive) await SettingsShellSelfTest.Run(window, Check);
            if (!interactive) await ThemeSelfTest.Run(window, Check);
            var savedOnly = new ConnectionProfile { Name = "Never connected", Host = "192.0.2.30" };
            window.Profiles.Add(savedOnly);
            Check(!window.RecentConnections.Any(), "Saving a computer does not claim it was recently connected");
            window.Profiles.Remove(savedOnly);
            window.ShowAdd(); await Task.Delay(100); Snapshot(window, "native-add");
            checks.Add("Native connection form instantiated");
            var formSlot = (ContentControl)window.FindName("PageContent");
            Check(((CheckBox)((UserControl)formSlot.Content).FindName("SmartCardsInput")).IsChecked == true, "New connections allow smart-card and Windows Hello sign-in");
            var form = (UserControl)formSlot.Content;
            Check(Math.Abs(((TextBox)form.FindName("UserInput")).ActualHeight - ((ComboBox)form.FindName("GroupInput")).ActualHeight) < 1,
                "Related text and selection fields share the same control height");
            Check(((TextBox)form.FindName("UserInput")).Text == "", "A Windows account is not silently selected for a new remote computer");
            ((Button)form.FindName("UseWindowsAccountButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((TextBox)form.FindName("UserInput")).Text == window.CurrentWindowsAccount.UserName &&
                new ConnectionProfile { UserName = ((TextBox)form.FindName("UserInput")).Text, Domain = ((TextBox)form.FindName("DomainInput")).Text }.UserNameDisplay == window.CurrentWindowsAccount.QualifiedName &&
                ((TextBlock)form.FindName("AccountHint")).Text.Contains(window.CurrentWindowsAccount.QualifiedName, StringComparison.Ordinal),
                "Use my Windows account fills the username only after an explicit action and explains remote authentication");
            var saveButton = (Button)form.FindName("SaveButton");
            window.UpdateLayout();
            var buttonSize = new Size(saveButton.ActualWidth, saveButton.ActualHeight);
            window.Activate(); saveButton.Focus(); window.UpdateLayout();
            Check(buttonSize == new Size(saveButton.ActualWidth, saveButton.ActualHeight) &&
                ((Border)saveButton.Template.FindName("FocusRing", saveButton)).Opacity == 1,
                "Keyboard focus is visible without moving or resizing the action button");
            window.ShowAdd(new ConnectionProfile { Name = "Edit check", Host = "192.0.2.1", UserName = "remote.user", Domain = "REMOTE", RedirectSmartCards = false });
            var editForm = (UserControl)formSlot.Content;
            Check(((CheckBox)editForm.FindName("SmartCardsInput")).IsChecked == false, "Editing a connection preserves its smart-card opt-out");
            Check(((TextBox)editForm.FindName("UserInput")).Text == "remote.user" && ((TextBox)editForm.FindName("DomainInput")).Text == "REMOTE",
                "The detected local account never overwrites saved remote credentials");
            window.ShowAdd();
            var sshForm = (UserControl)formSlot.Content;
            ((Button)sshForm.FindName("SshChoice")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            Check(((TextBox)sshForm.FindName("PortInput")).Text == "22" && ((FrameworkElement)sshForm.FindName("SshOptions")).IsVisible &&
                !((FrameworkElement)sshForm.FindName("RdpOptions")).IsVisible, "Choosing SSH exposes key settings and port 22 without RDP-only options");
            Snapshot(window, "native-add-ssh");
            double formWidth = window.Width, formHeight = window.Height;
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            await Task.Delay(100); Snapshot(window, "native-add-ssh-compact");
            window.Width = formWidth; window.Height = formHeight;
            ((Button)sshForm.FindName("RdpChoice")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((TextBox)sshForm.FindName("PortInput")).Text == "3389", "Switching back to RDP restores its default port");
            int initialCount = window.Profiles.Count;
            var import = RdpFile.Parse("full address:s:192.0.2.12:3390\nusername:s:import.user\nredirectclipboard:i:1\nredirectprinters:i:1", "Imported PC");
            formSlot.Content = new Views.AddConnectionView(window, import: import);
            await Task.Delay(100);
            Check(window.Profiles.Count == initialCount && window.ActiveSession is null &&
                ((FrameworkElement)((UserControl)formSlot.Content).FindName("ImportNotice")).IsVisible,
                "RDP import is a review draft and neither saves nor connects automatically");
            Check(((Expander)((UserControl)formSlot.Content).FindName("RdpOptions")).IsExpanded,
                "Imported device-sharing and authentication options are expanded for review");
            Snapshot(window, "native-rdp-import");
            ((Button)((UserControl)formSlot.Content).FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.Profiles.Count == initialCount + 1 && window.Profiles.Last().Port == 3390,
                "Saving an import adds a new profile instead of overwriting an existing connection");
            window.Profiles.Remove(window.Profiles.Last());
            var localSsh = new ConnectionProfile { Name = "SSH terminal check (local shell)", Kind = ConnectionKind.Ssh, Host = "localhost", Port = 22 };
            window.Profiles.Add(localSsh);
            window.OpenConnection(localSsh, terminal => terminal.StartProcessAsync(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), ["/d", "/q"]));
            await WaitUntil(() => window.ActiveSession?.IsReady == true || localSsh.State == SessionState.Failed);
            var sshSession = window.ActiveSession ?? throw new InvalidOperationException("SSH session view was not created.");
            var terminal = sshSession.TerminalControl ?? throw new InvalidOperationException("SSH renderer was not created.");
            Check(sshSession.IsReady && localSsh.State == SessionState.TerminalOpen && !localSsh.LastConnected.HasValue && localSsh.LastOpened.HasValue,
                "An embedded terminal reports open, not falsely authenticated, and records its last-opened time");
            await terminal.InjectTestInputAsync("echo __TERMINAL_INPUT_OK__\r");
            await WaitForTerminal(terminal, "__TERMINAL_INPUT_OK__");
            checks.Add("Input crosses the local web bridge into ConPTY and output returns to the terminal renderer");
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
            using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "screenshots", "native-ssh-terminal.png")))
                await terminal.CaptureAsync(stream);
            if (!interactive)
            {
                var systemTheme = App.Theme.Current;
                string transcript = await terminal.ReadTextAsync();
                foreach (bool dark in new[] { false, true })
                {
                    var palette = ThemePalette.Create(dark, ThemePalette.Parse("#00A884"));
                    App.Theme.Apply(palette);
                    await Task.Delay(100);
                    using var rendererTheme = JsonDocument.Parse(await terminal.ReadThemeAsync());
                    Check(rendererTheme.RootElement.GetProperty("background").GetString() == palette.Terminal["background"] &&
                        !terminal.Completion.IsCompleted && ReferenceEquals(sshSession.TerminalControl, terminal) &&
                        await terminal.ReadTextAsync() == transcript,
                        $"{palette.Name}: the existing SSH terminal adopts the palette without reconnecting or clearing output");
                    using var image = File.Create(Path.Combine(AppContext.BaseDirectory, "screenshots", dark ? "theme-dark-terminal.png" : "theme-light-terminal.png"));
                    await terminal.CaptureAsync(image);
                }
                App.Theme.Apply(systemTheme);
                await terminal.InjectTestInputAsync("echo __THEME_INPUT_OK__\r");
                await WaitForTerminal(terminal, "__THEME_INPUT_OK__");
                checks.Add("SSH input remains functional after live theme transitions");
                await CheckFitToWindow(window, sshSession, Check);
                await terminal.InjectTestInputAsync("echo __FIT_INPUT_OK__\r");
                await WaitForTerminal(terminal, "__FIT_INPUT_OK__");
                Check(ReferenceEquals(sshSession.TerminalControl, terminal) && !terminal.Completion.IsCompleted,
                    "Fit-to-window transitions preserve the ConPTY process and terminal input");
            }
            window.EnterFocus(sshSession); await Task.Delay(150);
            Check(window.IsInFocus && ReferenceEquals(window.ActiveSession?.TerminalControl, terminal),
                "SSH Focus mode retains the existing terminal and process");
            window.ShowFocusControls();
            Check(window.OwnedWindows.OfType<QuickControlsWindow>().Single().IsVisible,
                "The same native fullscreen controls are available over the SSH terminal");
            window.ExitFocus();
            window.ShowHome(); window.OpenConnection(localSsh);
            Check(ReferenceEquals(window.ActiveSession?.TerminalControl, terminal), "Navigating away and resuming does not recreate the SSH terminal");
            await terminal.InjectTestInputAsync("exit\r");
            await terminal.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Check(localSsh.State == SessionState.Disconnected && sshSession.IsReady,
                "Natural shell exit preserves a readable terminal and reports disconnection");
            window.ShowConnections(); await Task.Delay(100);
            var sshLibrary = (UserControl)formSlot.Content;
            Check(((DataGrid)sshLibrary.FindName("ConnectionsGrid")).SelectedItem == localSsh &&
                !((Button)sshLibrary.FindName("ExportButton")).IsEnabled, "SSH profiles appear in the library without offering an invalid RDP export");
            Snapshot(window, "native-ssh-library");
            window.Profiles.Remove(localSsh);
            window.ShowAdd();
            using (var host = new WindowsFormsHost())
            using (var rdp = new RdpActiveX())
            {
                var container = (ContentControl)window.FindName("PageContent");
                ((ISupportInitialize)rdp).BeginInit(); host.Child = rdp; ((ISupportInitialize)rdp).EndInit();
                container.Content = host; container.UpdateLayout(); rdp.CreateControl();
                checks.Add("Microsoft RDP ActiveX initialized in the WPF host: " + rdp.Client.Version);
                var profile = new ConnectionProfile { Name = "Configuration check", Host = "192.0.2.1" };
                rdp.Configure(profile, 1280, 720);
                Check(!rdp.Client.AdvancedSettings9.DisplayConnectionBar, "Microsoft connection bar is disabled");
                Check(rdp.Client.AdvancedSettings9.EnableCredSspSupport, "CredSSP is enabled");
                Check(rdp.Client.AdvancedSettings9.AuthenticationLevel == 2, "Certificate authentication policy is unchanged");
                Check(rdp.Client.AdvancedSettings9.RedirectSmartCards, "Native smart-card redirection is enabled for Windows Hello for Business");
                profile.RedirectSmartCards = false; rdp.Configure(profile, 1280, 720);
                Check(!rdp.Client.AdvancedSettings9.RedirectSmartCards, "Native smart-card redirection honors a connection's explicit opt-out");
                profile.RedirectSmartCards = true; rdp.Configure(profile, 1280, 720);
                Check(rdp.Client.AdvancedSettings9.RedirectSmartCards, "Smart-card support can be re-enabled before reconnecting");
                Check(rdp.BandwidthDetectionConfigured, "Native bandwidth detection setting is accepted");
                Check(!rdp.Client.AdvancedSettings9.RedirectClipboard && !rdp.Client.AdvancedSettings9.RedirectDrives &&
                    !rdp.Client.AdvancedSettings9.RedirectDevices && !rdp.Client.AdvancedSettings9.RedirectPrinters &&
                    !rdp.Client.AdvancedSettings9.RedirectPorts && !rdp.Client.AdvancedSettings9.AudioCaptureRedirectionMode,
                    "Clipboard, drives, devices, printers, ports, and microphone remain disabled by default");
                Check(rdp.Client.Connected == 0, "RDP configuration validated without contacting a remote computer");
                using (var listener = new TcpListener(IPAddress.Loopback, 0))
                {
                    listener.Start();
                    rdp.Client.Server = "127.0.0.1";
                    rdp.Client.AdvancedSettings9.RDPPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                    if (rdp.GetOcx() is not MSTSCLib.IMsRdpClientNonScriptable5 native) throw new InvalidOperationException("Native credential controls are unavailable.");
                    native.PromptForCredentials = native.PromptForCredsOnClient = native.AllowPromptingForCredentials = false;
                    var disconnected = new TaskCompletionSource<bool>();
                    bool connectingEvent = false;
                    rdp.Notification += (_, notification) =>
                    {
                        if (notification.Kind == "Connecting") connectingEvent = true;
                        if (notification.Kind == "Disconnected") disconnected.TrySetResult(true);
                    };
                    var accept = listener.AcceptTcpClientAsync();
                    rdp.Connect();
                    using (var peer = await accept.WaitAsync(TimeSpan.FromSeconds(5)))
                    {
                        var header = new byte[64];
                        int count = await peer.GetStream().ReadAsync(header).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                        Check(count >= 4 && header[0] == 3, "Microsoft RDP sends a transport handshake to the local-only test listener");
                    }
                    await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(connectingEvent && rdp.Client.Connected == 0, "COM connecting/disconnected callbacks are delivered after the local listener closes");
                }
                container.Content = null;
            }
            if (!interactive) await VncSelfTest.Run(window, Check);
            foreach (var profile in WorkspaceStore.PreviewConnections()) window.Profiles.Add(profile);
            window.ShowHome(); await Task.Delay(150); Snapshot(window, "native-home");
            window.ShowConnections(); await Task.Delay(150); Snapshot(window, "native-connections");
            var library = (Views.ConnectionsView)formSlot.Content;
            ((TextBox)library.FindName("SearchBox")).Text = "no-matching-device";
            Check(((FrameworkElement)library.FindName("EmptyResults")).IsVisible && !((FrameworkElement)library.FindName("Details")).IsVisible,
                "An empty search shows recovery actions instead of stale connection details");
            ((Button)library.FindName("ClearFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((StackPanel)library.FindName("FilterTabs")).Children.OfType<Button>().Single(b => (string)b.Tag == "Work")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((DataGrid)library.FindName("ConnectionsGrid")).Items.Cast<ConnectionProfile>().All(p => p.Group == "Work") &&
                ((Expander)window.FindName("WorkGroupNav")).FontWeight == FontWeights.SemiBold,
                "Connection filters and sidebar group selection stay in sync");
            double normalWidth = window.Width, normalHeight = window.Height;
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            window.ShowHome(); await Task.Delay(100);
            var homeScroll = (ScrollViewer)((UserControl)formSlot.Content).FindName("HomeScroll");
            var sidebarScroll = (ScrollViewer)window.FindName("SidebarScroll");
            Check(homeScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible && homeScroll.ViewportHeight < window.ActualHeight &&
                sidebarScroll.ScrollableHeight > 0, "Compact windows scroll home content and navigation instead of overlapping footer controls");
            sidebarScroll.ScrollToEnd(); await Task.Delay(100);
            var cloudButton = (Expander)window.FindName("CloudGroupNav");
            var sessionPeek = (Button)window.FindName("SessionPeek");
            Check(cloudButton.TransformToAncestor(window).Transform(new Point(0, cloudButton.ActualHeight)).Y <=
                sessionPeek.TransformToAncestor(window).Transform(new Point()).Y, "The last sidebar group remains reachable above the session and Settings controls");
            sidebarScroll.ScrollToTop(); await Task.Delay(100); Snapshot(window, "native-home-compact");
            window.ShowConnections(); await Task.Delay(100); Snapshot(window, "native-connections-compact");
            window.ShowAdd(); await Task.Delay(100); Snapshot(window, "native-add-compact");
            window.Width = normalWidth; window.Height = normalHeight;
            window.ShowConnections(); await Task.Delay(100);
            window.Activate();
            window.OpenConnection(window.Profiles[0]); await Task.Delay(300);
            var session = window.ActiveSession ?? throw new InvalidOperationException("Preview session was not created.");
            Snapshot(window, "native-session");
            if (!interactive)
            {
                await CheckFitToWindow(window, session, Check, "native-fit-windowed");
                window.WindowState = WindowState.Maximized; await Task.Delay(100);
                SettingsShellSelfTest.CheckWorkArea(window, Check);
                await CheckFitToWindow(window, session, Check, "native-fit-maximized");
                window.EnterFocus(session); await Task.Delay(100);
                Check(Bounds(window).Equals(NativeMethods.MonitorBounds(new WindowInteropHelper(window).Handle)),
                    "Focus mode still covers the complete monitor rather than stopping at the taskbar");
                window.ExitFocus(); await Task.Delay(100);
                Check(window.WindowState == WindowState.Maximized && ReferenceEquals(window.ActiveSession, session),
                    "Leaving Focus restores the maximized manager and retains the session");
                SettingsShellSelfTest.CheckWorkArea(window, Check);
                window.WindowState = WindowState.Normal; await Task.Delay(100);
            }
            var before = Bounds(window);
            if (interactive) Click((Button)session.FindName("FullScreenButton"));
            else window.EnterFocus(session);
            await Task.Delay(300);
            var handle = window.OwnedWindows.OfType<EdgeHandleWindow>().Single();
            var quick = window.OwnedWindows.OfType<QuickControlsWindow>().Single();
            var panel = window.OwnedWindows.OfType<FocusPanelWindow>().Single();
            var monitor = NativeMethods.MonitorBounds(new WindowInteropHelper(window).Handle);
            Check(window.IsInFocus, "Focus mode enters successfully");
            Check(Bounds(window).Equals(monitor), "Fullscreen bounds exactly match the monitor in physical pixels");
            Check(ReferenceEquals(window.ActiveSession, session), "Entering Focus preserves the session view");
            if (!interactive)
            {
                await Task.Delay(500);
                Check(handle.IsVisible && handle.Owner == window && !handle.Topmost, "The handle is an owned native window, not globally topmost");
                Check(handle.IsVisible, "The default handle does not disappear after idle or background focus checks");
                var grip = Descendants<Border>(handle).Single(b => b.Name == "Grip");
                var rim = Descendants<Border>(handle).Single(b => b.Name == "ContrastRim");
                var outline = Descendants<Border>(handle).Single(b => b.Name == "ContrastOutline");
                Check(Math.Min(grip.ActualWidth, grip.ActualHeight) == 10 && Math.Max(grip.ActualWidth, grip.ActualHeight) == 48,
                    "The rendered grip is 10 DIP thick and 48 DIP long");
                Check(rim.Padding == new Thickness(2) && outline.Padding == new Thickness(1), "The grip has a two-DIP bright rim and one-DIP dark outline");
                var rimColor = ((SolidColorBrush)rim.Background).Color;
                var outlineColor = ((SolidColorBrush)outline.Background).Color;
                Check(Enumerable.Range(0, 1001).All(i => Math.Max(Contrast(Luminance(rimColor), i / 1000d),
                    Contrast(Luminance(outlineColor), i / 1000d)) >= 3), "At least one outline exceeds 3:1 contrast across the background luminance range");
                Snapshot(window, "native-focus");
                using (var nativeInput = new System.Windows.Forms.Control())
                {
                    nativeInput.CreateControl();
                    nativeInput.Capture = true;
                    Check(NativeMethods.GetCapture() == nativeInput.Handle, "A native control holds mouse capture before the popup opens");
                    window.ShowFocusControls();
                    Check(NativeMethods.GetCapture() == IntPtr.Zero, "Opening controls releases native mouse capture");
                    Check(Keyboard.FocusedElement is Button focusedButton && Window.GetWindow(focusedButton) == quick &&
                        NativeMethods.GetFocus() == new WindowInteropHelper(quick).Handle,
                        "Pointer-opened controls receive both WPF keyboard focus and native HWND focus");
                }
                Check(quick.IsVisible && quick.Owner == window && Descendants<Button>(quick).Count() == 4, "Native quick controls expose all four actions");
                Snapshot(window, "native-focus-controls");
                var themeBeforeFocusCheck = App.Theme.Current;
                App.Theme.Apply(ThemePalette.Create(true, ThemePalette.Parse("#00A884")));
                await Task.Delay(80);
                Check(((SolidColorBrush)grip.Background).Color == App.Theme.Current.Colors["AccentFill"] &&
                    ((SolidColorBrush)((Border)quick.Content).Background).Color == App.Theme.Current.Colors["Surface"] &&
                    ReferenceEquals(window.ActiveSession, session) && window.IsInFocus,
                    "The visible fullscreen handle and quick controls follow the new accent/theme without replacing the session");
                Snapshot(window, "theme-dark-focus-controls");
                Descendants<Button>(quick).Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(panel.IsVisible && panel.Owner == window, "More opens the owned detailed controls");
                Check(Keyboard.FocusedElement is Button panelButton && Window.GetWindow(panelButton) == panel &&
                    NativeMethods.GetFocus() == new WindowInteropHelper(panel).Handle, "More transfers native and WPF focus to the detailed controls");
                Snapshot(window, "native-focus-more");
                Snapshot(window, "theme-dark-focus-more");
                App.Theme.Apply(themeBeforeFocusCheck);
                foreach (var edge in Enum.GetValues<DockEdge>())
                {
                    handle.Place(monitor, edge, .4);
                    var edgeBounds = Bounds(handle);
                    Check(edge switch { DockEdge.Top => edgeBounds.Top == monitor.Top, DockEdge.Right => edgeBounds.Right == monitor.Right,
                        DockEdge.Bottom => edgeBounds.Bottom == monitor.Bottom, _ => edgeBounds.Left == monitor.Left }, "Native handle geometry: " + edge);
                    var paintBounds = outline.TransformToAncestor(handle).TransformBounds(new Rect(0, 0, outline.ActualWidth, outline.ActualHeight));
                    Check(paintBounds.Left >= 0 && paintBounds.Top >= 0 && paintBounds.Right <= handle.ActualWidth &&
                        paintBounds.Bottom <= handle.ActualHeight, "The thicker handle is fully inside its window at " + edge);
                }
                handle.Place(monitor, DockEdge.Top, .4);
                HandleContrastSamples(handle);
                window.HideFocusOverlays();
                Check(!handle.IsVisible && !panel.IsVisible && !quick.IsVisible, "Hiding controls removes all native overlay windows");
                Snapshot(window, "native-focus-invisible");
                window.ShowFocusControls();
                Descendants<Button>(quick).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.WindowState == WindowState.Minimized && ReferenceEquals(window.ActiveSession, session), "The Minimize button minimizes without replacing the session");
                window.WindowState = WindowState.Normal;
                window.ShowFocusControls();
                Descendants<Button>(quick).ElementAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!window.IsInFocus && Bounds(window).Equals(before), "The Windowed button restores exact windowed geometry");
                window.EnterFocus(session);
                window.ShowFocusControls();
                Descendants<Button>(quick).ElementAt(2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!window.IsInFocus && !handle.IsVisible && !quick.IsVisible, "The Disconnect button exits the local preview session");
                File.WriteAllText(path, JsonSerializer.Serialize(new { passed = true, checks, liveRdpTested = false, interactiveInputTested = false,
                    note = "Noninteractive checks only. Run --interaction-test from an unobstructed desktop for real pointer and hotkey checks." }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            Check(handle.IsVisible, "Focus mode shows the owned blue handle");
            Snapshot(window, "native-focus");

            Click(handle); await Task.Delay(200);
            Check(quick.IsVisible, "An actual pointer click opens the quick-controls strip");
            Check(Descendants<Button>(quick).Count() == 4, "The strip has exactly Minimize, Windowed, Disconnect and More actions");
            Snapshot(window, "native-focus-controls");
            var desktop = Bounds(window); SetCursorPos(desktop.Left + 200, desktop.Top + 120); MouseClick(); await Task.Delay(200);
            Check(!quick.IsVisible, "Clicking the desktop dismisses the strip without swallowing the click");
            Click(handle); await Task.Delay(150);
            Click(Descendants<Button>(quick).Last()); await Task.Delay(200);
            Check(panel.IsVisible, "More opens the detailed controls panel");
            Snapshot(window, "native-focus-more");
            foreach (string edge in new[] { "Top", "Right", "Bottom", "Left" })
            {
                Click((Button)panel.FindName(edge + "Dock")); await Task.Delay(120);
                var rect = Bounds(handle);
                Check(edge switch { "Top" => rect.Top == monitor.Top, "Right" => rect.Right == monitor.Right,
                    "Bottom" => rect.Bottom == monitor.Bottom, _ => rect.Left == monitor.Left }, "Native handle docks at " + edge);
            }
            var invisible = (CheckBox)panel.FindName("InvisibleToggle");
            Check(invisible.IsEnabled, "Focus recovery shortcuts register successfully");
            Click(invisible); await Task.Delay(150);
            Check(!handle.IsVisible, "Invisible mode removes the native handle window and its hit target");
            Click(Descendants<Button>(panel).Single(b => Equals(b.Content, "Back to my work  →"))); await Task.Delay(120);
            Check(!panel.IsVisible && !quick.IsVisible && !handle.IsVisible, "Invisible mode leaves no app overlays");
            Snapshot(window, "native-focus-invisible");
            Chord(0x20); await Task.Delay(200);
            Check(quick.IsVisible, "Ctrl+Alt+Space recovers controls in invisible mode using the native hotkey");
            Click(Descendants<Button>(quick).First()); await Task.Delay(200);
            Check(window.WindowState == WindowState.Minimized && !quick.IsVisible && !handle.IsVisible, "Minimize hides all overlays and keeps Focus session alive");
            window.WindowState = WindowState.Normal; window.Activate(); await Task.Delay(200);
            Check(window.IsInFocus && ReferenceEquals(window.ActiveSession, session), "Restoring the window retains the same Focus session");
            Chord(0x24); await Task.Delay(200);
            Check(!window.IsInFocus && Bounds(window).Equals(before), "Ctrl+Alt+Home restores exact windowed geometry");

            window.EnterFocus(session); await Task.Delay(180);
            Check(!handle.IsVisible, "The explicit invisible preference survives leaving and re-entering Focus");
            Chord(0x20); await Task.Delay(150);
            Click(Descendants<Button>(quick).Last()); await Task.Delay(150);
            Click(invisible); await Task.Delay(150);
            Check(handle.IsVisible, "The user can restore the persistent visible handle");
            Click(Descendants<Button>(panel).Single(b => Equals(b.Content, "Back to my work  →"))); await Task.Delay(120);
            Click(handle); await Task.Delay(120);
            Click(Descendants<Button>(quick).ElementAt(1)); await Task.Delay(180);
            Check(!window.IsInFocus, "The Windowed quick action exits Focus without reconnecting");

            window.EnterFocus(session); await Task.Delay(180);
            var start = Bounds(handle);
            SetCursorPos((start.Left + start.Right) / 2, (start.Top + start.Bottom) / 2);
            mouse_event(0x2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(50);
            SetCursorPos(monitor.Right - 5, monitor.Top + monitor.Height / 3); await Task.Delay(150);
            mouse_event(0x4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(180);
            Check(Bounds(handle).Right == monitor.Right, "An actual pointer drag moves the handle to a different edge");
            using (var foreground = new System.Windows.Forms.Form { Width = 300, Height = 160, ShowInTaskbar = false, Text = "Focus handoff test" })
            {
                foreground.Show(); foreground.Activate(); await Task.Delay(250);
                Check(handle.IsVisible && !handle.Topmost && !quick.IsVisible && !panel.IsVisible,
                    "Switching windows dismisses popups without hiding the session's non-topmost handle");
                foreground.Close();
            }
            window.Activate(); await Task.Delay(200);
            Check(handle.IsVisible, "Returning to the session restores the handle");
            Click(handle); await Task.Delay(150);
            Click(Descendants<Button>(quick).ElementAt(2)); await Task.Delay(150);
            Check(!window.IsInFocus, "The Disconnect quick action exits the preview from a single pointer click");
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed = true, checks, liveRdpTested = false, interactiveInputTested = true }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            AppLog.Write("self-test-failed", details: new { error.Message, error.StackTrace });
            return 1;
        }
    }
    private static NativeMethods.Rect Bounds(Window window)
    {
        if (!NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect)) throw new Win32Exception("Cannot read test window geometry.");
        return rect;
    }
    private static async Task WaitUntil(Func<bool> predicate)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!predicate())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(25)) throw new TimeoutException("The native terminal did not reach the expected state.");
            await Task.Delay(50);
        }
    }
    private static async Task WaitForTerminal(Views.SshTerminalView terminal, string text)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!(await terminal.ReadTextAsync()).Contains(text, StringComparison.Ordinal))
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("The terminal did not render the expected output.");
            await Task.Delay(50);
        }
    }
    internal static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Click(FrameworkElement target)
    {
        var point = target.PointToScreen(new(target.ActualWidth / 2, target.ActualHeight / 2));
        var hit = WindowFromPoint(new NativeMethods.Point { X = (int)point.X, Y = (int)point.Y });
        var window = Window.GetWindow(target) ?? throw new InvalidOperationException("The test target has no window.");
        if (GetAncestor(hit, 2) != new WindowInteropHelper(window).Handle)
            throw new InvalidOperationException("Another window covers the test target. No click was injected.");
        SetCursorPos((int)point.X, (int)point.Y); MouseClick();
    }
    private static void MouseClick()
    {
        if (!NativeMethods.GetCursorPos(out var cursor) || !IsTestWindow(GetAncestor(WindowFromPoint(cursor), 2)))
            throw new InvalidOperationException("The pointer is outside the test windows. No click was injected.");
        mouse_event(0x2, 0, 0, 0, UIntPtr.Zero); mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);
    }
    private static void Chord(byte key)
    {
        if (!IsTestWindow(NativeMethods.GetForegroundWindow()))
            throw new InvalidOperationException("The test app lost foreground input. No hotkey was injected.");
        keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }
    private static bool IsTestWindow(IntPtr hwnd) => Application.Current.Windows.Cast<Window>().Any(w => new WindowInteropHelper(w).Handle == hwnd);
    private static double Luminance(Color color)
    {
        static double Linear(byte value) => value <= 10 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    private static double Contrast(double first, double second) => (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    private static void HandleContrastSamples(EdgeHandleWindow handle)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var samples = new[] { ("Light desktop", Colors.White), ("Dark desktop", Color.FromRgb(14, 16, 24)),
                ("Same blue desktop", Color.FromRgb(36, 92, 229)), ("Mixed content", Color.FromRgb(224, 230, 238)) };
            for (int i = 0; i < samples.Length; i++)
            {
                var (label, color) = samples[i];
                int left = i * 240;
                drawing.DrawRectangle(new SolidColorBrush(color), null, new Rect(left, 0, 240, 140));
                if (i == 3)
                    for (int tile = 0; tile < 8; tile++)
                        drawing.DrawRectangle(tile % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(35, 45, 65)), null,
                            new Rect(left + tile * 30, 48, 30, 92));
                drawing.DrawText(new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 13, Luminance(color) > .4 ? Brushes.Black : Brushes.White, 1), new Point(left + 18, 18));
                drawing.DrawRectangle(new VisualBrush(handle), null, new Rect(left + (240 - handle.ActualWidth) / 2, 70,
                    handle.ActualWidth, handle.ActualHeight));
            }
        }
        var output = new RenderTargetBitmap(960, 140, 96, 96, PixelFormats.Pbgra32);
        output.Render(visual);
        SaveSnapshot(output, "native-handle-contrast");
    }
    internal static async Task CheckFitToWindow(MainWindow window, SessionView session, Action<bool, string> check, string? screenshot = null)
    {
        var originalBounds = Bounds(window);
        var originalState = window.WindowState;
        var fitButton = (Button)session.FindName("FitToWindowButton");
        var exitButton = (Button)window.FindName("ExitFitViewButton");
        var sessions = (Grid)window.FindName("SessionsContainer");
        var desktop = (Grid)session.FindName("DesktopContainer");
        var toolbar = (Border)session.FindName("ToolbarPanel");
        check(!session.IsFitToWindow && fitButton.IsEnabled, "Ready sessions offer Fit to window without enabling it automatically");
        fitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(180); window.UpdateLayout();
        var fitBounds = desktop.TransformToAncestor(sessions).TransformBounds(new Rect(0, 0, desktop.ActualWidth, desktop.ActualHeight));
        check(session.IsFitToWindow && !window.IsInFocus && originalState == window.WindowState && Bounds(window).Equals(originalBounds),
            "Fit to window preserves the native window bounds and maximized state rather than entering fullscreen");
        check(Math.Abs(fitBounds.X) < 1 && Math.Abs(fitBounds.Y) < 1 &&
            Math.Abs(fitBounds.Width - sessions.ActualWidth) < 1 && Math.Abs(fitBounds.Height - sessions.ActualHeight) < 1 &&
            toolbar.Visibility == Visibility.Collapsed && ((DockPanel)session.FindName("HeadingPanel")).Visibility == Visibility.Collapsed &&
            ((Border)session.FindName("FooterPanel")).Visibility == Visibility.Collapsed,
            "Fit view removes session chrome and fills the entire content area beside the sidebar");
        check(((Border)window.FindName("SidebarChrome")).IsVisible && ((Grid)window.FindName("TitleChrome")).IsVisible &&
            ((Border)window.FindName("StatusChrome")).IsVisible && exitButton.IsVisible &&
            System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(exitButton),
            "Sidebar, Windows controls, and a caption-hit-testable Exit fit view button remain available");
        if (screenshot is not null) Snapshot(window, screenshot);
        window.ShowHome();
        check(!exitButton.IsVisible, "Fit-view recovery controls are hidden when navigating to normal app pages");
        window.OpenConnection(session.Profile); await Task.Delay(100);
        check(ReferenceEquals(window.ActiveSession, session) && session.IsFitToWindow && exitButton.IsVisible,
            "Resuming the same session retains its fit-to-window layout");
        window.MinimizeSession(); await Task.Delay(60);
        window.WindowState = originalState; await Task.Delay(120);
        check(session.IsFitToWindow && Bounds(window).Equals(originalBounds), "Minimize and restore retain fit view and exact window geometry");
        window.EnterFocus(session); await Task.Delay(120);
        check(window.IsInFocus && session.IsFitToWindow && !exitButton.IsVisible, "Fullscreen can temporarily take over a fit-to-window session");
        window.ExitFocus(); await Task.Delay(120);
        check(!window.IsInFocus && session.IsFitToWindow && exitButton.IsVisible && Bounds(window).Equals(originalBounds),
            "Leaving fullscreen returns to the previous fit-to-window view");
        exitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(120);
        check(!session.IsFitToWindow && !exitButton.IsVisible && toolbar.IsVisible && fitButton.IsVisible &&
            ((Grid)session.FindName("LayoutRoot")).Margin == new Thickness(28, 27, 28, 23),
            "Exit fit view restores normal session controls and margins in one action");
    }
    internal static void Snapshot(Window window, string name)
    {
        window.UpdateLayout();
        var output = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var viewport = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
            drawing.DrawRectangle(new VisualBrush(window) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = viewport }, null, viewport);
            var main = Bounds(window);
            double scale = NativeMethods.GetDpiForWindow(new WindowInteropHelper(window).Handle) / 96d;
            foreach (Window overlay in window.OwnedWindows)
            {
                if (!overlay.IsVisible) continue;
                var rect = Bounds(overlay);
                drawing.DrawRectangle(new VisualBrush(overlay), null, new Rect((rect.Left - main.Left) / scale, (rect.Top - main.Top) / scale,
                    overlay.ActualWidth, overlay.ActualHeight));
            }
            if (window.FindName("AccountPopup") is System.Windows.Controls.Primitives.Popup { IsOpen: true, Child: FrameworkElement card })
            {
                var point = card.PointToScreen(new Point());
                drawing.DrawRectangle(new VisualBrush(card), null, new Rect((point.X - main.Left) / scale, (point.Y - main.Top) / scale,
                    card.ActualWidth, card.ActualHeight));
            }
        }
        output.Render(visual);
        SaveSnapshot(output, name);
    }
    private static void SaveSnapshot(BitmapSource bitmap, string name)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots"); Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
