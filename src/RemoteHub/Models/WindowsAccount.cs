using System.Globalization;

namespace RemoteHub.Models;

public sealed record WindowsAccount(string UserName, string Domain)
{
    public string QualifiedName => Qualify(UserName, Domain);
    public string Initials
    {
        get
        {
            var name = new StringInfo(UserName);
            return name.SubstringByTextElements(0, Math.Min(2, name.LengthInTextElements)).ToUpperInvariant();
        }
    }

    public static WindowsAccount ReadCurrent() => new(Environment.UserName, Environment.UserDomainName);

    internal static string Qualify(string userName, string domain) =>
        string.IsNullOrWhiteSpace(domain) || userName.Contains('\\') || userName.Contains('@')
            ? userName.Trim() : $"{domain.Trim()}\\{userName.Trim()}";
}
