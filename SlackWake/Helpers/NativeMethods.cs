// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Runtime.InteropServices;

namespace SlackWake.Helpers;

/// <summary>
/// Win32 P/Invoke surface. Kept tiny on purpose — only the calls we actually need.
/// </summary>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime; // Tick count of the last input event.
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>
    /// Move and resize a window in physical pixels — bypasses WPF's DIP scaling so
    /// the overlay lands exactly on the target monitor regardless of mixed DPIs.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    /// <summary>
    /// Release an HICON obtained from <c>Bitmap.GetHicon()</c>. Without this we leak
    /// a GDI handle every time we rebuild a tray icon.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Force a window to the foreground regardless of who currently owns input focus.
    /// Needed for the overlay because Windows refuses normal Activate() from a tray
    /// process that wasn't the foreground app — without this, the overlay appears on
    /// top (Topmost=true) but keystrokes go to the previously-focused app.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // --- Low-level keyboard hook -------------------------------------------------
    // Used by KeyboardCaptureHook so the overlay can be dismissed by any key even
    // when Windows' foreground lock leaves keyboard focus with another app.

    public const int WH_KEYBOARD_LL = 13;

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // --- Display wake -------------------------------------------------------------
    // Used by DisplayWake so an alert turns a power-saved (idle-off) monitor back on.

    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint esFlags);

    public const uint INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_MOVE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Only the mouse member of the INPUT union is used; KEYBDINPUT/HARDWAREINPUT
    // are smaller than MOUSEINPUT, so this layout has the correct native size.
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // --- DWM window styling (purely cosmetic) ---------------------------------
    // Used to give the WinForms tray context menu the rounded corners and dark
    // backdrop of a modern Windows 11 flyout. None of this touches app behavior.

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    // DWM_WINDOW_CORNER_PREFERENCE values.
    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}
