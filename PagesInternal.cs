using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace HaloBrowser;

/// <summary>History, Bookmarks, Downloads and Settings pages plus their message handlers.</summary>
public static partial class Pages
{
    const string PageCss = """
    .page{max-width:860px;margin:0 auto;padding:40px 24px 80px}
    header{display:flex;align-items:flex-end;justify-content:space-between;gap:16px;margin-bottom:24px;flex-wrap:wrap}
    h1{font-size:36px;font-weight:600;letter-spacing:-1px;margin:0;color:var(--p)}
    header p{margin:4px 0 0;color:var(--var)}
    .actions{display:flex;gap:10px;align-items:center}
    .search,.field{height:44px;border-radius:100px;border:2px solid transparent;background:var(--dim);padding:0 20px;font:inherit;font-size:14px;outline:none;transition:.2s}
    .search{width:240px}.field{width:280px;border-color:var(--line);background:#fff}
    .search:focus,.field:focus{border-color:var(--p);background:#fff}
    h3{margin:26px 0 10px;font-size:13px;font-weight:600;color:var(--var);text-transform:uppercase;letter-spacing:.8px}
    .list{display:flex;flex-direction:column;gap:8px}
    .row{display:flex;align-items:center;gap:14px;padding:12px 16px;border-radius:22px;background:#fff;border:1px solid var(--line);transition:background .15s}
    .row:hover{background:var(--dim)}
    .dot{flex:none;width:38px;height:38px;border-radius:50%;background:var(--c);color:var(--oc);display:grid;place-items:center;font-weight:600}
    .grow{flex:1;min-width:0}.grow a,.grow b{display:block;color:var(--on);font-weight:500;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .sub{display:block;font-size:12px;color:var(--var);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .time{font-size:12px;color:var(--var);flex:none}
    .x{border:0;background:transparent;width:34px;height:34px;border-radius:50%;cursor:pointer;color:var(--var);font-size:14px;flex:none}.x:hover{background:var(--c)}
    .empty{text-align:center;color:var(--var);padding:70px 0}
    .bar{height:6px;border-radius:3px;background:var(--c);overflow:hidden;margin-top:8px}.bar i{display:block;height:100%;background:var(--p);transition:width .3s}
    .set{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:14px 0;border-bottom:1px solid var(--line)}.set:last-child{border:0}
    .set b{font-weight:500}.set small{display:block;color:var(--var);margin-top:2px}
    .chips{display:flex;flex-wrap:wrap;gap:8px;justify-content:flex-end}
    .chip{border:1px solid var(--line);background:#fff;border-radius:100px;padding:8px 18px;font:inherit;font-size:14px;cursor:pointer;transition:.15s;color:var(--on)}
    .chip.on{background:var(--c);color:var(--oc);border-color:transparent}
    .swatch{width:44px;height:44px;border-radius:50%;border:3px solid #fff;outline:2px solid var(--line);cursor:pointer;transition:transform .15s}
    .swatch:hover{transform:scale(1.1)}.swatch.on{outline-color:var(--p)}
    .sw{position:relative;width:52px;height:32px;flex:none}
    .sw input{opacity:0;position:absolute;inset:0;margin:0;cursor:pointer;z-index:1;width:100%;height:100%}
    .sw span{position:absolute;inset:0;border-radius:100px;background:#fff;border:2px solid #79747E;transition:.2s}
    .sw span::after{content:'';position:absolute;top:6px;left:6px;width:16px;height:16px;border-radius:50%;background:#79747E;transition:.2s}
    .sw input:checked+span{background:var(--p);border-color:var(--p)}
    .sw input:checked+span::after{left:22px;top:2px;width:24px;height:24px;background:#fff}
    .checks{display:flex;flex-wrap:wrap;gap:8px 22px;margin:6px 0 16px}.checks label{display:flex;gap:8px;align-items:center;cursor:pointer}
    .checks input{accent-color:var(--p);width:18px;height:18px}
    """;

    static string Letter(string s) => string.IsNullOrWhiteSpace(s) ? "?" : s.Trim().Substring(0, 1).ToUpper();

    static string Page(string title, string sub, string actions, string content, string script = "") =>
        Shell(title, "<style>" + PageCss + "</style><div class='page'><header><div><h1>" + E(title) + "</h1><p>" + E(sub)
            + "</p></div><div class='actions'>" + actions + "</div></header>" + content + "</div>", script);

    static string Row(string title, string sub, string url, string right, string key = "") =>
        $"<div class='row' data-s='{E(key.ToLower())}'><span class='dot'>{E(Letter(title))}</span><div class='grow'>" +
        $"<a href='#' data-url='{E(url)}'>{E(title)}</a><span class='sub'>{E(sub)}</span></div>{right}</div>";

    static string Internal(string u, string full) => u switch
    {
        "halo://history" => History(),
        "halo://bookmarks" => Bookmarks(),
        "halo://downloads" => Downloads(),
        "halo://settings" => Settings(full.Contains("#privacy")),
        _ => NewTab()
    };

    // ---------------- History ----------------
    static string History()
    {
        var items = Store.History.OrderByDescending(h => h.Visited).Take(1000).ToList();
        var sb = new StringBuilder();
        foreach (var g in items.GroupBy(h => h.Visited.Date))
        {
            var label = g.Key == DateTime.Today ? "Today" : g.Key == DateTime.Today.AddDays(-1) ? "Yesterday" : g.Key.ToString("dddd, MMM d");
            sb.Append("<section><h3>" + E(label) + "</h3><div class='list'>");
            foreach (var h in g)
                sb.Append(Row(h.Title, h.Url, h.Url,
                    $"<span class='time'>{h.Visited:h:mm tt}</span><button class='x' title='Remove' data-t='{h.Visited.Ticks}'>&#x2715;</button>", h.Title + " " + h.Url));
            sb.Append("</div></section>");
        }
        if (items.Count == 0) sb.Append("<div class='empty'>No browsing history yet.</div>");
        var actions = "<input class='search' id='q' placeholder='Search history'><button class='pill' id='clearAll'>Clear all</button>";
        var script = """
        document.getElementById('q').oninput=e=>{const q=e.target.value.toLowerCase();
          document.querySelectorAll('section').forEach(s=>{let any=false;
            s.querySelectorAll('.row').forEach(r=>{const hit=r.dataset.s.includes(q);r.style.display=hit?'':'none';any=any||hit;});
            s.style.display=any?'':'none';});};
        document.getElementById('clearAll').onclick=()=>post({type:'hist-clear'});
        document.addEventListener('click',e=>{const x=e.target.closest('.x');if(!x)return;
          const row=x.closest('.row'),sec=row.closest('section');post({type:'hist-del',t:x.dataset.t});row.remove();
          if(sec&&!sec.querySelector('.row'))sec.remove();});
        """;
        return Page("History", $"{items.Count} pages", actions, sb.ToString(), script);
    }

    // ---------------- Bookmarks ----------------
    static string Bookmarks()
    {
        var sb = new StringBuilder("<div class='list'>");
        foreach (var b in Store.Bookmarks.OrderByDescending(b => b.Added))
            sb.Append(Row(b.Title, b.Url, b.Url, $"<button class='x' title='Remove' data-u='{E(b.Url)}'>&#x2715;</button>", b.Title + " " + b.Url));
        sb.Append("</div>");
        if (Store.Bookmarks.Count == 0) sb.Append("<div class='empty'>No bookmarks yet. Press Ctrl+D on any page to save it.</div>");
        var script = """
        document.getElementById('q').oninput=e=>{const q=e.target.value.toLowerCase();
          document.querySelectorAll('.row').forEach(r=>r.style.display=r.dataset.s.includes(q)?'':'none');};
        document.addEventListener('click',e=>{const x=e.target.closest('.x');if(!x)return;
          post({type:'bm-del',url:x.dataset.u});x.closest('.row').remove();});
        """;
        return Page("Bookmarks", $"{Store.Bookmarks.Count} saved", "<input class='search' id='q' placeholder='Search bookmarks'>", sb.ToString(), script);
    }

    // ---------------- Downloads ----------------
    static string DownloadsJson() => JsonSerializer.Serialize(
        Store.Downloads.OrderByDescending(d => d.Started).Select(d => new
        { d.Id, d.FileName, d.Path, d.State, d.Received, d.Total, Started = d.Started.ToString("MMM d, h:mm tt") })).Replace("<", "\\u003c");

    static string Downloads()
    {
        var script = "let items=" + DownloadsJson() + """
        ;
        const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
        const fmt=b=>b>=1073741824?(b/1073741824).toFixed(1)+' GB':b>=1048576?(b/1048576).toFixed(1)+' MB':b>=1024?(b/1024).toFixed(0)+' KB':b+' B';
        function render(){
          const l=document.getElementById('list');
          if(!items.length){l.innerHTML='<div class="empty">No downloads yet.</div>';return;}
          l.innerHTML=items.map(d=>{
            const prog=d.State==='InProgress', pct=d.Total>0?Math.round(d.Received*100/d.Total):0;
            const status=prog?(d.Total>0?fmt(d.Received)+' of '+fmt(d.Total)+' \u00b7 '+pct+'%':fmt(d.Received)+' downloaded'):d.State==='Completed'?fmt(d.Total||d.Received)+' \u00b7 '+d.Started:'Download failed or cancelled';
            const btns=prog?'<button class="pill" data-a="dl-cancel" data-id="'+d.Id+'">Cancel</button>':(d.State==='Completed'?'<button class="pill" data-a="dl-open" data-id="'+d.Id+'">Open</button><button class="pill" data-a="dl-folder" data-id="'+d.Id+'">Show in folder</button>':'')+'<button class="x" title="Remove from list" data-a="dl-remove" data-id="'+d.Id+'">&#x2715;</button>';
            return '<div class="row"><span class="dot">&#x2193;</span><div class="grow"><b>'+esc(d.FileName)+'</b><span class="sub">'+esc(status)+'</span>'+(prog?'<div class="bar"><i style="width:'+(d.Total>0?pct:30)+'%"></i></div>':'')+'</div>'+btns+'</div>';
          }).join('');
        }
        window.setDownloads=a=>{items=a;render();};
        document.addEventListener('click',e=>{const b=e.target.closest('[data-a]');if(b)post({type:b.dataset.a,id:b.dataset.id});});
        document.getElementById('clearAll').onclick=()=>post({type:'dl-clear'});
        render();
        """;
        return Page("Downloads", "Files saved to " + Store.Settings.DownloadFolder, "<button class='pill' id='clearAll'>Clear list</button>",
            "<div class='list' id='list'></div>", script);
    }

    /// <summary>Pushes the current download list into any open Downloads page.</summary>
    public static void PushDownloads(MainWindow win)
    {
        foreach (var t in win.Tabs.ToList())
            if (t.InternalUrl == "halo://downloads" && t.View.CoreWebView2 != null)
                _ = t.View.CoreWebView2.ExecuteScriptAsync("window.setDownloads&&setDownloads(" + DownloadsJson() + ")");
    }

    // ---------------- Settings ----------------
    static string Switch(string key, string label, string desc, bool on) =>
        $"<div class='set'><div><b>{E(label)}</b><small>{E(desc)}</small></div><label class='sw'><input type='checkbox' data-k='{key}' {(on ? "checked" : "")}><span></span></label></div>";

    static string Settings(bool toPrivacy)
    {
        var s = Store.Settings;
        var sb = new StringBuilder();

        sb.Append("<div class='card'><h3 style='margin-top:0'>Appearance</h3><div class='set'><div><b>Theme color</b><small>Pastel accent used across Halo</small></div><div class='chips'>");
        foreach (var (name, (p, c, oc)) in Theme.Presets)
            sb.Append($"<button class='swatch{(name == s.ThemeColor ? " on" : "")}' title='{name}' data-k='ThemeColor' data-v='{name}' data-p='{p}' data-c='{c}' data-oc='{oc}' style='background:linear-gradient(135deg,{c} 50%,{p} 50%)'></button>");
        sb.Append("</div></div>");
        sb.Append(Switch("ShowBookmarksBar", "Show bookmarks bar", "Display saved bookmarks under the toolbar", s.ShowBookmarksBar));
        sb.Append("</div>");

        sb.Append("<div class='card' style='margin-top:18px'><h3 style='margin-top:0'>Search &amp; startup</h3><div class='set'><div><b>Search engine</b><small>Used by the address bar and New Tab page</small></div><div class='chips'>");
        foreach (var name in Store.Engines.Keys)
            sb.Append($"<button class='chip{(name == s.SearchEngine ? " on" : "")}' data-k='SearchEngine' data-v='{name}'>{name}</button>");
        sb.Append("</div></div><div class='set'><div><b>Home page</b><small>Leave empty to use the New Tab page</small></div>");
        sb.Append($"<input class='field' data-k='HomePage' placeholder='https://example.com' value='{E(s.HomePage == "halo://newtab" ? "" : s.HomePage)}'></div></div>");

        sb.Append("<div class='card' id='privacy' style='margin-top:18px'><h3 style='margin-top:0'>Privacy</h3>");
        sb.Append(Switch("SaveHistory", "Save browsing history", "Record the pages you visit", s.SaveHistory));
        sb.Append(Switch("BlockTrackers", "Block trackers", "Stop known advertising and tracking requests", s.BlockTrackers));
        sb.Append(Switch("DoNotTrack", "Send \"Do Not Track\"", "Ask websites not to track you", s.DoNotTrack));
        sb.Append("<h3>Clear browsing data</h3><div class='checks'>"
            + "<label><input type='checkbox' id='c-history' checked> History</label>"
            + "<label><input type='checkbox' id='c-cookies'> Cookies &amp; site data</label>"
            + "<label><input type='checkbox' id='c-cache'> Cached files</label>"
            + "<label><input type='checkbox' id='c-downloads'> Download list</label></div>"
            + "<button class='pill fill' id='clear'>Clear data</button></div>");

        sb.Append("<div class='card' style='margin-top:18px'><h3 style='margin-top:0'>Downloads</h3>");
        sb.Append(Switch("AskDownloadLocation", "Ask where to save each file", "Show a save dialog for every download", s.AskDownloadLocation));
        sb.Append($"<div class='set'><div><b>Download folder</b><small>Where files are saved by default</small></div><input class='field' data-k='DownloadFolder' value='{E(s.DownloadFolder)}'></div></div>");

        var script = """
        const g=id=>document.getElementById(id).checked;
        document.querySelectorAll('[data-k]').forEach(el=>{
          const k=el.dataset.k;
          if(el.type==='checkbox') el.onchange=()=>post({type:'setting',key:k,value:el.checked});
          else if(el.tagName==='INPUT') el.onchange=()=>post({type:'setting',key:k,value:el.value});
          else el.onclick=()=>{
            document.querySelectorAll('[data-k="'+k+'"]').forEach(x=>x.classList.remove('on'));el.classList.add('on');
            post({type:'setting',key:k,value:el.dataset.v});
            if(k==='ThemeColor'){const r=document.documentElement.style;r.setProperty('--p',el.dataset.p);r.setProperty('--c',el.dataset.c);r.setProperty('--oc',el.dataset.oc);}
          };
        });
        document.getElementById('clear').onclick=()=>post({type:'clear',history:g('c-history'),cookies:g('c-cookies'),cache:g('c-cache'),downloads:g('c-downloads')});
        """ + (toPrivacy ? "setTimeout(()=>document.getElementById('privacy').scrollIntoView({behavior:'smooth'}),150);" : "");

        return Page("Settings", "Make Halo yours", "", sb.ToString(), script);
    }

    // ---------------- Messages from internal pages ----------------
    public static bool Handle(MainWindow win, TabVM tab, string type, JsonElement m)
    {
        string Str(string n) => m.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
        switch (type)
        {
            case "hist-del":
                if (long.TryParse(Str("t"), out var ticks)) { Store.History.RemoveAll(h => h.Visited.Ticks == ticks); Store.SaveHistory(); }
                return true;
            case "hist-clear":
                Store.History.Clear(); Store.SaveHistory(); win.Navigate(tab, tab.InternalUrl); return true;
            case "bm-del":
                Store.Bookmarks.RemoveAll(b => b.Url == Str("url")); Store.SaveBookmarks(); win.OnDataChanged(); return true;
            case "dl-open":
            case "dl-folder":
                var d = Store.Downloads.FirstOrDefault(x => x.Id == Str("id"));
                if (d == null) return true;
                if (!File.Exists(d.Path)) { win.Toast("That file was moved or deleted"); return true; }
                try
                {
                    if (type == "dl-open") Process.Start(new ProcessStartInfo(d.Path) { UseShellExecute = true });
                    else Process.Start("explorer.exe", $"/select,\"{d.Path}\"");
                }
                catch { win.Toast("Couldn't open the file"); }
                return true;
            case "dl-cancel":
                win.CancelDownload(Str("id")); return true;
            case "dl-remove":
                Store.Downloads.RemoveAll(x => x.Id == Str("id") && x.State != "InProgress"); Store.SaveDownloads(); PushDownloads(win); return true;
            case "dl-clear":
                Store.Downloads.RemoveAll(x => x.State != "InProgress"); Store.SaveDownloads(); PushDownloads(win); return true;
            case "setting":
                if (m.TryGetProperty("value", out var val)) ApplySetting(win, Str("key"), val);
                return true;
            case "clear":
                _ = ClearData(win, tab, m); return true;
        }
        return false;
    }

    static void ApplySetting(MainWindow win, string key, JsonElement v)
    {
        var s = Store.Settings;
        bool b = v.ValueKind == JsonValueKind.True;
        string str = v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
        switch (key)
        {
            case "ThemeColor": if (Theme.Presets.ContainsKey(str)) { s.ThemeColor = str; Theme.Apply(str); } break;
            case "SearchEngine": if (Store.Engines.ContainsKey(str)) s.SearchEngine = str; break;
            case "HomePage": s.HomePage = str.Length == 0 ? "halo://newtab" : Store.ToUrl(str); break;
            case "ShowBookmarksBar": s.ShowBookmarksBar = b; break;
            case "SaveHistory": s.SaveHistory = b; break;
            case "BlockTrackers": s.BlockTrackers = b; break;
            case "DoNotTrack": s.DoNotTrack = b; break;
            case "AskDownloadLocation": s.AskDownloadLocation = b; break;
            case "DownloadFolder": if (str.Length > 0) s.DownloadFolder = str; break;
        }
        Store.SaveSettings();
        win.OnDataChanged();
    }

    static async Task ClearData(MainWindow win, TabVM tab, JsonElement m)
    {
        bool B(string n) => m.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
        var kinds = (CoreWebView2BrowsingDataKinds)0;
        if (B("cookies")) kinds |= CoreWebView2BrowsingDataKinds.Cookies | CoreWebView2BrowsingDataKinds.AllDomStorage;
        if (B("cache")) kinds |= CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage;
        try { if (kinds != 0) await tab.View.CoreWebView2.Profile.ClearBrowsingDataAsync(kinds); } catch { }
        if (B("history")) { Store.History.Clear(); Store.SaveHistory(); }
        if (B("downloads")) { Store.Downloads.RemoveAll(d => d.State != "InProgress"); Store.SaveDownloads(); }
        win.Toast("Browsing data cleared");
    }
}
