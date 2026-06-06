// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SlackWake.Helpers;

namespace SlackWake.Services;

/// <summary>
/// Polls the system idle timer once per second using Win32 GetLastInputInfo.
/// "Idle time" = milliseconds since the last keyboard/mouse input system-wide
/// (not just for our process), which is exactly what we want for "is the user
/// at their desk?". Polling at 1 Hz costs effectively zero CPU.
/// </summary>
public class IdleMonitorService
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    /// <summary>Fires every tick with the current idle duration.</summary>
    public event Action<TimeSpan>? IdleTimeChanged;

    public IdleMonitorService()
    {
        _timer.Tick += (_, _) => IdleTimeChanged?.Invoke(GetIdleTime());
    }

    public TimeSpan GetIdleTime()
    {
        var lii = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
        };
        if (!NativeMethods.GetLastInputInfo(ref lii))
            return TimeSpan.Zero;

        // Environment.TickCount and LASTINPUTINFO.dwTime share the same 32-bit
        // tick clock. Unsigned subtraction handles the ~49-day wraparound cleanly.
        uint deltaMs = (uint)Environment.TickCount - lii.dwTime;
        return TimeSpan.FromMilliseconds(deltaMs);
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}
