// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SlackWake.Helpers;
using SlackWake.Models;
using SlackWake.Services;
using Brush = System.Windows.Media.Brush;
using Forms = System.Windows.Forms;

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

    // Raw OS fact: has the user been away from keyboard/mouse longer than the
    // configured timeout? SlackWake's own "active vs idle" state is derived from
    // this (see IsActive) — when the user is idle, SlackWake is active (armed).
    private bool _userIdle;
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
        PreviewSoundCommand = new RelayCommand<SoundLibrary.SoundOption>(PreviewSound);
        StopPreviewCommand = new RelayCommand(_preview.Stop);
        PickFlashColorACommand = new RelayCommand(() => PickFlashColor(isA: true));
        PickFlashColorBCommand = new RelayCommand(() => PickFlashColor(isA: false));
        TestOverlayCommand = new RelayCommand(TestOverlay);
    }

    public ICommand TestOverlayCommand { get; }

    private void TestOverlay()
    {
        // Bypass the enabled + user-idle gate that real Slack events go through —
        // the user clicked "Test" specifically to preview the overlay with the
        // current settings, so suppressing it would defeat the purpose.
        var sample = new SlackEvent(
            Sender: "Test user",
            Channel: "#slackwake-test",
            Text: "This is a preview of your SlackWake overlay. Press ESC, click, "
                + "or type any key to dismiss.");
        _showOverlay(sample);
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
            if (_settings.IdleTimeoutSeconds == clamped)
            {
                // Input was clamped to the already-stored value (e.g., user typed
                // "5" but the floor is 10). The TextBox is still showing the raw
                // input — force a target refresh so the display snaps to bounds.
                if (value != clamped) Raise();
                return;
            }
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
            Raise(nameof(CanAutoStopAlerts));
            Save();
        }
    }

    public int SoundDelaySeconds
    {
        get => _settings.SoundDelaySeconds;
        set
        {
            var clamped = Math.Clamp(value, 0, 120);
            if (_settings.SoundDelaySeconds == clamped)
            {
                if (value != clamped) Raise();
                return;
            }
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
            Raise(nameof(CanAutoStopAlerts));
            Save();
        }
    }

    public bool AlertAutoStopEnabled
    {
        get => _settings.AlertAutoStopEnabled;
        set
        {
            if (_settings.AlertAutoStopEnabled == value) return;
            _settings.AlertAutoStopEnabled = value;
            Raise();
            Save();
        }
    }

    public int AlertMaxDurationSeconds
    {
        get => _settings.AlertMaxDurationSeconds;
        set
        {
            // Lower bound = 5s so the cap is meaningfully different from "just play once";
            // upper bound = 600s (10 min) since past that point you're really asking for
            // the alert to run until dismissed anyway.
            var clamped = Math.Clamp(value, 5, 600);
            if (_settings.AlertMaxDurationSeconds == clamped)
            {
                if (value != clamped) Raise();
                return;
            }
            _settings.AlertMaxDurationSeconds = clamped;
            Raise();
            Save();
        }
    }

    /// <summary>Whether the shared auto-stop cap can actually do anything — true when at
    /// least one <em>continuous</em> alert is active (the sound is set to loop, or the
    /// visual flash is on). When false the auto-stop control greys out, since a single
    /// sound play and a dismissed overlay already end on their own.</summary>
    public bool CanAutoStopAlerts => (SoundEnabled && SoundLoop) || FlashEnabled;

    public bool FlashEnabled
    {
        get => _settings.FlashEnabled;
        set
        {
            if (_settings.FlashEnabled == value) return;
            _settings.FlashEnabled = value;
            Raise();
            Raise(nameof(CanAutoStopAlerts));
            Save();
        }
    }

    public int FlashIntervalMs
    {
        get => _settings.FlashIntervalMs;
        set
        {
            // Lower bound = 100ms; faster than that risks photosensitive-seizure territory
            // (the standard guideline is to stay under ~3 Hz, i.e. >= ~167ms half-cycle).
            // Upper bound = 2000ms — past that it doesn't read as "flashing" anymore.
            var clamped = Math.Clamp(value, 100, 2000);
            if (_settings.FlashIntervalMs == clamped)
            {
                if (value != clamped) Raise();
                return;
            }
            _settings.FlashIntervalMs = clamped;
            Raise();
            Save();
        }
    }

    public string FlashColorA
    {
        get => _settings.FlashColorA;
        set
        {
            var sanitized = ColorUtil.ToHex(ColorUtil.Parse(value));
            if (string.Equals(_settings.FlashColorA, sanitized, StringComparison.OrdinalIgnoreCase)) return;
            _settings.FlashColorA = sanitized;
            Raise();
            Raise(nameof(FlashColorABrush));
            Raise(nameof(FlashColorAContrastBrush));
            Save();
        }
    }

    public string FlashColorB
    {
        get => _settings.FlashColorB;
        set
        {
            var sanitized = ColorUtil.ToHex(ColorUtil.Parse(value));
            if (string.Equals(_settings.FlashColorB, sanitized, StringComparison.OrdinalIgnoreCase)) return;
            _settings.FlashColorB = sanitized;
            Raise();
            Raise(nameof(FlashColorBBrush));
            Raise(nameof(FlashColorBContrastBrush));
            Save();
        }
    }

    public bool KeywordFilterEnabled
    {
        get => _settings.KeywordFilterEnabled;
        set
        {
            if (_settings.KeywordFilterEnabled == value) return;
            _settings.KeywordFilterEnabled = value;
            Raise();
            Save();
        }
    }

    public string KeywordFilterText
    {
        get => _settings.KeywordFilterText;
        set
        {
            var newValue = value ?? string.Empty;
            if (string.Equals(_settings.KeywordFilterText, newValue, StringComparison.Ordinal)) return;
            _settings.KeywordFilterText = newValue;
            Raise();
            Save();
        }
    }

    public Brush FlashColorABrush => new SolidColorBrush(ColorUtil.Parse(_settings.FlashColorA));
    public Brush FlashColorBBrush => new SolidColorBrush(ColorUtil.Parse(_settings.FlashColorB));
    public Brush FlashColorAContrastBrush =>
        new SolidColorBrush(ColorUtil.ContrastingTextColor(ColorUtil.Parse(_settings.FlashColorA)));
    public Brush FlashColorBContrastBrush =>
        new SolidColorBrush(ColorUtil.ContrastingTextColor(ColorUtil.Parse(_settings.FlashColorB)));

    public ICommand PickFlashColorACommand { get; }
    public ICommand PickFlashColorBCommand { get; }

    private void PickFlashColor(bool isA)
    {
        var currentHex = isA ? _settings.FlashColorA : _settings.FlashColorB;
        var currentWpf = ColorUtil.Parse(currentHex);

        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(currentWpf.R, currentWpf.G, currentWpf.B),
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;

        var picked = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        if (isA) FlashColorA = picked;
        else FlashColorB = picked;
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

    // ---- Hover preview (dropdown) ----
    // The open sound dropdown previews whichever entry the cursor settles on, the
    // way the iOS ringtone picker and the Slack/Discord notification-sound pickers
    // do. Both commands route through the same PreviewPlayer the toggle button uses,
    // so a hover preview supersedes (and silences) any prior preview — there's never
    // overlapping audio. The view debounces hover, so a quick sweep stays silent.

    /// <summary>Preview a specific sound on hover. Parameter is the hovered <see cref="SoundLibrary.SoundOption"/>.</summary>
    public ICommand PreviewSoundCommand { get; }

    /// <summary>Silence any active preview — fired when the dropdown closes (selection or dismissal).</summary>
    public ICommand StopPreviewCommand { get; }

    private void PreviewSound(SoundLibrary.SoundOption? sound)
    {
        if (sound == null) return;
        _preview.Play(sound.FilePath);
    }

    public AppSettings Settings => _settings;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    // Status indicator dot colors follow the traffic-light / presence convention
    // used across Slack, Teams, and Discord, here keyed on SlackWake's own state:
    // green = active (armed), amber = idle (paused), red = disabled. Frozen so the
    // single shared instances are safe to hand to the binding from any thread.
    private static readonly Brush ActiveBrush = FreezeBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xB6, 0x7D));   // green
    private static readonly Brush IdleBrush = FreezeBrush(System.Windows.Media.Color.FromRgb(0xE3, 0xB3, 0x41));      // amber
    private static readonly Brush DisabledBrush = FreezeBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x48, 0x4D));  // red

    /// <summary>Color for the status dot, keyed on the same flags that pick the
    /// leading word of <see cref="Status"/> so the dot and the text always agree.
    /// SlackWake is active (armed) precisely when the user is idle.</summary>
    public Brush StatusBrush =>
        !Enabled ? DisabledBrush
        : _userIdle ? ActiveBrush
        : IdleBrush;

    private static Brush FreezeBrush(System.Windows.Media.Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
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

    /// <summary>True when SlackWake is active — i.e. armed so the next Slack ping
    /// fires the overlay. This is the case when the user has been idle longer than
    /// the configured timeout. Exposed so the tray icon can flip to its active
    /// (armed) state in lockstep. When false, SlackWake is idle (alerts paused
    /// because the user is at the keyboard).</summary>
    public bool IsActive => _userIdle;

    private void OnIdleTick(TimeSpan idle)
    {
        var userIdle = idle.TotalSeconds >= _settings.IdleTimeoutSeconds;
        if (userIdle == _userIdle) return;
        _userIdle = userIdle;
        Log.Write($"slackwake state -> {(_userIdle ? "ACTIVE (armed)" : "IDLE (paused)")} (user idle raw={idle.TotalSeconds:F1}s threshold={_settings.IdleTimeoutSeconds}s)");
        Raise(nameof(IsActive));
        RecomputeStatus();
    }

    private void OnSlackNotification(SlackEvent evt)
    {
        Log.Write($"VM received SlackEvent: enabled={Enabled} userIdle={_userIdle} sender='{evt.Sender}' channel='{evt.Channel}' text='{evt.Text}'");

        // The gate (Enabled + user-idle) must be evaluated on the UI thread, which
        // owns these flags. This event arrives on a Slack poll thread, so checking
        // here would race a concurrent disable and could act on a stale read of
        // Enabled — letting an overlay fire even though monitoring was turned off.
        // Marshal first, then decide on the thread that mutates the flags.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.Invoke(() =>
        {
            // Only fire overlays when SlackWake is active — i.e. the user is
            // actually away. That is the whole point of the app; a user at the
            // keyboard hears Slack's own notifications.
            if (!Enabled || !_userIdle)
            {
                Log.Write("  -> suppressed (slackwake idle or disabled)");
                return;
            }

            // Mute pings whose content matches a user keyword (noisy bots, channels,
            // topics). Checked after the idle/enabled gate so it only costs anything
            // on pings we'd otherwise fire on.
            if (IsMutedByKeyword(evt, out var matched))
            {
                Log.Write($"  -> suppressed (keyword filter matched '{matched}')");
                return;
            }

            Log.Write("  -> dispatching overlay");
            _showOverlay(evt);
        });
    }

    /// <summary>
    /// True when keyword filtering is on and the event's sender, channel, or text
    /// contains any configured keyword (case-insensitive substring match). The
    /// matching keyword is returned via <paramref name="matched"/> for logging.
    /// </summary>
    private bool IsMutedByKeyword(SlackEvent evt, out string matched)
    {
        matched = string.Empty;
        if (!_settings.KeywordFilterEnabled) return false;

        var keywords = _settings.KeywordFilterText;
        if (string.IsNullOrWhiteSpace(keywords)) return false;

        // Slack carries the message's formatting markers inline in the toast text:
        // bold is *word*, italic _word_, strikethrough ~word~, code `word`. A raw
        // substring search would miss "deploy failed" inside "*deploy* failed", so
        // strip those markers from the haystack (and the keyword) before matching.
        var haystack = StripFormatting(string.Join(' ',
            evt.Sender ?? string.Empty,
            evt.Channel ?? string.Empty,
            evt.Text ?? string.Empty));

        foreach (var keyword in ParseKeywords(keywords))
        {
            if (haystack.IndexOf(StripFormatting(keyword), StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matched = keyword;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Removes Slack mrkdwn formatting delimiters (<c>*</c> bold, <c>_</c> italic,
    /// <c>~</c> strikethrough, <c>`</c> code) so keyword matching sees the underlying
    /// words. Slack emits these markers inline in the notification text, which would
    /// otherwise break substring matches against unformatted keywords.
    /// </summary>
    private static string StripFormatting(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is not ('*' or '_' or '~' or '`')) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Splits the keyword string into individual keywords. Entries are separated by
    /// commas or line breaks (so the user can lay them out one-per-line), but anything
    /// inside double quotes is taken verbatim as one keyword — so a phrase that itself
    /// contains a comma (e.g. <c>"is now on-call for"</c>) stays intact. Surrounding
    /// whitespace is trimmed; blank entries are dropped.
    /// </summary>
    private static IEnumerable<string> ParseKeywords(string input)
    {
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in input)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if ((c == ',' || c == '\n' || c == '\r') && !inQuotes)
            {
                if (current.ToString().Trim() is { Length: > 0 } token) yield return token;
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.ToString().Trim() is { Length: > 0 } last) yield return last;
    }

    // Magic string published by SlackMonitorService when the listener is healthy.
    // Keeping it in sync with the service is intentional — duplicating the constant
    // is cheaper than threading an enum across both layers.
    private const string ListenerHealthyStatus = "Watching Slack notifications";

    private void RecomputeStatus()
    {
        Status = ComputeStatusText();
        // The dot color is keyed on the same flags as the text — recompute it in
        // lockstep so the two never drift apart.
        Raise(nameof(StatusBrush));
    }

    private string ComputeStatusText()
    {
        if (!Enabled) return "Disabled";

        // When the listener has something useful to report (permission denied,
        // listener error, still connecting…), surface that instead of the
        // operational explanation — the user needs to see and fix it.
        if (_slackConnectionStatus != ListenerHealthyStatus)
            return (_userIdle ? "Active — " : "Idle — ") + _slackConnectionStatus;

        // Happy path: explain what the current state actually means for alerts,
        // named from SlackWake's own perspective. SlackWake is "active" when it is
        // armed to fire (the user is away) and "idle" when it is standing down
        // (the user is at the keyboard, so Slack's own notifications suffice).
        return _userIdle
            ? "Active: overlay armed for the next Slack ping"
            : "Idle: alerts paused while using the computer";
    }

    private void Save() => _settingsService.Save(_settings);
}
