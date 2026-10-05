using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using RemoteHub.Models;
using RemoteHub.Themes;

namespace RemoteHub.Services;

internal static class ThemeSelfTest
{
    public static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var original = App.Theme.Current;
        var slot = (ContentControl)window.FindName("PageContent");
        var fixtures = WorkspaceStore.PreviewConnections().ToArray();
        double width = window.Width, height = window.Height;
        try
        {
            foreach (var profile in fixtures) window.Profiles.Add(profile);
            foreach (bool dark in new[] { false, true })
            {
                App.Theme.Apply(ThemePalette.Create(dark, ThemePalette.Parse("#245CE5")));
                window.ShowHome(); await Task.Delay(80);
                var favoriteIcons = NativeSelfTest.Descendants<Controls.Icon>(slot).Where(icon => icon.Kind == "Star").ToArray();
                check(favoriteIcons.Length == 3 && favoriteIcons.All(icon => ColorOf(icon.Stroke) == App.Theme.Current.Colors["ImageControlInk"]),
                    $"{App.Theme.Current.Name}: favorite controls retain a contrasting icon over artwork");
                NativeSelfTest.Snapshot(window, dark ? "theme-dark-home" : "theme-light-home");
            }
            window.ShowConnections();
            var library = (UserControl)slot.Content;
            var grid = (DataGrid)library.FindName("ConnectionsGrid");
            var selected = grid.SelectedItem;
            foreach (bool dark in new[] { false, true })
            {
                var palette = ThemePalette.Create(dark, ThemePalette.Parse("#00A884"));
                App.Theme.Apply(palette); await Task.Delay(80);
                check(ColorOf(window.Background) == palette.Colors["Surface"] &&
                    ColorOf(window.Foreground) == palette.Colors["Ink"] &&
                    ColorOf(((Border)window.FindName("SidebarChrome")).Background) == palette.Colors["Sidebar"],
                    $"{palette.Name}: existing shell surfaces update immediately");
                check(ReferenceEquals(slot.Content, library) && ReferenceEquals(grid.SelectedItem, selected) &&
                    ColorOf(grid.Background) == palette.Colors["Surface"], $"{palette.Name}: live changes preserve the library and selection");
                check(NativeSelfTest.Descendants<ScrollViewer>(grid).First().ScrollableWidth < 1,
                    $"{palette.Name}: selection indicators do not introduce horizontal list overflow");
                NativeSelfTest.Snapshot(window, dark ? "theme-dark-library" : "theme-light-library");
            }
            foreach (var profile in fixtures) window.Profiles.Remove(profile);
            window.ShowAdd();
            var form = (UserControl)slot.Content;
            var name = (TextBox)form.FindName("NameInput");
            var group = (ComboBox)form.FindName("GroupInput");
            var options = (Expander)form.FindName("RdpOptions");
            name.Text = "Unsaved theme-change draft";
            ((TextBox)form.FindName("HostInput")).Text = "192.0.2.40";
            options.IsExpanded = true;
            name.CaretIndex = 7;
            foreach (bool dark in new[] { false, true })
            {
                var palette = ThemePalette.Create(dark, ThemePalette.Parse("#F3CF42"));
                App.Theme.Apply(palette); await Task.Delay(80);
                check(ReferenceEquals(slot.Content, form) && name.Text == "Unsaved theme-change draft" &&
                    options.IsExpanded && name.CaretIndex == 7 &&
                    ColorOf(name.Background) == palette.Colors["Surface"],
                    $"{palette.Name}: in-place theme changes preserve unsaved fields and expansion");
                var save = (Button)form.FindName("SaveButton");
                check(ColorOf(save.Background) == palette.Colors["AccentFill"] && ColorOf(save.Foreground) == palette.Colors["OnAccent"] &&
                    ThemePalette.ContrastRatio(ColorOf(save.Background), ColorOf(save.Foreground)) >= 4.5,
                    $"{palette.Name}: bright system accents use readable primary-button labels");
                group.IsDropDownOpen = true; await Task.Delay(80);
                var popup = (Popup)group.Template.FindName("PART_Popup", group);
                check(popup.IsOpen && ColorOf(((Border)popup.Child).Background) == palette.Colors["Surface"],
                    $"{palette.Name}: the open combo popup follows the theme");
                group.IsDropDownOpen = false;
                NativeSelfTest.Snapshot(window, dark ? "theme-dark-form" : "theme-light-form");
            }
            ((Button)form.FindName("SshChoice")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            await Task.Delay(100); NativeSelfTest.Snapshot(window, "theme-dark-ssh-compact");
            var contrast = new ContrastColors(Colors.Black, Colors.White, Colors.Yellow, Colors.Black);
            App.Theme.Apply(ThemePalette.Create(false, Colors.Red, contrast));
            await Task.Delay(80);
            check(ColorOf(name.Background) == Colors.Black && ColorOf(name.Foreground) == Colors.White &&
                ColorOf(((Button)form.FindName("SaveButton")).Foreground) == Colors.Black,
                "Windows high contrast takes priority on the existing form");
            check(ColorOf(((Button)form.FindName("SshChoice")).BorderBrush) == Colors.Yellow &&
                ColorOf(((Button)form.FindName("RdpChoice")).BorderBrush) == Colors.White,
                "High contrast keeps the selected protocol visibly distinct");
            NativeSelfTest.Snapshot(window, "theme-high-contrast-form");
            var applied = App.Theme.Current;
            SendMessage(new WindowInteropHelper(window).Handle, 0x001A, IntPtr.Zero, IntPtr.Zero);
            for (int attempt = 0; attempt < 50 && ReferenceEquals(App.Theme.Current, applied); attempt++) await Task.Delay(20);
            check(!ReferenceEquals(App.Theme.Current, applied) && App.Theme.Current.Name == original.Name &&
                App.Theme.Current.Colors["AccentFill"] == original.Colors["AccentFill"],
                "The real window notification path restores the current Windows app theme and accent");
        }
        finally
        {
            foreach (var profile in fixtures) window.Profiles.Remove(profile);
            window.Width = width; window.Height = height;
            App.Theme.Apply(original);
            window.ShowHome();
        }
    }
    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
