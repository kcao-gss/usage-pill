using System.IO;
using System.Security;
using System.Windows.Threading;
using Microsoft.Win32;

namespace UsagePill.Ui;

/// <summary>
/// Tracks the Windows app theme so the pill can follow it. Construct and start this on the UI
/// thread: the timer binds to the current dispatcher, so off the UI thread it never ticks.
/// </summary>
public sealed class ThemeWatcher : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };

    private volatile bool _disposed;

    public ThemeWatcher()
    {
        // ReadIsDark falls back to the current value, so IsDark is seeded at its declaration.
        IsDark = ReadIsDark();
        _timer.Tick += (_, _) =>
        {
            if (_disposed) return;
            var current = ReadIsDark();
            if (current == IsDark) return;
            IsDark = current;
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // A throwing subscriber must not escape onto the dispatcher and kill the app.
            }
        };
    }

    /// <summary>True when Windows is in dark mode; dark is the assumed default.</summary>
    public bool IsDark { get; private set; } = true;

    public event EventHandler? Changed;

    public void Start()
    {
        if (_disposed) return;
        _timer.Start();
    }

    /// <summary>Reads the theme, keeping the last known value if the registry is unreadable.</summary>
    private bool ReadIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is not int light || light == 0;
        }
        catch (Exception e) when (e is SecurityException or IOException or UnauthorizedAccessException)
        {
            // A transient registry failure on one of thousands of daily polls is not fatal.
            return IsDark;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }
}
