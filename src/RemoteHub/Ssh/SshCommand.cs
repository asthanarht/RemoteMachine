using System.Globalization;
using System.Text;
using RemoteHub.Models;

namespace RemoteHub.Ssh;

public static class SshCommand
{
    public static string Executable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
    public static IReadOnlyList<string> Arguments(ConnectionProfile profile)
    {
        if (profile.Kind != ConnectionKind.Ssh || profile.IsPreview) throw new InvalidOperationException("A real SSH profile is required.");
        if (profile.Validate() is { } error) throw new ArgumentException(error);
        var arguments = new List<string>
        {
            "-tt", "-o", "StrictHostKeyChecking=ask", "-o", "EscapeChar=none", "-o", "PermitLocalCommand=no",
            "-o", "ForwardAgent=no", "-o", "ForwardX11=no", "-o", "ClearAllForwardings=yes",
            "-o", "RemoteCommand=none", "-p", profile.Port.ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(profile.UserName)) { arguments.Add("-l"); arguments.Add(profile.UserName); }
        if (!string.IsNullOrWhiteSpace(profile.SshKeyPath)) { arguments.Add("-i"); arguments.Add(profile.SshKeyPath); }
        arguments.Add("--"); arguments.Add(profile.Host);
        return arguments;
    }

    public static string CommandLine(string executable, IEnumerable<string> arguments) =>
        string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote));

    private static string Quote(string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("Process arguments cannot contain null characters.");
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"')) return value;
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"');
        return result.ToString();
    }
}
