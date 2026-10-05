using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RemoteHub.Models;
using RemoteHub.Services;
using RemoteHub.Ssh;

namespace RemoteHub.Views;

public sealed class SshTerminalView : UserControl, IDisposable
{
    private const string HostName = "terminal.remote.invalid";
    private const string PageUrl = "https://terminal.remote.invalid/index.html";
    private readonly WebView2 _web = new();
    private readonly CancellationTokenSource _closed = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Guid _profileId;
    private PseudoConsole? _process;
    private TaskCompletionSource? _ack;
    private long _sequence;
    private int _columns = 80, _rows = 24;
    private bool _disposed;
    public bool IsReady { get; private set; }
    public event Action<int, string?>? Ended;
    public event Action? Started;
    public Task Completion { get; private set; } = Task.CompletedTask;

    public SshTerminalView(Guid profileId)
    {
        _profileId = profileId;
        Content = _web;
        SetResourceReference(BackgroundProperty, "TerminalBackground");
        ApplyTheme();
        App.Theme.Changed += ThemeChanged;
    }
    public Task StartAsync(ConnectionProfile profile)
    {
        if (!File.Exists(SshCommand.Executable)) throw new FileNotFoundException("Install the Windows OpenSSH Client optional feature to use SSH.");
        if (profile.SshKeyPath.Length > 0 && !File.Exists(profile.SshKeyPath))
            throw new FileNotFoundException("The selected SSH private-key file is unavailable. Edit the connection and choose an existing key.");
        return StartProcessAsync(SshCommand.Executable, SshCommand.Arguments(profile));
    }
    internal async Task StartProcessAsync(string executable, IReadOnlyList<string> arguments)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TerminalAssets");
        foreach (string file in new[] { "index.html", "terminal.js", "terminal.css", "xterm.js", "xterm.css", "addon-fit.js" })
            if (!File.Exists(Path.Combine(directory, file))) throw new FileNotFoundException("A required local terminal asset is missing. Restore the complete published app folder.");
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteWorkspace", "TerminalWebView");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: cache);
        _closed.Token.ThrowIfCancellationRequested();
        await _web.EnsureCoreWebView2Async(environment);
        _closed.Token.ThrowIfCancellationRequested();
        var core = _web.CoreWebView2;
        LocalWebContent.Configure(core, environment, HostName, directory, PageUrl,
            ["/index.html", "/terminal.js", "/terminal.css", "/xterm.js", "/xterm.css", "/addon-fit.js"],
            () => !_ready.Task.IsCompleted, () => AppLog.Write("ssh-navigation-blocked", _profileId));
        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess) _ready.TrySetException(new InvalidOperationException("The local terminal page could not load: " + e.WebErrorStatus));
        };
        core.ProcessFailed += (_, _) =>
        {
            IsReady = false;
            _ready.TrySetException(new InvalidOperationException("The terminal renderer stopped. Reconnect to open a new terminal."));
            Fail("The terminal renderer stopped. Reconnect to open a new terminal.");
        };
        core.WebMessageReceived += MessageReceived;
        ApplyTheme();
        core.Navigate(PageUrl);
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), _closed.Token);
        ApplyTheme();
        _process = PseudoConsole.Start(executable, arguments, _columns, _rows);
        IsReady = true;
        Post(new { type = "opened" });
        Started?.Invoke();
        Completion = RunSession(_process);
        FocusTerminal();
    }
    private async Task RunSession(PseudoConsole process)
    {
        try
        {
            var decoder = Encoding.UTF8.GetDecoder();
            var characters = new char[8194];
            var output = Consume();
            int code = await process.ExitCode;
            await process.FinishAsync();
            await output;
            if (!_closed.IsCancellationRequested)
            {
                Post(new { type = "closed" });
                Ended?.Invoke(code, null);
            }
            async Task Consume()
            {
                try
                {
                    await foreach (var bytes in process.Output.ReadAllAsync(_closed.Token))
                    {
                        int count = decoder.GetChars(bytes, 0, bytes.Length, characters, 0, false);
                        if (count > 0) await WriteOutput(new string(characters, 0, count));
                    }
                    int remaining = decoder.GetChars([], 0, 0, characters, 0, true);
                    if (remaining > 0) await WriteOutput(new string(characters, 0, remaining));
                }
                catch (Exception error) when (error is IOException or COMException or InvalidOperationException)
                {
                    Fail("The terminal output could not be displayed. " + error.Message);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or COMException or InvalidOperationException or Win32Exception)
        { Fail("The SSH terminal stopped. " + error.Message); }
    }
    private async Task WriteOutput(string text)
    {
        _ack = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(new { type = "output", id = ++_sequence, data = text });
        await _ack.Task.WaitAsync(_closed.Token);
        _ack = null;
    }
    private async void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed || _closed.IsCancellationRequested || e.Source != PageUrl) return;
        try
        {
            string json = e.WebMessageAsJson;
            if (json.Length > 7 * 1024 * 1024) throw new InvalidDataException("The terminal input limit was exceeded.");
            using var message = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = message.RootElement;
            string? type = root.GetProperty("type").GetString();
            switch (type)
            {
                case "ready":
                case "resize":
                    _columns = root.GetProperty("columns").GetInt32(); _rows = root.GetProperty("rows").GetInt32();
                    if (_columns is < 2 or > 1000 || _rows is < 1 or > 500) throw new InvalidDataException("The terminal dimensions are outside the supported range.");
                    _process?.Resize(_columns, _rows);
                    if (type == "ready") _ready.TrySetResult();
                    break;
                case "ack":
                    if (root.GetProperty("id").GetInt64() == _sequence) _ack?.TrySetResult();
                    break;
                case "input":
                    string data = root.GetProperty("data").GetString() ?? "";
                    if (data.Length > 1048576) throw new InvalidDataException("The terminal input limit was exceeded.");
                    if (_process is not { } process) throw new InvalidOperationException("The SSH process is not ready for input.");
                    await process.WriteAsync(data, _closed.Token);
                    if (!_disposed) Post(new { type = "input-ack" });
                    break;
                case "copy": await CopySelectionAsync(); break;
                case "paste-request": await PasteClipboardAsync(); break;
                case "paste": await PasteAsync(root.GetProperty("data").GetString() ?? ""); break;
                case "error": Fail("The terminal input buffer is full. Reconnect before sending more input."); break;
                default: throw new InvalidDataException("The terminal sent an unsupported message.");
            }
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception error) when (error is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or IOException or COMException or Win32Exception or FormatException)
        {
            if (!_closed.IsCancellationRequested) Fail("Terminal input failed. " + error.Message);
        }
    }
    private void Post(object value)
    {
        if (!_disposed) _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value));
    }
    private void ThemeChanged(object? sender, EventArgs e)
    {
        if (_disposed || _closed.IsCancellationRequested) return;
        try { ApplyTheme(); }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        { Fail("The terminal appearance could not be updated. " + error.Message); }
    }
    private void ApplyTheme()
    {
        var palette = App.Theme.Current;
        Color background = palette.Colors["TerminalBackground"];
        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(background.R, background.G, background.B);
        if (_web.CoreWebView2 is not { } core) return;
        core.Profile.PreferredColorScheme = palette.IsDark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        if (_ready.Task.IsCompletedSuccessfully)
            Post(new { type = "theme", colors = palette.Terminal, dark = palette.IsDark });
    }
    public void FocusTerminal()
    {
        if (!IsReady || _disposed) return;
        _web.Focus();
        Post(new { type = "focus" });
    }
    public async Task CopySelectionAsync()
    {
        if (!IsReady || _disposed) return;
        try
        {
            string? selection = JsonSerializer.Deserialize<string>(await _web.ExecuteScriptAsync("window.terminalSelection()"));
            if (!string.IsNullOrEmpty(selection)) Clipboard.SetText(selection);
        }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or OperationCanceledException)
        { if (!_disposed) MessageBox.Show(Window.GetWindow(this), "Copy failed. Select terminal text and try again.", "SSH clipboard"); }
    }
    public async Task PasteClipboardAsync()
    {
        if (!IsReady || _disposed) return;
        try { if (Clipboard.ContainsText()) await PasteAsync(Clipboard.GetText()); }
        catch (Exception error) when (error is ExternalException or InvalidOperationException or OperationCanceledException)
        { if (!_disposed) MessageBox.Show(Window.GetWindow(this), "Paste failed. Check that the terminal is still open and try again.", "SSH clipboard"); }
    }
    private async Task PasteAsync(string text)
    {
        if (!IsReady || _disposed || _process?.ExitCode.IsCompleted != false) return;
        if (text.Length > 65536) { MessageBox.Show(Window.GetWindow(this), "Paste at most 65,536 characters at a time.", "SSH paste"); return; }
        if (text.IndexOfAny(['\r', '\n']) >= 0 && MessageBox.Show(Window.GetWindow(this),
            "This paste contains multiple lines. A shell can execute them immediately.\n\nPaste into this SSH session?",
            "Confirm multiline paste", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await _web.ExecuteScriptAsync($"window.terminalPaste({JsonSerializer.Serialize(text)})");
        FocusTerminal();
    }
    private void Fail(string message)
    {
        if (_closed.IsCancellationRequested) return;
        AppLog.Write("ssh-terminal-error", _profileId, new { message });
        try { if (IsReady) Post(new { type = "closed" }); }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        { AppLog.Write("ssh-renderer-unavailable", _profileId, new { type = error.GetType().Name }); }
        _closed.Cancel();
        Ended?.Invoke(-1, message);
        _ = StopProcess();
    }
    private async Task StopProcess()
    {
        if (_process is null) return;
        try { await _process.DisposeAsync(); }
        catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException)
        { AppLog.Write("ssh-cleanup-error", _profileId, new { error.Message }); }
    }
    internal Task<string> ReadTextAsync() => _web.ExecuteScriptAsync("window.terminalText()");
    internal Task<string> ReadThemeAsync() => _web.ExecuteScriptAsync("window.terminalTheme()");
    internal Task InjectTestInputAsync(string text) => _web.ExecuteScriptAsync($"window.terminalPaste({JsonSerializer.Serialize(text)})");
    internal Task CaptureAsync(Stream stream) => _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _closed.Cancel(); IsReady = false;
        App.Theme.Changed -= ThemeChanged;
        _ = StopProcess();
        _web.Dispose();
    }
}
