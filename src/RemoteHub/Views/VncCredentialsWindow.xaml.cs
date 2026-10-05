using System.Text;

namespace RemoteHub.Views;

internal sealed record VncCredentials(string Username, string Password);
public partial class VncCredentialsWindow : Window
{
    private readonly bool _mac;
    internal VncCredentials? Credentials { get; private set; }
    internal VncCredentialsWindow(Window owner, string endpoint, string username, bool mac)
    {
        InitializeComponent(); Owner = owner; _mac = mac; Endpoint.Text = endpoint; User.Text = username;
        if (!mac)
        {
            Heading.Text = "Enter the VNC password"; UserFields.Visibility = Visibility.Collapsed; Password.MaxLength = 8;
            Help.Text = "Use the separate VNC password configured in Screen Sharing, not your Mac account password. Standard VNC accepts at most 8 ASCII characters. This password is never saved.";
        }
        Loaded += (_, _) => { if (mac && User.Text.Length == 0) User.Focus(); else Password.Focus(); };
        Closed += (_, _) => Password.Clear();
    }
    private void ConnectClicked(object sender, RoutedEventArgs e)
    {
        string password = Password.Password, username = User.Text.Trim();
        if (password.Length == 0 || password.Contains('\0') || username.Contains('\0') || (_mac && (username.Length == 0 || Encoding.UTF8.GetByteCount(username) > 63 || Encoding.UTF8.GetByteCount(password) > 63)) ||
            (!_mac && (password.Length > 8 || password.Any(c => c is < ' ' or > '~'))))
        {
            Error.Text = _mac ? "Enter a username and password of at most 63 UTF-8 bytes each." : "Enter a VNC password of 1 to 8 printable ASCII characters.";
            Error.Visibility = Visibility.Visible; return;
        }
        Credentials = new(username, password); DialogResult = true;
    }
}
