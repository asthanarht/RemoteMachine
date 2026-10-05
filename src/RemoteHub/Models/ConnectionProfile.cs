using System.Net;
using System.Text.Json.Serialization;

namespace RemoteHub.Models;

public enum SessionState { Saved, Connecting, Authenticating, Connected, Reconnecting, Disconnected, Failed, Preview, TerminalOpen }
public enum ConnectionKind { Rdp, Ssh, Vnc }
public enum DockEdge { Left, Top, Right, Bottom }

public sealed class ConnectionProfile : INotifyPropertyChanged
{
    private bool _favorite;
    private bool _isCurrentSession;
    private SessionState _state;
    private DateTimeOffset? _lastConnected;
    private DateTimeOffset? _lastOpened;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3389;
    public ConnectionKind Kind { get; set; }
    public string SshKeyPath { get; set; } = "";
    public bool VncViewOnly { get; set; }
    public bool VncTrustedNetwork { get; set; }
    public int VncScrollSpeed { get; set; } = 1;
    public static IReadOnlyList<int> VncScrollSpeeds { get; } = Array.AsReadOnly(Enumerable.Range(1, 8).ToArray());
    public string UserName { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Group { get; set; } = "Work";
    public string Tone { get; set; } = "Blue";
    public bool RedirectClipboard { get; set; }
    public bool RedirectSmartCards { get; set; } = true;
    public bool AllowWindowsCredentialSaving { get; set; }
    public bool LowBandwidth { get; set; }
    public bool Favorite { get => _favorite; set { _favorite = value; Changed(); } }
    public DateTimeOffset? LastConnected { get => _lastConnected; set { _lastConnected = value; Changed(); Changed(nameof(LastConnectedText)); Changed(nameof(LastUsedText)); } }
    public DateTimeOffset? LastOpened { get => _lastOpened; set { _lastOpened = value; Changed(); Changed(nameof(LastUsedText)); } }
    [JsonIgnore] public DateTimeOffset? LastUsed => LastOpened > LastConnected || !LastConnected.HasValue ? LastOpened : LastConnected;
    [JsonIgnore] public string LastUsedText => LastUsed is { } time ? time.LocalDateTime.ToString("MMM d, h:mm tt") : "Not used yet";
    [JsonIgnore] public bool IsPreview { get; set; }
    [JsonIgnore] public bool IsCurrentSession
    {
        get => _isCurrentSession;
        set { if (_isCurrentSession != value) { _isCurrentSession = value; Changed(); } }
    }
    [JsonIgnore] public string Protocol => Kind switch { ConnectionKind.Ssh => "SSH", ConnectionKind.Vnc => "VNC", _ => "RDP" };
    [JsonIgnore] public string IconKind => Kind == ConnectionKind.Ssh ? "Terminal" : "Monitor";
    [JsonIgnore] public string PlatformLabel => Kind switch { ConnectionKind.Ssh => "SSH · Terminal", ConnectionKind.Vnc => "Screen sharing · VNC", _ => "Windows · RDP" };
    [JsonIgnore] public string ConnectionDescription => Kind switch { ConnectionKind.Ssh => "Windows OpenSSH · Embedded terminal", ConnectionKind.Vnc => "macOS Screen Sharing · Embedded VNC", _ => "Windows · Remote Desktop" };
    [JsonIgnore] public string CredentialDescription => Kind switch
    {
        ConnectionKind.Ssh => "OpenSSH handles passwords, keys, and host verification. Secrets are not saved in this app.",
        ConnectionKind.Vnc => "Mac account or VNC password requested on connect. VNC screen and input are not encrypted; use a trusted network, VPN, or existing SSH tunnel. Passwords are never saved.",
        _ => "Native RDP rendering. Credentials are handled by Windows, not stored in this app."
    };
    [JsonIgnore] public string UserNameDisplay => string.IsNullOrWhiteSpace(UserName) ? Kind == ConnectionKind.Ssh ? "OpenSSH default" : "Ask on connect" :
        Kind != ConnectionKind.Rdp ? UserName : WindowsAccount.Qualify(UserName, Domain);
    public static int DefaultPort(ConnectionKind kind) => kind switch { ConnectionKind.Rdp => 3389, ConnectionKind.Ssh => 22, ConnectionKind.Vnc => 5900, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    [JsonIgnore] public string Endpoint => Port == DefaultPort(Kind) ? Host : $"{(Host.Contains(':') ? $"[{Host}]" : Host)}:{Port}";
    [JsonIgnore] public bool IsSessionActive => State is SessionState.Connected or SessionState.Connecting or SessionState.Authenticating or SessionState.Reconnecting or SessionState.TerminalOpen;
    [JsonIgnore] public SessionState State { get => _state; set { _state = value; Changed(); Changed(nameof(StateText)); Changed(nameof(ConnectLabel)); } }
    [JsonIgnore] public string StateText => State switch
    {
        SessionState.Authenticating => "Signing in", SessionState.Preview => "Design preview", SessionState.TerminalOpen => "Terminal open",
        SessionState.Saved => "Saved", _ => State.ToString()
    };
    [JsonIgnore] public string ConnectLabel => IsPreview ? "Preview" : IsSessionActive ? "Resume" : "Connect";
    [JsonIgnore] public string LastConnectedText => LastConnected is not { } time ? "Not connected yet" : time.LocalDateTime.ToString("MMM d, h:mm tt");
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public string? Validate()
    {
        if (Validate(Name, Host, Port) is { } error) return error;
        if (!Enum.IsDefined(Kind)) return "Select a supported connection type.";
        if (Kind == ConnectionKind.Vnc && !VncTrustedNetwork) return "VNC traffic is not encrypted. Confirm that you will use a trusted network, VPN, or an existing SSH tunnel.";
        if (VncScrollSpeed is < 1 or > 8) return "Choose a VNC scroll speed between 1x and 8x.";
        if (UserName is null || Domain is null || SshKeyPath is null) return "Account and key fields must not be null.";
        if (UserName.Length > 256 || Domain.Length > 256 || UserName.Any(char.IsControl) || Domain.Any(char.IsControl))
            return "Account fields must be at most 256 characters and cannot contain control characters.";
        if (SshKeyPath.Length > 32767 || SshKeyPath.Any(char.IsControl) || (SshKeyPath.Length > 0 && !Path.IsPathFullyQualified(SshKeyPath)))
            return "Choose an absolute path to the SSH private-key file.";
        return null;
    }

    public static string? Validate(string name, string host, int port)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) return "Enter a connection name of 1 to 80 characters.";
        if (string.IsNullOrWhiteSpace(host)) return "Enter the remote PC name or IP address.";
        host = host.Trim();
        if (host.Length > 253 || host.Any(char.IsWhiteSpace) || host.IndexOfAny(['/', '\\', '@']) >= 0)
            return "Enter a PC name or IP address, not a URL or username.";
        if (!IPAddress.TryParse(host, out _) && (host.Contains(':') || host.Split('.').Any(part =>
            part.Length is < 1 or > 63 || !char.IsAsciiLetterOrDigit(part[0]) || !char.IsAsciiLetterOrDigit(part[^1]) ||
            part.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))))
            return "That PC name or IP address is not valid. Use the separate port field.";
        if (host.All(c => char.IsAsciiDigit(c) || c == '.') && !IPAddress.TryParse(host, out _))
            return "Enter a valid IP address.";
        if (port is < 1 or > 65535) return "The port must be between 1 and 65535.";
        return null;
    }
}

public sealed class WorkspaceData
{
    public int Version { get; set; } = 3;
    public List<ConnectionProfile> Connections { get; set; } = [];
    public DockEdge DockEdge { get; set; } = DockEdge.Left;
    public double DockPosition { get; set; } = .5;
    public bool HideFocusHandle { get; set; }
}
