using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
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

        AvailableSounds = SoundLibrary.Enumerate();
        _selectedSound = ResolveSelectedSound(_settings.SoundFilePath);

        _preview = new PreviewPlayer();
        _preview.IsPlayingChanged += (_, _) =>
        {
            // The play/pause glyph and the IsPlaying flag flip together — fire both
            // notifications so any binding (button content, future state styling) updates.
            Raise(nameof(IsPreviewPlaying));
            Raise(nameof(PreviewButtonGlyph));
        };
        TogglePreviewCommand = new RelayCommand(TogglePreview);
    }

    private SoundLibrary.SoundOption ResolveSelectedSound(string path)
    {
        foreach (var s in AvailableSounds)
        {
            if (string.Equals(s.FilePath, path, StringComparison.OrdinalIgnoreCase))
                return s;
        }
        // Saved path no longer exists (folder change, removed file) — fall back to the
        // system-default entry, which we guarantee is always at index 0.
        return AvailableSounds[0];
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

    public bool SoundEnabled
    {
        get => _settings.SoundEnabled;
        set
        {
            if (_settings.SoundEnabled == value) return;
            _settings.SoundEnabled = value;
            Raise();
            Save();
        }
    }

    public int SoundDelaySeconds
    {
        get => _settings.SoundDelaySeconds;
        set
        {
            var clamped = Math.Clamp(value, 0, 120);
            if (_settings.SoundDelaySeconds == clamped) return;
            _settings.SoundDelaySeconds = clamped;
            Raise();
            Save();
        }
    }

    public bool SoundLoop
    {
        get => _settings.SoundLoop;
        set
        {
            if (_settings.SoundLoop == value) return;
            _settings.SoundLoop = value;
            Raise();
            Save();
        }
    }

    public bool SoundLoopMaxEnabled
    {
        get => _settings.SoundLoopMaxEnabled;
        set
        {
            if (_settings.SoundLoopMaxEnabled == value) return;
            _settings.SoundLoopMaxEnabled = value;
            Raise();
            Save();
        }
    }

    public int SoundLoopMaxSeconds
    {
        get => _settings.SoundLoopMaxSeconds;
        set
        {
            // Lower bound = 5s so the cap is meaningfully different from "just play once";
            // upper bound = 600s (10 min) since past that point you're really asking for
            // the loop to run until dismissed anyway.
            var clamped = Math.Clamp(value, 5, 600);
            if (_settings.SoundLoopMaxSeconds == clamped) return;
            _settings.SoundLoopMaxSeconds = clamped;
            Raise();
            Save();
        }
    }

    public IReadOnlyList<SoundLibrary.SoundOption> AvailableSounds { get; }

    private SoundLibrary.SoundOption _selectedSound = null!;
    public SoundLibrary.SoundOption SelectedSound
    {
        get => _selectedSound;
        set
        {
            if (value == null || _selectedSound == value) return;
            // Silence first — before we touch settings or notify the UI. The user's
            // mental model is "I clicked a new item, the old sound stops now".
            _preview.Stop();
            _selectedSound = value;
            _settings.SoundFilePath = value.FilePath;
            Raise();
            Save();
        }
    }

    private readonly PreviewPlayer _preview;

    public ICommand TogglePreviewCommand { get; }

    public bool IsPreviewPlaying => _preview.IsPlaying;

    // Unicode play / pause glyphs render in any font — no Segoe MDL2 dependency.
    public string PreviewButtonGlyph => _preview.IsPlaying ? "⏸" : "▶";

    private void TogglePreview()
    {
        if (_preview.IsPlaying)
            _preview.Stop();
        else
            _preview.Play(_selectedSound.FilePath);
    }

    public AppSettings Settings => _settings;

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
