using System.Windows;
using System.Windows.Media;

namespace HaloBrowser;

/// <summary>Pastel accent presets. Light theme only.</summary>
public static class Theme
{
    // name -> (primary, container, onContainer)
    public static readonly Dictionary<string, (string P, string C, string OC)> Presets = new()
    {
        ["Blue"]     = ("#0B57D0", "#D3E3FD", "#041E49"),
        ["Mint"]     = ("#146C43", "#C4EED0", "#00210E"),
        ["Lavender"] = ("#6750A4", "#E9DDFF", "#21005D"),
        ["Peach"]    = ("#9C4300", "#FFDBC8", "#331200"),
        ["Rose"]     = ("#A8365F", "#FFD9E2", "#3E001D"),
    };

    public static (string P, string C, string OC) Get(string name) =>
        Presets.TryGetValue(name ?? "", out var p) ? p : Presets["Blue"];

    static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    public static void Apply(string name)
    {
        var (p, c, oc) = Get(name);
        var r = Application.Current.Resources;
        r["HaloPrimary"] = B(p);
        r["HaloContainer"] = B(c);
        r["HaloOnContainer"] = B(oc);
    }
}
