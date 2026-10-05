using System.Text;
using RemoteHub.Models;
using RemoteHub.Services;
using RemoteHub.Vnc;

internal static class VncTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        var profile = new ConnectionProfile { Name = "Mac studio", Host = "studio.local", Kind = ConnectionKind.Vnc, Port = 5900, UserName = "macuser" };
        check(profile.Validate() is not null, "VNC requires explicit acknowledgement of unencrypted transport");
        profile.VncTrustedNetwork = true; profile.VncViewOnly = true;
        check(profile.VncScrollSpeed == 1, "Existing and new connections retain original wheel speed unless explicitly changed");
        foreach (int speed in ConnectionProfile.VncScrollSpeeds)
        {
            profile.VncScrollSpeed = speed;
            check(profile.Validate() is null, $"VNC scroll speed {speed}x is supported");
        }
        foreach (int speed in new[] { 0, -1, 9, int.MaxValue })
        {
            profile.VncScrollSpeed = speed;
            check(profile.Validate() is not null, "Out-of-range scroll speed is rejected");
        }
        profile.VncScrollSpeed = 3;
        check(profile.Validate() is null && profile.Protocol == "VNC" && profile.Endpoint == "studio.local" &&
            profile.UserNameDisplay == "macuser" && ConnectionProfile.DefaultPort(profile.Kind) == 5900, "VNC profile metadata uses the Mac/VNC account and port, not Windows credentials");
        var store = new WorkspaceStore(directory);
        var mixed = new WorkspaceData { Connections = [profile, new() { Name = "PC", Host = "pc.local" },
            new() { Name = "Shell", Host = "shell.local", Kind = ConnectionKind.Ssh, Port = 22 }] };
        profile.IsCurrentSession = true;
        store.Save(mixed);
        check(!File.ReadAllText(store.FilePath).Contains("IsCurrentSession") && !store.Load().Connections.First().IsCurrentSession,
            "The currently viewed sidebar session is transient and never persisted as connection data");
        var restored = store.Load().Connections.First();
        check(restored.Kind == ConnectionKind.Vnc && restored.VncViewOnly && restored.VncTrustedNetwork && restored.UserName == "macuser" && restored.VncScrollSpeed == 3,
            "VNC preferences survive workspace persistence without password fields");
        check(store.Load().Connections.Skip(1).All(connection => connection.VncScrollSpeed == 1),
            "Changing one VNC scroll speed does not change other connections");
        string legacyVnc = System.Text.Json.JsonSerializer.Serialize(mixed).Replace(",\"VncScrollSpeed\":3", "").Replace(",\"VncScrollSpeed\":1", "");
        File.WriteAllText(store.FilePath, legacyVnc);
        check(store.Load().Connections.All(connection => connection.VncScrollSpeed == 1) && File.ReadAllText(store.FilePath) == legacyVnc,
            "Existing schema 3 files without scroll speed load at 1x without being rewritten");
        string invalidSpeed = legacyVnc.Replace("\"VncViewOnly\":true", "\"VncScrollSpeed\":0,\"VncViewOnly\":true");
        File.WriteAllText(store.FilePath, invalidSpeed);
        bool badSpeed = false;
        try { store.Load(); } catch (InvalidDataException) { badSpeed = true; }
        check(badSpeed && File.ReadAllText(store.FilePath) == invalidSpeed, "Invalid saved scroll speed is reported without rewriting the workspace");
        store.Save(mixed);
        check(store.Load().Version == 3 && store.Load().Connections.Select(p => p.Kind).SequenceEqual(new[] { ConnectionKind.Vnc, ConnectionKind.Rdp, ConnectionKind.Ssh }),
            "Schema 3 preserves mixed RDP, SSH, and VNC connections");
        string previous = System.Text.Json.JsonSerializer.Serialize(new WorkspaceData { Version = 2, Connections = mixed.Connections.Skip(1).ToList() });
        File.WriteAllText(store.FilePath, previous);
        check(store.Load().Version == 3 && store.Load().Connections.Count == 2 && File.ReadAllText(store.FilePath) == previous,
            "Schema 2 upgrades in memory without rewriting saved connections at startup");
        profile.Host = "2001:db8::1"; profile.Port = 5901;
        check(profile.Endpoint == "[2001:db8::1]:5901" && profile.Validate() is null, "VNC supports IPv6 and a nondefault port");
        foreach (byte type in new byte[] { 2, 30 })
        {
            var guard = new VncHandshakeGuard();
            byte[] greeting = Encoding.ASCII.GetBytes("RFB 003.008\n").Concat(new[] { type }).ToArray();
            foreach (byte value in greeting) guard.ValidateOutgoing(new[] { value });
            check(guard.SecurityType == type, "Fragmented VNC password and Apple authentication negotiation is accepted");
        }
        foreach (byte type in new byte[] { 1, 6, 19, 255 })
        {
            bool rejected = false;
            try { new VncHandshakeGuard().ValidateOutgoing(Encoding.ASCII.GetBytes("RFB 003.008\n").Concat(new[] { type }).ToArray()); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "Passwordless and unsupported VNC authentication is blocked before credential traffic");
        }
        bool legacy = false;
        try { new VncHandshakeGuard().ValidateOutgoing(Encoding.ASCII.GetBytes("RFB 003.003\n")); }
        catch (InvalidDataException) { legacy = true; }
        check(legacy, "Unsupported legacy VNC negotiation fails explicitly");
        bool export = false;
        try { RdpFile.Export(profile); } catch (InvalidOperationException) { export = true; }
        check(export, "VNC connections cannot be exported as RDP profiles");
    }
}
