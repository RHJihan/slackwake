// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using SlackWake.Helpers;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace SlackWake.Services;

/// <summary>Minimal Slack event payload surfaced to the UI.</summary>
public record SlackEvent(string? Sender, string? Channel, string? Text);

/// <summary>
/// Polls the Windows toast listener for new notifications and surfaces Slack ones.
///
/// Why polling instead of <c>NotificationChanged</c>: the event-based path is
/// unreliable for unpackaged Win32/WPF apps. <c>RequestAccessAsync</c> reports
/// <c>Allowed</c>, but the event never fires for many users on Win10/11.
/// <c>GetNotificationsAsync</c> is the same data source and works reliably.
///
/// The price is ~1.5s of latency. For an overlay that wakes you up from idle,
/// that's invisible.
/// </summary>
public class SlackMonitorService
{
    private UserNotificationListener? _listener;
    private System.Timers.Timer? _pollTimer;
    private readonly HashSet<uint> _seen = new();
    private bool _firstPass = true;
    private bool _running;

    // Bumped on every Stop(). Start() captures the value before its first await
    // and bails if it changed, so a disable that lands mid-startup (during the
    // RequestAccessAsync await) cannot leave a poll timer running. Start/Stop are
    // both driven from the UI thread, and the async continuation resumes there
    // too, so plain int access is safe — no interlocking needed.
    private int _generation;

    public event Action<SlackEvent>? NotificationReceived;
    public event Action<string>? StatusChanged;

    public async void Start()
    {
        if (_running) return;
        Log.Write("SlackMonitor.Start()");

        var startGen = _generation;

        try
        {
            _listener = UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync();
            Log.Write($"RequestAccessAsync -> {status}");

            // Stop() was called while we were awaiting access — the user disabled
            // monitoring mid-startup. Abort instead of spinning up the poll timer,
            // otherwise the app would stay armed despite being disabled.
            if (startGen != _generation)
            {
                Log.Write("  -> startup aborted (stopped during access request)");
                return;
            }

            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                StatusChanged?.Invoke(
                    $"Notification access {status} - turn on " +
                    "Settings > Privacy > Notifications > Let apps access notifications");
                return;
            }

            _running = true;
            _firstPass = true;
            _seen.Clear();

            // First poll runs immediately so existing toasts in Action Center get
            // recorded as "already seen" and don't replay as fake events.
            await PollAsync();

            _pollTimer = new System.Timers.Timer(1500) { AutoReset = true };
            _pollTimer.Elapsed += async (_, _) => await PollAsync();
            _pollTimer.Start();

            StatusChanged?.Invoke("Watching Slack notifications");
        }
        catch (Exception ex)
        {
            Log.Write("Start error: " + ex);
            StatusChanged?.Invoke("Listener error: " + ex.Message);
        }
    }

    public void Stop()
    {
        Log.Write("SlackMonitor.Stop()");
        // Invalidate any in-flight Start() so a startup that's still awaiting
        // access won't resume and re-arm the poller after we've stopped.
        _generation++;
        if (_pollTimer != null)
        {
            try { _pollTimer.Stop(); _pollTimer.Dispose(); }
            catch { /* ignored */ }
            _pollTimer = null;
        }
        _listener = null;
        _running = false;
        _seen.Clear();
    }

    private async Task PollAsync()
    {
        var listener = _listener;
        if (listener == null) return;

        try
        {
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);

            // Build the set of currently-present IDs so we can prune _seen
            // (otherwise it grows forever as Action Center cycles).
            var currentIds = new HashSet<uint>();
            foreach (var n in notifications)
            {
                currentIds.Add(n.Id);
                if (_seen.Contains(n.Id)) continue;
                _seen.Add(n.Id);

                if (_firstPass)
                {
                    Log.Write($"  startup-skip id={n.Id} app='{SafeAppName(n)}'");
                    continue;
                }
                ProcessNew(n);
            }

            // Drop IDs that no longer exist in Action Center.
            _seen.RemoveWhere(id => !currentIds.Contains(id));

            if (_firstPass)
            {
                Log.Write($"  startup pass complete, baseline={_seen.Count}");
                _firstPass = false;
            }
        }
        catch (Exception ex)
        {
            Log.Write("Poll error: " + ex.Message);
        }
    }

    private void ProcessNew(UserNotification n)
    {
        var appName = SafeAppName(n);
        var appId = SafeAppId(n);
        Log.Write($"  new toast id={n.Id} app='{appName}' appId='{appId}'");

        if (!IsSlack(appName, appId))
        {
            Log.Write("    -> ignored (not Slack)");
            return;
        }

        string? title = null;
        string? body = null;
        try
        {
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding != null)
            {
                var texts = binding.GetTextElements();
                if (texts.Count > 0) title = texts[0].Text;
                if (texts.Count > 1)
                {
                    var sb = new StringBuilder();
                    for (int i = 1; i < texts.Count; i++)
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(texts[i].Text);
                    }
                    body = sb.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("    text extraction failed: " + ex.Message);
        }

        Log.Write($"    -> firing event. title='{title}' body='{body}'");

        string? sender = null;
        string? channel = null;
        if (!string.IsNullOrWhiteSpace(title))
        {
            if (title.StartsWith("#")) channel = title;
            else sender = title;
        }
        NotificationReceived?.Invoke(new SlackEvent(sender, channel, body));
    }

    private static bool IsSlack(string appName, string appId)
    {
        // Generous match: Slack's DisplayName has been observed as "Slack",
        // "Slack Technologies", and an AUMID like "com.squirrel.slack.slack".
        if (appName.IndexOf("slack", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (appId.IndexOf("slack", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static string SafeAppName(UserNotification n)
    {
        try { return n.AppInfo?.DisplayInfo?.DisplayName ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string SafeAppId(UserNotification n)
    {
        try { return n.AppInfo?.AppUserModelId ?? string.Empty; }
        catch { return string.Empty; }
    }
}
