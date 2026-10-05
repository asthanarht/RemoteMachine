using System.Diagnostics;
using System.Text.Json;

namespace RemoteHub.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    public static string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteWorkspace", "logs");
    public static string LogPath => Path.Combine(DirectoryPath, $"session-{DateTime.Today:yyyy-MM-dd}.jsonl");
    public static string? LastWriteError { get; private set; }
    public static void Write(string action, Guid? connectionId = null, object? details = null)
    {
        var line = JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, action, connectionId, details });
        Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LastWriteError = error.Message;
            Trace.TraceError("Diagnostics could not be written: {0}", error.Message);
        }
    }
}
