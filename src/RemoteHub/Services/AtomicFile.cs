namespace RemoteHub.Services;

internal static class AtomicFile
{
    public static void Write(string path, Action<Stream> write, string? backup = null)
    {
        path = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A destination directory is required.", nameof(path));
        string temporary = Path.Combine(directory, $".remote-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                write(stream);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, backup);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
