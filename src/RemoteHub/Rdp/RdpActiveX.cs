using System.Runtime.InteropServices;
using MSTSCLib;
using RemoteHub.Models;
using RemoteHub.Services;
using Forms = System.Windows.Forms;

namespace RemoteHub.Rdp;

public sealed record RdpNotification(string Kind, int Code = 0, string? Message = null);

public sealed class RdpActiveX : Forms.AxHost
{
    public const string ClientClassId = "A0C63C30-F08D-4AB4-907C-34905D770C7D";
    private IMsRdpClient10? _client;
    private IMsRdpClientNonScriptable5? _native;
    private ConnectionPointCookie? _cookie;
    private RdpEventSink? _sink;
    private Guid? _profileId;
    public bool BandwidthDetectionConfigured { get; private set; }
    public IMsRdpClient10 Client => _client ?? throw new InvalidOperationException("The Microsoft RDP control is not initialized.");
    public event EventHandler<RdpNotification>? Notification;
    public RdpActiveX() : base(ClientClassId) { Dock = Forms.DockStyle.Fill; }
    protected override void AttachInterfaces()
    {
        var control = ControlObject();
        _client = (IMsRdpClient10)control;
        _native = (IMsRdpClientNonScriptable5)control;
    }
    protected override void CreateSink()
    {
        _sink = new RdpEventSink(this);
        _cookie = new ConnectionPointCookie(ControlObject(), _sink, typeof(IMsTscAxEvents));
    }
    protected override void DetachSink()
    {
        _cookie?.Disconnect();
        _cookie = null;
        _sink = null;
    }
    internal void Emit(string kind, int code = 0, string? message = null) => Notification?.Invoke(this, new(kind, code, message));

    private object ControlObject() => GetOcx() ?? throw new InvalidOperationException("Microsoft RDP did not provide a control instance.");
    public void Configure(ConnectionProfile profile, int pixelWidth, int pixelHeight)
    {
        if (profile.IsPreview || profile.Kind != ConnectionKind.Rdp) throw new InvalidOperationException("Only real RDP profiles can use the Microsoft RDP control.");
        if (profile.Validate() is { } error) throw new ArgumentException(error);
        if (Client.Connected != 0) throw new InvalidOperationException("Disconnect before changing connection settings.");
        _profileId = profile.Id;
        Client.Server = profile.Host;
        Client.UserName = profile.UserName;
        Client.Domain = profile.Domain;
        Client.DesktopWidth = Math.Clamp(pixelWidth, 200, 8192);
        Client.DesktopHeight = Math.Clamp(pixelHeight, 200, 8192);
        Client.ColorDepth = 32;
        Client.AdvancedSettings.ContainerHandledFullScreen = 1;
        Client.SecuredSettings2.KeyboardHookMode = 0;
        var settings = Client.AdvancedSettings9;
        settings.RDPPort = profile.Port;
        settings.EnableCredSspSupport = true;
        settings.AuthenticationLevel = 2;
        settings.DisplayConnectionBar = false;
        settings.SmartSizing = true;
        settings.BandwidthDetection = true;
        BandwidthDetectionConfigured = true;
        settings.EnableAutoReconnect = true;
        settings.RedirectClipboard = profile.RedirectClipboard;
        settings.RedirectDrives = false;
        settings.RedirectPrinters = false;
        settings.RedirectPorts = false;
        settings.RedirectDevices = false;
        settings.RedirectSmartCards = profile.RedirectSmartCards;
        settings.AudioCaptureRedirectionMode = false;
        if (profile.LowBandwidth) settings.PerformanceFlags = 0x1 | 0x2 | 0x4 | 0x8;
        var native = _native ?? throw new InvalidOperationException("Native RDP settings are unavailable.");
        native.DisableConnectionBar = true;
        native.UseMultimon = false;
        native.EnableCredSspSupport = true;
        native.PromptForCredentials = true;
        native.PromptForCredsOnClient = true;
        native.AllowPromptingForCredentials = true;
        native.AllowCredentialSaving = profile.AllowWindowsCredentialSaving;
        object hardware = true;
        try { ((IMsRdpExtendedSettings)ControlObject()).set_Property("EnableHardwareMode", ref hardware); }
        catch (COMException exception)
        {
            AppLog.Write("hardware-decoding-option-unavailable", profile.Id, new { exception.HResult });
            Emit("Warning", exception.HResult, "The installed RDP component could not enable hardware-assisted decoding. Native decoding remains available.");
        }
        AppLog.Write("rdp-configured", profile.Id, new { engine = Client.Version, profile.Port, bandwidthDetection = BandwidthDetectionConfigured,
            profile.LowBandwidth, profile.RedirectSmartCards, width = Client.DesktopWidth, height = Client.DesktopHeight });
    }

    public void Connect() => Client.Connect();
    public void Disconnect() { if (Client.Connected != 0) Client.Disconnect(); }
    public void FocusRemote() { if (!IsDisposed && IsHandleCreated) Focus(); }
    public void ResizeDesktop(int width, int height, uint scale)
    {
        if (_client is null || _client.Connected != 1) return;
        width = Math.Clamp(width, 200, 8192);
        height = Math.Clamp(height, 200, 8192);
        try { _client.UpdateSessionDisplaySettings((uint)width, (uint)height, 0, 0, 0, Math.Clamp(scale, 100u, 500u), scale >= 180 ? 180u : scale >= 140 ? 140u : 100u); }
        catch (COMException exception)
        {
            AppLog.Write("display-resize-unavailable", _profileId, new { width, height, exception.HResult });
            Emit("Warning", exception.HResult, "This server could not change resolution. The existing desktop is being scaled to fit; the session was not reconnected.");
        }
    }
    public string DisconnectDescription(int reason)
    {
        if (_client is null) return $"Connection ended (code {reason}).";
        try { return _client.GetErrorDescription((uint)reason, (uint)_client.ExtendedDisconnectReason); }
        catch (COMException exception)
        {
            AppLog.Write("disconnect-description-unavailable", _profileId, new { reason, exception.HResult });
            return $"Connection ended (code {reason}). Check the remote address, network, and Remote Desktop permissions.";
        }
    }
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RdpEventSink(RdpActiveX owner) : IMsTscAxEvents
{
    public void OnConnecting() => owner.Emit("Connecting");
    public void OnConnected() => owner.Emit("Connected");
    public void OnLoginComplete() => owner.Emit("LoginComplete");
    public void OnDisconnected(int reason) => owner.Emit("Disconnected", reason, owner.DisconnectDescription(reason));
    public void OnEnterFullScreenMode() => owner.Emit("EnterFullScreen");
    public void OnLeaveFullScreenMode() => owner.Emit("LeaveFullScreen");
    public void OnRequestGoFullScreen() => owner.Emit("EnterFullScreen");
    public void OnRequestLeaveFullScreen() => owner.Emit("LeaveFullScreen");
    public void OnFatalError(int code) => owner.Emit("FatalError", code);
    public void OnWarning(int code) => owner.Emit("Warning", code, $"The Microsoft RDP component reported warning {code}.");
    public void OnRemoteDesktopSizeChange(int width, int height) => owner.Emit("DesktopSizeChanged", 0, $"{width} x {height}");
    public void OnIdleTimeoutNotification() => owner.Emit("Warning", 0, "The remote server reported an idle timeout.");
    public void OnRequestContainerMinimize() => owner.Emit("Minimize");
    public void OnConfirmClose(out bool allowClose) => allowClose = true;
    public void OnReceivedTSPublicKey(string key, out bool continueLogon) => continueLogon = false;
    public void OnAutoReconnecting(int reason, int attempt, out AutoReconnectContinueState state)
    {
        state = AutoReconnectContinueState.autoReconnectContinueAutomatic;
        owner.Emit("Reconnecting", attempt);
    }
    public void OnAutoReconnecting2(int reason, bool networkAvailable, int attempt, int maximum) => owner.Emit("Reconnecting", attempt);
    public void OnAutoReconnected() => owner.Emit("AutoReconnected");
    public void OnAuthenticationWarningDisplayed() => owner.Emit("AuthenticationDialog");
    public void OnAuthenticationWarningDismissed() => owner.Emit("AuthenticationDialogClosed");
    public void OnLogonError(int error) => owner.Emit("LogonError", error);
    public void OnFocusReleased(int direction) => owner.Emit("FocusReleased");
    public void OnNetworkStatusChanged(uint quality, int bandwidth, int rtt) => owner.Emit("NetworkStatus", rtt, $"Quality {quality}/4 | {Math.Max(0, bandwidth) / 1000} Kbit/s | {rtt} ms RTT");
    public void OnServiceMessageReceived(string message) => owner.Emit("ServiceMessage", 0, message);
    public void OnChannelReceivedData(string channel, string data) { }
    public void OnRemoteProgramResult(string program, RemoteProgramResult result, bool executable) { }
    public void OnRemoteProgramDisplayed(bool displayed, uint information) { }
    public void OnRemoteWindowDisplayed(bool displayed, ref _RemotableHandle handle, RemoteWindowDisplayedAttribute attribute) { }
    public void OnUserNameAcquired(string userName) { }
    public void OnMouseInputModeChanged(bool relative) { }
    public void OnConnectionBarPullDown() { }
    public void OnDevicesButtonPressed() { }
}
