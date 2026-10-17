using System.IO;
using System.Text.Json;

namespace HaloBrowser;

public class AppSettings
{
    public string SearchEngine { get; set; } = "Google";
    public string HomePage { get; set; } = "halo://newtab";
    public string ThemeColor { get; set; } = "Blue";
    public bool BlockTrackers { get; set; } = true;
    public bool DoNotTrack { get; set; } = true;
    public bool SaveHistory { get; set; } = true;
    public bool AskDownloadLocation { get; set; } = false;
    public bool ShowBookmarksBar { get; set; } = true;
    public string DownloadFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}

public class BookmarkItem { public string Title { get; set; } public string Url { get; set; } public DateTime Added { get; set; } = DateTime.Now; }
public class HistoryItem  { public string Title { get; set; } public string Url { get; set; } public DateTime Visited { get; set; } = DateTime.Now; }
public class DownloadItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; }
    public string Path { get; set; }
    public string Url { get; set; }
    public string State { get; set; } = "InProgress"; // InProgress, Completed, Interrupted
    public long Received { get; set; }
    public long Total { get; set; }
    public DateTime Started { get; set; } = DateTime.Now;
}

/// <summary>JSON persistence in %LOCALAPPDATA%\HaloBrowser plus URL helpers.</summary>
public static class Store
{
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HaloBrowser");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Settings = new();
    public static List<BookmarkItem> Bookmarks = new();
    public static List<HistoryItem> History = new();
    public static List<DownloadItem> Downloads = new();

    public static readonly Dictionary<string, string> Engines = new()
    {
        ["Google"] = "https://www.google.com/search?q={0}",
        ["Bing"] = "https://www.bing.com/search?q={0}",
        ["DuckDuckGo"] = "https://duckduckgo.com/?q={0}",
        ["Brave"] = "https://search.brave.com/search?q={0}",
        ["Ecosia"] = "https://www.ecosia.org/search?q={0}",
    };

    public static string UserDataFolder => Path.Combine(Dir, "WebView2");

    static T Read<T>(string file, T fallback)
    {
        try
        {
            var p = Path.Combine(Dir, file);
            if (File.Exists(p)) return JsonSerializer.Deserialize<T>(File.ReadAllText(p)) ?? fallback;
        }
        catch { }
        return fallback;
    }

    static void Write(string file, object data)
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(Path.Combine(Dir, file), JsonSerializer.Serialize(data, Json)); }
        catch { }
    }

    public static void Load()
    {
        Directory.CreateDirectory(Dir);
        Settings = Read("settings.json", new AppSettings());
        Bookmarks = Read("bookmarks.json", new List<BookmarkItem>());
        History = Read("history.json", new List<HistoryItem>());
        Downloads = Read("downloads.json", new List<DownloadItem>());
        foreach (var d in Downloads.Where(x => x.State == "InProgress")) d.State = "Interrupted";
    }

    public static void SaveSettings() => Write("settings.json", Settings);
    public static void SaveBookmarks() => Write("bookmarks.json", Bookmarks);
    public static void SaveHistory() => Write("history.json", History);
    public static void SaveDownloads() => Write("downloads.json", Downloads);

    // ---- history ----
    public static void AddHistory(string title, string url)
    {
        if (!Settings.SaveHistory || string.IsNullOrWhiteSpace(url) || url.StartsWith("halo://") || url.StartsWith("about:") || url.StartsWith("data:")) return;
        var last = History.LastOrDefault();
        if (last != null && last.Url == url) { last.Title = string.IsNullOrWhiteSpace(title) ? last.Title : title; SaveHistory(); return; }
        History.Add(new HistoryItem { Title = string.IsNullOrWhiteSpace(title) ? url : title, Url = url });
        if (History.Count > 5000) History.RemoveRange(0, History.Count - 5000);
        SaveHistory();
    }

    // ---- bookmarks ----
    public static bool IsBookmarked(string url) => Bookmarks.Any(b => b.Url == url);

    /// <returns>true if now bookmarked</returns>
    public static bool ToggleBookmark(string title, string url)
    {
        var existing = Bookmarks.FirstOrDefault(b => b.Url == url);
        bool added = existing == null;
        if (added) Bookmarks.Add(new BookmarkItem { Title = string.IsNullOrWhiteSpace(title) ? url : title, Url = url });
        else Bookmarks.Remove(existing);
        SaveBookmarks();
        return added;
    }

    // ---- URL helpers ----
    public static string SearchUrl(string query)
    {
        var tpl = Engines.TryGetValue(Settings.SearchEngine, out var t) ? t : Engines["Google"];
        return string.Format(tpl, Uri.EscapeDataString(query));
    }

    /// <summary>Turns whatever was typed into a navigable URL or a search.</summary>
    public static string ToUrl(string input)
    {
        input = (input ?? "").Trim();
        if (input.Length == 0) return Settings.HomePage;
        if (input.StartsWith("halo://", StringComparison.OrdinalIgnoreCase)) return input.ToLowerInvariant();
        if (Uri.TryCreate(input, UriKind.Absolute, out var u) &&
            (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeFile || u.Scheme == "about"))
            return input;
        bool looksLikeHost = !input.Contains(' ') &&
            (input.Contains('.') || input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase));
        if (looksLikeHost && Uri.TryCreate("https://" + input, UriKind.Absolute, out _)) return "https://" + input;
        return SearchUrl(input);
    }
}
