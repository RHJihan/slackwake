// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SlackWake.Helpers;
using SlackWake.Services;
using Color = System.Windows.Media.Color;
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
    // Alpha channels used to derive the per-state palette from the two configured
    // flash colors. Kept in sync with the static defaults in OverlayWindow.xaml so
    // toggling flash off resumes the same visual.
    private const byte BackgroundAlpha = 0xE6;  // ~90% opaque
    private const byte MessageBoxAlpha = 0x22;  // subtle contrast tint
    private const byte HintAlpha = 0xAA;        // dimmer than full foreground

    private static readonly string[] FlashBrushKeys =
    {
        "OverlayBackgroundBrush",
        "OverlayForegroundBrush",
        "OverlayMessageBoxBrush",
        "OverlayHintBrush",
    };

    private DispatcherTimer? _alertDelayTimer;
    private LoopingSoundPlayer? _loopingPlayer;

    private bool _soundEnabled;
    private bool _soundLoop;
    private bool _soundLoopMaxEnabled;
    private int _soundLoopMaxSeconds;
    private string _soundFilePath = string.Empty;

    private bool _flashEnabled;
    private int _flashIntervalMs;
    private Color _flashColorA;
    private Color _flashColorB;
    private bool _flashRunning;

    private int _alertDelaySeconds;

    public OverlayWindow(string? sender, string? channel, string? text)
        : this(sender, channel, text,
               soundEnabled: false, alertDelaySeconds: 0,
               soundLoop: false, soundLoopMaxEnabled: false, soundLoopMaxSeconds: 0,
               soundFilePath: string.Empty,
               flashEnabled: false, flashIntervalMs: 0,
               flashColorA: Colors.Black, flashColorB: Colors.White)
    {
    }

    public OverlayWindow(
        string? sender,
        string? channel,
        string? text,
        bool soundEnabled,
        int alertDelaySeconds,
        bool soundLoop,
        bool soundLoopMaxEnabled,
        int soundLoopMaxSeconds,
        string soundFilePath,
        bool flashEnabled,
        int flashIntervalMs,
        Color flashColorA,
        Color flashColorB)
    {
        InitializeComponent();

        SenderText.Text = string.IsNullOrWhiteSpace(sender) ? string.Empty : $"From: {sender}";
        ChannelText.Text = string.IsNullOrWhiteSpace(channel) ? string.Empty : $"Channel: {channel}";
        MessageText.Text = string.IsNullOrWhiteSpace(text) ? "(no preview available)" : text;

        _soundEnabled = soundEnabled;
        _alertDelaySeconds = Math.Clamp(alertDelaySeconds, 0, 120);
        _soundLoop = soundLoop;
        _soundLoopMaxEnabled = soundLoopMaxEnabled;
        _soundLoopMaxSeconds = Math.Max(1, soundLoopMaxSeconds);
        _soundFilePath = soundFilePath ?? string.Empty;

        _flashEnabled = flashEnabled;
        _flashIntervalMs = Math.Clamp(flashIntervalMs, 100, 2000);
        _flashColorA = flashColorA;
        _flashColorB = flashColorB;

        Loaded += OnLoaded;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    /// <summary>
    /// Owner-only entry point: this overlay is the "secondary" instance on a non-primary
    /// monitor, so we suppress sound here — only the primary overlay plays audio.
    /// Flash stays on every monitor; visual is the whole point of the overlay fan-out.
    /// </summary>
    public void SuppressSound()
    {
        _soundEnabled = false;
    }

    // ---- Lifecycle ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The overlay is shown by a tray-process from idle state, so Windows refuses
        // to give us foreground via Activate() alone — the window appears on top
        // because Topmost=true, but keyboard focus stays with the previously-focused
        // app and ESC / any key would do nothing. SetForegroundWindow is the
        // reliable escape hatch; Keyboard.Focus then routes input into the window.
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetForegroundWindow(hwnd);
        Activate();
        Focus();
        Keyboard.Focus(this);

        BeginAlertsAfterDelay();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        // If the overlay loses then regains foreground (alt-tab, click on a different
        // monitor, etc.), re-route keyboard focus into the window so subsequent
        // keystrokes still dismiss it.
        Keyboard.Focus(this);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopAlertDelay();
        StopSound();
        StopFlash();
    }

    // ---- Alert timing ----

    private void BeginAlertsAfterDelay()
    {
        if (_alertDelaySeconds <= 0)
        {
            StartSound();
            StartFlash();
            return;
        }

        _alertDelayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_alertDelaySeconds)
        };
        _alertDelayTimer.Tick += (_, _) =>
        {
            _alertDelayTimer?.Stop();
            _alertDelayTimer = null;
            StartSound();
            StartFlash();
        };
        _alertDelayTimer.Start();
    }

    private void StopAlertDelay()
    {
        _alertDelayTimer?.Stop();
        _alertDelayTimer = null;
    }

    // ---- Sound ----

    private void StartSound()
    {
        if (!_soundEnabled) return;

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
        _loopingPlayer?.Stop();
        _loopingPlayer = null;
    }

    // ---- Flash ----

    private void StartFlash()
    {
        if (!_flashEnabled) return;

        // Build the two visual states from the user's chosen colors. Foreground /
        // message-box / hint are derived per-state via WCAG contrast so the text
        // is legible no matter which background color the user picked.
        var stateA = BuildPalette(_flashColorA);
        var stateB = BuildPalette(_flashColorB);
        var halfCycle = TimeSpan.FromMilliseconds(_flashIntervalMs);

        AnimateBrush("OverlayBackgroundBrush", stateA.Background, stateB.Background, halfCycle);
        AnimateBrush("OverlayForegroundBrush", stateA.Foreground, stateB.Foreground, halfCycle);
        AnimateBrush("OverlayMessageBoxBrush", stateA.MessageBox, stateB.MessageBox, halfCycle);
        AnimateBrush("OverlayHintBrush", stateA.Hint, stateB.Hint, halfCycle);

        _flashRunning = true;
    }

    private static Palette BuildPalette(Color baseColor)
    {
        var contrast = ColorUtil.ContrastingTextColor(baseColor);
        return new Palette(
            Background: ColorUtil.WithAlpha(baseColor, BackgroundAlpha),
            Foreground: contrast,
            MessageBox: ColorUtil.WithAlpha(contrast, MessageBoxAlpha),
            Hint: ColorUtil.WithAlpha(contrast, HintAlpha));
    }

    private void AnimateBrush(string brushResourceKey, Color from, Color to, TimeSpan halfCycle)
    {
        // Brushes declared in a ResourceDictionary are normally unfrozen, but the
        // framework freezes shared brushes in some scenarios — clone defensively so
        // BeginAnimation is guaranteed to work. Re-assigning the key fires
        // {DynamicResource} consumers to pick up the new instance.
        var brush = (SolidColorBrush)Resources[brushResourceKey];
        if (brush.IsFrozen)
        {
            brush = brush.Clone();
            Resources[brushResourceKey] = brush;
        }

        var animation = new ColorAnimation
        {
            From = from,
            To = to,
            Duration = halfCycle,
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private void StopFlash()
    {
        if (!_flashRunning) return;
        foreach (var key in FlashBrushKeys)
        {
            if (Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        }
        _flashRunning = false;
    }

    private readonly record struct Palette(Color Background, Color Foreground, Color MessageBox, Color Hint);

    // ---- Input ----

    private void Window_KeyDown(object sender, KeyEventArgs e) => Close();
    private void Window_TextInput(object sender, TextCompositionEventArgs e) => Close();
    private void Window_MouseDown(object sender, MouseButtonEventArgs e) => Close();
}
