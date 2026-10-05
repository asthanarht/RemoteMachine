using RemoteHub.Models;
using RemoteHub.Services;

namespace RemoteHub.Views;

public partial class AddConnectionView : UserControl
{
    private readonly MainWindow _shell;
    private readonly ConnectionProfile? _original;
    private ConnectionKind _kind;
    public AddConnectionView(MainWindow shell, ConnectionProfile? original = null, RdpImport? import = null)
    {
        InitializeComponent(); _shell = shell; _original = original;
        UseWindowsAccountButton.ToolTip = $"Fill in {_shell.CurrentWindowsAccount.QualifiedName}. This does not grant access or bypass Windows authentication.";
        var initial = original ?? import?.Profile;
        SetProtocol(initial?.Kind ?? ConnectionKind.Rdp, false);
        if (initial is null) return;
        Heading.Text = import is null ? "A familiar connection, refined." : "Review your imported connection.";
        NameInput.Text = initial.Name; HostInput.Text = initial.Host; PortInput.Text = initial.Port.ToString();
        UserInput.Text = initial.UserName; DomainInput.Text = initial.Domain; FavoriteInput.IsChecked = initial.Favorite;
        ClipboardInput.IsChecked = initial.RedirectClipboard; SaveCredentialsInput.IsChecked = initial.AllowWindowsCredentialSaving;
        SmartCardsInput.IsChecked = initial.RedirectSmartCards; KeyInput.Text = initial.SshKeyPath;
        LowBandwidthInput.IsChecked = initial.LowBandwidth;
        VncViewOnlyInput.IsChecked = initial.VncViewOnly; VncTrustedInput.IsChecked = initial.VncTrustedNetwork;
        VncScrollSpeedInput.SelectedItem = initial.VncScrollSpeed;
        GroupInput.SelectedIndex = initial.Group switch { "Home" => 1, "Cloud" => 2, _ => 0 };
        if (import is not null)
        {
            ImportNotice.Visibility = Visibility.Visible;
            RdpOptions.IsExpanded = true;
            ImportMessage.Text = "Nothing has connected or been saved. The original file is unchanged. Only supported PC settings are imported; passwords are never imported.\n\n" +
                (import.Warnings.Count == 0 ? "The connection settings are ready to review." : string.Join("\n\n", import.Warnings));
        }
    }
    private void ProtocolClicked(object sender, RoutedEventArgs e) => SetProtocol(Enum.Parse<ConnectionKind>((string)((Button)sender).Tag), true);
    private void SetProtocol(ConnectionKind kind, bool adjustPort)
    {
        if (adjustPort && PortInput.Text == ConnectionProfile.DefaultPort(_kind).ToString()) PortInput.Text = ConnectionProfile.DefaultPort(kind).ToString();
        _kind = kind;
        bool ssh = kind == ConnectionKind.Ssh;
        RdpOptions.Visibility = RdpHelp.Visibility = UseWindowsAccountButton.Visibility = kind == ConnectionKind.Rdp ? Visibility.Visible : Visibility.Collapsed;
        SshOptions.Visibility = SshHelp.Visibility = ssh ? Visibility.Visible : Visibility.Collapsed;
        VncOptions.Visibility = VncHelp.Visibility = kind == ConnectionKind.Vnc ? Visibility.Visible : Visibility.Collapsed;
        UserInput.Tag = ssh ? "Use OpenSSH configuration" : "Ask on connect";
        AccountHint.Text = ssh ? "Sign in directly in the embedded terminal. Leave the username blank to use your OpenSSH configuration or its default account." :
            "Windows asks for credentials when you connect. Your remote computer may use a different account. No password is stored here.";
        if (kind == ConnectionKind.Vnc)
        {
            UserInput.Tag = "Mac account short name";
            AccountHint.Text = "Use your Mac account, not your Windows account or Apple ID. Passwords are requested only when connecting and never saved.";
        }
        foreach (var button in new[] { RdpChoice, SshChoice, VncChoice })
        {
            bool selected = (string)button.Tag == kind.ToString();
            button.SetResourceReference(BackgroundProperty, selected ? "AccentSoft" : "Surface");
            button.SetResourceReference(BorderBrushProperty, selected ? "SelectionIndicator" : "FieldBorder");
            button.SetResourceReference(ForegroundProperty, selected ? "Accent" : "Ink");
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, selected ? "Selected" : "Not selected");
        }
    }
    private void BrowseKeyClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose an SSH private key", Filter = "Key files|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(_shell) == true) KeyInput.Text = dialog.FileName;
    }
    private void CancelClicked(object sender, RoutedEventArgs e) => _shell.ShowConnections();
    private void UseWindowsAccountClicked(object sender, RoutedEventArgs e)
    {
        var account = _shell.CurrentWindowsAccount;
        UserInput.Text = account.UserName;
        DomainInput.Text = account.UserName.Contains('\\') || account.UserName.Contains('@') ? "" : account.Domain;
        AccountHint.Text = $"Suggested account: {account.QualifiedName}. Windows still handles sign-in; the remote computer may require a different account.";
        UserInput.Focus();
    }
    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortInput.Text, out int port)) port = 0;
        string? error = ConnectionProfile.Validate(NameInput.Text, HostInput.Text, port);
        if (error is not null) { ErrorText.Text = error; ErrorPanel.Visibility = Visibility.Visible; return; }
        var profile = new ConnectionProfile
        {
            Id = _original?.Id ?? Guid.NewGuid(), Name = NameInput.Text.Trim(), Host = HostInput.Text.Trim(), Port = port,
            Kind = _kind, SshKeyPath = _kind == ConnectionKind.Ssh ? KeyInput.Text.Trim() : "",
            VncViewOnly = _kind == ConnectionKind.Vnc && VncViewOnlyInput.IsChecked == true,
            VncTrustedNetwork = _kind == ConnectionKind.Vnc && VncTrustedInput.IsChecked == true,
            VncScrollSpeed = _kind == ConnectionKind.Vnc ? (int)VncScrollSpeedInput.SelectedItem : 1,
            UserName = UserInput.Text.Trim(), Domain = _kind == ConnectionKind.Rdp ? DomainInput.Text.Trim() : "", Group = (string)((ComboBoxItem)GroupInput.SelectedItem).Content,
            Favorite = FavoriteInput.IsChecked == true, RedirectClipboard = _kind == ConnectionKind.Rdp && ClipboardInput.IsChecked == true,
            RedirectSmartCards = SmartCardsInput.IsChecked == true,
            AllowWindowsCredentialSaving = SaveCredentialsInput.IsChecked == true, LowBandwidth = LowBandwidthInput.IsChecked == true,
            Tone = GroupInput.SelectedIndex switch { 1 => "Sand", 2 => "Mint", _ => "Blue" },
            LastConnected = _original?.Kind == _kind ? _original.LastConnected : null,
            LastOpened = _original?.Kind == _kind ? _original.LastOpened : null
        };
        if (profile.Validate() is { } validation) { ErrorText.Text = validation; ErrorPanel.Visibility = Visibility.Visible; return; }
        if (_shell.SaveConnection(profile, _original)) _shell.ShowConnections();
    }
}
