using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace HaloBrowser;

public partial class MainWindow
{
    // Hosts of well-known ad and tracking networks (subdomains are matched too).
    static readonly string[] TrackerHosts =
    {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "google-analytics.com", "adservice.google.com",
        "scorecardresearch.com", "quantserve.com", "hotjar.com", "taboola.com", "outbrain.com", "adnxs.com", "criteo.com",
        "criteo.net", "amazon-adsystem.com", "ads-twitter.com", "analytics.twitter.com", "pubmatic.com", "rubiconproject.com",
        "openx.net", "moatads.com", "mixpanel.com", "adsrvr.org", "casalemedia.com", "bluekai.com", "demdex.net", "krxd.net"
    };

    static bool IsTracker(string host)
    {
        foreach (var t in TrackerHosts)
            if (host == t || host.EndsWith("." + t, StringComparison.Ordinal)) return true;
        return false;
    }

    readonly Dictionary<string, CoreWebView2DownloadOperation> _downloads = new();
    WindowState _prevState = WindowState.Normal;

    /// <summary>Privacy controls, download manager and full-screen video for one tab.</summary>
    void HookFeatures(TabVM tab)
    {
        var cw = tab.View.CoreWebView2;

        // ---- Privacy: tracker blocking + Do Not Track / Global Privacy Control (settings are read live) ----
        cw.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        cw.WebResourceRequested += (s, e) =>
        {
            if (e.ResourceContext != CoreWebView2WebResourceContext.Document &&
                Store.Settings.BlockTrackers &&
                Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var u) &&
                (u.Scheme == "http" || u.Scheme == "https") && IsTracker(u.Host))
            {
                e.Response = cw.Environment.CreateWebResourceResponse(null, 403, "Blocked by Halo", "");
                return;
            }
            if (Store.Settings.DoNotTrack)
            {
                e.Request.Headers.SetHeader("DNT", "1");
                e.Request.Headers.SetHeader("Sec-GPC", "1");
            }
        };

        // ---- Downloads ----
        cw.DownloadStarting += (s, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                var op = e.DownloadOperation;
                var suggested = Path.GetFileName(e.ResultFilePath);
                var dir = Store.Settings.DownloadFolder;
                try { Directory.CreateDirectory(dir); }
                catch { dir = Path.GetDirectoryName(e.ResultFilePath); }
                var path = UniquePath(Path.Combine(dir, suggested));

                if (Store.Settings.AskDownloadLocation)
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog { FileName = suggested, InitialDirectory = dir };
                    if (dlg.ShowDialog(this) != true) { e.Cancel = true; return; }
                    path = dlg.FileName;
                }

                e.ResultFilePath = path;
                e.Handled = true;   // Halo shows its own download UI

                var item = new DownloadItem { FileName = Path.GetFileName(path), Path = path, Url = op.Uri, Total = (long)(op.TotalBytesToReceive ?? 0UL) };
                _downloads[item.Id] = op;
                Store.Downloads.Add(item);
                Store.SaveDownloads();
                Pages.PushDownloads(this);
                Toast("Downloading " + item.FileName);

                var last = DateTime.MinValue;
                op.BytesReceivedChanged += (_, _) =>
                {
                    item.Received = op.BytesReceived;
                    if (op.TotalBytesToReceive.HasValue) item.Total = (long)op.TotalBytesToReceive.Value;
                    if ((DateTime.Now - last).TotalMilliseconds > 300) { last = DateTime.Now; Pages.PushDownloads(this); }
                };
                op.StateChanged += (_, _) =>
                {
                    item.Received = op.BytesReceived;
                    if (op.State == CoreWebView2DownloadState.InProgress) return;
                    item.State = op.State == CoreWebView2DownloadState.Completed ? "Completed" : "Interrupted";
                    if (item.State == "Completed" && item.Total == 0) item.Total = item.Received;
                    _downloads.Remove(item.Id);
                    Store.SaveDownloads();
                    Pages.PushDownloads(this);
                    Toast(item.State == "Completed" ? "Downloaded " + item.FileName : "Download stopped: " + item.FileName);
                };
            }
            finally { deferral.Complete(); }
        };

        // ---- Full-screen video: hide Halo's chrome and maximize ----
        cw.ContainsFullScreenElementChanged += (s, e) =>
        {
            bool on = cw.ContainsFullScreenElement;
            foreach (UIElement el in ((Grid)Content).Children)
                if (el != WebHost && el != ToastPopup) el.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (on) { _prevState = WindowState; WindowStyle = WindowStyle.None; WindowState = WindowState.Normal; WindowState = WindowState.Maximized; }
            else { WindowStyle = WindowStyle.SingleBorderWindow; WindowState = _prevState; RefreshBookmarks(); }
        };
    }

    public void CancelDownload(string id)
    {
        if (_downloads.TryGetValue(id, out var op)) op.Cancel();
    }

    static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }
}

