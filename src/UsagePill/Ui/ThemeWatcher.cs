using System.Windows.Threading;
using Microsoft.Win32;

namespace UsagePill.Ui;

/// <summary>Tracks the Windows app theme so the pill can follow it.</summary>
public sealed class ThemeWatcher : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };

    private bool _disposed;

    public ThemeWatcher()
    {
        IsDark = ReadIsDark();
        _timer.Tick += (_, _) =>
        {
            if (_disposed) return;
            var current = ReadIsDark();
            if (current == IsDark) return;
            IsDark = current;
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public bool IsDark { get; private set; }

    public event EventHandler? Changed;

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
    }

    private static bool ReadIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is not int light || light == 0;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }
}
