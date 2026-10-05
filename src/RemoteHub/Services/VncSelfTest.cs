using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using RemoteHub.Models;
using RemoteHub.Views;
using RemoteHub.Focus;

namespace RemoteHub.Services;

internal static class VncSelfTest
{
    public static async Task<int> RunPerformance(MainWindow window)
    {
        var results = new List<object>();
        string path = Path.Combine(AppContext.BaseDirectory, "vnc-performance-results.json");
        try
        {
            foreach (var (width, height) in new[] { (1920, 1080), (2880, 1800) })
            {
                await using var peer = new LoopbackPeer(2, width: width, height: height, frames: 5);
                var profile = new ConnectionProfile { Name = "Synthetic VNC performance check", Host = "127.0.0.1", Port = peer.Port,
                    Kind = ConnectionKind.Vnc, VncTrustedNetwork = true };
                window.OpenConnection(profile, null, viewer => viewer.StartAsync(profile, _ => Task.FromResult<VncCredentials?>(new("", "testpass"))));
                var session = window.ActiveSession ?? throw new InvalidOperationException("Performance viewer is missing.");
                try
                {
                    for (int i = 0; peer.FrameTimes.Count < 5 && i < 2400; i++)
                    {
                        if (peer.Error is { } error) throw new InvalidOperationException("Performance fixture failed.", error);
                        if (profile.State == SessionState.Failed) throw new InvalidOperationException("Performance connection failed.");
                        await Task.Delay(25);
                    }
                    if (peer.FrameTimes.Count != 5) throw new TimeoutException("The full-size framebuffer did not finish: " +
                        JsonSerializer.Serialize(new { peer.Authenticated, frames = peer.FrameTimes.Count, transport = session.VncControl?.TransportState }));
                    using var snapshot = JsonDocument.Parse(await session.VncControl!.SnapshotAsync());
                    var root = snapshot.RootElement;
                    if (root.GetProperty("width").GetInt32() != width || root.GetProperty("height").GetInt32() != height ||
                        root.GetProperty("firstPixel")[0].GetInt32() != 4 ||
                        root.GetProperty("lastPixel")[0].GetInt32() != (width * height - 1 + 4) % 251)
                        throw new InvalidDataException("Large framebuffer pixels were lost or reordered.");
                    if (await session.VncControl.ValidateFrameAsync() != "true")
                        throw new InvalidDataException("The complete framebuffer does not match the sent pattern.");
                    window.SetFitToWindow(session, true); await Task.Delay(250);
                    await CheckViewport(window, session.VncControl);
                    var available = (Grid)window.FindName("SessionsContainer");
                    if (Math.Abs(session.VncControl.ActualWidth - available.ActualWidth) > 1 ||
                        Math.Abs(session.VncControl.ActualHeight - available.ActualHeight) > 1)
                        throw new InvalidDataException("Fit-to-window VNC did not fill the available app content area.");
                    window.EnterFocus(session); await Task.Delay(250);
                    await CheckViewport(window, session.VncControl);
                    window.ExitFocus(); await Task.Delay(250);
                    if (!session.IsFitToWindow) throw new InvalidDataException("Fullscreen did not restore the prior fit-to-window mode.");
                    await CheckViewport(window, session.VncControl);
                    window.SetFitToWindow(session, false); await Task.Delay(250);
                    await CheckViewport(window, session.VncControl);
                    double[] times = peer.FrameTimes.ToArray(), sorted = times.Skip(1).Order().ToArray();
                    int peakSlots = session.VncControl.PeakReceiveSlots;
                    if (peakSlots is < 2 or > 4) throw new InvalidDataException("The shared-memory receive window is not bounded and pipelined.");
                    double median = (sorted[1] + sorted[2]) / 2;
                    if (median >= 250) throw new InvalidOperationException("Local frame transfer exceeds the 250 ms regression budget.");
                    results.Add(new { width, height, bytesPerFrame = 16 + width * height * 4, milliseconds = times,
                        completePixelPatternVerified = true, fullscreenFitVerified = true, appWindowFitVerified = true, peakReceiveSlots = peakSlots,
                        medianWarmMilliseconds = median });
                }
                finally { session.Disconnect(); window.ShowHome(); }
            }
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed = true, workload = "Local raw framebuffer decode/update-request round trip; not live Mac latency", results },
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { passed = false, error = error.ToString(), results }));
            return 1;
        }
    }
    private static async Task CheckViewport(MainWindow window, VncDesktopView control)
    {
        using var layout = JsonDocument.Parse(await control.LayoutAsync());
        var size = layout.RootElement;
        double width = size.GetProperty("viewportWidth").GetDouble(), height = size.GetProperty("viewportHeight").GetDouble();
        double remoteWidth = size.GetProperty("remoteWidth").GetDouble(), remoteHeight = size.GetProperty("remoteHeight").GetDouble();
        double scale = Math.Min(width / remoteWidth, height / remoteHeight);
        if (Math.Abs(width - control.ActualWidth) > 2 || Math.Abs(height - control.ActualHeight) > 2 ||
            Math.Abs(size.GetProperty("width").GetDouble() - remoteWidth * scale) > 2 ||
            Math.Abs(size.GetProperty("height").GetDouble() - remoteHeight * scale) > 2 ||
            Math.Abs(size.GetProperty("x").GetDouble() - (width - remoteWidth * scale) / 2) > 2 ||
            Math.Abs(size.GetProperty("y").GetDouble() - (height - remoteHeight * scale) / 2) > 2)
            throw new InvalidOperationException("The VNC viewport is boxed in or not proportionally fitted: " +
                size.GetRawText() + $"; host={control.ActualWidth}x{control.ActualHeight}; window={window.ActualWidth}x{window.ActualHeight}");
    }
    public static async Task Run(MainWindow window, Action<bool, string> check)
    {
        window.ShowAdd();
        var slot = (ContentControl)window.FindName("PageContent");
        var form = (AddConnectionView)slot.Content;
        ((Button)form.FindName("VncChoice")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(100); window.UpdateLayout();
        check(((TextBox)form.FindName("PortInput")).Text == "5900" && ((StackPanel)form.FindName("VncOptions")).IsVisible &&
            !((Expander)form.FindName("RdpOptions")).IsVisible && !((Button)form.FindName("UseWindowsAccountButton")).IsVisible,
            "VNC has port 5900, Mac setup guidance, and no Windows-only sign-in controls");
        check(((CheckBox)form.FindName("VncTrustedInput")).IsChecked != true, "Unencrypted VNC requires an explicit user choice");
        check((int)((ComboBox)form.FindName("VncScrollSpeedInput")).SelectedItem == 1,
            "The VNC form defaults to original scroll distance rather than accelerating every connection");
        NativeSelfTest.Snapshot(window, "native-vnc-form");
        ((TextBox)form.FindName("PortInput")).Text = "5901";
        ((Button)form.FindName("SshChoice")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(((TextBox)form.FindName("PortInput")).Text == "5901", "Changing protocol preserves a manually configured port");
        ((TextBox)form.FindName("PortInput")).Text = "22";
        ((Button)form.FindName("VncChoice")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(((TextBox)form.FindName("PortInput")).Text == "5900", "Switching from the SSH default to VNC selects its default port");
        var editable = new ConnectionProfile { Name = "Scroll settings fixture", Host = "127.0.0.1", Port = 5900,
            Kind = ConnectionKind.Vnc, VncTrustedNetwork = true, VncScrollSpeed = 3 };
        window.Profiles.Add(editable);
        try
        {
            window.ShowAdd(editable);
            var editor = (AddConnectionView)slot.Content;
            check((int)((ComboBox)editor.FindName("VncScrollSpeedInput")).SelectedItem == 3, "Editing a VNC connection loads its saved scroll speed");
            ((ComboBox)editor.FindName("VncScrollSpeedInput")).SelectedItem = 4;
            ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(window.Profiles.Single(connection => connection.Id == editable.Id).VncScrollSpeed == 4,
                "Saving the VNC form retains the explicitly selected per-connection scroll speed");
        }
        finally { window.Profiles.Remove(window.Profiles.Single(connection => connection.Id == editable.Id)); window.ShowHome(); }
        foreach (var (security, viewOnly, reject, cancel) in new (byte, bool, bool, bool)[]
            { (30, false, false, false), (2, false, false, false), (1, false, false, false),
              (30, true, false, false), (2, false, true, false), (30, false, false, true), (0, false, false, false) })
        {
            await using var peer = new LoopbackPeer(security, reject);
            var profile = new ConnectionProfile { Name = "Local VNC protocol check", Host = "127.0.0.1", Port = peer.Port,
                Kind = ConnectionKind.Vnc, VncTrustedNetwork = true, VncViewOnly = viewOnly, UserName = "fixture-user", VncScrollSpeed = viewOnly ? 3 : 1 };
            window.Profiles.Add(profile);
            try
            {
                bool nativeDialog = security == 30 && !viewOnly && !cancel;
                bool dialogCompleted = false;
                DateTimeOffset? dialogOpened = null;
                window.OpenConnection(profile, null, viewer => nativeDialog ? viewer.StartAsync(profile) : viewer.StartAsync(profile, mac =>
                {
                    check(mac == (security == 30), "The sign-in request distinguishes Mac account authentication from a VNC-only password");
                    return Task.FromResult<VncCredentials?>(cancel ? null : new("fixture-user", reject ? "badpass!" : "testpass"));
                }));
                var session = window.ActiveSession ?? throw new InvalidOperationException("VNC test session was not created.");
                await Wait(() =>
                {
                    if (nativeDialog && !dialogCompleted && window.OwnedWindows.OfType<VncCredentialsWindow>().FirstOrDefault(d => d.IsVisible) is { } dialog)
                    {
                        dialogOpened ??= DateTimeOffset.UtcNow;
                        if (DateTimeOffset.UtcNow - dialogOpened.Value < TimeSpan.FromMilliseconds(150)) return false;
                        var password = (PasswordBox)dialog.FindName("Password");
                        var connect = (Button)dialog.FindName("ConnectButton");
                        connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        check(((TextBlock)dialog.FindName("Error")).Visibility == Visibility.Visible && dialog.Credentials is null,
                            "The real native Mac sign-in dialog rejects an empty password");
                        ((TextBox)dialog.FindName("User")).Text = new string('\u00e9', 40); password.Password = "testpass";
                        connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        check(dialog.Credentials is null, "Mac sign-in validates UTF-8 byte limits rather than silently truncating credentials");
                        ((TextBox)dialog.FindName("User")).Text = "fixture-user";
                        dialog.UpdateLayout(); NativeSelfTest.Snapshot(dialog, "native-vnc-sign-in");
                        connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); dialogCompleted = true;
                        check(password.Password.Length == 0, "The native password field is cleared when its dialog closes");
                    }
                    return session.IsReady || profile.State == SessionState.Failed || (cancel && profile.State == SessionState.Disconnected);
                }, peer);
                if (cancel)
                {
                    check(profile.State == SessionState.Disconnected && !profile.LastConnected.HasValue && session.VncControl is null,
                        "Cancelling VNC credentials closes the pending connection without recording a successful sign-in");
                    continue;
                }
                if (security is 0 or 1 || reject)
                {
                    check(profile.State == SessionState.Failed && !profile.LastConnected.HasValue && !session.IsReady,
                        security == 0 ? "A server closing before authentication fails explicitly without recording a connection" :
                            reject ? "A wrong VNC password is reported as failed without recording a connection" :
                            "A passwordless server is rejected and never recorded as a successful connection");
                    continue;
                }
                if (!session.IsReady) throw new InvalidOperationException(((TextBlock)session.FindName("PlaceholderMessage")).Text);
                check(peer.Authenticated && profile.State == SessionState.Connected && profile.LastConnected.HasValue,
                    security == 30 ? "Apple RFB 003.889 / ARD type 30 encrypts the test Mac credentials correctly" : "Standard VNC challenge-response authenticates with the separate VNC password");
                var control = session.VncControl ?? throw new InvalidOperationException("Embedded VNC viewer is missing.");
                await Wait(() => peer.FrameSent, peer);
                bool rendered = false;
                for (int attempt = 0; attempt < 80 && !rendered; attempt++)
                {
                    using var image = JsonDocument.Parse(await control.SnapshotAsync());
                    var root = image.RootElement;
                    rendered = root.ValueKind == JsonValueKind.Object && root.GetProperty("width").GetInt32() == 32 &&
                        root.GetProperty("height").GetInt32() == 24 && root.GetProperty("firstPixel")[0].GetInt32() == 230 &&
                        root.GetProperty("firstPixel")[1].GetInt32() == 32 && root.GetProperty("firstPixel")[2].GetInt32() == 50;
                    if (!rendered) await Task.Delay(25);
                }
                check(rendered, security == 2 ? "The embedded renderer decodes compressed TightPNG pixels without external image requests" :
                    "The real embedded renderer decodes framebuffer pixels from the local VNC server");
                await control.TestInputAsync();
                if (viewOnly)
                {
                    await control.TestWheelAsync([new { deltaY = 300 }, new { deltaX = -300 }]);
                    control.SendMacShortcut("copy"); await Task.Delay(200);
                    check(peer.KeyEvents == 0 && peer.PointerEvents == 0 && !((ComboBox)session.FindName("MacShortcut")).IsEnabled &&
                        !((ComboBox)session.FindName("VncScrollSpeed")).IsEnabled,
                        "View-only mode blocks remote keyboard, pointer, wheel, and Mac shortcut commands");
                    continue;
                }
                await Wait(() => peer.KeyEvents >= 2 && peer.PointerEvents >= 2, peer);
                check(peer.SawA && peer.SawLeftClick && peer.LastX is > 0 and < 32 && peer.LastY is > 0 and < 24,
                    $"Keyboard and scaled pointer events cross the embedded bridge (key={peer.SawA}, click={peer.SawLeftClick}, position={peer.LastX},{peer.LastY})");
                control.SendMacShortcut("copy");
                await Wait(() => peer.SawCommand, peer);
                check(peer.SawCommand, "Mac shortcuts send the Command key without capturing the local Windows key");
                await CheckWheel(control, peer, check);
                await CheckScrollSpeed(session, peer, check);
                await NativeSelfTest.CheckFitToWindow(window, session, check);
                if (security == 30)
                {
                    double normalWidth = window.Width;
                    window.Width = window.MinWidth; await Task.Delay(100); window.UpdateLayout();
                    var toolbar = (Border)session.FindName("ToolbarPanel");
                    foreach (string name in new[] { "VncScrollSpeed", "MacShortcut", "FitToWindowButton", "FullScreenButton" })
                    {
                        var element = (FrameworkElement)session.FindName(name);
                        var bounds = element.TransformToAncestor(toolbar).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                        check(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= toolbar.ActualWidth && bounds.Bottom <= toolbar.ActualHeight,
                            $"Compact VNC toolbar keeps {name} inside the visible controls");
                    }
                    NativeSelfTest.Snapshot(window, "native-vnc-scroll-compact");
                    window.Width = normalWidth; await Task.Delay(100);
                    using var image = File.Create(Path.Combine(AppContext.BaseDirectory, "screenshots", "native-vnc-frame.png"));
                    await control.CaptureAsync(image);
                    window.SetFitToWindow(session, true); await Task.Delay(180);
                    await CheckViewport(window, control);
                    await CheckWheel(control, peer, check);
                    window.EnterFocus(session); await Task.Delay(100);
                    check(window.IsInFocus && ReferenceEquals(session.VncControl, control), "VNC Focus uses the retained embedded viewer and existing fullscreen controls");
                    await Task.Delay(200);
                    var desktopBounds = control.TransformToAncestor(window).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
                    using var layout = JsonDocument.Parse(await control.LayoutAsync());
                    var size = layout.RootElement;
                    check(Math.Abs(desktopBounds.X) < 1 && Math.Abs(desktopBounds.Y) < 1 &&
                        Math.Abs(desktopBounds.Width - window.ActualWidth) < 1 && Math.Abs(desktopBounds.Height - window.ActualHeight) < 1,
                        $"Fullscreen VNC host fills the monitor rather than a content-sized box (host {desktopBounds}; window {window.ActualWidth}x{window.ActualHeight})");
                    check(Math.Abs(size.GetProperty("width").GetDouble() - size.GetProperty("viewportWidth").GetDouble()) < 2 ||
                        Math.Abs(size.GetProperty("height").GetDouble() - size.GetProperty("viewportHeight").GetDouble()) < 2,
                        "Fullscreen VNC fits at least one display edge without a boxed-in desktop");
                    await CheckWheel(control, peer, check);
                    var panel = window.OwnedWindows.OfType<FocusPanelWindow>().Single();
                    var focusSpeed = (ComboBox)panel.FindName("VncScrollSpeed");
                    focusSpeed.SelectedItem = 5;
                    check(profile.VncScrollSpeed == 5 && (int)((ComboBox)session.FindName("VncScrollSpeed")).SelectedItem == 5,
                        "Fullscreen scroll selection updates the same saved setting and windowed selector");
                    await ExpectWheel(control, peer, check, [new { deltaY = 50 }], WheelSteps(16, 5),
                        "Fullscreen sensitivity changes apply without reconnecting the VNC desktop");
                    panel.Show(); panel.UpdateLayout(); NativeSelfTest.Snapshot(panel, "native-vnc-scroll-focus"); panel.Hide();
                    focusSpeed.SelectedItem = 1;
                    window.MinimizeSession(); await Task.Delay(100);
                    check(window.WindowState == WindowState.Minimized && session.IsLive && ReferenceEquals(session.VncControl, control),
                        "Minimizing VNC retains its authenticated connection and renderer");
                    window.WindowState = WindowState.Normal; await Task.Delay(100);
                    window.ExitFocus(); window.ShowSettings(); window.OpenConnection(profile);
                    check(ReferenceEquals(window.ActiveSession, session) && ReferenceEquals(session.VncControl, control),
                        "VNC survives navigation without reconnecting or replacing the viewer");
                    check(session.IsFitToWindow && ((Button)window.FindName("ExitFitViewButton")).IsVisible,
                        "The retained VNC session returns to fit view after fullscreen, navigation, and resume");
                }
                window.SetFitToWindow(session, true);
                session.Disconnect();
                await peer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
                check(!session.IsLive && !session.IsReady && profile.State == SessionState.Disconnected, "Disconnect closes the VNC socket and releases the viewer");
                check(!session.IsFitToWindow && !((Button)window.FindName("ExitFitViewButton")).IsVisible &&
                    ((Border)session.FindName("ToolbarPanel")).IsVisible,
                    "Disconnect restores the normal session layout and removes the fit-view recovery button");
                if (security == 2)
                {
                    foreach (bool invalidWheel in new[] { true, false })
                    {
                        await using var nextPeer = new LoopbackPeer(2);
                        profile.Port = nextPeer.Port;
                        profile.VncScrollSpeed = 3;
                        ((Button)session.FindName("ReconnectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await Wait(() => session.IsReady || profile.State == SessionState.Failed, nextPeer);
                        check(session.IsReady && nextPeer.Authenticated && !ReferenceEquals(session.VncControl, control),
                            "Reconnect authenticates a fresh VNC viewer after the previous socket was closed");
                        if (invalidWheel)
                        {
                            window.SetFitToWindow(session, true); await Task.Delay(180);
                            await CheckViewport(window, session.VncControl!);
                            await session.VncControl!.TestInputAsync();
                            await Wait(() => nextPeer.PointerEvents >= 2, nextPeer);
                            await ExpectWheel(session.VncControl!, nextPeer, check, [new { deltaY = 50 }], WheelSteps(16, 3),
                                "A newly connected renderer starts with the saved 3x scroll setting");
                            int pointers = nextPeer.PointerEvents;
                            await session.VncControl!.TestWheelAsync([new { deltaX = 25600, deltaY = 25650 }]);
                            await Wait(() => profile.State == SessionState.Failed, nextPeer);
                            check(session.VncControl is null && nextPeer.PointerEvents == pointers,
                                "Excessive combined wheel distance fails explicitly before emitting input or blocking the renderer");
                            check(!session.IsFitToWindow && !((Button)window.FindName("ExitFitViewButton")).IsVisible,
                                "A failed VNC session restores normal controls instead of leaving an empty fit view");
                        }
                        else
                        {
                            await session.VncControl!.TestInvalidAckAsync();
                            await Wait(() => profile.State == SessionState.Failed, nextPeer);
                            check(session.VncControl is null, "Invalid shared-buffer acknowledgements close VNC rather than reusing an unconsumed slot");
                        }
                        session.Disconnect();
                    }
                }
            }
            finally
            {
                window.ActiveSession?.Disconnect();
                window.Profiles.Remove(profile); window.ShowHome();
            }
        }
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var loading = new ConnectionProfile { Name = "VNC startup cancellation check", Kind = ConnectionKind.Vnc,
            Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, VncTrustedNetwork = true };
        window.Profiles.Add(loading);
        try
        {
            Task? startup = null;
            window.OpenConnection(loading, null, viewer =>
            {
                startup = viewer.StartAsync(loading);
                window.ActiveSession!.Disconnect();
                return startup;
            });
            for (int i = 0; startup is null && i < 200; i++) await Task.Delay(25);
            if (startup is null) throw new TimeoutException("The cancellation fixture did not start.");
            try { await startup.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
            check(loading.State == SessionState.Disconnected && window.ActiveSession?.VncControl is null &&
                !loading.LastConnected.HasValue && !listener.Pending(), "Disconnect during VNC renderer startup prevents a later network connection");
        }
        finally { window.ActiveSession?.Disconnect(); window.Profiles.Remove(loading); window.ShowHome(); }
        await CheckSessionNavigation(window, check);
    }
    private static async Task CheckSessionNavigation(MainWindow window, Action<bool, string> check)
    {
        var peers = new[] { new LoopbackPeer(30), new LoopbackPeer(2), new LoopbackPeer(30) };
        var profiles = new[]
        {
            new ConnectionProfile { Name = "Design Mac", Group = "Work" },
            new ConnectionProfile { Name = "Engineering workstation with a deliberately long display name", Group = "Work" },
            new ConnectionProfile { Name = "Home Mac", Group = "Home" }
        };
        var offline = new ConnectionProfile { Name = "Offline Windows PC", Host = "127.0.0.1", Group = "Work" };
        var sessions = new List<SessionView>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        double width = window.Width, height = window.Height;
        var theme = App.Theme.Current;
        var sidebar = (ScrollViewer)window.FindName("SidebarScroll");
        Button Entry(ConnectionProfile profile)
        {
            ((Expander)window.FindName(profile.Group + "GroupNav")).IsExpanded = true;
            var list = (ItemsControl)window.FindName(profile.Group + "SessionList");
            window.UpdateLayout();
            var presenter = (ContentPresenter)list.ItemContainerGenerator.ContainerFromItem(profile);
            return (Button)list.ItemTemplate.FindName("ResumeSessionButton", presenter);
        }
        try
        {
            window.Profiles.Add(offline);
            for (int index = 0; index < profiles.Length; index++)
            {
                var profile = profiles[index];
                profile.Kind = ConnectionKind.Vnc; profile.Host = "127.0.0.1"; profile.Port = peers[index].Port;
                profile.UserName = "fixture-user"; profile.VncTrustedNetwork = true;
                window.Profiles.Add(profile);
                bool gated = index == 0;
                window.OpenConnection(profile, null, async viewer =>
                {
                    starts++;
                    if (gated) await gate.Task;
                    await viewer.StartAsync(profile, _ => Task.FromResult<VncCredentials?>(new("fixture-user", "testpass")));
                });
                var session = window.ActiveSession ?? throw new InvalidOperationException("Navigation fixture session is missing.");
                sessions.Add(session);
                if (gated)
                {
                    await Wait(() => starts == 1, peers[index]);
                    check(window.WorkSessions.SequenceEqual(new[] { profile }) && profile.State == SessionState.Connecting && !session.IsReady,
                        "An opening session appears once with its real Connecting status, excluding saved offline machines");
                    Entry(profile).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(starts == 1 && ReferenceEquals(window.ActiveSession, session), "Selecting a pending session does not start another connection");
                    gate.SetResult();
                }
                await Wait(() => session.IsReady || profile.State == SessionState.Failed, peers[index]);
                check(session.IsReady, "Each navigation fixture authenticates its own retained VNC session");
            }
            check(window.WorkSessions.SequenceEqual(profiles.Take(2)) && window.HomeSessions.SequenceEqual(profiles.Skip(2)) &&
                window.CloudSessions.Count == 0 && !window.WorkSessions.Contains(offline),
                "Work and Home list only their active machines; Cloud is empty and offline profiles stay out");
            var viewers = sessions.Select(session => session.VncControl).ToArray();
            var signIns = profiles.Select(profile => profile.LastConnected).ToArray();
            Entry(profiles[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.SetFitToWindow(sessions[0], true);
            Entry(profiles[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(ReferenceEquals(window.ActiveSession, sessions[2]) && !sessions[2].IsFitToWindow && sessions[0].IsFitToWindow &&
                profiles[2].IsCurrentSession && profiles.Count(profile => profile.IsCurrentSession) == 1,
                "One click switches groups, highlights only the displayed machine, and preserves each session's layout");
            Entry(profiles[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(180);
            check(sessions[0].IsFitToWindow && ((Button)window.FindName("ExitFitViewButton")).IsVisible,
                "Switching back restores the retained fit view without a reconnect");
            await CheckViewport(window, viewers[0]!);
            await CheckWheel(viewers[0]!, peers[0], check);
            var expander = (Expander)window.FindName("WorkGroupNav");
            var automation = new System.Windows.Automation.Peers.ExpanderAutomationPeer(expander);
            var provider = (System.Windows.Automation.Provider.IExpandCollapseProvider)automation.GetPattern(System.Windows.Automation.Peers.PatternInterface.ExpandCollapse);
            provider.Collapse(); provider.Expand();
            check(expander.IsExpanded && ReferenceEquals(window.ActiveSession, sessions[0]),
                "Accessible group expansion and collapse leave the current remote desktop untouched");
            var workList = (ItemsControl)window.FindName("WorkSessionList");
            window.UpdateLayout();
            var viewAll = (Button)workList.Template.FindName("ViewAllButton", workList);
            check(viewAll.Content is TextBlock label && label.Text == "View all Work connections",
                "The group library link clearly names the saved connections it opens");
            viewAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var library = (ConnectionsView)((ContentControl)window.FindName("PageContent")).Content;
            check(((DataGrid)library.FindName("ConnectionsGrid")).Items.Count == 3 && profiles.All(profile => !profile.IsCurrentSession) &&
                sessions.All(session => session.IsReady), "View all opens the group's full library without ending any session or showing a stale selection");
            Entry(profiles[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(180);
            check(starts == 3 && sessions.Select(session => session.VncControl).SequenceEqual(viewers) &&
                profiles.Select(profile => profile.LastConnected).SequenceEqual(signIns) && peers.All(peer => !peer.Completion.IsCompleted),
                "Switching among three connected desktops reuses every viewer, authentication and socket");
            await viewers[1]!.TestInputAsync();
            await Wait(() => peers[1].SawA && peers[1].SawLeftClick, peers[1]);
            check(ReferenceEquals(window.ActiveSession, sessions[1]), "The selected retained desktop still receives keyboard and pointer input");
            ((Expander)window.FindName("CloudGroupNav")).IsExpanded = true;
            foreach (bool dark in new[] { false, true })
            {
                App.Theme.Apply(RemoteHub.Themes.ThemePalette.Create(dark, RemoteHub.Themes.ThemePalette.Parse("#0078D4")));
                await Task.Delay(100); sidebar.ScrollToEnd();
                NativeSelfTest.Snapshot(window, dark ? "native-session-groups-dark" : "native-session-groups-light");
            }
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            await Task.Delay(100); sidebar.ScrollToEnd(); await Task.Delay(100);
            var cloud = (Expander)window.FindName("CloudGroupNav");
            var footer = (Button)window.FindName("SessionPeek");
            check(sidebar.ScrollableHeight > 0 && cloud.TransformToAncestor(window).Transform(new Point(0, cloud.ActualHeight)).Y <=
                footer.TransformToAncestor(window).Transform(new Point()).Y,
                "Expanded groups remain scrollable above fixed session and Settings controls on compact windows");
            NativeSelfTest.Snapshot(window, "native-session-groups-compact");
            var staleEntry = Entry(profiles[2]);
            sessions[2].Disconnect(); await peers[2].Completion.WaitAsync(TimeSpan.FromSeconds(5)); window.UpdateLayout();
            var homeList = (ItemsControl)window.FindName("HomeSessionList");
            check(window.HomeSessions.Count == 0 && ((TextBlock)homeList.Template.FindName("EmptySessions", homeList)).IsVisible,
                "A disconnected machine disappears immediately and its group shows the empty state");
            staleEntry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(starts == 3 && !sessions[2].IsLive && ((TextBlock)window.FindName("StatusText")).Text.Contains("session has ended"),
                "An obsolete session entry reports that it ended instead of silently reconnecting");
        }
        finally
        {
            foreach (var session in sessions) session.Disconnect();
            gate.TrySetCanceled();
            foreach (var peer in peers) await peer.DisposeAsync();
            foreach (var profile in profiles) window.Profiles.Remove(profile);
            window.Profiles.Remove(offline);
            window.Width = width; window.Height = height; App.Theme.Apply(theme);
            sidebar.ScrollToTop(); window.ShowHome();
        }
    }
    private static async Task CheckWheel(VncDesktopView control, LoopbackPeer peer, Action<bool, string> check)
    {
        await control.TestInputAsync();
        int barrier = peer.WheelBarriers;
        await control.TestWheelAsync([]);
        await Wait(() => peer.WheelBarriers > barrier, peer);
        int centerX = peer.LastX, centerY = peer.LastY;
        check(centerX is >= 15 and <= 16 && centerY is >= 11 and <= 12,
            "The viewport center maps to the center framebuffer pixel, allowing subpixel scaling truncation");
        Task Expect(object[] events, byte[] masks, string description) => ExpectWheel(control, peer, check, events, masks, description);
        static byte[] Steps(byte mask, int count, byte held = 0) => WheelSteps(mask, count, held);
        await Expect([new { deltaY = 300 }], Steps(16, 6), "A fast vertical wheel event preserves all six VNC scroll steps, not just one");
        await Expect([new { deltaY = -300 }], Steps(8, 6), "Upward scrolling preserves distance and direction");
        await Expect([new { deltaY = 49 }], [], "Fine wheel input below one step waits rather than jumping");
        await Expect([new { deltaY = 1 }], Steps(16, 1), "Fine wheel input accumulates to one step");
        await Expect([new { deltaY = 120 }, new { deltaY = 30 }], Steps(16, 3), "Wheel remainder is retained across separate events");
        await Expect([new { deltaY = -120 }, new { deltaY = -30 }], Steps(8, 3), "Negative fractional distance is retained without reversing direction");
        await Expect([new { deltaY = 40 }, new { deltaY = -40 }], [], "Opposite fine movements cancel without a spurious scroll");
        await Expect([new { deltaX = -300 }], Steps(32, 6), "Horizontal scrolling preserves all leftward steps");
        await Expect([new { deltaX = 100 }], Steps(64, 2), "Horizontal scrolling preserves rightward steps");
        await Expect([new { deltaMode = 1, deltaY = 3 }, new { deltaY = 43 }], Steps(16, 2), "Line-mode wheel input retains the converted fractional remainder");
        await Expect([new { deltaY = 100, buttons = 1 }], Steps(16, 2, 1), "Wheel pulses preserve an already-held mouse button");
        await Expect([new { deltaY = -100, deltaX = 100, buttons = 2 }],
            Steps(64, 2, 4).Concat(Steps(8, 2, 4)).ToArray(), "Diagonal wheel pulses preserve both axes and the held right button");
        using var layout = JsonDocument.Parse(await control.LayoutAsync());
        int pageWidth = (int)layout.RootElement.GetProperty("viewportWidth").GetDouble();
        int pageHeight = (int)layout.RootElement.GetProperty("viewportHeight").GetDouble();
        await Expect([new { deltaMode = 2, deltaX = 1, deltaY = 1 },
            new { deltaX = (50 - pageWidth % 50) % 50, deltaY = (50 - pageHeight % 50) % 50 }],
            Steps(64, pageWidth / 50).Concat(Steps(16, pageHeight / 50))
                .Concat(Steps(64, pageWidth % 50 == 0 ? 0 : 1)).Concat(Steps(16, pageHeight % 50 == 0 ? 0 : 1)).ToArray(),
            "Page-mode wheel units use the current viewport dimensions and preserve remainders");
        await Expect([new { deltaY = 0, deltaX = 0 }], [], "Zero wheel input does not produce remote scroll events");
        await Expect([new { deltaY = 51200 }], Steps(16, 1024), "The maximum supported wheel burst emits exactly 1024 steps at scaled coordinates");
    }
    private static byte[] WheelSteps(byte mask, int count, byte held = 0) =>
        Enumerable.Range(0, count).SelectMany(_ => new[] { (byte)(mask | held), held }).ToArray();
    private static async Task ExpectWheel(VncDesktopView control, LoopbackPeer peer, Action<bool, string> check,
        object[] events, byte[] masks, string description)
    {
        int offset = peer.PointerMessages.Count, marker = peer.WheelBarriers, centerX = peer.LastX, centerY = peer.LastY;
        await control.TestWheelAsync(events);
        await Wait(() => peer.WheelBarriers > marker, peer);
        var pointers = peer.PointerMessages.ToArray().Skip(offset).ToArray();
        byte[] actual = pointers.Select(pointer => pointer.Mask).ToArray();
        bool correct = actual.SequenceEqual(masks) && pointers.All(pointer => pointer.X == centerX && pointer.Y == centerY);
        check(correct, description + (correct ? "" :
            $" (expected {string.Join(',', masks)} at {centerX},{centerY}; received {string.Join(';', pointers)})"));
    }
    private static async Task CheckScrollSpeed(SessionView session, LoopbackPeer peer, Action<bool, string> check)
    {
        var control = session.VncControl ?? throw new InvalidOperationException("VNC scroll fixture is missing.");
        var selector = (ComboBox)session.FindName("VncScrollSpeed");
        check(selector.IsEnabled && (int)selector.SelectedItem == 1, "Connected VNC exposes its original scroll speed");
        foreach (int speed in ConnectionProfile.VncScrollSpeeds)
        {
            selector.SelectedItem = speed;
            check(session.Profile.VncScrollSpeed == speed && ReferenceEquals(session.VncControl, control),
                $"Selecting {speed}x updates only this connection without replacing the viewer");
            await ExpectWheel(control, peer, check, [new { deltaY = 50 }, new { deltaY = -50 },
                new { deltaX = 50, buttons = 1 }, new { deltaX = -50, buttons = 1 }],
                WheelSteps(16, speed).Concat(WheelSteps(8, speed)).Concat(WheelSteps(64, speed, 1)).Concat(WheelSteps(32, speed, 1)).ToArray(),
                $"{speed}x produces exactly {speed} times the wheel pulses on both axes, preserving direction and held buttons");
            await ExpectWheel(control, peer, check, [new { deltaY = 25 }, new { deltaY = 25 }], WheelSteps(16, speed),
                $"{speed}x retains fine wheel movement between events");
        }
        selector.SelectedItem = 3;
        await ExpectWheel(control, peer, check, [new { deltaMode = 1, deltaY = 3 }, new { deltaY = 43 }], WheelSteps(16, 6),
            "3x scales line and pixel units consistently while retaining fractional distance");
        await ExpectWheel(control, peer, check, [new { deltaY = 10 }], [], "Fine input stays pending until a full accelerated step");
        selector.SelectedItem = 1;
        await ExpectWheel(control, peer, check, [new { deltaY = 20 }], [], "Changing speed clears the old fractional remainder without jumping");
        await ExpectWheel(control, peer, check, [new { deltaY = 30 }], WheelSteps(16, 1), "Returning to 1x restores the original wheel distance");
    }
    private static async Task Wait(Func<bool> predicate, LoopbackPeer peer)
    {
        for (int i = 0; i < 600; i++)
        {
            if (peer.Error is { } error) throw new InvalidOperationException("Local VNC fixture failed.", error);
            if (predicate()) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("The local VNC check did not reach its expected state.");
    }
    private sealed class LoopbackPeer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _closed = new(TimeSpan.FromSeconds(90));
        private readonly byte _security;
        private readonly bool _reject;
        private readonly int _width, _height, _frames;
        private int _framesSent;
        private readonly System.Diagnostics.Stopwatch _frameClock = new();
        public System.Collections.Concurrent.ConcurrentQueue<double> FrameTimes { get; } = new();
        private TcpClient? _peer;
        private byte _red = 16, _green = 8, _blue;
        public int Port { get; }
        public Task Completion { get; }
        public Exception? Error { get; private set; }
        public bool Authenticated { get; private set; }
        public bool FrameSent { get; private set; }
        public int KeyEvents { get; private set; }
        public int PointerEvents { get; private set; }
        public int WheelBarriers { get; private set; }
        public System.Collections.Concurrent.ConcurrentQueue<(byte Mask, int X, int Y)> PointerMessages { get; } = new();
        public bool SawA { get; private set; }
        public bool SawCommand { get; private set; }
        public bool SawLeftClick { get; private set; }
        public int LastX { get; private set; }
        public int LastY { get; private set; }
        public LoopbackPeer(byte security, bool reject = false, int width = 32, int height = 24, int frames = 1)
        {
            _width = width; _height = height; _frames = frames;
            _security = security; _reject = reject; _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; Completion = Run();
        }
        private async Task<byte[]> Read(NetworkStream stream, int count)
        {
            byte[] bytes = new byte[count]; await stream.ReadExactlyAsync(bytes, _closed.Token); return bytes;
        }
        private async Task Run()
        {
            try
            {
                _peer = await _listener.AcceptTcpClientAsync(_closed.Token);
                _peer.NoDelay = true;
                using var stream = _peer.GetStream();
                if (_security == 0) return;
                byte[] greeting = Encoding.ASCII.GetBytes(_security == 30 ? "RFB 003.889\n" : "RFB 003.008\n");
                await stream.WriteAsync(greeting.AsMemory(0, 5), _closed.Token);
                await Task.Delay(5);
                await stream.WriteAsync(greeting.AsMemory(5), _closed.Token);
                if (Encoding.ASCII.GetString(await Read(stream, 12)) != "RFB 003.008\n") throw new InvalidDataException("Unexpected RFB version.");
                await stream.WriteAsync(new byte[] { 1, _security }, _closed.Token);
                if (_security == 1) { await Read(stream, 1); throw new InvalidDataException("Passwordless security selection should have been blocked."); }
                if ((await Read(stream, 1))[0] != _security) throw new InvalidDataException("Unexpected authentication type.");
                if (_security == 30)
                {
                    byte[] prime = Convert.FromHexString("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFF61");
                    var modulus = new BigInteger(prime, true, true);
                    var secret = new BigInteger(12345678912345);
                    byte[] publicKey = Fixed(BigInteger.ModPow(5, secret, modulus), 16);
                    byte[] parameters = new byte[] { 0, 5, 0, 16 }.Concat(prime).Concat(publicKey).ToArray();
                    await stream.WriteAsync(parameters.AsMemory(0, 20), _closed.Token);
                    await Task.Delay(50);
                    await stream.WriteAsync(parameters.AsMemory(20), _closed.Token);
                    byte[] response = await Read(stream, 144);
                    var clientKey = new BigInteger(response.AsSpan(128, 16), true, true);
                    byte[] shared = Fixed(BigInteger.ModPow(clientKey, secret, modulus), 16);
                    using var aes = Aes.Create(); aes.Key = MD5.HashData(shared);
                    byte[] credentials = aes.DecryptEcb(response.AsSpan(0, 128), PaddingMode.None);
                    Authenticated = Text(credentials.AsSpan(0, 64)) == "fixture-user" && Text(credentials.AsSpan(64, 64)) == "testpass";
                    CryptographicOperations.ZeroMemory(credentials); CryptographicOperations.ZeroMemory(shared);
                }
                else
                {
                    byte[] challenge = RandomNumberGenerator.GetBytes(16);
                    await stream.WriteAsync(challenge, _closed.Token);
                    using var des = DES.Create();
                    des.Key = Encoding.ASCII.GetBytes("testpass").Select(Reverse).ToArray();
                    Authenticated = (await Read(stream, 16)).SequenceEqual(des.EncryptEcb(challenge, PaddingMode.None));
                }
                if (_reject)
                {
                    if (Authenticated) throw new InvalidDataException("The incorrect test password unexpectedly authenticated.");
                    await stream.WriteAsync(new byte[] { 0, 0, 0, 1, 0, 0, 0, 6, 100, 101, 110, 105, 101, 100 }, _closed.Token);
                    await Read(stream, 1); return;
                }
                if (!Authenticated) throw new InvalidDataException("VNC authentication response did not match the fixture.");
                await stream.WriteAsync(new byte[4], _closed.Token);
                if ((await Read(stream, 1))[0] != 1) throw new InvalidDataException("VNC must use shared mode.");
                byte[] name = Encoding.ASCII.GetBytes("Local protocol fixture - not a Mac");
                using var init = new MemoryStream();
                byte[] serverInit = [0, 0, 0, 0, 32, 24, 0, 1, 0, 255, 0, 255, 0, 255, 16, 8, 0, 0, 0, 0];
                BinaryPrimitives.WriteUInt16BigEndian(serverInit, (ushort)_width);
                BinaryPrimitives.WriteUInt16BigEndian(serverInit.AsSpan(2), (ushort)_height);
                init.Write(serverInit);
                byte[] length = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)name.Length); init.Write(length); init.Write(name);
                await stream.WriteAsync(init.ToArray(), _closed.Token);
                while (!_closed.IsCancellationRequested)
                {
                    byte type = (await Read(stream, 1))[0];
                    switch (type)
                    {
                        case 0:
                            byte[] format = await Read(stream, 19);
                            if (format[3] != 32 || format[5] != 0) throw new InvalidDataException("Unexpected test pixel format.");
                            _red = format[13]; _green = format[14]; _blue = format[15]; break;
                        case 2:
                            byte[] encodings = await Read(stream, 3);
                            await Read(stream, BinaryPrimitives.ReadUInt16BigEndian(encodings.AsSpan(1)) * 4); break;
                        case 3:
                            await Read(stream, 9);
                            if (_frameClock.IsRunning) { FrameTimes.Enqueue(_frameClock.Elapsed.TotalMilliseconds); _frameClock.Reset(); }
                            if (_framesSent < _frames)
                            {
                                byte[] frame = new byte[16 + _width * _height * 4];
                                frame[3] = 1;
                                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(8), (ushort)_width);
                                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(10), (ushort)_height);
                                uint color = (230u << _red) | (32u << _green) | (50u << _blue);
                                for (int i = 16; i < frame.Length; i += 4)
                                {
                                    uint pixel = _frames == 1 ? color : ((uint)(((i - 16) / 4 + _framesSent) % 251) << _red) | (32u << _green) | (50u << _blue);
                                    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(i), pixel);
                                }
                                if (_security == 2 && _frames == 1) frame = PngFrame(frame);
                                _framesSent++; _frameClock.Restart();
                                await stream.WriteAsync(frame, _closed.Token); FrameSent = true;
                            }
                            break;
                        case 4:
                            byte[] key = await Read(stream, 7); KeyEvents++;
                            uint symbol = BinaryPrimitives.ReadUInt32BigEndian(key.AsSpan(3));
                            SawA |= symbol == 0x61; SawCommand |= symbol == 0xffe7;
                            if (symbol == 0x62 && key[0] == 0) WheelBarriers++;
                            break;
                        case 5:
                            byte[] pointer = await Read(stream, 5); PointerEvents++;
                            SawLeftClick |= (pointer[0] & 1) != 0; LastX = BinaryPrimitives.ReadUInt16BigEndian(pointer.AsSpan(1));
                            LastY = BinaryPrimitives.ReadUInt16BigEndian(pointer.AsSpan(3));
                            PointerMessages.Enqueue((pointer[0], LastX, LastY)); break;
                        default: throw new InvalidDataException("Unexpected VNC client message: " + type);
                    }
                }
            }
            catch (EndOfStreamException) { }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
            catch (IOException) when (_closed.IsCancellationRequested) { }
            catch (Exception error) { Error = error; }
        }
        private static byte[] PngFrame(byte[] raw)
        {
            byte[] pixels = new byte[32 * 24 * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 50; pixels[i + 1] = 32; pixels[i + 2] = 230; pixels[i + 3] = 255; }
            var source = System.Windows.Media.Imaging.BitmapSource.Create(32, 24, 96, 96, PixelFormats.Bgra32, null, pixels, 128);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            using var image = new MemoryStream(); encoder.Save(image);
            byte[] png = image.ToArray(), header = raw[..16];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(12), -260);
            using var frame = new MemoryStream(); frame.Write(header); frame.WriteByte(0xa0);
            int length = png.Length;
            frame.WriteByte((byte)((length & 127) | (length > 127 ? 128 : 0)));
            if (length > 127) frame.WriteByte((byte)(length >> 7));
            frame.Write(png); return frame.ToArray();
        }
        private static byte[] Fixed(BigInteger value, int size)
        {
            byte[] result = new byte[size], bytes = value.ToByteArray(true, true);
            bytes.CopyTo(result, size - bytes.Length); return result;
        }
        private static string Text(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes[..bytes.IndexOf((byte)0)]);
        private static byte Reverse(byte value)
        {
            int result = 0;
            for (int bit = 0; bit < 8; bit++) { result = (result << 1) | (value & 1); value >>= 1; }
            return (byte)result;
        }
        public async ValueTask DisposeAsync()
        {
            _closed.Cancel(); _peer?.Dispose(); _listener.Stop(); await Completion; _closed.Dispose();
        }
    }
}
