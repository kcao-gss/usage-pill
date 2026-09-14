using System.Windows;
using UsagePill.Settings;
using UsagePill.Ui;

namespace UsagePill;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = new SettingsStore(SettingsStore.DefaultPath()).Load();
        var theme = new ThemeWatcher();
        theme.Start();

        var window = new PillWindow(settings, theme);
        window.Show();
    }
}
