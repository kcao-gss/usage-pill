using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.CSharp.RuntimeBinder;
using UsagePill.Claude;
using UsagePill.Core;
using UsagePill.Polling;
using UsagePill.Settings;
using UsagePill.Ui;

namespace UsagePill;

public partial class App : Application
{
    // Rebuilding the poller restarts its backoff ladder and fires an immediate refresh, so a
    // rapid drag across the Interval slider (which raises SettingsChanged on every tick) must
    // not rebuild it once per tick. This debounce coalesces a burst of drags into a single
    // rebuild once the user settles on a value.
    private static readonly TimeSpan IntervalDebounce = TimeSpan.FromMilliseconds(800);

    // Coalesces a burst of SettingsChanged events (a slider drag can raise up to a hundred) into
    // a single settings.json write, without ever leaving a change unwritten: OpenSettings' Closed
    // handler and OnExit both flush synchronously regardless of this timer's state.
    private static readonly TimeSpan SettingsSaveDebounce = TimeSpan.FromMilliseconds(500);

    // See DetailPopup.LastHiddenAt: the click that dismisses the popup deactivates it (hiding it)
    // before PillWindow.LeftClicked is raised for that same click. A click landing this soon
    // after a deactivation-triggered hide is dismiss-only, not a request to reopen.
    private static readonly TimeSpan DetailReopenGuard = TimeSpan.FromMilliseconds(250);

    private SettingsStore _settingsStore = null!;
    private AppSettings _settings = null!;
    private ThemeWatcher _theme = null!;
    private PillWindow _pill = null!;
    private DetailPopup _detail = null!;
    private TrayIcon _tray = null!;
    private UsagePoller _poller = null!;
    private HttpClient _http = null!;
    private IUsageProvider _provider = null!;
    private DispatcherTimer _intervalDebounceTimer = null!;
    private DispatcherTimer _settingsSaveDebounceTimer = null!;
    private SettingsWindow? _settingsWindow;
    private int _activePollIntervalMinutes;
    private bool _tornDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // An unhandled exception on the UI thread would otherwise terminate the process without
        // OnExit running, stranding the tray icon and leaving unsaved settings on disk. Shutting
        // down explicitly instead routes through the normal OnExit teardown.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Shutdown();
        };

        try
        {
            BuildAndStart();
        }
        catch
        {
            TeardownPartialStartup();
            throw;
        }
    }

    private void BuildAndStart()
    {
        _settingsStore = new SettingsStore(SettingsStore.DefaultPath());
        _settings = _settingsStore.Load();

        // The Startup-folder shortcut, not the persisted flag, is the source of truth: a user
        // who deletes the shortcut by hand, or copies settings.json to another machine, must not
        // see a checkbox that lies about what will actually happen at the next logon.
        var startupEnabled = StartupShortcut.IsEnabled();
        if (_settings.StartWithWindows != startupEnabled)
        {
            _settings = _settings with { StartWithWindows = startupEnabled };
        }

        _theme = new ThemeWatcher();
        _theme.Start();

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _provider = new ClaudeUsageProvider(new ClaudeCredentialStore(ClaudeCredentialStore.DefaultPath()), _http, TimeProvider.System);

        _intervalDebounceTimer = new DispatcherTimer { Interval = IntervalDebounce };
        _intervalDebounceTimer.Tick += (_, _) => ApplyPendingInterval();

        _settingsSaveDebounceTimer = new DispatcherTimer { Interval = SettingsSaveDebounce };
        _settingsSaveDebounceTimer.Tick += (_, _) =>
        {
            _settingsSaveDebounceTimer.Stop();
            PersistSettings();
        };

        _activePollIntervalMinutes = _settings.PollIntervalMinutes;
        _poller = CreatePoller(_activePollIntervalMinutes);

        _pill = new PillWindow(_settings, _theme);
        _detail = new DetailPopup(_settings.WarnThresholdPercent);
        _tray = new TrayIcon();
        _tray.StartWithWindowsChecked = startupEnabled;

        _pill.LeftClicked += (_, pressedAt) => ToggleDetail(pressedAt);
        _pill.RightClicked += (_, _) => _tray.ShowContextMenu();

        _tray.RefreshRequested += async (_, _) => await _poller.RefreshNowAsync();
        _tray.TogglePillRequested += (_, _) => TogglePill();
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.StartWithWindowsToggled += (_, _) => ToggleStartWithWindows();
        _tray.QuitRequested += (_, _) => Shutdown();

        _pill.Show();
        _poller.Start();
    }

    private UsagePoller CreatePoller(int intervalMinutes)
    {
        var poller = new UsagePoller(_provider, new BackoffPolicy(TimeSpan.FromMinutes(intervalMinutes)), TimeProvider.System);
        poller.StateChanged += (_, state) => Dispatcher.Invoke(() => Render(state));
        return poller;
    }

    private void Render(UsageState state)
    {
        _pill.Apply(state);
        _detail.Apply(state, _provider.DisplayName);
        _tray.Apply(state, _settings.WarnThresholdPercent);
    }

    private void ToggleDetail(DateTime pressedAt)
    {
        if (_detail.IsVisible)
        {
            _detail.Hide();
            return;
        }

        // Compared against the moment this click's mouse-down fired, not against now: LeftClicked
        // only reaches here after DragMove releases, so "now" would measure how long the button
        // was held rather than how soon this click followed the deactivation-triggered hide.
        if (pressedAt - _detail.LastHiddenAt < DetailReopenGuard) return;

        _detail.ShowNear(_pill);
    }

    private void TogglePill()
    {
        if (_pill.IsVisible) _pill.Hide();
        else _pill.Show();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(_settings);
        window.SettingsChanged += (_, updated) =>
        {
            var startupChanged = updated.StartWithWindows != _settings.StartWithWindows;
            _settings = updated;
            _pill.Apply(updated);
            _detail.WarnThresholdPercent = updated.WarnThresholdPercent;
            Render(_poller.State);

            if (startupChanged) ReconcileStartWithWindows(updated.StartWithWindows);

            // Coalesce a burst of slider-drag events into one write; flushed for real on window
            // close and on exit regardless of whether this timer has fired yet.
            _settingsSaveDebounceTimer.Stop();
            _settingsSaveDebounceTimer.Start();

            // Debounce a changed poll interval instead of rebuilding on every slider tick; see
            // IntervalDebounce above.
            if (updated.PollIntervalMinutes != _activePollIntervalMinutes)
            {
                _intervalDebounceTimer.Stop();
                _intervalDebounceTimer.Start();
            }
        };
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _settingsSaveDebounceTimer.Stop();
            PersistSettings();
        };

        _settingsWindow = window;
        window.Show();
    }

    private void ToggleStartWithWindows()
    {
        ReconcileStartWithWindows(!StartupShortcut.IsEnabled());
        _settingsSaveDebounceTimer.Stop();
        PersistSettings();
    }

    /// <summary>
    /// Applies a desired Start-with-Windows state and reconciles every surface - the settings
    /// flag, the tray checkbox, and an open settings window - with what actually exists on disk
    /// afterward, since a locked Startup folder can make the write itself fail.
    /// </summary>
    private void ReconcileStartWithWindows(bool desired)
    {
        try
        {
            StartupShortcut.Set(desired);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or COMException or InvalidOperationException or RuntimeBinderException)
        {
            // Best effort: a locked Startup folder, an unregistered/blocked wshom.ocx, or any
            // other shortcut-writer failure must not crash the app - but it must not be silent
            // either, so the user knows the toggle did not take effect.
            _tray.ShowError("Start with Windows", "Could not update the Windows Startup shortcut. " + ex.Message);
        }

        var actual = StartupShortcut.IsEnabled();
        _settings = _settings with { StartWithWindows = actual };
        _tray.StartWithWindowsChecked = actual;
        _settingsWindow?.SetStartWithWindows(actual);
    }

    /// <summary>
    /// Swaps in a freshly built poller once the user has settled on a poll interval. The old
    /// poller's schedule and backoff state are discarded; the new one starts immediately, which
    /// re-fetches promptly rather than waiting out whatever was left of the old interval.
    /// </summary>
    private void ApplyPendingInterval()
    {
        _intervalDebounceTimer.Stop();
        if (_settings.PollIntervalMinutes == _activePollIntervalMinutes) return;

        var old = _poller;
        _activePollIntervalMinutes = _settings.PollIntervalMinutes;
        _poller = CreatePoller(_activePollIntervalMinutes);
        _poller.Start();
        old.Dispose();
    }

    private void PersistSettings()
    {
        try
        {
            _settingsStore.Save(_settings with { Window = _pill.CurrentPosition });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a locked or full settings file must not crash the app or block
            // teardown - see OnExit, which relies on this never throwing.
        }
    }

    /// <summary>
    /// Disposes whatever OnStartup managed to construct before it failed, so a partial startup
    /// never leaves a ghost tray icon (or any other native resource) behind.
    /// </summary>
    private void TeardownPartialStartup()
    {
        if (_tornDown) return;
        _tornDown = true;

        _intervalDebounceTimer?.Stop();
        _settingsSaveDebounceTimer?.Stop();
        _poller?.Dispose();
        _detail?.Close();
        _tray?.Dispose();
        _pill?.Close();
        _theme?.Dispose();
        _http?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tornDown)
        {
            base.OnExit(e);
            return;
        }
        _tornDown = true;

        _settingsSaveDebounceTimer.Stop();
        PersistSettings();

        _intervalDebounceTimer.Stop();
        _poller.Dispose();
        _detail.Close();
        _settingsWindow?.Close();
        _tray.Dispose();
        _theme.Dispose();
        _http.Dispose();
        base.OnExit(e);
    }
}
