using System;
using System.Windows;
using System.Windows.Threading;
using SlackWake.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using TextCompositionEventArgs = System.Windows.Input.TextCompositionEventArgs;

namespace SlackWake.Views;

/// <summary>
/// Fullscreen, topmost, click-through-to-dismiss notification overlay.
///
/// Notes on multi-monitor: App.ShowOverlay positions one of these per Screen
/// in screen pixels and leaves WindowState=Normal so the explicit Left/Top/Width/Height
/// take effect (Maximized would always pin to the primary display).
/// </summary>
public partial class OverlayWindow : Window
{
    private DispatcherTimer? _soundDelayTimer;
    private LoopingSoundPlayer? _loopingPlayer;
    private bool _soundEnabled;
    private int _soundDelaySeconds;
    private bool _soundLoop;
    private bool _soundLoopMaxEnabled;
    private int _soundLoopMaxSeconds;
    private string _soundFilePath = string.Empty;

    public OverlayWindow(string? sender, string? channel, string? text)
        : this(sender, channel, text,
               soundEnabled: false, soundDelaySeconds: 0,
               soundLoop: false, soundLoopMaxEnabled: false, soundLoopMaxSeconds: 0,
               soundFilePath: string.Empty)
    {
    }

    public OverlayWindow(
        string? sender,
        string? channel,
        string? text,
        bool soundEnabled,
        int soundDelaySeconds,
        bool soundLoop,
        bool soundLoopMaxEnabled,
        int soundLoopMaxSeconds,
        string soundFilePath)
    {
        InitializeComponent();

        SenderText.Text = string.IsNullOrWhiteSpace(sender) ? string.Empty : $"From: {sender}";
        ChannelText.Text = string.IsNullOrWhiteSpace(channel) ? string.Empty : $"Channel: {channel}";
        MessageText.Text = string.IsNullOrWhiteSpace(text) ? "(no preview available)" : text;

        _soundEnabled = soundEnabled;
        _soundDelaySeconds = Math.Clamp(soundDelaySeconds, 0, 120);
        _soundLoop = soundLoop;
        _soundLoopMaxEnabled = soundLoopMaxEnabled;
        _soundLoopMaxSeconds = Math.Max(1, soundLoopMaxSeconds);
        _soundFilePath = soundFilePath ?? string.Empty;

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            StartSoundIfEnabled();
        };

        Closed += (_, _) => StopSound();
    }

    /// <summary>
    /// Owner-only entry point: this overlay is the "secondary" instance on a non-primary
    /// monitor, so we suppress sound here — only the primary overlay plays audio.
    /// </summary>
    public void SuppressSound()
    {
        _soundEnabled = false;
    }

    private void StartSoundIfEnabled()
    {
        if (!_soundEnabled) return;

        if (_soundDelaySeconds <= 0)
        {
            StartPlayback();
            return;
        }

        _soundDelayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_soundDelaySeconds)
        };
        _soundDelayTimer.Tick += (_, _) =>
        {
            _soundDelayTimer?.Stop();
            _soundDelayTimer = null;
            StartPlayback();
        };
        _soundDelayTimer.Start();
    }

    private void StartPlayback()
    {
        if (_soundLoop)
        {
            TimeSpan? max = _soundLoopMaxEnabled
                ? TimeSpan.FromSeconds(_soundLoopMaxSeconds)
                : null;
            _loopingPlayer = new LoopingSoundPlayer();
            _loopingPlayer.Start(_soundFilePath, loop: true, maxDuration: max);
        }
        else
        {
            // Single fire-and-forget — no need to track end-of-stream.
            SoundLibrary.PlayOnce(_soundFilePath);
        }
    }

    private void StopSound()
    {
        _soundDelayTimer?.Stop();
        _soundDelayTimer = null;
        _loopingPlayer?.Stop();
        _loopingPlayer = null;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e) => Close();
    private void Window_TextInput(object sender, TextCompositionEventArgs e) => Close();
    private void Window_MouseDown(object sender, MouseButtonEventArgs e) => Close();
}
