using System.Text.Json;
using RemoteHub.Models;
using RemoteHub.Native;
using RemoteHub.Services;

internal static class SettingsTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        var store = new AppSettingsStore(directory);
        check(store.Load().Theme == ThemePreference.System && !File.Exists(store.FilePath), "Missing settings default to System without altering existing files");
        foreach (var theme in Enum.GetValues<ThemePreference>())
        {
            store.Save(new AppSettings { Theme = theme });
            check(new AppSettingsStore(directory).Load().Theme == theme && File.ReadAllText(store.FilePath).Contains($"\"{theme}\""),
                $"The {theme} preference survives a new settings-store instance and uses a readable JSON value");
        }
        check(File.Exists(store.FilePath + ".bak") && !File.ReadAllText(store.FilePath).Contains("Connections"),
            "Settings use atomic backups independently of saved connections");
        foreach (string text in new[] { "null", "{broken", "{\"version\":9,\"theme\":\"Dark\"}", "{\"theme\":\"Unsupported\"}",
            "{\"theme\":2}", "{\"version\":1,\"theme\":\"Light\",\"unexpected\":true}", "{\"theme\":\"Light\",\"theme\":\"Dark\"}" })
        {
            File.WriteAllText(store.FilePath, text);
            bool rejected = false;
            try { store.Load(); }
            catch (Exception error) when (error is JsonException or InvalidDataException) { rejected = true; }
            check(rejected && File.ReadAllText(store.FilePath) == text, "Invalid settings are reported and preserved rather than replaced");
        }
        foreach (bool systemDark in new[] { false, true })
            check(SystemTheme.UsesDarkPalette(ThemePreference.System, systemDark) == systemDark &&
                !SystemTheme.UsesDarkPalette(ThemePreference.Light, systemDark) &&
                SystemTheme.UsesDarkPalette(ThemePreference.Dark, systemDark),
                "Manual appearance choices stay fixed while System follows Windows");
        var monitors = new[]
        {
            new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
            new NativeMethods.Rect { Left = -2560, Top = -400, Right = 0, Bottom = 1040 },
            new NativeMethods.Rect { Left = 1920, Top = 0, Right = 5760, Bottom = 2160 }
        };
        foreach (var monitor in monitors)
        {
            foreach (string edge in new[] { "Left", "Top", "Right", "Bottom" })
            {
                var work = monitor;
                switch (edge)
                {
                    case "Left": work.Left += 60; break;
                    case "Top": work.Top += 60; break;
                    case "Right": work.Right -= 60; break;
                    case "Bottom": work.Bottom -= 60; break;
                }
                var metrics = WindowWorkArea.MaximizedMetrics(monitor, work);
                check(metrics.Position.X + monitor.Left == work.Left && metrics.Position.Y + monitor.Top == work.Top &&
                    metrics.Size.X == work.Width && metrics.Size.Y == work.Height,
                    $"Maximized physical-pixel bounds honor a {edge} taskbar on monitor starting at {monitor.Left},{monitor.Top}");
            }
        }
    }
}
