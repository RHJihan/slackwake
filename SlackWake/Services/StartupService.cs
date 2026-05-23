using System;
using Microsoft.Win32;

namespace SlackWake.Services;

/// <summary>
/// Toggles the HKCU "Run" registry value used by Windows to auto-launch apps at
/// logon. Per-user (HKCU), so no admin elevation is required.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SlackWake";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) != null;
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                // Quote the path so spaces in "C:\Program Files\..." don't break parsing.
                // The "--minimized" hint lets App.OnStartup decide to stay in the tray.
                key.SetValue(ValueName, $"\"{exe}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Registry write may fail under locked-down policies. Silent fail is fine —
            // we'll just not auto-start; the user can still launch manually.
        }
    }
}
