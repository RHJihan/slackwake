// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.IO;

namespace SlackWake.Helpers;

/// <summary>
/// Tiny append-only debug log at %AppData%\SlackWake\debug.log. Capped at a few
/// hundred KB so it can't run away. Intended for diagnostics, not telemetry.
/// </summary>
internal static class Log
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SlackWake", "debug.log");

    private static readonly object Sync = new();
    private const long MaxBytes = 256 * 1024;

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                var dir = System.IO.Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(dir);

                // Cheap rollover: truncate when too large.
                if (File.Exists(LogPath))
                {
                    var info = new FileInfo(LogPath);
                    if (info.Length > MaxBytes) File.WriteAllText(LogPath, string.Empty);
                }

                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never throw into the app.
        }
    }
}
