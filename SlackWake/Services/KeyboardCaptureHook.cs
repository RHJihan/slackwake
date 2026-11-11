// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SlackWake.Helpers;

namespace SlackWake.Services;

/// <summary>
/// System-wide keyboard capture (WH_KEYBOARD_LL) used while the alert overlay is up.
///
/// Why a hook and not window focus: the overlay is opened by a background tray
/// process while the user is idle, so the last input event belongs to whatever app
/// they were using. Windows' foreground lock then makes SetForegroundWindow fail
/// silently — the overlay is drawn on top (Topmost) but keystrokes keep going to the
/// app underneath, and only a mouse click (which hits the overlay directly) dismisses
/// it. A low-level hook sees every keystroke regardless of which window has focus.
///
/// While active, every keystroke is swallowed so the dismissing key never leaks into
/// the app beneath (e.g. typing a stray character into a Slack message box). The
/// first key-down raises <see cref="KeyPressed"/> once. On <see cref="Stop"/>, any
/// key still physically held is kept captured until it is released, so the app
/// beneath never receives an orphan key-up (a lone Win/Alt key-up can open the
/// Start menu or a menu bar). A short safety timeout guarantees the hook is always
/// removed even if a release is never observed — a stuck hook would kill the keyboard.
/// </summary>
public sealed class KeyboardCaptureHook
{
    // Upper bound on how long Stop() waits for held keys to be released before
    // unhooking regardless. Long enough for a normal key release, short enough that
    // a missed key-up can never leave the keyboard captured.
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(2);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    // Held as a field: the native hook only stores a function pointer, so the
    // delegate must be rooted here or the GC would collect it out from under Windows.
    private readonly NativeMethods.LowLevelKeyboardProc _proc;

    // Keys whose key-down was swallowed and whose key-up has not arrived yet.
    private readonly HashSet<uint> _heldKeys = new();

    private IntPtr _hook = IntPtr.Zero;
    private DispatcherTimer? _releaseTimer;
    private bool _keyPressedRaised;
    private bool _stopping;

    /// <summary>Fires once, on the UI thread, for the first key pressed while capturing.</summary>
    public event Action? KeyPressed;

    public KeyboardCaptureHook()
    {
        _proc = HookCallback;
    }

    /// <summary>
    /// Install the hook on the calling (UI) thread. Returns false if Windows refused
    /// it; callers should then fall back to focus-based keyboard handling.
    /// </summary>
    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;

        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            Log.Write($"[KeyboardCapture] SetWindowsHookEx failed (Win32 error {Marshal.GetLastWin32Error()})");
            return false;
        }
        return true;
    }

    /// <summary>
    /// End capture. Unhooks immediately, or — if keys are still held — once they are
    /// released (bounded by <see cref="ReleaseTimeout"/>). New key presses pass
    /// through to other apps from this point on. Safe to call more than once.
    /// </summary>
    public void Stop()
    {
        if (_hook == IntPtr.Zero || _stopping) return;
        _stopping = true;

        if (_heldKeys.Count == 0)
        {
            Unhook();
            return;
        }

        _releaseTimer = new DispatcherTimer { Interval = ReleaseTimeout };
        _releaseTimer.Tick += (_, _) => Unhook();
        _releaseTimer.Start();
    }

    private void Unhook()
    {
        _releaseTimer?.Stop();
        _releaseTimer = null;
        _heldKeys.Clear();

        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Windows silently removes low-level hooks that exceed LowLevelHooksTimeout,
        // so this stays trivial: classify, update state, and defer any real work
        // (closing windows, stopping audio) to the dispatcher.
        if (nCode < 0 || _hook == IntPtr.Zero)
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

        var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        var message = (int)wParam;

        if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
        {
            // Auto-repeat of a key we already captured stays captured.
            if (_heldKeys.Contains(info.vkCode)) return Swallow();

            // After Stop(), new presses belong to whatever app now has focus.
            if (_stopping)
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);

            _heldKeys.Add(info.vkCode);
            if (!_keyPressedRaised)
            {
                _keyPressedRaised = true;
                _dispatcher.BeginInvoke(() => KeyPressed?.Invoke());
            }
            return Swallow();
        }

        if (message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
        {
            if (_heldKeys.Remove(info.vkCode))
            {
                if (_stopping && _heldKeys.Count == 0)
                    _dispatcher.BeginInvoke(Unhook);
                return Swallow();
            }
            // Key-up for a key that went down before capture began: the app beneath
            // saw the key-down, so it must see the matching key-up too.
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    // A non-zero return from a low-level hook stops the keystroke from reaching
    // any other hook or window.
    private static IntPtr Swallow() => new(1);
}
