// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System.Runtime.InteropServices;
using SlackWake.Helpers;

namespace SlackWake.Services;

/// <summary>
/// Turns the monitor(s) back on when an alert fires and keeps them on while the
/// overlay is visible. This applies when Windows has turned the display off for
/// power saving and the machine is still awake. It cannot wake a PC that is
/// asleep or hibernating, because SlackWake isn't running then.
///
/// Two mechanisms, both needed in practice:
///   1. SetThreadExecutionState(ES_DISPLAY_REQUIRED | ES_CONTINUOUS) resets the
///      display idle timer and holds the display on until released. On many
///      systems this alone powers an idle-off display back on.
///   2. A zero-distance synthetic mouse move. Windows treats this as user input,
///      which reliably powers the display on even where (1) only blocks a future
///      timeout. The cursor doesn't move.
///
/// The execution state is per-thread, so Acquire and Release must both be called
/// on the same (UI) thread.
/// </summary>
internal static class DisplayWake
{
    private static bool _held;

    public static void Acquire()
    {
        var prev = NativeMethods.SetThreadExecutionState(
            NativeMethods.ES_CONTINUOUS | NativeMethods.ES_DISPLAY_REQUIRED | NativeMethods.ES_SYSTEM_REQUIRED);
        _held = prev != 0;
        if (!_held) Log.Write("DisplayWake: SetThreadExecutionState failed");

        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT { dx = 0, dy = 0, dwFlags = NativeMethods.MOUSEEVENTF_MOVE }
            }
        };
        var sent = NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != 1) Log.Write($"DisplayWake: SendInput failed (err={Marshal.GetLastWin32Error()})");
    }

    public static void Release()
    {
        if (!_held) return;
        NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
        _held = false;
    }
}
