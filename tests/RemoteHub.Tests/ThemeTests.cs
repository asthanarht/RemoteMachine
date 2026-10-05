using System.Windows.Media;
using RemoteHub.Themes;

internal static class ThemeTests
{
    public static void Run(Action<bool, string> check)
    {
        var samples = new List<Color>
        {
            Colors.Black, Colors.White, Colors.Yellow, Colors.Lime, Colors.Cyan,
            Colors.Magenta, Colors.Red, Colors.Blue, ThemePalette.Parse("#245CE5")
        };
        for (int r = 0; r <= 255; r += 51)
            for (int g = 0; g <= 255; g += 51)
                for (int b = 0; b <= 255; b += 51)
                    samples.Add(Color.FromRgb((byte)r, (byte)g, (byte)b));
        foreach (bool dark in new[] { false, true })
        {
            bool readableText = true, readableAccent = true, exactAccent = true, terminalText = true, outlines = true;
            foreach (Color accent in samples)
            {
                var palette = ThemePalette.Create(dark, accent);
                var c = palette.Colors;
                foreach (string surface in new[] { "Surface", "Sidebar", "Subtle", "Titlebar", "StatusSurface", "AccentSoft" })
                {
                    readableText &= ThemePalette.ContrastRatio(c["Ink"], c[surface]) >= 4.5 && ThemePalette.ContrastRatio(c["Muted"], c[surface]) >= 4.5;
                    readableAccent &= ThemePalette.ContrastRatio(c["Accent"], c[surface]) >= 4.5;
                }
                foreach (var pair in new[] { ("Success", "SuccessSurface"), ("Danger", "DangerSurface"), ("AvatarInk", "AvatarSurface"), ("SandInk", "SandSurface"), ("ImageControlInk", "ImageControl") })
                    readableText &= ThemePalette.ContrastRatio(c[pair.Item1], c[pair.Item2]) >= 4.5;
                readableAccent &= ThemePalette.ContrastRatio(c["OnAccent"], c["AccentFill"]) >= 4.5;
                exactAccent &= c["AccentFill"] == accent;
                outlines &= ThemePalette.ContrastRatio(c["FieldBorder"], c["Surface"]) >= 3;
                Color background = ThemePalette.Parse(palette.Terminal["background"]);
                foreach (var pair in palette.Terminal.Where(p => p.Key is not "background" and not "selectionBackground" and not "cursorAccent" and not "black"))
                    terminalText &= ThemePalette.ContrastRatio(ThemePalette.Parse(pair.Value), background) >= 4.5;
                terminalText &= ThemePalette.ContrastRatio(ThemePalette.Parse(palette.Terminal["selectionForeground"]), ThemePalette.Parse(palette.Terminal["selectionBackground"])) >= 4.5;
            }
            string name = dark ? "Dark" : "Light";
            check(readableText, $"{name}: body, secondary, and semantic text exceed 4.5:1 across {samples.Count} accents");
            check(readableAccent, $"{name}: accent text and primary-button labels exceed 4.5:1");
            check(exactAccent, $"{name}: primary fills and the fullscreen handle preserve the exact system accent");
            check(terminalText, $"{name}: terminal text, ANSI colors, and selections remain readable");
            check(outlines, $"{name}: input boundaries exceed 3:1");
        }
        foreach (bool dark in new[] { false, true })
        {
            var background = dark ? Colors.Black : Colors.White;
            var foreground = dark ? Colors.White : Colors.Black;
            var contrast = new ContrastColors(background, foreground, Colors.Yellow, Colors.Black);
            var palette = ThemePalette.Create(!dark, Colors.Red, contrast);
            check(palette.IsHighContrast && palette.IsDark == dark && palette.Colors["Surface"] == background &&
                palette.Colors["Ink"] == foreground && palette.Colors["AccentFill"] == Colors.Yellow &&
                palette.Colors["OnAccent"] == Colors.Black, "High contrast uses Windows' chosen colors ahead of ordinary dark mode and accent");
            check(palette.Terminal["background"] == ThemePalette.Hex(background) && palette.Terminal["foreground"] == ThemePalette.Hex(foreground),
                "The terminal honors the same high-contrast palette");
        }
    }
}
