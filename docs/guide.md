# RemoteMachine technical guide

[Back to the README](../README.md)

A Windows RDP, SSH, and VNC workspace implementing the approved light "Connected workspace" design. C# / .NET 10 and WPF host Microsoft's installed Remote Desktop ActiveX control, an embedded Windows OpenSSH terminal, and a local noVNC renderer for macOS Screen Sharing. This app is a client, not a remote-access server.

## Run

After building for ARM64, run from the repository root:

```powershell
& '.\artifacts\win-arm64\RemoteMachine.exe'
```

Or, from this directory:

```powershell
.\Run.ps1
.\Run.ps1 -DesignPreview
.\Run.ps1 -FocusPreview
```

The normal app starts with your saved connections, or an empty workspace. Use **New connection** to save a PC name or IP address, then **Connect**. The destination must have Remote Desktop enabled and be reachable on the configured port. Windows Home can run this client but does not normally host incoming RDP sessions.

Version 0.3.5 adds **grouped live-session navigation**: expandable Work, Home, and Cloud lists switch existing sessions in one click. Version 0.3.4 added **Fit to window**, an optional expanded session layout that uses the full app content area beside the sidebar without entering fullscreen. Version 0.3.3 added **per-connection VNC scroll speed**, adjustable from 1x to 8x without reconnecting, including fullscreen controls. Existing connections keep 1x unless explicitly changed. Version 0.3.2 fixed lost mouse-wheel distance in **embedded VNC for macOS Screen Sharing**: fast wheel events now send every complete scroll step, and fine movement retains its fractional remainder. Version 0.3.1 introduced the bounded shared-memory screen-data path and explicit viewport refitting after native resize, fullscreen, and resume. Image quality and the Mac's resolution are unchanged. Version 0.3.0 introduced embedded VNC and applied the supplied R-and-arrow logo to the shell, session controls, Settings, window/taskbar identity, and executable. It retains saved System/Light/Dark appearance settings, the top-right Windows account menu, taskbar-aware maximized bounds, embedded SSH, standard-PC `.rdp` import/export, and movable fullscreen controls. The Microsoft RDP transport and fullscreen input-handoff behavior are unchanged.

Version 0.3.6 renames the app to **RemoteMachine** (one word, singular). Window titles, sidebar, About, dialogs, accessible labels, embedded page titles, executable metadata, and the launch script use the new name. The supplied R-and-arrow symbol and executable icon are unchanged. The longer sidebar name is sized to fit without clipping; Settings pairs the original symbol with native text instead of showing the previous name baked into the old wordmark.

The source retains solution `RemoteHub.sln`, project `src\RemoteHub\RemoteHub.csproj`, and internal namespace `RemoteHub`; these developer-only names are not the product name. The built executable is **`RemoteMachine.exe`**. The app still uses `%LOCALAPPDATA%\RemoteWorkspace` for existing connections, settings, logs, and terminal data. This intentional compatibility path avoids migrating or losing user data. Existing shortcuts to `RemoteHub.exe` must be updated to the new executable.

### Logo assets

`src\RemoteHub\Assets\RemoteHub-original.png` preserves the supplied 1536x1024 R-and-arrow artwork sheet unchanged. `RemoteHub-mark.png` is a direct 264x264 crop of the largest standalone symbol, retaining its original colors and alpha channel. Asset filenames retain their original provenance. Only the standalone mark and ICO are embedded: the source sheet and `RemoteHub-lockup.png`, which contain the previous name, are retained in source but are not displayed or included as app resources. The labels printed on the source sheet are not separate high-resolution files; no vector or invented 1024-pixel source is claimed. The compact sidebar and Settings use the supplied symbol alongside accessible, theme-adaptive native text. The previous source artwork is retained in `Assets\Archive`.

`RemoteHub.ico` contains 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256-pixel images for Windows scaling. It is compiled into `RemoteMachine.exe` and also assigned to WPF windows; it is not just a shortcut icon. These are raster assets, not editable vector files. To reproduce them locally with the existing .NET SDK:

```powershell
& '.\.tools\dotnet\dotnet.exe' run --project '.\tools\BrandAssets\BrandAssets.csproj' -- '.\src\RemoteHub\Assets'
```

Design preview uses fictional, explicitly labeled profiles and an illustrative desktop. It never contacts the sample addresses or writes them to your real workspace. Preview screens are not live remote content.

The published app is framework-dependent: Windows 11 and the matching **.NET 10 Desktop Runtime** are required. Install the runtime matching your build architecture. Do not copy only the EXE; keep the entire published folder.

## Switch between connected machines

Click **Work**, **Home**, or **Cloud** in the sidebar to expand its active sessions without leaving the current desktop. Click a machine underneath to switch directly to its existing RDP, VNC, or SSH session. The displayed machine has a highlighted row and check mark; each row shows its protocol and actual state. Long names are shortened visually, with the full name and endpoint available on hover.

The **live** count includes connected/open sessions as well as those currently connecting, signing in, or reconnecting; it is not a count of saved machines. Pending sessions show their real status, and selecting one does not open a second connection. Ended/failed sessions leave the list. An empty group shows **No active sessions**.

Use **View all Work/Home/Cloud connections** below a group's list for its complete library, including saved/offline machines. **All connections** and **Favorites** continue to work as before. Groups expand when opening or resuming their sessions and can be collapsed without disconnecting anything. The sidebar scrolls when needed, while Settings and the session summary stay accessible.

Switching retains each session's viewer, authentication, fit-to-window layout, and scroll-speed setting. This does not capture thumbnails or send remote input. Group expansion and the current-row highlight are temporary UI state, not saved connection fields. In fullscreen, use the existing windowed action to return to the sidebar.

## Fit to window

In a connected session, select **Fit to window** beside **Focus full screen**. The session heading, toolbar, footer, and surrounding margins disappear; the desktop or terminal fills the content area beside the sidebar. RemoteMachine's sidebar, title bar, window controls, and bottom app status bar remain available. The native window is not resized or switched to fullscreen.

Use **Exit fit view** in the title bar to restore the normal session layout and its controls. The same session stays connected. Navigation, minimize/restore, and temporary fullscreen preserve the current session's fit view; leaving fullscreen returns to it. Disconnect or failure restores the normal layout. This is a per-session view choice, not a new saved connection setting.

For VNC/macOS, the full desktop is fitted proportionally without stretching, cropping, or changing the Mac's resolution. Borders can still remain when aspect ratios differ. RDP and SSH continue using their existing native desktop-resize and terminal-fit behavior. Scroll-speed preferences and input mappings are unchanged.

## Adaptive Windows appearance

Open **Settings** in the sidebar or through the account button at the top right. Under **Appearance > App theme**, choose **Use Windows setting** (the default), **Light**, or **Dark**. Changes apply immediately and are remembered when you reopen the app.

With **Use Windows setting**, the manager follows **Windows Settings > Personalization > Colors > Choose your mode**. In Windows' Custom mode, **Choose your default app mode** controls the app, independently of the taskbar's Windows mode. Explicit Light/Dark choices remain selected when Windows appearance changes.

- Light and dark palettes apply to the shell, navigation, forms, lists, selection states, popups, session controls, and embedded SSH terminal.
- Windows accent changes update primary buttons and the fullscreen handle. Accent-colored text is adjusted for readability; button text switches between light and dark to suit bright or dark accents.
- **Settings > Accessibility > Contrast themes** takes priority and uses Windows' chosen background, text, and highlight colors.
- Changes apply while the app is open, without discarding unsaved connection fields, changing the selected connection, reconnecting sessions, or clearing terminal output.
- The handle retains its white rim and dark outer outline regardless of the accent, so it remains distinguishable over both light and dark remote backgrounds.
- Remote RDP/VNC pixels, the remote computer's theme, explicit colors emitted by terminal applications, and Microsoft-owned credential/security dialogs are not recolored. Decorative wallpaper artwork keeps its original colors.

Hover over the version label in the status bar to see the current theme. The app only reads system appearance; it does not change Windows settings. If system appearance cannot be read, it logs the failure and retains the last known values (the original light/blue palette on first startup).

Preferences live separately from connections in `%LOCALAPPDATA%\RemoteWorkspace\settings.json`:

```json
{
  "version": 1,
  "theme": "System"
}
```

Allowed theme values are `"System"`, `"Light"`, and `"Dark"`. To edit the file manually, close the app first, save your changes, and reopen it. The Settings page provides a selectable file path. Writes are atomic, with the previous version in `settings.json.bak`; a failed save leaves the previous preference active and displays an error. Invalid or unsupported files are reported rather than silently replaced. Design preview and self-test windows never change your saved preference.

Maximizing uses the current monitor's work area, keeping the status bar and navigation above the taskbar. Focus mode still fills the entire monitor; leaving Focus restores the previous window state.

## SSH terminals

Choose **New connection > SSH**, enter the host and port (22 by default), and save. The username and private-key file are optional. **Connect** opens a terminal inside this app, not a separate Windows Terminal window.

- Requires the Windows **OpenSSH Client** optional feature and the **Microsoft Edge WebView2 Evergreen Runtime**. Missing prerequisites produce an error; the app does not install Windows features automatically.
- Uses the system `ssh.exe` directly through ConPTY. OpenSSH handles password, key/passphrase, agent, and interactive authentication. Leave the username/key blank to use applicable OpenSSH configuration and defaults. The saved port always overrides a configured port.
- Confirm a new host's fingerprint through a trusted source before accepting it. Strict host-key checking is enabled; changed keys are not silently accepted.
- **Terminal open** means the SSH process and terminal are running, not that remote authentication succeeded. Follow prompts and inspect the terminal output. Recent history records when an SSH terminal was opened; RDP history still records completed sign-in.
- Use **Copy / Paste**, or **Ctrl+Shift+C / Ctrl+Shift+V**. Copy uses the selection. Multiline paste requires confirmation because a shell may execute commands immediately. A single paste is limited to 65,536 characters.
- Navigation, minimizing, and Focus mode retain the same terminal and process. A normal process exit keeps its output readable; **Reconnect** opens a fresh terminal.
- Disconnecting or closing an active SSH session terminates its local SSH process. Remote shells and foreground jobs may end; use a remote session manager such as tmux if persistence is required.

Only interactive shell sessions are included. File-transfer UI, forwarding, remote command presets, agent forwarding, and X11 forwarding are not implemented/enabled. Local commands and configured remote commands are disabled for these sessions. Existing OpenSSH configuration can still supply applicable host aliases, authentication, and proxy settings.

The SSH renderer uses locally packaged xterm.js in WebView2; it does not load terminal assets from a CDN at runtime. Its page cannot navigate to external sites, download files, or access arbitrary local files through the app bridge. SSH terminal input and transcripts are not logged or intentionally saved by this app. This renderer is separate from the native RDP path.

## macOS Screen Sharing / VNC

On the Mac, open **System Settings > General > Sharing > Screen Sharing**, enable it, and allow the Mac account you intend to use. Screen Sharing and Remote Management cannot both be enabled. Choose ordinary Screen Sharing; Apple's proprietary high-performance mode is not supported.

In RemoteMachine, choose **New connection > VNC**, enter the Mac's hostname/IP, port **5900**, and optionally its account short name. Confirm the network warning, save, and connect. The requested credentials depend on the server: Apple ARD authentication uses the **Mac account username and password**; classic VNC authentication uses the **separate VNC password** configured under the Mac's Screen Sharing settings. Do not enter an Apple ID or Windows password in place of the Mac credentials. Passwords are requested per connection and never saved to connection profiles or logs.

**The VNC screen and keyboard transport is not encrypted.** ARD's credential exchange is not end-to-end transport encryption and does not authenticate the server's identity like SSH host keys or a validated TLS certificate. Use only a trusted network, VPN, or an independently established SSH tunnel. Never expose port 5900 directly to the internet. RemoteMachine does not create a VNC tunnel automatically. See [Apple's third-party VNC security guidance](https://support.apple.com/guide/remote-desktop/virtual-network-computing-access-and-control-apde0dd523e/mac).

- The desktop remains inside RemoteMachine, including navigation, minimize, and movable Focus controls. Disconnect closes the connection without sending a Mac logout command.
- **View only** disables remote keyboard/pointer input. Fit-to-window scaling does not change the Mac's display resolution. Fullscreen refits the desktop to the largest proportional size: there is no cropping or stretching. Borders remain only where the Mac and Windows display aspect ratios differ.
- **Scroll speed:** use the **Scroll: 1x** dropdown in the session toolbar, or the same dropdown in fullscreen **Session controls** (handle > More). Choose 1x-8x; try 3x if movement is immediate but each wheel notch moves too little. The change applies without reconnecting and is saved only for that connection. It is also available when adding/editing a VNC connection. View-only sessions disable this control. Existing profiles without this optional field retain 1x, and RDP/SSH are unaffected. macOS scroll settings and framebuffer quality are not changed.
- Mouse-wheel scrolling preserves horizontal/vertical direction and fractional remainders for browser pixel, line, and page units, applying the chosen multiplier to normalized distance. Changing speed clears pending fractions to avoid a jump. VNC still represents scrolling as discrete wheel steps rather than native macOS smooth-scroll gestures. Malformed input or a single event exceeding 1024 combined steps after scaling fails the session explicitly, rather than freezing the renderer or silently dropping excess distance.
- **Mac shortcuts** sends Command+C, Command+V, Command+Space, or Command+Tab. Windows system shortcuts remain local. Copy/paste here operates on the Mac's clipboard only: Windows clipboard synchronization and file transfer are not implemented.
- Supports RFB 3.7/3.8, Apple's `003.889` banner, ARD security type 30, and classic VNC password security type 2. Passwordless modes, RFB 3.3, TLS/VeNCrypt-only servers, and other authentication schemes are explicitly rejected; there is no insecure fallback.
- Requires Microsoft Edge WebView2 Runtime. Local **noVNC 1.6.0** runs in an InPrivate WebView with a direct .NET TCP connection. Incoming bytes use four 256 KiB shared-memory slots exposed read-only to the local viewer, with strict ordered acknowledgements before slot reuse; queued screen bytes are bounded to 1 MiB. Small input/control messages remain separate. There is no external browser, websockify service, local WebSocket listener, CDN, or cloud relay. This does not change the native RDP path.

The native checks use local synthetic VNC servers, including real ARD credential decryption and classic challenge-response, fragmented negotiation, the actual native sign-in dialog, raw and TightPNG framebuffer decoding, scaled input, view-only blocking, reconnect, authentication failure, and startup/sign-in cancellation. Wheel checks verify actual RFB press/release messages: distance, fractions, both axes, pixel/line/page units, held buttons, scaled coordinates, fullscreen, view-only, and burst limits. Sensitivity checks verify exact pulse counts at each speed, live windowed/fullscreen changes, and saved speed on reconnect. The user confirmed substantially improved Mac responsiveness in 0.3.1, but scrolling still moved too little per wheel notch in 0.3.2 despite responding immediately. **The best scroll-speed setting for the user's Mac still needs live confirmation.** Local performance results do not measure the Mac's encoding speed, Wi-Fi, VPN, or end-to-end input latency.

### VNC performance regression check

Version 0.3.0 encoded every incoming chunk as base64 and waited for a renderer acknowledgement after at most 64 KiB. Full-desktop updates paid repeated text conversion, per-byte JavaScript decoding, and cross-process round trips. Version 0.3.1 removes base64 from the screen-data path and pipelines bounded shared-memory slots. It does not request lower image quality or alter the native Microsoft RDP path.

Run `RemoteMachine.exe --vnc-performance-test` from a published folder for an isolated local fixture. It sends five full-size, patterned raw frames at 1920x1080 and 2880x1800, verifies every final pixel and proportional fullscreen/windowed geometry, and writes `vnc-performance-results.json` beside the EXE. The timing runs from the local server sending each update to receiving noVNC's next update request; it is not an on-screen presentation or live Mac benchmark. The first frame is excluded from the warm median. The regression budget is a warm median below 250 ms on this development machine, with at most four receive slots in flight.

The old bridge measured approximately 597 ms at 1920x1080 and 1,814 ms at 2880x1800 on this machine. The staged 0.3.1 build measured about 19 ms and 44 ms respectively with unchanged pixel data. These are synthetic client-side improvements, not promised real-world frame rates. Actual responsiveness still depends on the Mac, negotiated encoding, resolution, and network.

The `VncAssets` folder ships noVNC 1.6.0 source and license notices (MPL-2.0, with bundled MIT/BSD components), plus `dependencies.json` recording the upstream version, source URL, archive hash, and local modifications. `novnc/core/rfb.js` includes the RemoteHub 0.3.2 wheel-distance correction and 0.3.3 sensitivity extension; other upstream files remain unchanged. The modified MPL source is included in the published app. Preserve this folder when distributing it.

Workspace schema 3 adds VNC profiles and view-only/trusted-network preferences. Versions 1 and 2 load without rewriting the file at startup; the next explicit workspace save writes schema 3. The compatibility data directory and separate appearance settings are unchanged. Earlier builds do not understand schema 3.

## Import and export `.rdp` files

Select **Import .rdp** on Home or All connections. Choose one standard-PC file, review its editable settings and any warnings, then **Save connection**. Importing does not connect automatically or replace an existing profile. The original file is unchanged. Device-sharing and authentication options are expanded for review.

Supported fields are host/address, port, username/domain, clipboard sharing, smart-card redirection, and reduced visual effects. IPv6 addresses and UTF-8 or BOM-marked UTF-16 files are supported, with a 1 MiB size limit. Individual visual-effect flags may be mapped to the reduced-effects preset with a warning.

Passwords, tokens, and binary credential fields are not imported. Other unsupported settings are omitted with warnings naming the fields, never secret values. Files requiring unsupported gateway, broker/workspace, RemoteApp, administrative-console, alternate-shell, or Entra-specific connection modes are rejected. Signed files are rejected because this import path cannot verify publisher signatures. Malformed or duplicate settings are rejected rather than guessed. Import is **not** a way to add Dev Box/Windows App workspace support.

To export, select a real RDP profile in **All connections**, then **Export .rdp** in its details. SSH, VNC, and design-preview profiles cannot be exported. The file contains supported metadata and secure defaults, not passwords or private keys, and is written atomically as UTF-16 LE. It opens windowed with the standard connection bar enabled in an external RDP client: this app's movable Focus controls cannot be encoded in an `.rdp` file. Export is not lossless round-tripping of arbitrary Microsoft client settings.

## The full-screen experience

After RDP/VNC sign-in, or once an SSH terminal opens, select **Focus full screen**:

| Control | Behavior |
| --- | --- |
| Accent-colored edge handle | A thicker pill in your Windows accent color, with a white rim and dark outer outline for contrast on light, dark, and same-colored desktops. Stays visible by default, without idle fading. Click for **Minimize / Windowed / Disconnect / More**, or drag to another edge/position. |
| Minimize | Minimize to the taskbar without disconnecting. Restore from the taskbar. |
| Windowed | Return to the manager while keeping the same RDP control, SSH terminal/process, or VNC viewer. |
| Disconnect | Confirm before disconnecting. RDP and VNC do not issue sign-out or shutdown commands; retention follows server policy. SSH closes the connection and may end its remote shell and foreground jobs. |
| More | Choose one of four edges or enable invisible mode. |
| Hide handle (invisible mode) | An explicit, saved workspace preference. Hides the actual handle window and hit target. Only available when recovery shortcuts register successfully; a shortcut conflict restores the handle for safety. |
| Ctrl+Alt+Space | Reveal controls near the pointer. |
| Ctrl+Alt+Home | Leave Focus mode. |
| Escape | Remains available to remote applications; only dismisses our controls while they have keyboard focus. |

The native Microsoft connection bar is disabled. The handle does not auto-hide when focus changes: it remains attached to the visible fullscreen session. It is an owned window, not globally topmost, so other local applications can cover it normally. Minimizing, leaving Focus mode, or disconnecting removes it. Returning native input focus to the remote desktop dismisses control popups; switching to another application also releases the shortcuts. The focus controls do not install a global mouse or keyboard hook.

The 10-DIP grip (previously 6 DIP) has a 2-DIP white rim and 1-DIP dark outline, with a larger pointer target. Only its fill follows the Windows accent; the dual-contrast outlines remain fixed. It does not sample or copy remote frames or add animation/rendering work to the RDP pipeline.

Opening the control strip explicitly releases native RDP mouse capture and activates/focuses the local controls, for both pointer and keyboard entry. Popup dismissal uses the actual foreground HWND rather than asynchronously updated WPF activation flags or a low-level mouse hook. Popup-to-popup activation is not treated as an outside click. This corrects the 0.1.2 input handoff where a visible strip could fail to receive actions. Local `focus-controls-opened` and `focus-action` diagnostics record the handoff and chosen action, not remote keystrokes or screen contents.

Windows/system key combinations are intentionally kept **local** in this version, so Alt+Tab, the Windows key, and recovery remain available. This differs from mstsc configurations that route system keys to the remote PC.

## Performance approach

- Direct hosting of Microsoft's `mstscax.dll`: no browser renderer, proxy, frame capture, or frame copying in the live RDP path.
- Native bandwidth detection and auto-reconnect, CredSSP/NLA support, and hardware-assisted decoding requested through Microsoft's supported setting.
- Focus, minimize, and restore retain the same control and connection.
- Display-size changes are debounced by 350 ms and sent to the server without reconnecting. If unsupported, Microsoft SmartSizing remains enabled and the app reports the limitation.
- Optional low-bandwidth mode reduces desktop effects.
- No reachability ping before connecting and no added connection delay.

**Using Microsoft's engine is a sound performance foundation, not proof of mstsc parity.** mstsc and the embeddable control can differ in version, negotiated features, policies, and defaults. A controlled, same-target performance comparison has not been completed. Local smoke checks are not measurements of a real remote session's responsiveness.

### Compare against mstsc

Use the same client, server, network, target port, desktop dimensions, display scaling, redirections, credential state, and server policies. Test both clients without the other holding an active session. Alternate at least five cold and five warm connections per client; compare medians and the spread.

The app logs elapsed time for transport-connected and login-complete callbacks. These are **not first-frame timings or input-latency measurements**. Separately measure first usable frame, typing response, scrolling/video smoothness, client CPU/GPU, and negotiated transport. Match clipboard and other redirections, which mstsc may enable by default. Diagnose differences before claiming equal speed.

## Build and checks

Requires the .NET 10 SDK, Windows desktop targeting support, and the registered Microsoft RDP client control. SSH checks also require OpenSSH Client. SSH and VNC checks require WebView2 Runtime.

The application references **Microsoft.Web.WebView2 1.0.4191.47**. `NuGet.Config` uses Microsoft's public `dotnet-public` feed because the default NuGet endpoint was unreachable from this environment; TLS certificate validation remains enabled. The published `TerminalAssets` folder includes **xterm.js 6.0.0**, **addon-fit 0.11.0**, their MIT license notices, and a `vendor.json` provenance record. Keep these files together when redistributing the app.

```powershell
.\Build.ps1
# Build for an x64 Windows computer:
.\Build.ps1 -Runtime win-x64

.\tools\Smoke-Test.ps1 -Executable .\artifacts\win-arm64\RemoteMachine.exe
```

`Build.ps1` runs profile/storage/docking, `.rdp` parsing/export, schema migration, SSH argument-quoting, ConPTY input/output/lifetime, and local OpenSSH transport checks, then publishes the app. It uses `.tools\dotnet\dotnet.exe` when a project-local SDK is present; otherwise it uses `dotnet` on PATH.

The native smoke check verifies COM hosting/configuration, RDP transport against a **local loopback listener that immediately closes**, COM callbacks, import review, protocol controls, and embedded terminal/Focus/navigation lifecycle. Its terminal test runs a clearly labeled local `cmd.exe` shell through the same renderer and ConPTY bridge. Neither that shell nor the loopback SSH banner test authenticates to a remote SSH server. Authenticated SSH sessions, server-specific authentication methods, and live RDP performance still require end-to-end verification against authorized targets. The native check writes `self-test-results.json` and screen PNGs in `screenshots` beside the EXE.

Theme checks exercise both palettes with 225 accent samples, 4.5:1 text contrast, 3:1 field boundaries, and supplied high-contrast colors. Native checks change only the test app's palette, preserve draft fields and selection, verify the actual Windows settings-notification path, and change a running local terminal's palette without restarting it. They do not modify your Windows theme, connect to saved hosts, or inject pointer input.

Settings checks cover JSON persistence and rejection, atomic backups, failed-save preservation, immediate changes, navigation, and Windows notifications under manual overrides. Shell checks verify account placement/dismissal, maximized native/footer bounds against the real monitor work area, and maximized-to-Focus restoration. Synthetic geometry checks cover taskbars on all edges and monitors with negative coordinates; physical mixed-DPI monitor transitions still require interactive verification.

Branding checks verify RemoteMachine assembly/product/window names, native EXE metadata, full sidebar-name layout at normal and compact widths, accessible labels, Settings branding, compatibility storage paths, shared logo resources, all ten ICO resolutions, transparent logo corners, and the running window's native icon. Windows extracts the icon from the published EXE and its pixels are compared against the supplied-logo ICO, so an in-app image alone cannot satisfy the check.

The separate input test moves the pointer and sends hotkeys:

```powershell
.\tools\Smoke-Test.ps1 -Executable .\artifacts\win-arm64\RemoteMachine.exe -Interactive
```

Run it from an unobstructed desktop; minimize other fullscreen RDP clients first. Click the test window if asked, then leave input untouched. It aborts rather than clicking through another application's window. Version 0.1.3 passed real pointer checks for all four quick actions, outside-click dismissal, dragging, invisible-mode recovery, minimize/restore, and keyboard exit in a local preview session. This is not a live-RDP end-to-end test. Mixed-DPI monitor transitions, monitor unplug, certificate/credential dialogs, and true live-session reconnection require further interactive testing.

### COM bindings

`lib\Interop.MSTSCLib.dll` contains managed interface definitions generated from the locally installed Windows component, not a copy of Microsoft's RDP implementation. This generated binary is excluded from Git; `Build.ps1` creates it when missing. To regenerate manually:

```powershell
.\tools\Generate-RdpInterop.ps1
```

The generator uses Windows PowerShell 5.1's .NET Framework type-library importer. It does not register or replace Windows components. Never redistribute `mstscax.dll` with this app.

## Local data and credentials

- The top-right account button opens your current Windows username and account domain, using local Windows account metadata. Its menu also opens Settings and dismisses with Escape or an outside click. This is not an Azure sign-in or proof of access to another computer.
- For RDP, **New connection > Use my Windows account** explicitly fills the username/domain as a suggestion. New RDP connections still ask on connect by default; existing remote account settings are never overwritten automatically. Windows performs the actual authentication. SSH uses OpenSSH's account defaults when its username is blank.
- Passwords, PINs, access tokens, and saved Credential Manager secrets are never retrieved or displayed. Detected identity is not automatically written into profiles; only the account fields explicitly saved for a connection are persisted.
- `%LOCALAPPDATA%\RemoteWorkspace\workspace.json`: connection metadata, protocol, SSH key file paths (never key contents), handle position, and the explicit hide-handle preference; atomic saves and a previous-version `.bak`.
- `%LOCALAPPDATA%\RemoteWorkspace\settings.json`: versioned appearance preferences, with atomic saves and a previous-version `.bak`. Changing the app theme does not rewrite your connections.
- Workspace schemas 1 and 2 load without rewriting the file on startup. The next save writes schema 3 for mixed RDP/SSH/VNC profiles. Earlier builds cannot read schema 3; preserve a copy of your original workspace before rolling back. The `.bak` tracks the previous save, not a permanent pre-upgrade backup.
- `%LOCALAPPDATA%\RemoteWorkspace\TerminalWebView`: WebView2 runtime data for the local terminal renderer. OpenSSH continues to manage its own configuration, agent, and known-hosts data.
- `%LOCALAPPDATA%\RemoteWorkspace\VncWebView`: WebView2 runtime profile directory; VNC controllers run in InPrivate mode. VNC credentials remain transient, not part of the saved workspace.
- `%LOCALAPPDATA%\RemoteWorkspace\logs\session-yyyy-MM-dd.jsonl`: local diagnostics and event timings.
- The native Windows prompt handles RDP authentication; Windows credential saving is opt-in. SSH passwords and passphrases are entered directly into OpenSSH's terminal prompts. VNC uses a native masked sign-in dialog and an ephemeral credential exchange; it never saves passwords.
- RDP clipboard sharing is opt-in. Drive, printer, port, generic device, and microphone redirection are disabled. SSH clipboard transfer requires an explicit copy or paste action.
- Smart-card redirection is enabled by default, including for existing profiles created before 0.1.1. Windows Hello for Business can use RDP's redirected smart-card authentication path. To opt out per computer, open **Edit connection > More options > Allow smart cards and Windows Hello for Business**. Redirection makes these authentication devices available to the remote session; enable it only for computers you trust. Disabling it can prevent smart-card/Hello sign-in.
- Certificate/authentication warnings are not bypassed. No telemetry or background host scanning.
- A malformed workspace is reported instead of silently replaced.

### Windows Hello / smart-card sign-in

Version 0.1.0 incorrectly disabled smart-card redirection for all connections. Selecting a smart-card or Windows Hello for Business credential could therefore end the connection with "The selected user credential requires that local smart card and 'Windows Hello for Business' devices be made available to the remote session." Version 0.1.1 removes that client-side restriction and adds the per-connection option above. Restart the app after updating, then reconnect; existing connections do not need to be recreated.

This does not disable NLA/CredSSP, suppress certificate warnings, change organization policies, or store a PIN/password in the app. The device/account/server must still meet [Microsoft's Windows Hello for Business RDP requirements](https://learn.microsoft.com/en-us/windows/security/identity-protection/hello-for-business/rdp-sign-in); enabling redirection alone does not provision certificates or guarantee that every Hello deployment supports RDP.

## Microsoft Dev Box connections

**Embedded Dev Box workspace connections are not implemented.** The current control connects directly to a host and port. A Dev Box that works through Windows App or a managed Remote Desktop workspace also needs the service's workspace/broker and authentication flow; entering a Dev Box name in the direct-PC form does not provide that flow.

Microsoft's [Dev Center Get Remote Connection API](https://learn.microsoft.com/en-us/rest/api/devcenter/developer/dev-boxes/get-remote-connection?view=rest-devcenter-developer-2025-02-01) returns launch URLs (`cloudPcConnectionUrl`, `rdpConnectionUrl`, and `webUrl`). The documented [Windows App `ms-avd` integration](https://learn.microsoft.com/en-us/azure/virtual-desktop/uri-scheme) launches the official client; it does not embed that client in our window or transfer its fullscreen controls to us. An external-client substitution was explicitly declined for this project.

The [RDP extended-settings reference](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpextendedsettings-property) includes Entra/RDS authentication options, but those options alone are not a documented Dev Box brokered-session integration. The investigation has not established a publicly documented, supported embedding contract for the required flow. This is a support/feasibility gap, not a claim that every possible custom integration is technically impossible.

Before promising an embedded implementation, obtain an applicable supported SDK/API contract from Microsoft (or a licensed provider), confirm that it supports native hosted rendering and the organization's authentication policies, and verify an authorized end-to-end Dev Box session. No organization settings, conditional-access policies, credentials, or remote devices were changed during this investigation; no private client internals or token extraction were used.

## Current scope

Direct RDP, embedded SSH shells, embedded VNC/macOS Screen Sharing, standard-PC `.rdp` import/export, local profiles, favorites/groups/search, multiple retained sessions, one visible session at a time, and one monitor per Focus session. Dev Box/Windows App managed-workspace integration, shared/team workspaces, RDP gateway configuration, RemoteApp, SFTP UI, and spanning a remote session across multiple monitors are not implemented. The connection form explains the Dev Box limitation and VNC's network-security requirements.

The app recreates the approved manager and control surfaces natively; it cannot restyle the remote operating system or Microsoft's credential/security dialogs. The preview's decorative desktop is illustrative, never substituted for real RDP rendering.
