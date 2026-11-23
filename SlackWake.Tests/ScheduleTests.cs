// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using SlackWake.Models;
using Xunit;

namespace SlackWake.Tests;

/// <summary>Monitoring-schedule rules: per-day windows, midnight crossing, time zones, Copy to.</summary>
public class ScheduleTests
{
    // 2026-11-23 is a Monday: day 1 = Mon … 7 = Sun.
    private static DateTime At(int day, int h, int m = 0) => new(2026, 11, 22 + day, h, m, 0);

    private static DayWindow Win(int start, int end, bool on = true) => new(on, start * 60, end * 60);

    private static readonly DayWindow Off = new(false, 9 * 60, 17 * 60);

    private static List<DayWindow> Only(int index, DayWindow w) =>
        Enumerable.Range(0, 7).Select(i => i == index ? w : Off).ToList();

    [Fact]
    public void SameDayWindowIsStartInclusiveEndExclusive()
    {
        var days = Only(0, Win(9, 17));
        Assert.False(WeeklySchedule.IsActive(days, At(1, 8, 59)));
        Assert.True(WeeklySchedule.IsActive(days, At(1, 9)));
        Assert.True(WeeklySchedule.IsActive(days, At(1, 16, 59)));
        Assert.False(WeeklySchedule.IsActive(days, At(1, 17)));
        Assert.False(WeeklySchedule.IsActive(days, At(2, 10)));
    }

    [Fact]
    public void ElevenPmToSevenAmRunsIntoTheNextDate()
    {
        var days = Only(0, Win(23, 7)); // Monday night only
        Assert.True(days[0].CrossesMidnight);
        Assert.False(WeeklySchedule.IsActive(days, At(1, 22, 59)));
        Assert.True(WeeklySchedule.IsActive(days, At(1, 23)));
        Assert.True(WeeklySchedule.IsActive(days, At(2, 0, 30)));
        Assert.True(WeeklySchedule.IsActive(days, At(2, 6, 59)));
        Assert.False(WeeklySchedule.IsActive(days, At(2, 7)));
        Assert.False(WeeklySchedule.IsActive(days, At(2, 23, 30)));
        Assert.False(WeeklySchedule.IsActive(days, At(1, 2))); // belongs to Sunday, which is off
    }

    [Fact]
    public void SundayNightWrapsIntoMondayMorning()
    {
        var days = Only(6, Win(22, 6));
        Assert.True(WeeklySchedule.IsActive(days, At(7, 23)));
        Assert.True(WeeklySchedule.IsActive(days, At(1, 5, 59)));
        Assert.False(WeeklySchedule.IsActive(days, At(1, 6)));
    }

    [Fact]
    public void StartEqualToEndIsTwentyFourHours()
    {
        var days = Only(2, Win(8, 8));
        Assert.Equal(1440, days[2].DurationMinutes);
        Assert.True(WeeklySchedule.IsActive(days, At(4, 7, 59)));
        Assert.False(WeeklySchedule.IsActive(days, At(4, 8)));
    }

    [Fact]
    public void NextChangeCrossesMidnightAndSkipsToNextWeek()
    {
        Assert.Equal(At(2, 7), WeeklySchedule.NextChange(Only(0, Win(23, 7)), At(1, 23, 30)));
        Assert.Equal(new DateTime(2026, 11, 30, 9, 0, 0), WeeklySchedule.NextChange(Only(0, Win(9, 17)), At(1, 18)));
        Assert.Null(WeeklySchedule.NextChange(Enumerable.Range(0, 7).Select(_ => Win(0, 0)).ToList(), At(3, 12)));
        Assert.Null(WeeklySchedule.NextChange(Enumerable.Range(0, 7).Select(_ => Off).ToList(), At(3, 12)));
    }

    [Fact]
    public void DefaultsAndNormalization()
    {
        var s = new AppSettings();
        Assert.False(s.ScheduleEnabled);
        Assert.True(s.InSchedule(DateTimeOffset.UtcNow)); // schedule off → always allowed
        var fixedUp = WeeklySchedule.Normalize(new List<DayWindow> { new(true, -5, 99_999) });
        Assert.Equal(7, fixedUp.Count);
        Assert.Equal(0, fixedUp[0].StartMinute);
        Assert.Equal(1439, fixedUp[0].EndMinute);
        Assert.Equal("", ScheduleZones.Sanitize("Mars/Olympus_Mons"));
        Assert.Equal("America/New_York", ScheduleZones.Sanitize("America/New_York"));
    }

    [Fact]
    public void ScheduleInAnotherZoneIsEvaluatedInThatZone()
    {
        // New York weekdays 09:00–18:00. 2026-11-23 14:30 UTC = 09:30 EST Monday.
        var s = new AppSettings { ScheduleEnabled = true, ScheduleTimeZone = "America/New_York" };
        Assert.True(s.InSchedule(new DateTimeOffset(2026, 11, 23, 14, 30, 0, TimeSpan.Zero)));
        Assert.False(s.InSchedule(new DateTimeOffset(2026, 11, 23, 13, 30, 0, TimeSpan.Zero))); // 08:30 EST
        // July is EDT (UTC−4): 13:30 UTC Monday = 09:30 local → on. DST comes from the zone ID.
        Assert.True(s.InSchedule(new DateTimeOffset(2026, 7, 6, 13, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void NightWindowCrossesMidnightInTheSchedulesZone()
    {
        var s = new AppSettings
        {
            ScheduleEnabled = true,
            ScheduleTimeZone = "America/New_York",
            ScheduleDays = Enumerable.Range(0, 7).Select(_ => Win(23, 7)).ToList(),
        };
        // 02:00 Tue New York (07:00 UTC) → on; 08:00 Tue New York (13:00 UTC) → off.
        Assert.True(s.InSchedule(new DateTimeOffset(2026, 11, 24, 7, 0, 0, TimeSpan.Zero)));
        Assert.False(s.InSchedule(new DateTimeOffset(2026, 11, 24, 13, 0, 0, TimeSpan.Zero)));
    }

    // Renaming a key would silently reset everyone's saved schedule.
    [Fact]
    public void ScheduleSettingsKeepTheirJsonKeys()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings());
        Assert.Contains("\"ScheduleEnabled\":false", json);
        Assert.Contains("\"ScheduleDays\":[{\"Enabled\":true,\"StartMinute\":540,\"EndMinute\":1080}", json);
        Assert.Contains("\"ScheduleTimeZone\":\"\"", json);
    }

    [Fact]
    public void CopyToSelectedDaysCopiesTimesAndSwitchesThemOn()
    {
        var days = Enumerable.Range(0, 7).Select(_ => Off).ToList();
        days[0] = Win(23, 7); // Monday night
        WeeklySchedule.CopyWindow(days, 0, new[] { 1, 2, 0, 9 }); // self and out-of-range are ignored
        Assert.Equal(new[] { 1380, 1380, 1380 }, days.Take(3).Select(d => d.StartMinute));
        Assert.Equal(new[] { 420, 420, 420 }, days.Take(3).Select(d => d.EndMinute));
        Assert.True(days[1].Enabled && days[2].Enabled);
        Assert.False(days[3].Enabled); // not selected → untouched
        Assert.Same(Off, days[3]);
    }
}
