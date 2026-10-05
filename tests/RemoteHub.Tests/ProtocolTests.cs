using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using RemoteHub.Models;
using RemoteHub.Services;
using RemoteHub.Ssh;

internal static class ProtocolTests
{
    public static async Task Run(Action<bool, string> check, string temporary)
    {
        var imported = RdpFile.Parse("FULL ADDRESS:s:pc.example.com:3390\r\nusername:s:alex@example.com\r\nredirectclipboard:i:1\r\nredirectsmartcards:i:0\r\npassword 51:b:SECRET_SENTINEL\r\n", "Imported");
        check(imported.Profile.Host == "pc.example.com" && imported.Profile.Port == 3390 && imported.Profile.UserName == "alex@example.com",
            "RDP import handles case-insensitive settings, usernames, and address ports");
        check(imported.Profile.RedirectClipboard && !imported.Profile.RedirectSmartCards, "RDP redirection choices survive import");
        check(imported.Warnings.Any(w => w.Contains("discarded")) && !JsonSerializer.Serialize(imported).Contains("SECRET_SENTINEL"),
            "Imported password data is discarded rather than stored or displayed");
        check(RdpFile.Parse("full address:s:[2001:db8::10]:3391", "IPv6").Profile.Port == 3391 &&
            RdpFile.Parse("full address:s:2001:db8::10", "IPv6").Profile.Host == "2001:db8::10", "IPv6 imports distinguish bracketed ports from bare addresses");
        var downgraded = RdpFile.Parse("full address:s:pc\nredirectprinters:i:1\nauthentication level:i:0\nenablecredsspsupport:i:0", "Warnings");
        check(downgraded.Warnings.Any(w => w.Contains("authentication level") && w.Contains("redirectprinters")),
            "Unsupported redirection and weakened authentication settings are explicitly disclosed");
        foreach (string invalid in new[]
        {
            "", "full address:s:https://example.com", "full address:s:-oProxyCommand=bad", "full address:s:[host]",
            "full address:s:pc:3390\nserver port:i:3389", "full address:s:pc\nfull address:s:other",
            "full address:s:pc\nredirectclipboard:i:2", "full address:i:12", "full address:s:pc\0",
            "full address:s:pc\ngatewayhostname:s:gateway", "full address:s:pc\nloadbalanceinfo:s:broker",
            "full address:s:pc\nremoteapplicationmode:i:1", "full address:s:pc\nsignature:s:signed",
            "full address:s:pc\nenablerdsaadauth:i:1", "full address:s:pc\nalternate shell:s:program.exe"
        })
        {
            bool rejected = false;
            try { RdpFile.Parse(invalid, "Invalid"); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Reject malformed, ambiguous, or unsupported RDP settings");
        }
        var profile = new ConnectionProfile { Name = "Round trip", Host = "2001:db8::1", Port = 3392, UserName = "user", Domain = "CONTOSO",
            RedirectClipboard = true, RedirectSmartCards = false, LowBandwidth = true, AllowWindowsCredentialSaving = true };
        string export = RdpFile.Export(profile);
        var roundTrip = RdpFile.Parse(export, profile.Name);
        check(roundTrip.Profile.Host == profile.Host && roundTrip.Profile.Port == profile.Port && roundTrip.Profile.UserName == profile.UserName &&
            roundTrip.Profile.Domain == profile.Domain && roundTrip.Profile.LowBandwidth && !roundTrip.Profile.RedirectSmartCards &&
            roundTrip.Profile.RedirectClipboard && roundTrip.Warnings.Count == 0, "Supported RDP settings round-trip without unexplained loss");
        check(!export.Contains("password", StringComparison.OrdinalIgnoreCase) && export.Contains("authentication level:i:2") &&
            export.Contains("enablecredsspsupport:i:1") && export.Contains("redirectprinters:i:0"), "Exports contain safe authentication defaults and no passwords");
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode })
        {
            string path = Path.Combine(temporary, "encoding.rdp");
            File.WriteAllText(path, export, encoding);
            check(RdpFile.Read(path).Profile.Host == profile.Host, "Read UTF-8 and UTF-16 RDP encodings");
        }
        string destination = Path.Combine(temporary, "export.rdp");
        File.WriteAllText(destination, "old file");
        RdpFile.Write(destination, profile);
        check(File.ReadAllBytes(destination).AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) && RdpFile.Read(destination).Profile.Port == 3392,
            "RDP export atomically replaces the destination with Windows-compatible UTF-16");
        string oversized = Path.Combine(temporary, "oversized.rdp");
        using (var file = File.Create(oversized)) file.SetLength(RdpFile.MaximumBytes + 1);
        bool tooLarge = false;
        try { RdpFile.Read(oversized); } catch (InvalidDataException) { tooLarge = true; }
        check(tooLarge, "Oversized files are rejected before reading their contents");

        var ssh = new ConnectionProfile { Name = "SSH", Kind = ConnectionKind.Ssh, Host = "server.example.com", Port = 22,
            UserName = "alex", SshKeyPath = Path.Combine(temporary, "key with spaces"), LastOpened = DateTimeOffset.Now };
        check(ssh.Validate() is null && ssh.Protocol == "SSH" && ssh.IconKind == "Terminal" && ssh.LastUsed == ssh.LastOpened,
            "SSH profiles expose the correct protocol and truthful last-opened history");
        var store = new WorkspaceStore(temporary);
        store.Save(new WorkspaceData { Connections = [ssh, profile], HideFocusHandle = true });
        var restored = store.Load();
        check(restored.Version == 3 && restored.Connections[0].Kind == ConnectionKind.Ssh && restored.Connections[0].SshKeyPath == ssh.SshKeyPath &&
            restored.Connections[1].Kind == ConnectionKind.Rdp && restored.HideFocusHandle, "Mixed SSH/RDP workspaces preserve profiles and fullscreen preferences");
        bool blockedSshExport = false;
        try { RdpFile.Export(ssh); } catch (InvalidOperationException) { blockedSshExport = true; }
        check(blockedSshExport, "SSH profiles cannot be mislabeled as RDP files");
        var arguments = SshCommand.Arguments(ssh);
        check(arguments.Contains("StrictHostKeyChecking=ask") && arguments.Contains("ForwardAgent=no") && arguments.Contains("ClearAllForwardings=yes") &&
            arguments.Contains("RemoteCommand=none") && arguments.Last() == ssh.Host, "SSH enforces host verification and does not inherit forwarding or command presets");
        string[] tricky = ["", "two words", "a\"b", @"C:\ends with slash\", "\u65e5\u672c\u8a9e"];
        check(SplitCommand(SshCommand.CommandLine("C:\\Program Files\\ssh.exe", tricky)).SequenceEqual(new[] { "C:\\Program Files\\ssh.exe" }.Concat(tricky)),
            "Windows command-line quoting preserves spaces, quotes, Unicode, and trailing slashes without shell interpolation");
        check(new ConnectionProfile { Name = "Bad", Host = "host", UserName = "user\nsetting:i:0" }.Validate() is not null, "Account fields cannot inject RDP settings");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        await using (var console = PseudoConsole.Start(cmd, ["/d", "/q", "/c", "echo __PTY_OK__ & exit /b 7"], 100, 30))
        {
            var output = Read(console, timeout.Token);
            int exit = await console.ExitCode.WaitAsync(timeout.Token);
            check(exit == 7, $"ConPTY reports the real child exit status (received {exit})");
            await console.FinishAsync().WaitAsync(timeout.Token);
            check((await output).Contains("__PTY_OK__"), "ConPTY drains terminal output through natural process exit");
        }
        await using (var console = PseudoConsole.Start(cmd, ["/d", "/q"], 80, 24))
        {
            var output = Read(console, timeout.Token);
            console.Resize(120, 40);
            await console.WriteAsync("echo __INPUT_OK__\r\n", timeout.Token);
            await console.WriteAsync("exit\r\n", timeout.Token);
            await console.ExitCode.WaitAsync(timeout.Token);
            await console.FinishAsync().WaitAsync(timeout.Token);
            check((await output).Contains("__INPUT_OK__"), "ConPTY accepts interactive input and resizing");
        }
        await using (var console = PseudoConsole.Start(cmd, ["/d", "/q"], 80, 24))
        {
            await console.DisposeAsync().AsTask().WaitAsync(timeout.Token);
            check(console.ExitCode.IsCompleted, "Disconnect terminates the owned terminal process without waiting for user input");
        }
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        ssh.Host = "127.0.0.1"; ssh.Port = ((IPEndPoint)listener.LocalEndpoint).Port; ssh.SshKeyPath = "";
        var localArguments = new[] { "-F", "NUL" }.Concat(SshCommand.Arguments(ssh)).ToArray();
        await using (var console = PseudoConsole.Start(SshCommand.Executable, localArguments, 80, 24))
        {
            var output = Read(console, timeout.Token);
            using (var peer = await listener.AcceptTcpClientAsync(timeout.Token))
            {
                var bytes = new byte[512];
                int count = await peer.GetStream().ReadAsync(bytes, timeout.Token);
                check(Encoding.ASCII.GetString(bytes, 0, count).StartsWith("SSH-2.0-"), "Windows OpenSSH performs a real local-only SSH transport handshake");
            }
            check(await console.ExitCode.WaitAsync(timeout.Token) != 0, "A failed SSH transport is not reported as authenticated");
            await console.FinishAsync().WaitAsync(timeout.Token);
            await output;
        }
    }
    private static async Task<string> Read(PseudoConsole console, CancellationToken cancellationToken)
    {
        var data = new MemoryStream();
        await foreach (byte[] bytes in console.Output.ReadAllAsync(cancellationToken)) await data.WriteAsync(bytes, cancellationToken);
        return Encoding.UTF8.GetString(data.ToArray());
    }
    private static string[] SplitCommand(string command)
    {
        IntPtr args = CommandLineToArgvW(command, out int count);
        if (args == IntPtr.Zero) throw new InvalidOperationException("Windows could not parse the test command line.");
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(args, i * IntPtr.Size)) ?? "").ToArray(); }
        finally { LocalFree(args); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
