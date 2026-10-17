using System.Windows;

namespace HaloBrowser;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Store.Load();
        Theme.Apply(Store.Settings.ThemeColor);
        base.OnStartup(e);
    }
}
