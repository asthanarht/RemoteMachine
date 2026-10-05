namespace RemoteHub.Themes;

public sealed record ContrastColors(Color Background, Color Foreground, Color Highlight, Color HighlightText);

public sealed class ThemePalette
{
    public bool IsDark { get; }
    public bool IsHighContrast { get; }
    public IReadOnlyDictionary<string, Color> Colors { get; }
    public IReadOnlyDictionary<string, string> Terminal { get; }
    public string Name => IsHighContrast ? "High contrast" : IsDark ? "Dark" : "Light";

    private ThemePalette(bool dark, bool highContrast, Dictionary<string, Color> colors, Dictionary<string, string> terminal)
    {
        IsDark = dark; IsHighContrast = highContrast; Colors = colors; Terminal = terminal;
    }

    public static ThemePalette Create(bool dark, Color accent, ContrastColors? contrast = null)
    {
        accent = Color.FromRgb(accent.R, accent.G, accent.B);
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        void Add(string key, string light, string night) => colors.Add(key, Parse(dark ? night : light));
        Add("Surface", "#FFFFFF", "#182131");
        Add("Subtle", "#F2F6FC", "#202D40");
        Add("Sidebar", "#F6F8FC", "#131C2A");
        Add("Titlebar", "#F2F5FA", "#111925");
        Add("StatusSurface", "#FAFBFD", "#151E2D");
        Add("Ink", "#202C40", "#E5EDF8");
        Add("Muted", "#58667C", "#ACBBD0");
        Add("Line", "#DCE3EF", "#35445B");
        Add("FieldBorder", "#8492A6", "#74839A");
        Add("WindowBorder", "#8C99AB", "#66758B");
        Add("Success", "#216A46", "#91D9AF");
        Add("SuccessSurface", "#E9F3EF", "#1E382F");
        Add("SuccessLine", "#7D9F8B", "#5C9977");
        Add("Danger", "#AA3048", "#FFABB6");
        Add("DangerSurface", "#FFF2F4", "#39232E");
        Add("DangerLine", "#BC7E8B", "#B57688");
        Add("AvatarSurface", "#E7E1F0", "#372F49");
        Add("AvatarInk", "#625378", "#D9CAEB");
        Add("SandSurface", "#F6F0E6", "#362F25");
        Add("SandInk", "#795D2E", "#DDC18F");
        Add("ImageControl", "#253B59", "#253B59");
        Add("ImageControlInk", "#FFFFFF", "#FFFFFF");
        Add("InteractionShade", "#10233D", "#FFFFFF");
        Add("ScrollThumb", "#8190A6", "#8293AC");
        colors["AccentFill"] = accent;
        colors["OnAccent"] = ContrastRatio(accent, System.Windows.Media.Colors.White) >= ContrastRatio(accent, System.Windows.Media.Colors.Black)
            ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black;
        colors["AccentSoft"] = Blend(colors["Surface"], accent, dark ? .18 : .08);
        colors["Accent"] = Readable(accent, [colors["Surface"], colors["Subtle"], colors["Sidebar"], colors["AccentSoft"], colors["Titlebar"], colors["StatusSurface"]], 4.5, dark);
        colors["SelectionIndicator"] = colors["Accent"];
        colors["Transparent"] = System.Windows.Media.Colors.Transparent;
        // Both outlines stay fixed: one must contrast with any remote desktop, not just our local theme.
        colors["HandleRim"] = System.Windows.Media.Colors.White;
        colors["HandleOutline"] = Parse("#14223C");

        var terminal = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["background"] = dark ? "#152033" : "#F7F9FD",
            ["foreground"] = dark ? "#E5EDF8" : "#202C40",
            ["black"] = dark ? "#283548" : "#202C40", ["red"] = dark ? "#FF8C98" : "#A7253E",
            ["green"] = dark ? "#8CD8AE" : "#23633F", ["yellow"] = dark ? "#E8CD8B" : "#725611",
            ["blue"] = dark ? "#8BB5FF" : "#2453B8", ["magenta"] = dark ? "#C4A3E8" : "#794094",
            ["cyan"] = dark ? "#87D5DE" : "#146571", ["white"] = dark ? "#E5EDF8" : "#556379",
            ["brightBlack"] = dark ? "#96A7BE" : "#5C687B", ["brightRed"] = dark ? "#FFA5AE" : "#A7253E",
            ["brightGreen"] = dark ? "#B0E8C5" : "#23633F", ["brightYellow"] = dark ? "#F6E0A9" : "#725611",
            ["brightBlue"] = dark ? "#B4CEFF" : "#2453B8", ["brightMagenta"] = dark ? "#DCC3F6" : "#794094",
            ["brightCyan"] = dark ? "#B2E8EE" : "#146571", ["brightWhite"] = dark ? "#FFFFFF" : "#202C40"
        };
        if (contrast is not null)
        {
            dark = Luminance(contrast.Background) < Luminance(contrast.Foreground);
            foreach (string key in colors.Keys.ToArray())
                colors[key] = key switch
                {
                    "Transparent" or "HandleRim" or "HandleOutline" => colors[key],
                    "AccentFill" or "ImageControl" or "SelectionIndicator" => contrast.Highlight,
                    "OnAccent" or "ImageControlInk" => contrast.HighlightText,
                    "Surface" or "Subtle" or "Sidebar" or "Titlebar" or "StatusSurface" or "AccentSoft" or
                        "SuccessSurface" or "DangerSurface" or "AvatarSurface" or "SandSurface" => contrast.Background,
                    _ => contrast.Foreground
                };
            foreach (string key in terminal.Keys.ToArray())
                terminal[key] = Hex(key == "background" ? contrast.Background : contrast.Foreground);
        }
        terminal["cursor"] = terminal["foreground"];
        terminal["cursorAccent"] = terminal["background"];
        terminal["selectionBackground"] = Hex(contrast?.Highlight ?? Blend(Parse(terminal["background"]), accent, dark ? .25 : .22));
        terminal["selectionForeground"] = contrast is null ? terminal["foreground"] : Hex(contrast.HighlightText);
        colors["TerminalBackground"] = Parse(terminal["background"]);
        return new(dark, contrast is not null, colors, terminal);
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    public static double ContrastRatio(Color first, Color second)
    {
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static double Luminance(Color color)
    {
        static double Linear(byte value) { double c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    private static Color Blend(Color background, Color foreground, double amount) => Color.FromRgb(
        (byte)Math.Round(background.R + (foreground.R - background.R) * amount),
        (byte)Math.Round(background.G + (foreground.G - background.G) * amount),
        (byte)Math.Round(background.B + (foreground.B - background.B) * amount));
    private static Color Readable(Color color, Color[] backgrounds, double minimum, bool dark)
    {
        Color end = dark ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black;
        for (int step = 0; step <= 100; step++)
        {
            Color candidate = Blend(color, end, step / 100d);
            if (backgrounds.All(background => ContrastRatio(candidate, background) >= minimum)) return candidate;
        }
        throw new InvalidOperationException("The theme cannot provide a readable accent.");
    }
}
