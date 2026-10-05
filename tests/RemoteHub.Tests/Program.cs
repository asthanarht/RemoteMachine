using System.Text.Json;
using System.Text.Json.Nodes;
using RemoteHub.Focus;
using RemoteHub.Models;
using RemoteHub.Services;

int passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    passed++;
    Console.WriteLine("PASS " + description);
}

ThemeTests.Run(Check);
foreach (string host in new[] { "office-pc", "rdp.example.com", "192.0.2.10", "2001:db8::1" })
    Check(ConnectionProfile.Validate("Work", host, 3389) is null, "Valid hostname/address: " + host);
foreach (string host in new[] { "", "https://example.com", "user@host", "my pc", "-invalid", "bad..host", "999.1.1.1", "host:3389", "ms-avd:connect?resourceid=test", "https://devbox.microsoft.com" })
    Check(ConnectionProfile.Validate("Work", host, 3389) is not null, "Reject invalid address: " + host);
Check(ConnectionProfile.Validate("", "host", 3389) is not null, "A name is required");
Check(ConnectionProfile.Validate("Work", "host", 0) is not null, "Reject port zero");
Check(ConnectionProfile.Validate("Work", "host", 65536) is not null, "Reject port beyond 65535");
Check(ConnectionProfile.Validate("Work", "host", 65535) is null, "Accept maximum port");
Check(new WindowsAccount("alex", "CONTOSO").QualifiedName == @"CONTOSO\alex", "Windows identity includes its account domain");
Check(new WindowsAccount("alex", "").QualifiedName == "alex", "Accounts without a domain are not given an invented domain");
Check(new WindowsAccount("alex@example.com", "CONTOSO").QualifiedName == "alex@example.com", "UPNs are not prefixed with an unrelated domain");
Check(new WindowsAccount(@"CONTOSO\alex", "CONTOSO").QualifiedName == @"CONTOSO\alex", "Qualified usernames are not double-prefixed");
Check(new WindowsAccount("a", "PC").Initials == "A", "Short usernames have safe avatar initials");
Check(new WindowsAccount("a\u0301\uD83D\uDE80z", "PC").Initials == "A\u0301\uD83D\uDE80", "Avatar initials preserve complete Unicode text elements");
Check(new ConnectionProfile().UserNameDisplay == "Ask on connect", "Blank connection usernames have an explicit sign-in label");
Check(new ConnectionProfile { UserName = "  " }.UserNameDisplay == "Ask on connect", "Whitespace usernames show the sign-in prompt label");
Check(new ConnectionProfile { UserName = "alex", Domain = "CONTOSO" }.UserNameDisplay == @"CONTOSO\alex", "Connection details include the configured domain");
Check(new ConnectionProfile { UserName = "alex@example.com", Domain = "CONTOSO" }.UserNameDisplay == "alex@example.com", "Connection details preserve UPN usernames");
Check(EdgeHandleWindow.NearestEdge(-1900, 500, -1920, 0, 1920, 1080) == DockEdge.Left, "Dock correctly on negative-coordinate monitor");
Check(EdgeHandleWindow.NearestEdge(1200, 15, 0, 0, 1920, 1080) == DockEdge.Top, "Dock top edge");
Check(EdgeHandleWindow.NearestEdge(1900, 500, 0, 0, 1920, 1080) == DockEdge.Right, "Dock right edge");
Check(EdgeHandleWindow.NearestEdge(800, 1070, 0, 0, 1920, 1080) == DockEdge.Bottom, "Dock bottom edge");

string temp = Path.Combine(Path.GetTempPath(), "RemoteWorkspaceTests-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new WorkspaceStore(temp);
    Check(store.Load().Connections.Count == 0, "New workspaces start empty, not with fake reachable devices");
    Check(!store.Load().HideFocusHandle, "The Focus handle is always visible by default");
    var profile = new ConnectionProfile { Name = "Work", Host = "192.0.2.10", Favorite = true, State = SessionState.Connected };
    var data = new WorkspaceData { Connections = [profile], DockEdge = DockEdge.Right, DockPosition = .74, HideFocusHandle = true };
    store.Save(data);
    var loaded = store.Load();
    Check(loaded.Connections.Single().Id == profile.Id && loaded.Connections[0].Favorite, "Connection metadata survives a save/load round trip");
    Check(loaded.Connections[0].State == SessionState.Saved, "Transient session status is not persisted as connected");
    Check(loaded.Connections[0].RedirectSmartCards, "New profiles enable smart-card/Windows Hello redirection");
    Check(loaded.DockEdge == DockEdge.Right && loaded.DockPosition == .74, "Dock position persists");
    Check(loaded.HideFocusHandle, "The user's explicit hidden-handle preference survives reload");
    string json = File.ReadAllText(store.FilePath);
    Check(!json.Contains("\"Password\"", StringComparison.OrdinalIgnoreCase), "Workspace contains no password field");
    Check(!json.Contains("\"UserNameDisplay\"", StringComparison.Ordinal) && !json.Contains("\"CurrentWindowsAccount\"", StringComparison.Ordinal),
        "Presentation labels and the detected Windows identity are not persisted as connection data");
    var legacy = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("Test workspace is missing.");
    var legacyConnection = legacy["Connections"]?.AsArray().Single()?.AsObject() ?? throw new InvalidOperationException("Test profile is missing.");
    legacyConnection.Remove("RedirectSmartCards");
    legacyConnection.Remove("Kind");
    legacyConnection.Remove("SshKeyPath");
    legacy["Version"] = 1;
    legacy.Remove("HideFocusHandle");
    File.WriteAllText(store.FilePath, legacy.ToJsonString());
    string legacyText = File.ReadAllText(store.FilePath);
    Check(store.Load().Version == 3 && store.Load().Connections[0].Kind == ConnectionKind.Rdp && File.ReadAllText(store.FilePath) == legacyText,
        "Version-one workspaces migrate in memory as RDP without rewriting the original file");
    Check(store.Load().Connections[0].RedirectSmartCards, "Existing 0.1.0 profiles inherit the smart-card authentication fix");
    Check(!store.Load().HideFocusHandle, "Older workspaces keep the handle visible");
    data.HideFocusHandle = false; store.Save(data);
    Check(!store.Load().HideFocusHandle, "Choosing to keep the handle visible persists");
    profile.RedirectSmartCards = false; store.Save(data);
    Check(!store.Load().Connections[0].RedirectSmartCards, "An explicit smart-card opt-out survives save and reload");
    profile.RedirectSmartCards = true; store.Save(data);
    Check(store.Load().Connections[0].RedirectSmartCards, "Re-enabling smart-card redirection persists");
    profile.Name = "Updated workstation"; store.Save(data);
    Check(File.Exists(store.FilePath + ".bak"), "Atomic save preserves the previous workspace backup");
    Check(store.Load().Connections[0].Name == profile.Name, "Atomic replacement saves the update");
    data.Connections.AddRange(WorkspaceStore.PreviewConnections());
    bool refusedPreview = false;
    try { store.Save(data); } catch (InvalidOperationException) { refusedPreview = true; }
    Check(refusedPreview, "Preview profiles cannot be saved as real connections");
    File.WriteAllText(store.FilePath, "{not valid JSON");
    bool refusedCorrupt = false;
    try { store.Load(); } catch (JsonException) { refusedCorrupt = true; }
    Check(refusedCorrupt && File.ReadAllText(store.FilePath) == "{not valid JSON", "Corrupt data is reported and preserved, not overwritten");
    data = new() { Connections = [profile], DockPosition = 3 };
    File.WriteAllText(store.FilePath, JsonSerializer.Serialize(data));
    bool refusedDock = false;
    try { store.Load(); } catch (InvalidDataException) { refusedDock = true; }
    Check(refusedDock, "Invalid persisted dock positions are rejected");
    foreach (string invalid in new[] {
        JsonSerializer.Serialize(new WorkspaceData { Version = 999 }),
        JsonSerializer.Serialize(new WorkspaceData { Connections = [profile, profile] }),
        JsonSerializer.Serialize(new WorkspaceData { Connections = [profile] }).Replace("\"Group\":\"Work\"", "\"Group\":null")
    })
    {
        File.WriteAllText(store.FilePath, invalid);
        bool refused = false;
        try { store.Load(); } catch (InvalidDataException) { refused = true; }
        Check(refused, "Invalid versions, duplicate IDs, and null profile fields cannot enter the workspace");
    }
    await ProtocolTests.Run(Check, temp);
    SettingsTests.Run(Check, temp);
    VncTests.Run(Check, temp);
}
finally
{
    foreach (string file in Directory.Exists(temp) ? Directory.GetFiles(temp) : []) File.Delete(file);
    if (Directory.Exists(temp)) Directory.Delete(temp);
}
Console.WriteLine($"{passed} checks passed.");
