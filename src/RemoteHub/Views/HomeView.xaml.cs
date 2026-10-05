using RemoteHub.Models;

namespace RemoteHub.Views;

public partial class HomeView : UserControl
{
    private readonly MainWindow _shell;
    public HomeView(MainWindow shell)
    {
        InitializeComponent(); _shell = shell; DataContext = shell;
        PreviewNotice.Visibility = shell.IsDesignPreview ? Visibility.Visible : Visibility.Collapsed;
        RefreshEmptyStates();
        Loaded += (_, _) => { _shell.PropertyChanged += ShellDataChanged; RefreshEmptyStates(); };
        Unloaded += (_, _) => _shell.PropertyChanged -= ShellDataChanged;
    }
    private void ShellDataChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindow.FavoriteCount) or nameof(MainWindow.RecentConnections)) RefreshEmptyStates();
    }
    private void RefreshEmptyStates()
    {
        EmptyFavorites.Visibility = _shell.FavoriteCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool hasRecent = _shell.RecentConnections.Any();
        EmptyRecent.Visibility = hasRecent ? Visibility.Collapsed : Visibility.Visible;
        RecentGrid.Visibility = hasRecent ? Visibility.Visible : Visibility.Collapsed;
    }
    public void FocusSearch() => SearchBox.Focus();
    private void NewClicked(object sender, RoutedEventArgs e) => _shell.ShowAdd();
    private void ImportClicked(object sender, RoutedEventArgs e) => _shell.ImportRdp();
    private void FavoritesClicked(object sender, RoutedEventArgs e) => _shell.ShowConnections("Favorites");
    private void AllClicked(object sender, RoutedEventArgs e) => _shell.ShowConnections();
    private void GroupClicked(object sender, RoutedEventArgs e) => _shell.ShowConnections((string)((Button)sender).Tag);
    private void SearchClicked(object sender, RoutedEventArgs e) => _shell.ShowConnections("All", SearchBox.Text);
    private void SearchKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) SearchClicked(sender, e); }
    private void ConnectClicked(object sender, RoutedEventArgs e) => _shell.OpenConnection((ConnectionProfile)((Button)sender).Tag);
    private void FavoriteClicked(object sender, RoutedEventArgs e) { _shell.ToggleFavorite((ConnectionProfile)((Button)sender).Tag); _shell.ShowHome(); }
    private void RecentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(RecentGrid, source) is DataGridRow { Item: ConnectionProfile profile })
            _shell.OpenConnection(profile);
    }
    private void RecentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || RecentGrid.SelectedItem is not ConnectionProfile profile) return;
        e.Handled = true; _shell.OpenConnection(profile);
    }
}
