using System.Globalization;
using System.Net;
using System.Text;
using RemoteHub.Models;

namespace RemoteHub.Services;

public sealed record RdpImport(ConnectionProfile Profile, IReadOnlyList<string> Warnings);

public static class RdpFile
{
    public const int MaximumBytes = 1024 * 1024;
    private sealed record Setting(char Type, string Value);
    private static readonly Dictionary<string, Setting> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["screen mode id"] = new('i', "1"), ["use multimon"] = new('i', "0"),
        ["session bpp"] = new('i', "32"), ["smart sizing"] = new('i', "1"),
        ["displayconnectionbar"] = new('i', "1"), ["prompt for credentials"] = new('i', "1"),
        ["enablecredsspsupport"] = new('i', "1"), ["authentication level"] = new('i', "2"),
        ["negotiate security layer"] = new('i', "1"), ["autoreconnection enabled"] = new('i', "1"),
        ["bandwidthautodetect"] = new('i', "1"), ["networkautodetect"] = new('i', "1"),
        ["redirectprinters"] = new('i', "0"), ["redirectcomports"] = new('i', "0"),
        ["redirectposdevices"] = new('i', "0"), ["audiocapturemode"] = new('i', "0"),
        ["drivestoredirect"] = new('s', ""), ["devicestoredirect"] = new('s', "")
    };

    public static RdpImport Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("RDP files must be no larger than 1 MB.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("The file changed while it was being read. Try importing again.");
        Encoding encoding = new UTF8Encoding(false, true);
        int offset = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) offset = 3;
        try { return Parse(encoding.GetString(bytes, offset, bytes.Length - offset), Path.GetFileNameWithoutExtension(path)); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("The file must use valid UTF-8 or UTF-16 text.", error); }
    }

    public static RdpImport Parse(string text, string name)
    {
        if (text.Length > MaximumBytes || text.Contains('\0')) throw new InvalidDataException("The RDP file is too large or contains invalid text.");
        var fields = new Dictionary<string, Setting>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.TrimStart('\uFEFF').Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith(';')) continue;
            int colon = line.IndexOf(':');
            if (colon < 1 || colon + 2 >= line.Length || line[colon + 2] != ':' || !"sib".Contains(char.ToLowerInvariant(line[colon + 1])))
                throw new InvalidDataException("An RDP setting is malformed. Expected name:type:value.");
            string key = line[..colon].Trim().ToLowerInvariant();
            if (key.Length > 80 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != ' ' && c != '_'))
                throw new InvalidDataException("The file contains an invalid setting name.");
            var field = new Setting(char.ToLowerInvariant(line[colon + 1]), line[(colon + 3)..]);
            if (field.Value.Length > 65536 || field.Value.Any(char.IsControl)) throw new InvalidDataException($"Setting '{key}' contains invalid text.");
            if (!fields.TryAdd(key, field)) throw new InvalidDataException($"Setting '{key}' is repeated. Resolve the ambiguity before importing.");
            if (fields.Count > 256) throw new InvalidDataException("The file contains too many settings.");
        }

        string ReadString(string key, string fallback = "")
        {
            if (!fields.TryGetValue(key, out var value)) return fallback;
            if (value.Type != 's') throw new InvalidDataException($"Setting '{key}' must be text.");
            return value.Value.Trim();
        }
        int ReadInt(string key, int fallback = 0)
        {
            if (!fields.TryGetValue(key, out var value)) return fallback;
            if (value.Type != 'i' || !int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                throw new InvalidDataException($"Setting '{key}' must be an integer.");
            return number;
        }
        bool ReadBool(string key, bool fallback)
        {
            int value = ReadInt(key, fallback ? 1 : 0);
            return value is 0 or 1 ? value == 1 : throw new InvalidDataException($"Setting '{key}' must be 0 or 1.");
        }
        foreach (string key in new[] { "gatewayhostname", "loadbalanceinfo", "load balance info", "workspace id", "workspaceid",
            "alternate shell", "shell working directory", "signature", "signscope", "kdcproxyname", "gatewayaccesstoken", "remoteapplicationprogram" })
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value.Value))
                throw new InvalidDataException($"This file requires '{key}', which is not supported by standard-PC import. Nothing has been imported.");
        foreach (string key in new[] { "remoteapplicationmode", "administrative session", "connect to console",
            "gatewaybrokeringtype", "gatewayprofileusagemethod", "rdgiskdcproxy", "enablerdsaadauth", "targetisaadjoined" })
            if (ReadInt(key) != 0) throw new InvalidDataException($"This file requests '{key}', an unsupported connection mode. Nothing has been imported.");

        string address = ReadString("full address");
        if (address.Length == 0) throw new InvalidDataException("The RDP file has no full address.");
        string host = address;
        int? addressPort = null;
        if (address.StartsWith('['))
        {
            int end = address.IndexOf(']');
            if (end < 0) throw new InvalidDataException("The IPv6 address is missing its closing bracket.");
            host = address[1..end];
            if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                throw new InvalidDataException("Only IPv6 addresses can use address brackets.");
            string suffix = address[(end + 1)..];
            if (suffix.Length > 0)
            {
                if (!suffix.StartsWith(':') || !int.TryParse(suffix[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int port))
                    throw new InvalidDataException("The address contains an invalid port.");
                addressPort = port;
            }
        }
        else if (!IPAddress.TryParse(address, out _) && address.Contains(':'))
        {
            int colon = address.LastIndexOf(':');
            host = address[..colon];
            if (!int.TryParse(address[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port))
                throw new InvalidDataException("The address contains an invalid port.");
            addressPort = port;
        }
        int configuredPort = ReadInt("server port", addressPort ?? 3389);
        if (addressPort.HasValue && configuredPort != addressPort.Value)
            throw new InvalidDataException("The address and server-port settings disagree.");
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl))
        {
            name = host.Length <= 80 ? host : "Imported computer";
            warnings.Add("The connection name was replaced with a shorter name. Review it before saving.");
        }
        string[] performance = ["disable wallpaper", "disable full window drag", "disable menu anims", "disable themes"];
        bool[] effects = performance.Select(key => ReadBool(key, false)).ToArray();
        var profile = new ConnectionProfile
        {
            Name = name, Host = host, Port = configuredPort, UserName = ReadString("username"), Domain = ReadString("domain"),
            RedirectClipboard = ReadBool("redirectclipboard", false), RedirectSmartCards = ReadBool("redirectsmartcards", true),
            LowBandwidth = effects.Any(v => v), Favorite = true
        };
        if (profile.Validate() is { } error) throw new InvalidDataException(error);
        if (effects.Any(v => v) && effects.Any(v => !v)) warnings.Add("Individual visual-effect settings were mapped to the reduced-effects preset.");
        var mapped = new HashSet<string>(performance.Concat(new[] { "full address", "server port", "username", "domain", "redirectclipboard", "redirectsmartcards" }), StringComparer.OrdinalIgnoreCase);
        if (fields.Any(f => f.Value.Type == 'b' || f.Key.Contains("password", StringComparison.OrdinalIgnoreCase) || f.Key.Contains("token", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Saved passwords, tokens, and binary credential data were discarded. Windows will handle sign-in.");
        var omitted = fields.Where(f => !mapped.Contains(f.Key) && f.Value.Type != 'b' && !f.Key.Contains("password", StringComparison.OrdinalIgnoreCase) &&
            !f.Key.Contains("token", StringComparison.OrdinalIgnoreCase) && (!Defaults.TryGetValue(f.Key, out var expected) || expected != f.Value))
            .Select(f => f.Key).Order().ToArray();
        if (omitted.Length > 0)
            warnings.Add("Not retained (the app uses its own display, security, and device-sharing settings): " + string.Join(", ", omitted) + ".");
        return new(profile, warnings);
    }

    public static string Export(ConnectionProfile profile)
    {
        if (profile.IsPreview || profile.Kind != ConnectionKind.Rdp) throw new InvalidOperationException("Only real RDP profiles can be exported as .rdp files.");
        if (profile.Validate() is { } error) throw new InvalidDataException(error);
        string host = profile.Host.Contains(':') ? $"[{profile.Host}]" : profile.Host;
        var lines = new List<string> { $"full address:s:{host}", $"server port:i:{profile.Port.ToString(CultureInfo.InvariantCulture)}",
            $"username:s:{profile.UserName}", $"domain:s:{profile.Domain}",
            $"redirectclipboard:i:{(profile.RedirectClipboard ? 1 : 0)}", $"redirectsmartcards:i:{(profile.RedirectSmartCards ? 1 : 0)}" };
        lines.AddRange(Defaults.Select(f => $"{f.Key}:{f.Value.Type}:{f.Value.Value}"));
        foreach (string key in new[] { "disable wallpaper", "disable full window drag", "disable menu anims", "disable themes" })
            lines.Add($"{key}:i:{(profile.LowBandwidth ? 1 : 0)}");
        return string.Join("\r\n", lines) + "\r\n";
    }

    public static void Write(string path, ConnectionProfile profile)
    {
        string text = Export(profile);
        AtomicFile.Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, Encoding.Unicode, 1024, leaveOpen: true);
            writer.Write(text);
        });
    }
}
