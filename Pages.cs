using System.Net;
using System.Text.Json;

namespace HaloBrowser;

/// <summary>Builds Halo's internal pages (halo://...) as themed HTML.</summary>
public static partial class Pages
{
    static string E(string s) => WebUtility.HtmlEncode(s ?? "");

    public static string TitleFor(string url)
    {
        var u = (url ?? "").Split('#')[0];
        return u switch
        {
            "halo://newtab" => "New tab",
            "halo://history" => "History",
            "halo://bookmarks" => "Bookmarks",
            "halo://downloads" => "Downloads",
            "halo://settings" => "Settings",
            _ => "Halo"
        };
    }

    public static string UrlFromTitle(string title) => title switch
    {
        "History" => "halo://history",
        "Bookmarks" => "halo://bookmarks",
        "Downloads" => "halo://downloads",
        "Settings" => "halo://settings",
        _ => "halo://newtab"
    };

    /// <summary>Shared Material 3 Expressive stylesheet for every internal page.</summary>
    public static string Css()
    {
        var (p, c, oc) = Theme.Get(Store.Settings.ThemeColor);
        return """
        :root{--p:%P%;--c:%C%;--oc:%OC%;--dim:#F3F6FC;--on:#1F1F1F;--var:#444746;--line:#E1E3E1}
        *{box-sizing:border-box}
        body{margin:0;font-family:'Segoe UI Variable Display','Segoe UI',sans-serif;color:var(--on);background:#fff;animation:in .35s ease}
        @keyframes in{from{opacity:0;transform:translateY(8px)}to{opacity:1;transform:none}}
        .card{background:#fff;border:1px solid var(--line);border-radius:28px;padding:24px;box-shadow:0 2px 18px rgba(27,58,107,.10)}
        .pill{border:0;border-radius:100px;padding:10px 22px;font-family:inherit;font-size:14px;font-weight:500;background:var(--c);color:var(--oc);cursor:pointer;transition:filter .15s,transform .1s}
        .pill:hover{filter:brightness(.95)}.pill:active{transform:scale(.95)}
        .pill.fill{background:var(--p);color:#fff}
        a{color:var(--p);text-decoration:none}
        """.Replace("%P%", p).Replace("%C%", c).Replace("%OC%", oc);
    }

    /// <summary>Wraps a page body in the shared shell. The script posts messages to the app.</summary>
    public static string Shell(string title, string body, string script = "")
    {
        return "<!doctype html><html><head><meta charset='utf-8'><title>" + E(title) + "</title><style>" + Css() + "</style></head><body>"
            + body + "<script>const post=m=>window.chrome&&window.chrome.webview&&window.chrome.webview.postMessage(m);"
            + "document.addEventListener('click',e=>{const a=e.target.closest('[data-url]');if(a){e.preventDefault();post({type:'navigate',url:a.dataset.url});}});"
            + script + "</script></body></html>";
    }

    public static string Render(string url)
    {
        var u = (url ?? "").Split('#')[0];
        if (u == "halo://newtab") return NewTab();
        return Internal(u, url);
    }

    static string NewTab()
    {
        var tiles = Store.Bookmarks.Take(8).Select(b => (b.Title, b.Url)).ToList();
        if (tiles.Count == 0)
            tiles = new() { ("Google", "https://www.google.com"), ("YouTube", "https://www.youtube.com"),
                            ("Wikipedia", "https://www.wikipedia.org"), ("GitHub", "https://github.com") };
        var tileHtml = string.Concat(tiles.Select(t =>
            $"<a class='tile' href='#' data-url='{E(t.Url)}'><span class='dot'>{E(string.IsNullOrEmpty(t.Title) ? "?" : t.Title.Substring(0, 1).ToUpper())}</span><span class='name'>{E(t.Title)}</span></a>"));

        var css = """
        <style>
        .wrap{min-height:100vh;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:30px;padding:24px;
              background:radial-gradient(900px 500px at 50% -10%,var(--c),transparent 70%)}
        h1{font-size:68px;font-weight:600;letter-spacing:-2.5px;margin:0;color:var(--p)}
        form{width:min(640px,90vw)}
        input{width:100%;height:58px;border-radius:100px;border:2px solid transparent;background:var(--dim);padding:0 28px;font-size:16px;
              font-family:inherit;outline:none;transition:.2s;box-shadow:0 2px 12px rgba(27,58,107,.08)}
        input:focus{border-color:var(--p);background:#fff;box-shadow:0 4px 22px rgba(27,58,107,.14)}
        .tiles{display:flex;flex-wrap:wrap;justify-content:center;gap:14px;max-width:720px}
        .tile{width:120px;padding:18px 10px;border-radius:28px;background:#fff;border:1px solid var(--line);display:flex;flex-direction:column;
              align-items:center;gap:10px;color:var(--on);box-shadow:0 2px 14px rgba(27,58,107,.08);transition:transform .15s,box-shadow .15s}
        .tile:hover{transform:translateY(-3px);box-shadow:0 8px 24px rgba(27,58,107,.16)}
        .dot{width:46px;height:46px;border-radius:50%;background:var(--c);color:var(--oc);display:grid;place-items:center;font-size:20px;font-weight:600}
        .name{font-size:13px;max-width:100px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        </style>
        """;

        var body = css + "<div class='wrap'><h1>Halo</h1><form id='f'><input id='q' autofocus autocomplete='off' placeholder='Search "
                 + E(Store.Settings.SearchEngine) + " or type a URL'></form><div class='tiles'>" + tileHtml + "</div></div>";
        var script = "const q=document.getElementById('q');q.focus();document.getElementById('f').onsubmit=e=>{e.preventDefault();if(q.value.trim())post({type:'search',q:q.value});};";
        return Shell("New tab", body, script);
    }

}
