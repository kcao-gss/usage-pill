using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
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
    private int _activePollIntervalMinutes;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsStore = new SettingsStore(SettingsStore.DefaultPath());
        _settings = _settingsStore.Load();

        _theme = new ThemeWatcher();
        _theme.Start();

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _provider = new ClaudeUsageProvider(new ClaudeCredentialStore(ClaudeCredentialStore.DefaultPath()), _http, TimeProvider.System);

        _intervalDebounceTimer = new DispatcherTimer { Interval = IntervalDebounce };
        _intervalDebounceTimer.Tick += (_, _) => ApplyPendingInterval();

        _activePollIntervalMinutes = _settings.PollIntervalMinutes;
        _poller = CreatePoller(_activePollIntervalMinutes);

        _pill = new PillWindow(_settings, _theme);
        _detail = new DetailPopup(_settings.WarnThresholdPercent);
        _tray = new TrayIcon();

        _pill.LeftClicked += (_, _) => ToggleDetail();
        _pill.RightClicked += (_, _) => _tray.ShowContextMenu();

        _tray.RefreshRequested += async (_, _) => await _poller.RefreshNowAsync();
        _tray.TogglePillRequested += (_, _) => TogglePill();
        _tray.SettingsRequested += (_, _) => OpenSettings();
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

    private void ToggleDetail()
    {
        if (_detail.IsVisible) _detail.Hide();
        else _detail.ShowNear(_pill);
    }

    private void TogglePill()
    {
        if (_pill.IsVisible) _pill.Hide();
        else _pill.Show();
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings);
        window.SettingsChanged += (_, updated) =>
        {
            var startupChanged = updated.StartWithWindows != _settings.StartWithWindows;
            _settings = updated;
            _pill.Apply(updated);
            _detail.WarnThresholdPercent = updated.WarnThresholdPercent;
            Render(_poller.State);
            if (startupChanged) StartupShortcut.Set(updated.StartWithWindows);
            _settingsStore.Save(updated with { Window = _pill.CurrentPosition });

            // Debounce a changed poll interval instead of rebuilding on every slider tick; see
            // IntervalDebounce above.
            if (updated.PollIntervalMinutes != _activePollIntervalMinutes)
            {
                _intervalDebounceTimer.Stop();
                _intervalDebounceTimer.Start();
            }
        };
        window.Show();
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

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsStore.Save(_settings with { Window = _pill.CurrentPosition });
        _intervalDebounceTimer.Stop();
        _poller.Dispose();
        _detail.Close();
        _tray.Dispose();
        _theme.Dispose();
        _http.Dispose();
        base.OnExit(e);
    }
}
