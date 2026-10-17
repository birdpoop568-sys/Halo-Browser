using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace HaloBrowser;

public partial class MainWindow
{
    /// <summary>Replaces WebView2's native right-click menu with a Halo-styled one that runs the same commands.</summary>
    void HookContextMenu(TabVM tab)
    {
        tab.View.CoreWebView2.ContextMenuRequested += (s, e) =>
        {
            e.Handled = true;
            var deferral = e.GetDeferral();
            var cm = new ContextMenu { Placement = PlacementMode.MousePoint };
            var tgt = e.ContextMenuTarget;

            void AddSep() { if (cm.Items.Count > 0 && cm.Items[^1] is not Separator) cm.Items.Add(new Separator()); }

            if (tgt.HasLinkUri)
            {
                var link = tgt.LinkUri;
                var mi = new MenuItem { Header = "Open link in new tab" };
                mi.Click += (_, _) => _ = AddTab(link);
                cm.Items.Add(mi); AddSep();
            }
            if (tgt.HasSelection && !tgt.IsEditable && !string.IsNullOrWhiteSpace(tgt.SelectionText))
            {
                var text = tgt.SelectionText.Trim();
                var shown = text.Length > 28 ? text.Substring(0, 28) + "…" : text;
                var mi = new MenuItem { Header = $"Search {Store.Settings.SearchEngine} for \u201c{shown}\u201d" };
                mi.Click += (_, _) => _ = AddTab(Store.SearchUrl(text));
                cm.Items.Add(mi); AddSep();
            }

            foreach (var it in e.MenuItems)
            {
                if (it.Kind == CoreWebView2ContextMenuItemKind.Separator) { AddSep(); continue; }
                if (it.Kind == CoreWebView2ContextMenuItemKind.Submenu) continue;
                var label = it.Label.Replace("&", "");
                if ((it.Kind == CoreWebView2ContextMenuItemKind.CheckBox || it.Kind == CoreWebView2ContextMenuItemKind.Radio) && it.IsChecked) label = "\u2713 " + label;
                var mi = new MenuItem { Header = label, IsEnabled = it.IsEnabled, InputGestureText = it.ShortcutKeyDescription };
                var id = it.CommandId;
                mi.Click += (_, _) => e.SelectedCommandId = id;
                cm.Items.Add(mi);
            }
            if (cm.Items.Count > 0 && cm.Items[^1] is Separator) cm.Items.RemoveAt(cm.Items.Count - 1);

            if (cm.Items.Count == 0) { deferral.Complete(); return; }
            cm.Closed += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => deferral.Complete()));
            cm.IsOpen = true;
        };
    }
}
