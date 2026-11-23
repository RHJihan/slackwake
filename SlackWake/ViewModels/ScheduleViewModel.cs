// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using SlackWake.Models;

namespace SlackWake.ViewModels;

/// <summary>A selectable time of day ("9:00 AM"), in the user's short-time format.</summary>
public record TimeOption(int Minute, string Label);

/// <summary>A selectable time zone. <see cref="Id"/> is an IANA ID, or "" for this computer's zone.</summary>
public record ZoneOption(string Id, string WindowsId, string Label);

/// <summary>
/// "Monitor on a schedule" (port of the Android feature): per-day windows that may cross
/// midnight, in this computer's time zone or a chosen one. Owns no timers — the main
/// view-model's 1 Hz idle tick calls <see cref="Tick"/> to keep the live text current.
/// </summary>
public class ScheduleViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly Action _changed;
    private string _summaryText = string.Empty;
    private string _zoneNowText = string.Empty;
    private int _lastTickMinute = -1;

    public ScheduleViewModel(AppSettings settings, Action save, Action changed)
    {
        _settings = settings;
        _save = save;
        _changed = changed;

        ZoneOptions = BuildZoneOptions();

        // Days in the order the user's culture starts the week (Sunday first in en-US).
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        Days = new ObservableCollection<ScheduleDayViewModel>(
            Enumerable.Range(0, 7)
                .Select(i => (DayOfWeek)(((int)first + i) % 7))
                .Select(d => new ScheduleDayViewModel(this, WeeklySchedule.IndexOf(d), d)));

        Refresh();
    }

    public ObservableCollection<ScheduleDayViewModel> Days { get; }

    public IReadOnlyList<ZoneOption> ZoneOptions { get; }

    internal AppSettings Settings => _settings;

    public bool Enabled
    {
        get => _settings.ScheduleEnabled;
        set
        {
            if (_settings.ScheduleEnabled == value) return;
            _settings.ScheduleEnabled = value;
            Raise();
            Commit();
        }
    }

    /// <summary>The chosen zone, matched by IANA ID or, failing that, by the Windows zone it
    /// maps to — so "Asia/Kolkata" from Android selects the "Asia/Calcutta" entry here.</summary>
    public ZoneOption SelectedZone
    {
        get
        {
            var id = _settings.ScheduleTimeZone;
            if (string.IsNullOrEmpty(id)) return ZoneOptions[0];
            var match = ZoneOptions.FirstOrDefault(z => string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
                match = ZoneOptions.FirstOrDefault(z => z.WindowsId == windowsId);
            return match ?? ZoneOptions[0];
        }
        set
        {
            var id = value?.Id ?? string.Empty;
            if (ReferenceEquals(value, SelectedZone) || _settings.ScheduleTimeZone == id) return;
            _settings.ScheduleTimeZone = id;
            Raise();
            Commit();
        }
    }

    /// <summary>"On now · until 6:00 PM" / "Off now · starts Mon 9:00 AM".</summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => Set(ref _summaryText, value);
    }

    /// <summary>Current time in the chosen zone; empty when using this computer's zone.</summary>
    public string ZoneNowText
    {
        get => _zoneNowText;
        private set
        {
            if (Set(ref _zoneNowText, value)) Raise(nameof(ShowZoneNow));
        }
    }

    public bool ShowZoneNow => !string.IsNullOrEmpty(_zoneNowText);

    /// <summary>Called every second by the main view-model; recomputes only when the minute changes.</summary>
    public void Tick()
    {
        var minute = DateTime.Now.Minute;
        if (minute == _lastTickMinute) return;
        _lastTickMinute = minute;
        Refresh();
    }

    /// <summary>Copies a day's hours to other days (switching them on), then refreshes every row.</summary>
    internal void CopyDay(int fromIndex, IEnumerable<int> targetIndices)
    {
        WeeklySchedule.CopyWindow(_settings.ScheduleDays, fromIndex, targetIndices);
        foreach (var d in Days) d.Reload();
        Commit();
    }

    /// <summary>Only one "Copy to" panel is open at a time.</summary>
    internal void CloseCopyPanelsExcept(ScheduleDayViewModel keep)
    {
        foreach (var d in Days.Where(d => !ReferenceEquals(d, keep))) d.IsCopyOpen = false;
    }

    internal void Commit()
    {
        _save();
        Refresh();
        _changed();
    }

    internal static string FormatMinute(int minuteOfDay) =>
        DateTime.Today.AddMinutes(minuteOfDay).ToString("t", CultureInfo.CurrentCulture);

    private static string FormatWhen(DateTime when, DateTime reference) =>
        when.Date == reference.Date
            ? when.ToString("t", CultureInfo.CurrentCulture)
            : $"{when.ToString("ddd", CultureInfo.CurrentCulture)} {when.ToString("t", CultureInfo.CurrentCulture)}";

    private void Refresh()
    {
        var nowUtc = DateTimeOffset.Now;
        var zone = ScheduleZones.Resolve(_settings.ScheduleTimeZone);
        var usingLocal = string.IsNullOrEmpty(_settings.ScheduleTimeZone);
        var local = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;
        var days = _settings.ScheduleDays;

        var active = WeeklySchedule.IsActive(days, local);
        var next = WeeklySchedule.NextChange(days, local);
        string? whenText = null;
        if (next is { } n)
        {
            whenText = FormatWhen(n, local);
            if (!usingLocal)
            {
                // The same instant on this computer's clock (skipped/ambiguous DST times resolve forward).
                var here = TimeZoneInfo.ConvertTime(new DateTimeOffset(n, zone.GetUtcOffset(n)), TimeZoneInfo.Local).DateTime;
                whenText += $" ({FormatWhen(here, nowUtc.LocalDateTime)} your time)";
            }
        }

        SummaryText = (active, whenText) switch
        {
            (true, not null) => $"On now · until {whenText}",
            (true, null) => "On now · around the clock",
            (false, not null) => $"Off now · starts {whenText}",
            _ => "No days selected, so SlackWake won't alert.",
        };
        ZoneNowText = usingLocal ? string.Empty : $"Now {local.ToString("t", CultureInfo.CurrentCulture)} there · not this computer's time zone";

        foreach (var d in Days) d.RefreshNote();
    }

    /// <summary>This computer's zone first, then Windows' own list (as in Settings → Time &amp; language),
    /// kept only where an IANA ID exists so the value is portable to the Android app.</summary>
    private static IReadOnlyList<ZoneOption> BuildZoneOptions()
    {
        var list = new List<ZoneOption>
        {
            new(string.Empty, TimeZoneInfo.Local.Id, $"This computer's time zone — {TimeZoneInfo.Local.DisplayName}"),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(tz.Id, out var iana) && seen.Add(iana))
                list.Add(new ZoneOption(iana, tz.Id, tz.DisplayName));
        }
        return list;
    }
}

/// <summary>One day's row: on/off, start and end times, and a "next day" / "24 hours" note.</summary>
public class ScheduleDayViewModel : ObservableObject
{
    private readonly ScheduleViewModel _owner;
    private readonly int _index;
    private readonly DayOfWeek _day;
    private string _note = string.Empty;
    private IReadOnlyList<TimeOption> _timeOptions;
    private bool _isCopyOpen;

    public ScheduleDayViewModel(ScheduleViewModel owner, int index, DayOfWeek day)
    {
        _owner = owner;
        _index = index;
        _day = day;
        DayName = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);
        NextDayName = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName((DayOfWeek)(((int)day + 1) % 7));
        _timeOptions = BuildTimeOptions(Window.StartMinute, Window.EndMinute);

        ToggleCopyCommand = new RelayCommand(() => IsCopyOpen = !IsCopyOpen);
        CancelCopyCommand = new RelayCommand(() => IsCopyOpen = false);
        SelectWeekdaysCommand = new RelayCommand(() => SelectTargets(t => t.Index < 5));
        SelectAllCommand = new RelayCommand(() => SelectTargets(_ => true));
        ApplyCopyCommand = new RelayCommand(ApplyCopy);
    }

    public string DayName { get; }

    private string NextDayName { get; }

    /// <summary>Every 15 minutes (as in Outlook/Teams), plus the saved times if they are off-grid
    /// (e.g. 7:05 set on Android), so a value from a backup is never lost.</summary>
    public IReadOnlyList<TimeOption> TimeOptions
    {
        get => _timeOptions;
        private set => Set(ref _timeOptions, value);
    }

    private DayWindow Window => _owner.Settings.ScheduleDays[_index];

    public bool Enabled
    {
        get => Window.Enabled;
        set
        {
            if (Window.Enabled == value) return;
            Window.Enabled = value;
            Raise();
            _owner.Commit();
        }
    }

    public int StartMinute
    {
        get => Window.StartMinute;
        set
        {
            if (Window.StartMinute == value) return;
            Window.StartMinute = value;
            Raise();
            _owner.Commit();
        }
    }

    public int EndMinute
    {
        get => Window.EndMinute;
        set
        {
            if (Window.EndMinute == value) return;
            Window.EndMinute = value;
            Raise();
            _owner.Commit();
        }
    }

    /// <summary>"Ends Tuesday (next day)" for a window crossing midnight, "24 hours" when start == end.</summary>
    public string Note
    {
        get => _note;
        private set => Set(ref _note, value);
    }

    // ---- "Copy to" panel ----

    /// <summary>The other days as toggle chips, in the user's week order.</summary>
    public ObservableCollection<CopyTarget> CopyTargets { get; } = new();

    public bool IsCopyOpen
    {
        get => _isCopyOpen;
        set
        {
            if (!Set(ref _isCopyOpen, value) || !value) return;
            // Fresh selection each time the panel opens.
            CopyTargets.Clear();
            foreach (var d in _owner.Days.Where(d => !ReferenceEquals(d, this)))
                CopyTargets.Add(new CopyTarget(d._index, d._day));
            _owner.CloseCopyPanelsExcept(this);
        }
    }

    public string CopyPrompt => $"Copy {DayName}'s hours to:";

    public System.Windows.Input.ICommand ToggleCopyCommand { get; }
    public System.Windows.Input.ICommand CancelCopyCommand { get; }
    public System.Windows.Input.ICommand SelectWeekdaysCommand { get; }
    public System.Windows.Input.ICommand SelectAllCommand { get; }
    public System.Windows.Input.ICommand ApplyCopyCommand { get; }

    private void SelectTargets(Func<CopyTarget, bool> pick)
    {
        foreach (var t in CopyTargets) t.IsSelected = pick(t);
    }

    private void ApplyCopy()
    {
        var targets = CopyTargets.Where(t => t.IsSelected).Select(t => t.Index).ToList();
        IsCopyOpen = false;
        if (targets.Count > 0) _owner.CopyDay(_index, targets);
    }

    /// <summary>Re-reads this day from settings after a bulk change (e.g. a copy into it).</summary>
    internal void Reload()
    {
        var w = Window;
        if (TimeOptions.All(o => o.Minute != w.StartMinute) || TimeOptions.All(o => o.Minute != w.EndMinute))
            TimeOptions = BuildTimeOptions(w.StartMinute, w.EndMinute);
        Raise(nameof(Enabled));
        Raise(nameof(StartMinute));
        Raise(nameof(EndMinute));
    }

    internal void RefreshNote()
    {
        var w = Window;
        Note = w.StartMinute == w.EndMinute ? "On for 24 hours"
            : w.CrossesMidnight ? $"Ends {NextDayName} (next day)"
            : string.Empty;
    }

    private static IReadOnlyList<TimeOption> BuildTimeOptions(params int[] extra) =>
        Enumerable.Range(0, 96).Select(i => i * 15)
            .Concat(extra)
            .Distinct()
            .OrderBy(m => m)
            .Select(m => new TimeOption(m, ScheduleViewModel.FormatMinute(m)))
            .ToList();
}

/// <summary>A day chip in a "Copy to" panel.</summary>
public class CopyTarget : ObservableObject
{
    private bool _isSelected;

    public CopyTarget(int index, DayOfWeek day)
    {
        Index = index;
        ShortName = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(day);
        FullName = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);
    }

    /// <summary>ISO index (0 = Monday).</summary>
    public int Index { get; }

    public string ShortName { get; }

    public string FullName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}
