using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RemoteHub.Models;
using RemoteHub.Services;
using RemoteHub.Vnc;

namespace RemoteHub.Views;

public sealed class VncDesktopView : UserControl, IDisposable
{
    private const string Host = "vnc.remote.invalid", Page = "https://vnc.remote.invalid/index.html";
    private readonly WebView2 _web = new();
    private readonly CancellationTokenSource _closed = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _attached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _bufferReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private const int ReceiveSlotSize = 256 * 1024, ReceiveSlots = 4;
    private readonly SemaphoreSlim _receiveSlots = new(ReceiveSlots, ReceiveSlots);
    private CoreWebView2SharedBuffer? _receiveBuffer;
    private Stream? _receiveStream;
    private readonly VncHandshakeGuard _guard = new();
    private readonly Guid _profileId;
    private TcpClient? _client;
    private ConnectionProfile? _profile;
    private Func<bool, Task<VncCredentials?>>? _credentials;
    private long _sequence, _acknowledged;
    private bool _disposed, _ended, _writing, _prompting;
    private bool _refitQueued;
    public bool IsReady { get; private set; }
    internal bool IsClosed => _closed.IsCancellationRequested;
    public Task Completion { get; private set; } = Task.CompletedTask;
    internal int PeakReceiveSlots { get; private set; }
    public event Action? Started;
    public event Action<bool, string>? Ended;

    public VncDesktopView(Guid profileId)
    {
        _profileId = profileId; Content = _web;
        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(17, 24, 39);
        SizeChanged += (_, _) => RefitDesktop();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsReady) return;
            if (IsVisible) RefitDesktop(); else Post(new { type = "blur" });
        };
    }
    public Task StartAsync(ConnectionProfile profile) => StartAsync(profile, null);
    internal async Task StartAsync(ConnectionProfile profile, Func<bool, Task<VncCredentials?>>? credentials)
    {
        if (profile.Kind != ConnectionKind.Vnc || profile.Validate() is { })
            throw new InvalidDataException("The VNC connection is invalid or its unencrypted-transport warning has not been acknowledged.");
        _profile = profile; _credentials = credentials;
        string directory = Path.Combine(AppContext.BaseDirectory, "VncAssets");
        foreach (string file in new[] { "index.html", "vnc.js", "vnc.css", @"novnc\core\rfb.js" })
            if (!File.Exists(Path.Combine(directory, file))) throw new FileNotFoundException("Local VNC assets are missing. Restore the complete app folder.");
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteWorkspace", "VncWebView");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: cache).WaitAsync(_closed.Token);
        _closed.Token.ThrowIfCancellationRequested();
        var controller = environment.CreateCoreWebView2ControllerOptions();
        controller.IsInPrivateModeEnabled = true;
        await _web.EnsureCoreWebView2Async(environment, controller).WaitAsync(_closed.Token);
        _closed.Token.ThrowIfCancellationRequested();
        var core = _web.CoreWebView2;
        var assets = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".js" or ".html" or ".css")
            .Select(path => "/" + Path.GetRelativePath(directory, path).Replace('\\', '/'));
        LocalWebContent.Configure(core, environment, Host, directory, Page, assets, () => !_ready.Task.IsCompleted,
            () => AppLog.Write("vnc-navigation-blocked", _profileId));
        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess) _ready.TrySetException(new InvalidOperationException("The local VNC page could not load: " + e.WebErrorStatus));
        };
        core.ProcessFailed += (_, _) =>
        {
            _ready.TrySetException(new InvalidOperationException("The VNC renderer stopped."));
            Finish(true, "The VNC renderer stopped. Reconnect to open a new viewer.");
        };
        core.WebMessageReceived += MessageReceived;
        core.Navigate(Page);
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), _closed.Token);
        _receiveBuffer = environment.CreateSharedBuffer(ReceiveSlotSize * ReceiveSlots);
        _receiveStream = _receiveBuffer.OpenStream();
        core.PostSharedBufferToScript(_receiveBuffer, CoreWebView2SharedBufferAccess.ReadOnly,
            JsonSerializer.Serialize(new { slotSize = ReceiveSlotSize, slots = ReceiveSlots }));
        await _bufferReady.Task.WaitAsync(TimeSpan.FromSeconds(15), _closed.Token);
        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(profile.Host, profile.Port, _closed.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(15), _closed.Token);
        Post(new { type = "start", viewOnly = profile.VncViewOnly, scrollSpeed = profile.VncScrollSpeed });
        await _attached.Task.WaitAsync(TimeSpan.FromSeconds(15), _closed.Token);
        Completion = ReadLoop(_client.GetStream());
    }
    private async Task ReadLoop(NetworkStream stream)
    {
        try
        {
            byte[] buffer = new byte[ReceiveSlotSize];
            while (!_closed.IsCancellationRequested)
            {
                if (!await _receiveSlots.WaitAsync(TimeSpan.FromSeconds(20), _closed.Token))
                    throw new TimeoutException("The VNC renderer stopped consuming screen data.");
                int count = await stream.ReadAsync(buffer, _closed.Token);
                if (count == 0) { Finish(!IsReady, "The VNC server closed the connection."); return; }
                // Coalesce only bytes already available; never delay keyboard or handshake traffic to fill a slot.
                while (count < buffer.Length && stream.DataAvailable)
                {
                    int extra = await stream.ReadAsync(buffer.AsMemory(count), _closed.Token);
                    if (extra == 0) break;
                    count += extra;
                }
                _closed.Token.ThrowIfCancellationRequested();
                int offset = (int)(_sequence % ReceiveSlots) * ReceiveSlotSize;
                var shared = _receiveStream ?? throw new InvalidOperationException("The VNC receive buffer is unavailable.");
                shared.Seek(offset, SeekOrigin.Begin); shared.Write(buffer, 0, count);
                PeakReceiveSlots = Math.Max(PeakReceiveSlots, (int)(_sequence + 1 - _acknowledged));
                Post(new { type = "data", id = ++_sequence, offset, count });
            }
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or SocketException or TimeoutException or COMException or InvalidOperationException or NotSupportedException)
        { if (!_closed.IsCancellationRequested) Finish(true, "The VNC connection stopped. Check the network and reconnect.", error); }
    }
    private async void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed || _closed.IsCancellationRequested || e.Source != Page) return;
        try
        {
            string json = e.WebMessageAsJson;
            if (json.Length > 100000) throw new InvalidDataException("VNC bridge message is too large.");
            using var message = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 6 });
            var root = message.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready": _ready.TrySetResult(); break;
                case "buffer-ready": _bufferReady.TrySetResult(); break;
                case "transport-ready": _attached.TrySetResult(); break;
                case "ack":
                    if (root.GetProperty("id").GetInt64() != _acknowledged + 1 || _acknowledged >= _sequence)
                        throw new InvalidDataException("Unexpected VNC receive acknowledgement.");
                    _acknowledged++; _receiveSlots.Release(); break;
                case "send":
                    if (_writing || _client is null) throw new InvalidDataException("VNC transport is not ready.");
                    byte[] bytes = Convert.FromBase64String(root.GetProperty("data").GetString() ?? "");
                    if (bytes.Length is 0 or > 65536) throw new InvalidDataException("Invalid VNC send size.");
                    _guard.ValidateOutgoing(bytes);
                    _writing = true;
                    try { await _client.GetStream().WriteAsync(bytes, _closed.Token); }
                    finally { _writing = false; Array.Clear(bytes); }
                    Post(new { type = "send-ack" });
                    break;
                case "credentials":
                    if (_prompting) throw new InvalidDataException("The VNC server repeated its sign-in request.");
                    var types = root.GetProperty("types").EnumerateArray().Select(value => value.GetString()).ToArray();
                    bool mac = _guard.SecurityType == 30;
                    if (_guard.SecurityType is not 2 and not 30 || !types.Contains("password") || types.Any(type => type is not "username" and not "password") ||
                        types.Contains("username") != mac)
                        throw new InvalidDataException("The server requested an unsupported authentication method.");
                    _prompting = true;
                    VncCredentials? credentials;
                    try
                    {
                        // Native dialogs must open after WebView2's event callback returns.
                        credentials = _credentials is null ? await Dispatcher.InvokeAsync(() => PromptCredentials(mac)) : await _credentials(mac);
                    }
                    finally { _prompting = false; }
                    if (_closed.IsCancellationRequested) return;
                    if (credentials is null) { Finish(false, "VNC sign-in was cancelled."); return; }
                    Post(new { type = "credentials", credentials = new { username = credentials.Username, password = credentials.Password } });
                    break;
                case "connected":
                    if (_guard.SecurityType is not 2 and not 30) throw new InvalidDataException("The VNC session did not authenticate.");
                    IsReady = true; Started?.Invoke(); FocusDesktop(); break;
                case "security-failure": Finish(true, "VNC sign-in was rejected. Check the Mac's Screen Sharing permissions and the requested password type."); break;
                case "disconnected": Finish(!root.GetProperty("clean").GetBoolean(), "The VNC session ended. Reconnect to try again."); break;
                case "closed": Finish(false, "VNC disconnected. The Mac remains signed in."); break;
                case "error": Finish(true, "The VNC renderer could not process the session. The connection has been closed."); break;
                default: throw new InvalidDataException("Unsupported VNC bridge message.");
            }
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception error) when (error is JsonException or InvalidDataException or FormatException or KeyNotFoundException or InvalidOperationException or IOException or SocketException or COMException)
        { if (!_closed.IsCancellationRequested) Finish(true, error is InvalidDataException ? error.Message : "VNC communication failed. Reconnect to try again.", error); }
    }
    private VncCredentials? PromptCredentials(bool mac)
    {
        _closed.Token.ThrowIfCancellationRequested();
        var profile = _profile ?? throw new InvalidOperationException("VNC profile is unavailable.");
        var dialog = new VncCredentialsWindow(Window.GetWindow(this), profile.Endpoint, profile.UserName, mac);
        using var registration = _closed.Token.Register(() => Dispatcher.BeginInvoke(new Action(() => { if (dialog.IsVisible) dialog.Close(); })));
        return dialog.ShowDialog() == true ? dialog.Credentials : null;
    }
    private void Post(object value)
    {
        if (!_disposed && !_closed.IsCancellationRequested) _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value));
    }
    public void FocusDesktop() { if (IsReady && !_disposed) { RefitDesktop(); _web.Focus(); Post(new { type = "focus" }); } }
    private void RefitDesktop()
    {
        if (_refitQueued || !IsReady || _disposed || !IsVisible) return;
        _refitQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _refitQueued = false;
            if (IsReady && !_disposed && IsVisible) Post(new { type = "fit" });
        }));
    }
    public void SendMacShortcut(string action)
    {
        if (action is not ("copy" or "paste" or "spotlight" or "switch")) throw new ArgumentOutOfRangeException(nameof(action));
        if (!IsReady || _profile?.VncViewOnly == true) return;
        Post(new { type = "shortcut", action }); FocusDesktop();
    }
    public void SetScrollSpeed(int speed)
    {
        if (speed is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(speed));
        if (!IsReady || _profile?.VncViewOnly == true) return;
        try { Post(new { type = "scroll-speed", value = speed }); }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        { Finish(true, "The VNC scroll speed could not be applied. Reconnect to use the saved setting.", error); }
    }
    private void Finish(bool failed, string message, Exception? error = null)
    {
        if (_ended || _disposed) return;
        _ended = true; IsReady = false;
        AppLog.Write(failed ? "vnc-session-failed" : "vnc-session-ended", _profileId, new { type = error?.GetType().Name });
        _closed.Cancel(); _client?.Dispose();
        Ended?.Invoke(failed, message);
    }
    internal Task<string> SnapshotAsync() => _web.ExecuteScriptAsync("window.vncSnapshot()");
    internal Task<string> LayoutAsync() => _web.ExecuteScriptAsync("window.vncLayout()");
    internal Task<string> ValidateFrameAsync() => _web.ExecuteScriptAsync("window.vncValidatePattern(4)");
    internal Task TestInvalidAckAsync() => _web.ExecuteScriptAsync("window.chrome.webview.postMessage({type:'ack',id:0})");
    internal object TransportState => new { sent = _sequence, acknowledged = _acknowledged, security = _guard.SecurityType,
        ready = _ready.Task.IsCompletedSuccessfully, bufferReady = _bufferReady.Task.IsCompletedSuccessfully,
        attached = _attached.Task.IsCompletedSuccessfully, stream = _receiveStream?.GetType().FullName };
    internal Task TestInputAsync() => _web.ExecuteScriptAsync("window.vncTestKey();window.vncTestPointer()");
    internal Task TestWheelAsync(object[] events) => _web.ExecuteScriptAsync("window.vncTestWheel(" + JsonSerializer.Serialize(events) + ")");
    internal Task CaptureAsync(Stream stream) => _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; IsReady = false; _closed.Cancel(); _client?.Dispose(); _web.Dispose();
        _receiveStream?.Dispose(); _receiveBuffer?.Dispose();
    }
}
