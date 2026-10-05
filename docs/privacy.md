# RemoteMachine privacy policy

Effective date: October 5, 2026

RemoteMachine is a Windows remote-access client published by **Asthanarht**. It connects to computers that you choose through RDP, SSH, or VNC. It does not provide a cloud relay, a remote-access server, or an application account service.

## Information used by the app

The app processes the hostnames or IP addresses, ports, display names, groups, usernames or domains, and connection preferences you provide. SSH profiles can contain a private-key file path, but the app does not copy private-key contents into its saved profiles.

Remote screen content, keyboard and pointer input, and terminal data are processed to operate the connection. The app does not intentionally record remote screens or save terminal transcripts. Clipboard transfer occurs only through supported features that you enable or invoke.

The app reads your local Windows username and account domain to display your Windows identity and offer an optional account-name shortcut. It does not retrieve your Windows password, PIN, or saved Credential Manager secrets.

## Where information goes

Connection traffic is sent to the remote host you select, and to any intermediary you have independently configured, such as an SSH proxy or VPN. Your remote system and network provider control their own processing and logging.

RemoteMachine does not send app telemetry, sell personal information, scan your network for hosts, or upload your connection profiles to the publisher.

Windows, Microsoft Store, Microsoft Edge WebView2, and other system components may process information under their own settings and privacy terms. Installing or using this app does not disable those components' diagnostic or update behavior.

## Local storage and diagnostics

Connection metadata and appearance settings are stored locally in the app's data area. The existing desktop build uses `%LOCALAPPDATA%\RemoteWorkspace`; Windows may redirect local data for the packaged Store application.

Local diagnostics can contain event times, connection identifiers, error messages, and file paths. WebView2 also maintains local runtime/profile data. VNC uses an InPrivate WebView2 controller. Review diagnostic files before sharing them with anyone.

The app does not store connection passwords in its profiles or intentionally log authentication secrets. Windows handles RDP authentication and optional Windows credential saving. OpenSSH handles SSH passwords, keys, host verification, and its own configuration. VNC credentials are requested for each connection, handled in memory, and not saved in profiles.

## Connection security

RDP and SSH security depends on the remote server, negotiated connection, and your configuration. Verify server identities and do not ignore unexpected certificate or SSH host-key warnings.

**The current VNC screen/input transport is not encrypted.** Apple's ARD credential exchange does not provide encrypted screen transport or authenticated server identity. Use VNC only on a trusted network, VPN, or independently established SSH tunnel. Do not expose port 5900 directly to the internet. RemoteMachine does not create a tunnel automatically.

## Your controls

You choose which hosts to save and connect to. You can edit or remove profiles in the app, disable optional sharing, use VNC view-only mode, disconnect sessions, and change local appearance settings.

To remove local files manually, close the app first. Removing the app may not delete data left by an earlier unpackaged version, or information independently stored by Windows, OpenSSH, or your remote systems.

## Support and changes

The publisher's support page is [github.com/asthanarht/RemoteMachine/issues](https://github.com/asthanarht/RemoteMachine/issues). GitHub issues are public: do not post passwords, private keys, access tokens, private host details, diagnostic files, or screenshots containing personal information. For questions about this policy, open an issue without personal data; request a private contact method before sharing any private details.

GitHub handles information you submit to its service under its own privacy terms. A support report is sent only when you choose to submit it; the app does not automatically upload one.

This policy will be updated when relevant app behavior changes.
