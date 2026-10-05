using Microsoft.Web.WebView2.Core;

namespace RemoteHub.Services;

internal static class LocalWebContent
{
    public static void Configure(CoreWebView2 core, CoreWebView2Environment environment, string host, string directory,
        string page, IEnumerable<string> paths, Func<bool> loading, Action navigationBlocked)
    {
        var allowed = paths.ToHashSet(StringComparer.Ordinal);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.SetVirtualHostNameToFolderMapping(host, directory, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri == page && loading()) return;
            e.Cancel = true; navigationBlocked();
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == host &&
                uri.Port == 443 && uri.UserInfo.Length == 0 && allowed.Contains(uri.AbsolutePath)) return;
            e.Response = environment.CreateWebResourceResponse(Stream.Null, 403, "Local app assets only", "Content-Type: text/plain");
        };
    }
}
