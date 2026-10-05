using System.Text.Json;
using RemoteHub.Models;
using RemoteHub.Services;

namespace RemoteHub.Views;

public partial class SettingsView : UserControl
{
    private bool _ready;
    public SettingsView()
    {
        InitializeComponent();
        ThemeChoice.SelectedValue = App.Theme.Preference;
        SettingsPath.Text = App.Theme.SettingsPath;
        if (!App.Theme.SavesPreferences) StorageHelp.Text = "This preview changes appearance only in this window. Your saved settings are not changed.";
        _ready = true;
    }
    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ThemeChoice.SelectedValue is not ThemePreference preference) return;
        SettingsError.Visibility = Visibility.Collapsed;
        try { App.Theme.SetPreference(preference); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Write("settings-save-failed", details: new { type = error.GetType().Name });
            _ready = false; ThemeChoice.SelectedValue = App.Theme.Preference; _ready = true;
            ErrorText.Text = "Your theme choice could not be saved. The previous setting is unchanged.\n" + error.Message;
            SettingsError.Visibility = Visibility.Visible;
        }
    }
}
