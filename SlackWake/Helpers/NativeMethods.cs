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
}
