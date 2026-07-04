// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using SlackWake.Helpers;
using SlackWake.Services;
using SlackWake.ViewModels;
using SlackWake.Views;
using Wpf.Ui.Appearance;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace SlackWake;

/// <summary>
/// Application entry point. Wires up services, view-models, the tray icon, and the
/// overlay-spawn callback. Keeps a single instance of the settings window alive so
/// hiding/showing is instant and state survives across tray opens.
/// </summary>
public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _tray;
    private Icon? _iconActive;
    private Icon? _iconIdle;
    private Icon? _iconDisabled;
    private MainWindow? _settingsWindow;
    private MainViewModel? _vm;

    // Checkable tray-menu items mirroring the matching settings toggles. Kept as
    // fields so the menu's Opening handler can refresh their checked state from the
    // view-model — the user may have changed any of these in the settings window
    // since the menu was last shown.
    private Forms.ToolStripMenuItem? _miEnableMonitoring;
    private Forms.ToolStripMenuItem? _miSoundAlert;
    private Forms.ToolStripMenuItem? _miVisualAlert;
    private Forms.ToolStripMenuItem? _miAutoStop;

    // Guard: prevents a flood of Slack events from spawning multiple overlay sets.
    private bool _overlayOpen;
    private readonly List<OverlayWindow> _activeOverlays = new();

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Composition root — no DI container needed for an app this small.
        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        var idle = new IdleMonitorService();
        var slack = new SlackMonitorService();

        _vm = new MainViewModel(settings, settingsService, idle, slack, ShowOverlay, PickColor, PickAudioFile);

        // Match the Fluent palette to the user's current Windows light/dark theme and
        // accent before any window is shown. Purely presentational — does not touch the
        // app's monitoring/overlay behavior.
        ApplicationThemeManager.ApplySystemTheme();

        _settingsWindow = new MainWindow { DataContext = _vm };
        // Keep the window's Mica backdrop and palette in sync if the OS theme flips
        // while SlackWake is running.
        SystemThemeWatcher.Watch(_settingsWindow);
        InitTray();

        // Show the settings window on launch unless we were auto-started silently
        // (--minimized arg from the HKCU Run key) or the user previously asked for
        // a quiet launch.
        var startedMinimized = HasArg(e.Args, "--minimized");
        if (!startedMinimized && !settings.StartMinimized)
        {
            ShowSettings();
        }

        _vm.Start();
    }

    private static bool HasArg(string[] args, string name)
    {
        foreach (var a in args)
            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void InitTray()
    {
        _iconActive = TrayIconFactory.CreateActive();
        _iconIdle = TrayIconFactory.CreateIdle();
        _iconDisabled = TrayIconFactory.CreateDisabled();

        _tray = new Forms.NotifyIcon
        {
            Icon = _iconDisabled,   // real state applied by SyncTrayState below
            Text = "SlackWake",
            Visible = true
        };
        _tray.ContextMenuStrip = BuildContextMenu();
        // Left-click opens settings; right-click is left to the context menu.
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) ShowSettings();
        };

        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
        SyncTrayState();
    }

    /// <summary>
    /// Builds the tray right-click menu. The top group mirrors the most-used settings
    /// toggles as checkable items so the user can flip them without opening the window;
    /// "Enable monitoring" is the master switch and is rendered bold (the menu's default
    /// action), matching the Windows convention of emphasizing the primary item.
    /// </summary>
    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        // Give the menu the modern Windows 11 flyout look before building items so the
        // bold master item inherits the themed font.
        ModernMenuRenderer.Apply(menu);

        // Master switch — bold to read as the primary/default action.
        _miEnableMonitoring = new Forms.ToolStripMenuItem("Enable monitoring", null, (_, _) => ToggleEnabled())
        {
            CheckOnClick = true,
            Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold),
        };

        _miSoundAlert = new Forms.ToolStripMenuItem("Sound alert", null, (_, _) => ToggleSound())
        {
            CheckOnClick = true,
        };
        _miVisualAlert = new Forms.ToolStripMenuItem("Visual alert", null, (_, _) => ToggleVisual())
        {
            CheckOnClick = true,
        };
        _miAutoStop = new Forms.ToolStripMenuItem("Stop alert automatically", null, (_, _) => ToggleAutoStop())
        {
            CheckOnClick = true,
        };

        menu.Items.Add(_miEnableMonitoring);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_miSoundAlert);
        menu.Items.Add(_miVisualAlert);
        menu.Items.Add(_miAutoStop);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open settings", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        // The settings window can change any of these while the menu is closed, so
        // refresh the checkmarks each time the menu is about to show rather than only
        // when we build it.
        menu.Opening += (_, _) => RefreshContextMenu();
        return menu;
    }

    /// <summary>Pulls the current toggle state from the view-model onto the checkable
    /// menu items. Called right before the menu opens so it never shows stale checks.</summary>
    private void RefreshContextMenu()
    {
        if (_vm == null) return;
        if (_miEnableMonitoring != null) _miEnableMonitoring.Checked = _vm.Enabled;
        if (_miSoundAlert != null) _miSoundAlert.Checked = _vm.SoundEnabled;
        if (_miVisualAlert != null) _miVisualAlert.Checked = _vm.FlashEnabled;
        if (_miAutoStop != null) _miAutoStop.Checked = _vm.AlertAutoStopEnabled;
    }

    private void ToggleEnabled()
    {
        if (_vm != null) _vm.Enabled = !_vm.Enabled;
    }

    private void ToggleSound()
    {
        if (_vm != null) _vm.SoundEnabled = !_vm.SoundEnabled;
    }

    private void ToggleVisual()
    {
        if (_vm != null) _vm.FlashEnabled = !_vm.FlashEnabled;
    }

    private void ToggleAutoStop()
    {
        if (_vm != null) _vm.AlertAutoStopEnabled = !_vm.AlertAutoStopEnabled;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Enabled) ||
            e.PropertyName == nameof(MainViewModel.IsActive))
            SyncTrayState();
    }

    private void SyncTrayState()
    {
        if (_tray == null || _vm == null) return;

        if (!_vm.Enabled)
        {
            _tray.Icon = _iconDisabled;
            _tray.Text = "SlackWake — disabled";
        }
        else if (_vm.IsActive)
        {
            // Active = armed. Green icon flags that the next Slack ping will fire
            // the overlay, giving the user an at-a-glance signal even without
            // opening the settings window.
            _tray.Icon = _iconActive;
            _tray.Text = "SlackWake — active (overlay armed)";
        }
        else
        {
            _tray.Icon = _iconIdle;
            _tray.Text = "SlackWake — idle (alerts paused)";
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow == null) return;
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    /// <summary>
    /// Show the Fluent color picker modally over the settings window, seeded with
    /// <paramref name="initial"/>. Returns the chosen color, or null if cancelled.
    /// Wired into the view-model so it owns no View reference of its own.
    /// </summary>
    private MediaColor? PickColor(MediaColor initial)
    {
        var picker = new ColorPickerWindow(initial)
        {
            Owner = _settingsWindow,
        };
        return picker.ShowDialog() == true ? picker.SelectedColor : null;
    }

    /// <summary>
    /// Opens the standard Windows file picker for a custom alert sound and returns the
    /// chosen path, or null if the user cancelled. Injected into the view-model so the VM
    /// stays free of any dialog/View dependency (see <see cref="PickColor"/>).
    /// </summary>
    private string? PickAudioFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an audio file",
            Filter = SoundLibrary.FileDialogFilter,
            CheckFileExists = true,
            Multiselect = false,
            // Start where alert sounds usually live; harmless if the folder is absent.
            InitialDirectory = SoundLibrary.DefaultFolder,
        };
        return dialog.ShowDialog(_settingsWindow) == true ? dialog.FileName : null;
    }

    /// <summary>
    /// Open one fullscreen, topmost overlay on every connected monitor. Only one
    /// "set" of overlays can be open at once; closing any of them tears the rest
    /// down and resets the guard so the next Slack message can fire again.
    /// </summary>
    private void ShowOverlay(SlackEvent evt)
    {
        if (_overlayOpen) return;
        _overlayOpen = true;
        _activeOverlays.Clear();

        // Snapshot sound + flash settings once so every overlay in this fan-out
        // agrees, even if the user toggles the settings mid-alert. The "alert
        // delay" governs both sound and flash — they fire together.
        var soundEnabled = _vm?.Settings.SoundEnabled ?? false;
        var alertDelay = _vm?.Settings.SoundDelaySeconds ?? 0;
        var soundLoop = _vm?.Settings.SoundLoop ?? false;
        var soundPath = _vm?.Settings.SoundFilePath ?? string.Empty;
        var flashEnabled = _vm?.Settings.FlashEnabled ?? false;
        var flashIntervalMs = _vm?.Settings.FlashIntervalMs ?? 500;
        var flashColorA = ColorUtil.Parse(_vm?.Settings.FlashColorA ?? "#000000");
        var flashColorB = ColorUtil.Parse(_vm?.Settings.FlashColorB ?? "#FFFFFF");
        // Auto-stop cap — the sound and the visual flash opt in independently.
        var alertAutoStopEnabled = _vm?.Settings.AlertAutoStopEnabled ?? false;
        var alertMaxDurationSeconds = _vm?.Settings.AlertMaxDurationSeconds ?? 60;
        var alertAutoStopIncludesSound = _vm?.Settings.AlertAutoStopIncludesSound ?? true;
        var alertAutoStopIncludesVisual = _vm?.Settings.AlertAutoStopIncludesVisual ?? true;

        var first = true;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds; // physical pixels
            var w = new OverlayWindow(
                evt.Sender, evt.Channel, evt.Text,
                soundEnabled, alertDelay,
                soundLoop, soundPath,
                flashEnabled, flashIntervalMs,
                flashColorA, flashColorB,
                alertAutoStopEnabled, alertMaxDurationSeconds,
                alertAutoStopIncludesSound, alertAutoStopIncludesVisual)
            {
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            // Only the first overlay plays audio — otherwise N monitors = N
            // overlapping streams of the same alert.
            if (!first) w.SuppressSound();
            first = false;

            // Show first so the HWND exists, then move with Win32 in physical pixels.
            // This avoids the WPF DIP/physical mismatch when monitors run at
            // different DPI scales.
            w.Show();
            var hwnd = new WindowInteropHelper(w).Handle;
            NativeMethods.MoveWindow(hwnd, bounds.Left, bounds.Top, bounds.Width, bounds.Height, true);

            w.Closed += (_, _) => CloseAllOverlays();
            _activeOverlays.Add(w);
        }
        _activeOverlays[0].Activate();
    }

    private void CloseAllOverlays()
    {
        var snapshot = _activeOverlays.ToArray();
        _activeOverlays.Clear();
        foreach (var o in snapshot)
        {
            if (o.IsVisible) o.Close();
        }
        _overlayOpen = false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm?.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _iconActive?.Dispose();
        _iconIdle?.Dispose();
        _iconDisabled?.Dispose();
        base.OnExit(e);
    }
}
