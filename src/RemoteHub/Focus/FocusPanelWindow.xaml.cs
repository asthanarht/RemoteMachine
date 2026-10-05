using RemoteHub.Models;

namespace RemoteHub.Focus;

public partial class FocusPanelWindow : Window
{
    private bool _updating;
    public event Action<string>? ActionRequested;
    public event Action<DockEdge>? EdgeRequested;
    public event Action<bool>? InvisibleRequested;
    public event Action<int>? ScrollSpeedRequested;
    public FocusPanelWindow(Window owner)
    {
        InitializeComponent(); Owner = owner;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; ActionRequested?.Invoke("Close"); } };
    }
    public void Update(ConnectionProfile profile, DockEdge edge, bool invisible, bool canHide)
    {
        _updating = true;
        ComputerName.Text = profile.Name;
        ConnectionState.Text = profile.IsPreview ? "Local design preview - no connection" : $"{profile.Protocol} - {profile.StateText}";
        VncScrollOptions.Visibility = profile.Kind == ConnectionKind.Vnc ? Visibility.Visible : Visibility.Collapsed;
        VncScrollSpeed.SelectedItem = profile.VncScrollSpeed;
        VncScrollSpeed.IsEnabled = !profile.VncViewOnly && profile.State == SessionState.Connected;
        InvisibleToggle.IsEnabled = canHide || invisible;
        InvisibleToggle.IsChecked = invisible;
        InvisibleHelp.Text = invisible ? "Hidden by your saved preference. Ctrl + Alt + Space brings controls back; Ctrl + Alt + Home leaves Focus mode."
            : !canHide ? "A recovery shortcut is unavailable. The handle stays visible so you cannot become trapped."
            : "The handle stays visible unless you turn this on. Your choice is remembered.";
        foreach (var button in new[] { LeftDock, TopDock, RightDock, BottomDock })
        {
            bool selected = (string)button.Tag == edge.ToString();
            button.SetResourceReference(BackgroundProperty, selected ? "AccentSoft" : "Surface");
            button.SetResourceReference(BorderBrushProperty, selected ? "SelectionIndicator" : "FieldBorder");
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
        _updating = false;
    }
    private void CloseClicked(object sender, RoutedEventArgs e) => ActionRequested?.Invoke("Close");
    private void ActionClicked(object sender, RoutedEventArgs e) => ActionRequested?.Invoke((string)((Button)sender).Tag);
    private void DockClicked(object sender, RoutedEventArgs e) => EdgeRequested?.Invoke(Enum.Parse<DockEdge>((string)((Button)sender).Tag));
    private void InvisibleChanged(object sender, RoutedEventArgs e) { if (!_updating) InvisibleRequested?.Invoke(InvisibleToggle.IsChecked == true); }
    private void ScrollSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && VncScrollSpeed?.SelectedItem is int speed) ScrollSpeedRequested?.Invoke(speed);
    }
}
