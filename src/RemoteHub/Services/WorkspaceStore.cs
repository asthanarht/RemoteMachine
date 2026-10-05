using System.Text.Json;
using RemoteHub.Models;

namespace RemoteHub.Services;

public sealed class WorkspaceStore
{
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "workspace.json");
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    public WorkspaceStore(string? directory = null) => DirectoryPath = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteWorkspace");

    public WorkspaceData Load()
    {
        if (!File.Exists(FilePath)) return new();
        var data = JsonSerializer.Deserialize<WorkspaceData>(File.ReadAllText(FilePath), _json)
            ?? throw new InvalidDataException("The workspace file is empty. Your original file has not been changed.");
        if (data.Version is not 1 and not 2 and not 3) throw new InvalidDataException("This workspace was created by an unsupported app version.");
        if (data.Connections is null || data.Connections.Any(c => c is null || c.Id == Guid.Empty || c.Group is null || c.Tone is null ||
            c.Validate() is not null) ||
            data.Connections.Select(c => c.Id).Distinct().Count() != data.Connections.Count)
            throw new InvalidDataException("The workspace contains invalid or duplicate connections. Your original file has not been changed.");
        if (!Enum.IsDefined(data.DockEdge) || !double.IsFinite(data.DockPosition) || data.DockPosition is < 0 or > 1)
            throw new InvalidDataException("The saved dock position is invalid. Your original file has not been changed.");
        data.Version = 3;
        return data;
    }

    public void Save(WorkspaceData data)
    {
        if (data.Connections.Any(c => c.IsPreview)) throw new InvalidOperationException("Preview connections cannot be saved to your workspace.");
        if (data.Connections.Any(c => c.Validate() is not null)) throw new InvalidDataException("The workspace contains an invalid connection.");
        data.Version = 3;
        Directory.CreateDirectory(DirectoryPath);
        AtomicFile.Write(FilePath, stream => JsonSerializer.Serialize(stream, data, _json), FilePath + ".bak");
    }

    public static List<ConnectionProfile> PreviewConnections() =>
    [
        new() { Name="Design workstation", Host="192.0.2.10", Group="Work", Favorite=true, IsPreview=true, State=SessionState.Preview },
        new() { Name="Home office", Host="192.0.2.20", Group="Home", Favorite=true, Tone="Sand", IsPreview=true, State=SessionState.Preview },
        new() { Name="Development server", Host="198.51.100.30", Group="Cloud", Favorite=true, Tone="Mint", IsPreview=true, State=SessionState.Preview },
        new() { Name="File server", Host="192.0.2.40", Group="Work", IsPreview=true, State=SessionState.Preview },
        new() { Name="Studio PC", Host="192.0.2.50", Group="Work", Tone="Sand", IsPreview=true, State=SessionState.Preview },
        new() { Name="Living room PC", Host="192.0.2.60", Group="Home", Tone="Mint", IsPreview=true, State=SessionState.Preview }
    ];
}
