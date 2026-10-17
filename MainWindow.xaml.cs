using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HaloBrowser;

public class TabVM : INotifyPropertyChanged
{
    string _title = "New tab"; ImageSource _fav; bool _sel; double _w = 200;
    public WebView2 View { get; set; }
    public string InternalUrl { get; set; }   // e.g. "halo://history" while an internal page is shown
    public bool Loading { get; set; }
    public string Title { get => _title; set { _title = value; N(nameof(Title)); } }
    public ImageSource Favicon { get => _fav; set { _fav = value; N(nameof(Favicon)); } }
    public bool IsSelected { get => _sel; set { _sel = value; N(nameof(IsSelected)); } }
    public double Width { get => _w; set { _w = value; N(nameof(Width)); } }
    public string Url => InternalUrl ?? View?.CoreWebView2?.Source ?? "";
    public event PropertyChangedEventHandler PropertyChanged;
    void N(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

public partial class MainWindow : Window
{
    public readonly ObservableCollection<TabVM> Tabs = new();
    public TabVM Current { get; private set; }
    CoreWebView2Environment _env;
    readonly Stack<string> _closed = new();
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(1900) };

    // Forwards shortcut keys from web pages (WebView2 swallows keyboard focus from WPF).
    const string KeyScript = """
    (() => {
      if (window.__haloKeys) return; window.__haloKeys = true;
      const ctrlKeys = new Set(['t','w','l','r','d','h','j',',','tab','n','p','delete','1','2','3','4','5','6','7','8','9']);
      window.addEventListener('keydown', e => {
        const k = e.key.toLowerCase(), c = e.ctrlKey || e.metaKey;
        let hit = false;
        if (c && !e.altKey && ctrlKeys.has(k)) hit = true;
        else if (e.altKey && !c && (k === 'arrowleft' || k === 'arrowright' || k === 'home')) hit = true;
        else if (k === 'f5' || k === 'f6') hit = true;
        if (hit) {
          e.preventDefault(); e.stopPropagation();
          window.chrome.webview.postMessage({ type: 'key', key: k, ctrl: c, shift: e.shiftKey, alt: e.altKey });
        }
      }, true);
    })();
    """;

    public MainWindow()
    {
        InitializeComponent();
        TabStrip.ItemsSource = Tabs;
        Tabs.CollectionChanged += (_, _) => LayoutTabs();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastPopup.IsOpen = false; };
    }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { _env = await CoreWebView2Environment.CreateAsync(null, Store.UserDataFolder); }
        catch (Exception ex)
        {
            MessageBox.Show("The Microsoft Edge WebView2 Runtime is required.\nInstall it with:\nwinget install Microsoft.EdgeWebView2Runtime\n\n" + ex.Message,
                "Halo Browser", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close(); return;
        }
        RefreshBookmarks();
        await AddTab("halo://newtab");
    }

    // ---------------- Tabs ----------------
    public async Task<TabVM> AddTab(string input, bool select = true)
    {
        var wv = new WebView2 { Visibility = Visibility.Collapsed, DefaultBackgroundColor = System.Drawing.Color.White };
        var tab = new TabVM { View = wv, Title = "New tab" };
        WebHost.Children.Add(wv);
        Tabs.Add(tab);
        if (select) SelectTab(tab);
        await wv.EnsureCoreWebView2Async(_env);
        await wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(KeyScript);
        Hook(tab);
        Navigate(tab, input);
        return tab;
    }

    public void SelectTab(TabVM tab)
    {
        foreach (var t in Tabs) { t.IsSelected = t == tab; t.View.Visibility = t == tab ? Visibility.Visible : Visibility.Collapsed; }
        Current = tab;
        UpdateChrome();
        tab.View.Focus();
    }

    public void CloseTab(TabVM tab)
    {
        int i = Tabs.IndexOf(tab);
        if (i < 0) return;
        if (!string.IsNullOrEmpty(tab.Url)) _closed.Push(tab.Url);
        Tabs.Remove(tab);
        WebHost.Children.Remove(tab.View);
        tab.View.Dispose();
        if (Tabs.Count == 0) { Close(); return; }
        if (tab == Current) SelectTab(Tabs[Math.Min(i, Tabs.Count - 1)]);
    }

    void CycleTab(int dir)
    {
        if (Tabs.Count < 2) return;
        int i = (Tabs.IndexOf(Current) + dir + Tabs.Count) % Tabs.Count;
        SelectTab(Tabs[i]);
    }

    void LayoutTabs()
    {
        if (Tabs.Count == 0) return;
        double w = Math.Clamp((ActualWidth - 90) / Tabs.Count - 4, 96, 240);
        foreach (var t in Tabs) t.Width = w;
    }

    void Window_SizeChanged(object s, SizeChangedEventArgs e) => LayoutTabs();

    void Tab_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (InButton(e.OriginalSource as DependencyObject)) return;
        var tab = (TabVM)((FrameworkElement)s).DataContext;
        if (e.ChangedButton == MouseButton.Left) SelectTab(tab);
        else if (e.ChangedButton == MouseButton.Middle) CloseTab(tab);
    }

    void CloseTab_Click(object s, RoutedEventArgs e) => CloseTab((TabVM)((FrameworkElement)s).DataContext);

    static bool InButton(DependencyObject d)
    {
        while (d != null) { if (d is Button) return true; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d); }
        return false;
    }

    // ---------------- WebView events ----------------
    void Hook(TabVM tab)
    {
        var cw = tab.View.CoreWebView2;

        cw.NavigationStarting += (s, e) =>
        {
            if (e.Uri.StartsWith("halo://", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                var u = e.Uri; Dispatcher.BeginInvoke(() => Navigate(tab, u));
                return;
            }
            if (!e.Uri.StartsWith("data:") && !e.Uri.StartsWith("about:")) tab.InternalUrl = null;
            tab.Loading = true; Sync(tab);
        };

        cw.NavigationCompleted += (s, e) =>
        {
            tab.Loading = false;
            if (tab.InternalUrl == null && cw.Source.StartsWith("data:"))      // back/forward onto an internal page
                tab.InternalUrl = Pages.UrlFromTitle(cw.DocumentTitle);
            if (tab.InternalUrl == null) Store.AddHistory(cw.DocumentTitle, cw.Source);
            Sync(tab);
        };

        cw.SourceChanged += (s, e) => Sync(tab);
        cw.DocumentTitleChanged += (s, e) => Sync(tab);
        cw.HistoryChanged += (s, e) => { if (tab == Current) UpdateChrome(); };
        cw.WindowCloseRequested += (s, e) => CloseTab(tab);
        HookContextMenu(tab);
        HookFeatures(tab);

        cw.NewWindowRequested += (s, e) =>
        {
            e.Handled = true;
            var u = e.Uri; Dispatcher.BeginInvoke(async () => await AddTab(u));
        };

        cw.FaviconChanged += async (s, e) =>
        {
            try
            {
                if (tab.InternalUrl != null) { tab.Favicon = null; return; }
                using var st = await cw.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                if (st == null) { tab.Favicon = null; return; }
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = st; bi.EndInit(); bi.Freeze();
                tab.Favicon = bi;
            }
            catch { }
        };

        cw.WebMessageReceived += (s, e) =>
        {
            try { using var doc = JsonDocument.Parse(e.WebMessageAsJson); OnMessage(tab, doc.RootElement); }
            catch { }
        };
    }

    void OnMessage(TabVM tab, JsonElement m)
    {
        var type = m.GetProperty("type").GetString();
        if (type == "key")
        {
            Shortcut(m.GetProperty("key").GetString(), m.GetProperty("ctrl").GetBoolean(),
                     m.GetProperty("shift").GetBoolean(), m.GetProperty("alt").GetBoolean());
            return;
        }
        if (tab.InternalUrl == null) return;   // only Halo's own pages may send commands
        switch (type)
        {
            case "navigate": Navigate(tab, m.GetProperty("url").GetString()); break;
            case "search": Navigate(tab, Store.ToUrl(m.GetProperty("q").GetString())); break;
            default: Pages.Handle(this, tab, type, m); break;
        }
    }

    void Sync(TabVM tab)
    {
        var cw = tab.View.CoreWebView2;
        if (cw == null) return;
        if (tab.InternalUrl != null) { tab.Title = Pages.TitleFor(tab.InternalUrl); tab.Favicon = null; }
        else
        {
            var t = cw.DocumentTitle;
            tab.Title = string.IsNullOrWhiteSpace(t) ? (tab.Loading ? "Loading…" : cw.Source) : t;
        }
        if (tab == Current) UpdateChrome();
    }

    // ---------------- Navigation ----------------
    public void Navigate(TabVM tab, string input)
    {
        if (tab?.View?.CoreWebView2 == null) return;
        var url = Store.ToUrl(input);
        if (url.StartsWith("halo://"))
        {
            tab.InternalUrl = url;
            tab.View.NavigateToString(Pages.Render(url));
            Sync(tab);
        }
        else { tab.InternalUrl = null; tab.View.CoreWebView2.Navigate(url); }
    }

    public void OpenInternal(string url)
    {
        if (Current?.InternalUrl == "halo://newtab") Navigate(Current, url);
        else _ = AddTab(url);
    }

    void Reload()
    {
        var cw = Current?.View?.CoreWebView2; if (cw == null) return;
        if (Current.Loading) cw.Stop();
        else if (Current.InternalUrl != null) Navigate(Current, Current.InternalUrl);
        else cw.Reload();
    }

    void GoBack() { if (Current?.View?.CoreWebView2?.CanGoBack == true) Current.View.CoreWebView2.GoBack(); }
    void GoForward() { if (Current?.View?.CoreWebView2?.CanGoForward == true) Current.View.CoreWebView2.GoForward(); }

    void Back_Click(object s, RoutedEventArgs e) => GoBack();
    void Forward_Click(object s, RoutedEventArgs e) => GoForward();
    void Reload_Click(object s, RoutedEventArgs e) => Reload();
    void Home_Click(object s, RoutedEventArgs e) => Navigate(Current, Store.Settings.HomePage);
    void NewTab_Click(object s, RoutedEventArgs e) => _ = AddTab("halo://newtab");
    void Downloads_Click(object s, RoutedEventArgs e) => OpenInternal("halo://downloads");
    void Star_Click(object s, RoutedEventArgs e) => ToggleBookmark();

    // ---------------- Chrome (toolbar) state ----------------
    public void UpdateChrome()
    {
        if (Current == null) return;
        var cw = Current.View.CoreWebView2;
        BackBtn.IsEnabled = cw?.CanGoBack == true;
        FwdBtn.IsEnabled = cw?.CanGoForward == true;
        ReloadBtn.Content = Current.Loading ? "\uE711" : "\uE72C";
        ReloadBtn.ToolTip = Current.Loading ? "Stop loading" : "Reload (F5)";
        LoadBar.Visibility = Current.Loading ? Visibility.Visible : Visibility.Collapsed;
        var url = Current.Url;
        if (!Omnibox.IsKeyboardFocused) Omnibox.Text = Current.InternalUrl == "halo://newtab" ? "" : url;
        OmniHint.Text = $"Search {Store.Settings.SearchEngine} or type a URL";
        bool marked = Store.IsBookmarked(url);
        StarBtn.Content = marked ? "\uE735" : "\uE734";
        StarBtn.SetResourceReference(Control.ForegroundProperty, marked ? "HaloPrimary" : "HaloOnVariant");
        Title = Current.Title + " - Halo";
    }

    /// <summary>Called by internal pages after they change bookmarks/settings.</summary>
    public void OnDataChanged() { RefreshBookmarks(); UpdateChrome(); }

    // ---------------- Omnibox ----------------
    void Omnibox_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Navigate(Current, Omnibox.Text); Current.View.Focus(); }
        else if (e.Key == Key.Escape) { e.Handled = true; Current.View.Focus(); }
    }
    void Omnibox_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (!Omnibox.IsKeyboardFocusWithin) { e.Handled = true; Omnibox.Focus(); }
    }
    void Omnibox_GotFocus(object s, KeyboardFocusChangedEventArgs e) => Omnibox.SelectAll();
    void Omnibox_LostFocus(object s, KeyboardFocusChangedEventArgs e) => UpdateChrome();
    void Omnibox_TextChanged(object s, TextChangedEventArgs e) =>
        OmniHint.Visibility = Omnibox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ---------------- Bookmarks ----------------
    void ToggleBookmark()
    {
        var url = Current?.Url;
        if (string.IsNullOrEmpty(url) || Current.InternalUrl != null) { Toast("This page can't be bookmarked"); return; }
        bool added = Store.ToggleBookmark(Current.Title, url);
        RefreshBookmarks(); UpdateChrome();
        Toast(added ? "Bookmark added" : "Bookmark removed");
    }

    public void RefreshBookmarks()
    {
        BookmarkBar.Children.Clear();
        BookmarkBarHost.Visibility = Store.Settings.ShowBookmarksBar && Store.Bookmarks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var b in Store.Bookmarks.Take(40))
        {
            var btn = new Button
            {
                Content = new TextBlock { Text = b.Title, TextTrimming = TextTrimming.CharacterEllipsis },
                Style = (Style)FindResource("PillButton"), Height = 32, Padding = new Thickness(14, 0, 14, 0),
                FontSize = 13, MaxWidth = 180, Margin = new Thickness(0, 0, 6, 0), ToolTip = b.Url
            };
            btn.SetResourceReference(Control.BackgroundProperty, "HaloSurfaceDim");
            btn.SetResourceReference(Control.ForegroundProperty, "HaloOnSurface");
            var url = b.Url;
            btn.Click += (_, _) => Navigate(Current, url);
            var cm = new ContextMenu();
            var open = new MenuItem { Header = "Open in new tab" }; open.Click += (_, _) => _ = AddTab(url);
            var del = new MenuItem { Header = "Remove bookmark" };
            del.Click += (_, _) => { Store.Bookmarks.Remove(b); Store.SaveBookmarks(); OnDataChanged(); };
            cm.Items.Add(open); cm.Items.Add(del);
            btn.ContextMenu = cm;
            BookmarkBar.Children.Add(btn);
        }
    }

    // ---------------- Menu ----------------
    void MenuBtn_Click(object s, RoutedEventArgs e)
    {
        var m = (ContextMenu)FindResource("MainMenu");
        m.PlacementTarget = MenuBtn; m.Placement = PlacementMode.Bottom; m.HorizontalOffset = -235;
        m.IsOpen = true;
    }

    void Menu_Item(object s, RoutedEventArgs e)
    {
        var tag = (string)((MenuItem)s).Tag;
        switch (tag)
        {
            case "newtab": _ = AddTab("halo://newtab"); break;
            case "newwindow": new MainWindow().Show(); break;
            case "bookmark": ToggleBookmark(); break;
            case "print": Current?.View.CoreWebView2?.ShowPrintUI(); break;
            case "devtools": Current?.View.CoreWebView2?.OpenDevToolsWindow(); break;
            case "exit": Close(); break;
            default: OpenInternal(tag); break;
        }
    }

    public void Toast(string message)
    {
        ToastText.Text = message;
        ToastPopup.VerticalOffset = WebHost.ActualHeight / 2 - 70;
        ToastPopup.IsOpen = false; ToastPopup.IsOpen = true;
        _toastTimer.Stop(); _toastTimer.Start();
    }

    // ---------------- Keyboard shortcuts ----------------
    void Window_PreviewKeyDown(object s, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        string k = key switch
        {
            Key.Tab => "tab", Key.F5 => "f5", Key.F6 => "f6", Key.Left => "arrowleft", Key.Right => "arrowright",
            Key.Delete => "delete", Key.Home => "home", Key.OemComma => ",",
            >= Key.D1 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.A and <= Key.Z => key.ToString().ToLowerInvariant(),
            _ => null
        };
        if (k == null) return;
        var m = Keyboard.Modifiers;
        if (Shortcut(k, m.HasFlag(ModifierKeys.Control), m.HasFlag(ModifierKeys.Shift), m.HasFlag(ModifierKeys.Alt))) e.Handled = true;
    }

    public bool Shortcut(string k, bool ctrl, bool shift, bool alt)
    {
        k = k.ToLowerInvariant();
        if (ctrl && !alt)
        {
            switch (k)
            {
                case "t" when shift:
                    if (_closed.Count > 0) _ = AddTab(_closed.Pop());
                    return true;
                case "t": _ = AddTab("halo://newtab"); return true;
                case "w": if (Current != null) CloseTab(Current); return true;
                case "l": Omnibox.Focus(); return true;
                case "r": Reload(); return true;
                case "d": ToggleBookmark(); return true;
                case "h": OpenInternal("halo://history"); return true;
                case "j": OpenInternal("halo://downloads"); return true;
                case ",": OpenInternal("halo://settings"); return true;
                case "n": new MainWindow().Show(); return true;
                case "p": Current?.View.CoreWebView2?.ShowPrintUI(); return true;
                case "tab": CycleTab(shift ? -1 : 1); return true;
                case "delete" when shift: OpenInternal("halo://settings#privacy"); return true;
            }
            if (k.Length == 1 && k[0] >= '1' && k[0] <= '9')
            {
                int n = k[0] - '1';
                if (k == "9") n = Tabs.Count - 1;
                if (n < Tabs.Count) SelectTab(Tabs[n]);
                return true;
            }
        }
        else if (alt && !ctrl)
        {
            switch (k)
            {
                case "arrowleft": GoBack(); return true;
                case "arrowright": GoForward(); return true;
                case "home": Navigate(Current, Store.Settings.HomePage); return true;
            }
        }
        else if (!ctrl && !alt)
        {
            if (k == "f5") { Reload(); return true; }
            if (k == "f6") { Omnibox.Focus(); return true; }
        }
        return false;
    }
}
