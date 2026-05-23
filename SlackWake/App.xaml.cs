using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using SlackWake.Helpers;
using SlackWake.Services;
using SlackWake.ViewModels;
using SlackWake.Views;
using Forms = System.Windows.Forms;

namespace SlackWake;

/// <summary>
/// Application entry point. Wires up services, view-models, the tray icon, and the
/// overlay-spawn callback. Keeps a single instance of the settings window alive so
/// hiding/showing is instant and state survives across tray opens.
/// </summary>
public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _tray;
    private MainWindow? _settingsWindow;
    private MainViewModel? _vm;

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

        _vm = new MainViewModel(settings, settingsService, idle, slack, ShowOverlay);

        _settingsWindow = new MainWindow { DataContext = _vm };
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
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "SlackWake",
            Visible = true
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open settings", null, (_, _) => ShowSettings());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowSettings();
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
    /// Open one fullscreen, topmost overlay on every connected monitor. Only one
    /// "set" of overlays can be open at once; closing any of them tears the rest
    /// down and resets the guard so the next Slack message can fire again.
    /// </summary>
    private void ShowOverlay(SlackEvent evt)
    {
        if (_overlayOpen) return;
        _overlayOpen = true;
        _activeOverlays.Clear();

        foreach (var screen in Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds; // physical pixels
            var w = new OverlayWindow(evt.Sender, evt.Channel, evt.Text)
            {
                WindowStartupLocation = WindowStartupLocation.Manual
            };

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
        _vm?.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }
}
