using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteHub.Models;

namespace RemoteHub.Services;

public sealed class AppSettingsStore
{
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter<ThemePreference>(allowIntegerValues: false) }
    };
    public AppSettingsStore(string? directory = null) => DirectoryPath = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteWorkspace");
    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new();
        using var stream = File.OpenRead(FilePath);
        if (stream.Length > 65536) throw new InvalidDataException("The settings file must be no larger than 64 KB.");
        var settings = JsonSerializer.Deserialize<AppSettings>(stream, _json)
            ?? throw new InvalidDataException("The settings file is empty. Your original file has not been changed.");
        Validate(settings);
        return settings;
    }
    public void Save(AppSettings settings)
    {
        Validate(settings);
        Directory.CreateDirectory(DirectoryPath);
        AtomicFile.Write(FilePath, stream => JsonSerializer.Serialize(stream, settings, _json), FilePath + ".bak");
    }
    private static void Validate(AppSettings settings)
    {
        if (settings.Version != 1 || !Enum.IsDefined(settings.Theme))
            throw new InvalidDataException("The settings file has an unsupported version or theme. Your original file has not been changed.");
    }
}
