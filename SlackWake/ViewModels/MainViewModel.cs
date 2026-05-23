using System;
using System.Windows;
using SlackWake.Helpers;
using SlackWake.Models;
using SlackWake.Services;

namespace SlackWake.ViewModels;

/// <summary>
/// Glue between the settings POCO, the idle and Slack services, and the UI.
///
/// Threading model:
///   - Idle ticks come in on the WPF dispatcher (DispatcherTimer).
///   - Slack events arrive on thread-pool threads; we marshal to the UI thread
///     before opening any overlay window.
/// </summary>
public class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly IdleMonitorService _idle;
    private readonly SlackMonitorService _slack;
    private readonly Action<SlackEvent> _showOverlay;

    private bool _isIdle;
    private string _status = "Initializing";
    private string _slackConnectionStatus = "Disconnected";

    public MainViewModel(
        AppSettings settings,
        SettingsService settingsService,
        IdleMonitorService idle,
        SlackMonitorService slack,
        Action<SlackEvent> showOverlay)
    {
        _settings = settings;
        _settingsService = settingsService;
        _idle = idle;
        _slack = slack;
        _showOverlay = showOverlay;

        _idle.IdleTimeChanged += OnIdleTick;
        _slack.NotificationReceived += OnSlackNotification;
        _slack.StatusChanged += s =>
        {
            _slackConnectionStatus = s;
            RecomputeStatus();
        };
    }

    // ---- Two-way bindable properties ----

    public bool Enabled
    {
        get => _settings.Enabled;
        set
        {
            if (_settings.Enabled == value) return;
            _settings.Enabled = value;
            Raise();
            Save();
            Reconfigure();
        }
    }

    public int IdleTimeoutSeconds
    {
        get => _settings.IdleTimeoutSeconds;
        set
        {
            // Lower bound prevents nuisance-fast triggers; upper bound just a sanity guard.
            var clamped = Math.Clamp(value, 10, 7200);
            if (_settings.IdleTimeoutSeconds == clamped) return;
            _settings.IdleTimeoutSeconds = clamped;
            Raise();
            Save();
        }
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (_settings.StartWithWindows == value) return;
            _settings.StartWithWindows = value;
            Raise();
            Save();
            StartupService.Set(value);
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    // ---- Lifecycle ----

    public void Start()
    {
        _idle.Start();
        Reconfigure();
        StartupService.Set(_settings.StartWithWindows);
    }

    public void Stop()
    {
        _idle.Stop();
        _slack.Stop();
    }

    // ---- Internals ----

    private void Reconfigure()
    {
        if (Enabled)
            _slack.Start();
        else
            _slack.Stop();

        RecomputeStatus();
    }

    private void OnIdleTick(TimeSpan idle)
    {
        var newIsIdle = idle.TotalSeconds >= _settings.IdleTimeoutSeconds;
        if (newIsIdle == _isIdle) return;
        _isIdle = newIsIdle;
        Log.Write($"idle state -> {(_isIdle ? "IDLE" : "ACTIVE")} (raw={idle.TotalSeconds:F1}s threshold={_settings.IdleTimeoutSeconds}s)");
        RecomputeStatus();
    }

    private void OnSlackNotification(SlackEvent evt)
    {
        Log.Write($"VM received SlackEvent: enabled={Enabled} isIdle={_isIdle} sender='{evt.Sender}' channel='{evt.Channel}' text='{evt.Text}'");

        // Only fire overlays when the user is actually away — that is the whole
        // point of the app. Active users hear Slack's own notifications.
        if (!Enabled || !_isIdle)
        {
            Log.Write("  -> suppressed (not idle or disabled)");
            return;
        }

        Log.Write("  -> dispatching overlay");
        System.Windows.Application.Current?.Dispatcher.Invoke(() => _showOverlay(evt));
    }

    private void RecomputeStatus()
    {
        if (!Enabled) { Status = "Disabled"; return; }

        if (_isIdle)
            Status = "Idle - " + _slackConnectionStatus;
        else
            Status = "Active - " + _slackConnectionStatus;
    }

    private void Save() => _settingsService.Save(_settings);
}
