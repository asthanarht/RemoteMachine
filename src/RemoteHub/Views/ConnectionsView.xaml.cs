using RemoteHub.Models;

namespace RemoteHub.Views;

public partial class ConnectionsView : UserControl
{
    private readonly MainWindow _shell;
    private string _filter;
    private bool _ready;
    public ConnectionsView(MainWindow shell, string filter, string query)
    {
        _shell = shell; _filter = filter; InitializeComponent(); SearchBox.Text = query; _ready = true; Refresh();
    }
    public void FocusSearch() => SearchBox.Focus();
    public void Refresh()
    {
        var selected = (ConnectionsGrid.SelectedItem as ConnectionProfile)?.Id;
        string query = SearchBox.Text.Trim();
        var records = _shell.Profiles.Where(p => (_filter == "All" || (_filter == "Favorites" ? p.Favorite : p.Group == _filter)) &&
            (p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Host.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Group.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Protocol.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (StateFilter.SelectedIndex == 0 || (StateFilter.SelectedIndex == 1 ? p.IsSessionActive : p.State == SessionState.Saved))).ToList();
        ConnectionsGrid.ItemsSource = records;
        ConnectionsGrid.SelectedItem = records.FirstOrDefault(p => p.Id == selected) ?? records.FirstOrDefault();
        Heading.Text = _filter == "All" ? "All connections" : _filter;
        ResultCount.Text = $"{records.Count} of {_shell.Profiles.Count} connections";
        EmptyResults.Visibility = records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Details.Visibility = records.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in FilterTabs.Children.OfType<Button>())
        {
            bool selectedFilter = (string)button.Tag == _filter;
            button.SetResourceReference(BackgroundProperty, selectedFilter ? "AccentSoft" : "Transparent");
            button.SetResourceReference(ForegroundProperty, selectedFilter ? "Accent" : "Muted");
            button.BorderThickness = new Thickness(1);
            button.SetResourceReference(BorderBrushProperty, selectedFilter ? "SelectionIndicator" : "Transparent");
            button.FontWeight = selectedFilter ? FontWeights.SemiBold : FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, selectedFilter ? "Selected" : "Not selected");
        }
        bool noSavedConnections = _shell.ConnectionCount == 0;
        EmptyHeading.Text = noSavedConnections ? "Your computers belong here" : "No connections found";
        EmptyDescription.Text = noSavedConnections ? "Save a computer to make your first connection." : "Try another name, or clear your filters.";
        EmptyAddButton.Visibility = noSavedConnections ? Visibility.Visible : Visibility.Collapsed;
        ClearFiltersButton.Visibility = noSavedConnections ? Visibility.Collapsed : Visibility.Visible;
    }
    private ConnectionProfile? Selected => ConnectionsGrid.SelectedItem as ConnectionProfile;
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Details is null) return;
        Details.DataContext = Selected;
        FavoriteButton.Content = Selected?.Favorite == true ? "Remove from favorites" : "Add to favorites";
        ExportButton.IsEnabled = Selected is { Kind: ConnectionKind.Rdp, IsPreview: false };
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (_ready) Refresh(); }
    private void StateFilterChanged(object sender, SelectionChangedEventArgs e) { if (_ready) Refresh(); }
    private void NewClicked(object sender, RoutedEventArgs e) => _shell.ShowAdd();
    private void ImportClicked(object sender, RoutedEventArgs e) => _shell.ImportRdp();
    private void ExportClicked(object sender, RoutedEventArgs e) { if (Selected is { } profile) _shell.ExportRdp(profile); }
    private void FilterClicked(object sender, RoutedEventArgs e) { _filter = (string)((Button)sender).Tag; Refresh(); _shell.SelectConnectionFilter(_filter); }
    private void ClearClicked(object sender, RoutedEventArgs e) { _filter = "All"; StateFilter.SelectedIndex = 0; SearchBox.Clear(); Refresh(); _shell.SelectConnectionFilter(_filter); }
    private void ConnectClicked(object sender, RoutedEventArgs e) { if (Selected is { } profile) _shell.OpenConnection(profile); }
    private void DoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ConnectionsGrid, source) is DataGridRow { Item: ConnectionProfile profile })
            _shell.OpenConnection(profile);
    }
    private void ConnectionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || Selected is not { } profile) return;
        e.Handled = true; _shell.OpenConnection(profile);
    }
    private void FavoriteClicked(object sender, RoutedEventArgs e) { if (Selected is { } profile) { _shell.ToggleFavorite(profile); Refresh(); } }
    private void EditClicked(object sender, RoutedEventArgs e) { if (Selected is { } profile) _shell.ShowAdd(profile); }
    private void DeleteClicked(object sender, RoutedEventArgs e) { if (Selected is { } profile) { _shell.DeleteConnection(profile); Refresh(); } }
}
