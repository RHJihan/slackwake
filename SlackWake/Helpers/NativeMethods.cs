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
}
