// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SlackWake.Models;

/// <summary>
/// One day's monitoring window, in minutes after local midnight (0–1439). The window
/// belongs to the day it STARTS on: if <see cref="EndMinute"/> is at or before
/// <see cref="StartMinute"/> it crosses midnight and ends the next day (Monday 23:00–07:00
/// covers Monday 11 PM → Tuesday 7 AM). Start == end means 24 hours. Property names match
/// the Android app's settings keys, so the schedule survives a backup restored on either.
/// </summary>
public class DayWindow
{
    public const int MinutesPerDay = 24 * 60;

    public bool Enabled { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }

    public DayWindow() { }

    public DayWindow(bool enabled, int startMinute, int endMinute)
    {
        Enabled = enabled;
        StartMinute = startMinute;
        EndMinute = endMinute;
    }

    [JsonIgnore]
    public bool CrossesMidnight => EndMinute <= StartMinute;

    /// <summary>Length in minutes (1..1440).</summary>
    [JsonIgnore]
    public int DurationMinutes => CrossesMidnight ? MinutesPerDay - StartMinute + EndMinute : EndMinute - StartMinute;

    public DayWindow Normalized() =>
        new(Enabled, Math.Clamp(StartMinute, 0, MinutesPerDay - 1), Math.Clamp(EndMinute, 0, MinutesPerDay - 1));
}

/// <summary>
/// Weekly schedule rules. Index 0 = Monday … 6 = Sunday (ISO order, independent of how
/// the UI orders days for the user's culture). Port of the Android app's WeeklySchedule.
/// </summary>
public static class WeeklySchedule
{
    public const int Days = 7;

    /// <summary>Monday–Friday 09:00–18:00, weekend off.</summary>
    public static List<DayWindow> CreateDefault() =>
        Enumerable.Range(0, Days).Select(i => new DayWindow(i < 5, 9 * 60, 18 * 60)).ToList();

    /// <summary>Always exactly 7 valid windows: pads, truncates and clamps hand-edited data.</summary>
    public static List<DayWindow> Normalize(IReadOnlyList<DayWindow>? days)
    {
        var defaults = CreateDefault();
        return Enumerable.Range(0, Days)
            .Select(i => (days != null && i < days.Count && days[i] != null ? days[i] : defaults[i]).Normalized())
            .ToList();
    }

    public static int IndexOf(DayOfWeek day) => ((int)day + 6) % 7;

    /// <summary>Copies day <paramref name="from"/>'s times to <paramref name="targets"/> (ISO
    /// indices) and switches those days on, like the Android editor's "Copy to".</summary>
    public static void CopyWindow(IList<DayWindow> days, int from, IEnumerable<int> targets)
    {
        var source = days[from];
        foreach (var i in targets.Where(i => i != from && i >= 0 && i < days.Count))
            days[i] = new DayWindow(true, source.StartMinute, source.EndMinute);
    }

    /// <summary>Start and (exclusive) end of the window that starts on <paramref name="date"/>.</summary>
    public static (DateTime Start, DateTime End)? Span(IReadOnlyList<DayWindow> days, DateTime date)
    {
        var w = days[IndexOf(date.DayOfWeek)];
        if (!w.Enabled) return null;
        var start = date.Date.AddMinutes(w.StartMinute);
        return (start, start.AddMinutes(w.DurationMinutes));
    }

    /// <summary>True if <paramref name="now"/> (the schedule's local wall-clock time) is inside
    /// today's window or yesterday's window that runs past midnight.</summary>
    public static bool IsActive(IReadOnlyList<DayWindow> days, DateTime now)
    {
        foreach (var d in new[] { now.Date.AddDays(-1), now.Date })
        {
            if (Span(days, d) is { } s && now >= s.Start && now < s.End) return true;
        }
        return false;
    }

    /// <summary>The next moment the active state flips, or null if it never does.</summary>
    public static DateTime? NextChange(IReadOnlyList<DayWindow> days, DateTime now)
    {
        var current = IsActive(days, now);
        var candidates = new List<DateTime>();
        for (var i = -1; i <= 8; i++)
        {
            if (Span(days, now.Date.AddDays(i)) is { } s)
            {
                candidates.Add(s.Start);
                candidates.Add(s.End);
            }
        }
        // Touching windows (18:00 end, 18:00 start) are one block, so only real flips count.
        return candidates.Where(c => c > now).OrderBy(c => c).Cast<DateTime?>()
            .FirstOrDefault(c => IsActive(days, c!.Value) != current);
    }
}

/// <summary>
/// Time zone for the schedule, stored as an IANA ID ("America/New_York") like the Android
/// app, so the zone's DST rules apply and settings are portable. "" = this computer's zone.
/// .NET 8 on Windows 10 1903+ resolves IANA IDs through ICU.
/// </summary>
public static class ScheduleZones
{
    public static TimeZoneInfo Resolve(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId)) return TimeZoneInfo.Local;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var tz)) return tz;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out var windowsId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out tz)) return tz;
        return TimeZoneInfo.Local;
    }

    /// <summary>Keeps only IDs that resolve; anything else (typo, old backup) becomes "".</summary>
    public static string Sanitize(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId)) return string.Empty;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out _)) return ianaId;
        return TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out _) ? ianaId : string.Empty;
    }

    /// <summary>The schedule's wall-clock time at the instant <paramref name="nowUtc"/>.</summary>
    public static DateTime LocalTime(string? ianaId, DateTimeOffset nowUtc) =>
        TimeZoneInfo.ConvertTime(nowUtc, Resolve(ianaId)).DateTime;
}
