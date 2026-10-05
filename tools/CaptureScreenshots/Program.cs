using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RemoteHub;
using RemoteHub.Models;
using RemoteHub.Views;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--design-preview")
        {
            Console.Error.WriteLine("Usage: CaptureScreenshots --design-preview <output-directory>");
            return 1;
        }
        string output = Path.GetFullPath(args[1]);
        var method = typeof(App).Assembly.GetType("RemoteHub.Services.NativeSelfTest")?
            .GetMethod("Snapshot", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("The app's existing WPF snapshot helper is unavailable.");
        var snapshot = method.CreateDelegate<Action<Window, string>>();
        var app = new App();
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                if (app.MainWindow is not MainWindow { IsDesignPreview: true } window || App.Theme.SavesPreferences ||
                    window.Profiles.Any(profile => !profile.IsPreview))
                    throw new InvalidOperationException("Screenshot capture requires an isolated, non-persisting design preview.");

                Directory.CreateDirectory(output);
                window.Width = 1400;
                window.Height = 960;
                window.Profiles[0].Name = "Windows workstation";
                window.Profiles[1].Name = "Mac studio";
                window.Profiles[1].Kind = ConnectionKind.Vnc;
                window.Profiles[1].Port = 5900;
                window.Profiles[1].VncTrustedNetwork = true;
                window.Profiles[2].Name = "Linux build server";
                window.Profiles[2].Kind = ConnectionKind.Ssh;
                window.Profiles[2].Port = 22;

                App.Theme.SetPreference(ThemePreference.Light);
                window.ShowHome();
                await Task.Delay(250);
                Capture("home-light");

                App.Theme.SetPreference(ThemePreference.Dark);
                window.ShowConnections();
                await Task.Delay(200);
                Capture("connections-dark");

                App.Theme.SetPreference(ThemePreference.Light);
                window.ShowSettings();
                var settings = (SettingsView)((ContentControl)window.FindName("PageContent")).Content;
                ((TextBox)settings.FindName("SettingsPath")).Text =
                    @"C:\Users\alex.morgan\AppData\Local\RemoteWorkspace\settings.json";
                await Task.Delay(200);
                Capture("settings-light");

                window.OpenConnection(window.Profiles[0]);
                await Task.Delay(300);
                window.EnterFocus(window.ActiveSession ?? throw new InvalidOperationException("The fictional session did not open."));
                await Task.Delay(200);
                window.Activate();
                window.ShowFocusControls();
                Capture("fullscreen-controls");
                window.ExitFocus();
                window.Close();

                void Capture(string name)
                {
                    snapshot(window, name);
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "screenshots", name + ".png"),
                        Path.Combine(output, name + ".png"), overwrite: true);
                    Console.WriteLine($"Captured fictional preview: {name}.png");
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                app.Shutdown(1);
            }
        }));
        return app.Run();
    }
}
