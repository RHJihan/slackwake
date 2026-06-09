// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

namespace SlackWake.Models;

/// <summary>
/// Plain settings POCO. Serialized as JSON to %AppData%\SlackWake\settings.json
/// by <see cref="Services.SettingsService"/>. Keep this small and stable — adding
/// optional properties is safe; renaming/removing them will reset user state.
/// </summary>
public class AppSettings
{
    /// <summary>Master kill-switch. When false the service stays connected to nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long the user must be idle before Slack alerts start firing overlays.</summary>
    public int IdleTimeoutSeconds { get; set; } = 300;

    /// <summary>Register the exe under HKCU\...\Run when true.</summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>When true, skip showing the settings window on launch (used when auto-started).</summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>Play an audible alert in addition to showing the fullscreen overlay.</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>Seconds to wait after the overlay appears before the first sound plays. 0 = immediate.</summary>
    public int SoundDelaySeconds { get; set; } = 5;

    /// <summary>When true, keep replaying the alert sound until the user dismisses the overlay.</summary>
    public bool SoundLoop { get; set; } = false;

    /// <summary>
    /// Full path to the WAV file to play. Empty/null means use the built-in
    /// <see cref="System.Media.SystemSounds.Exclamation"/> sound. The settings UI
    /// populates this from C:\Windows\Media but any readable .wav path works.
    /// </summary>
    public string SoundFilePath { get; set; } = string.Empty;

    /// <summary>Strobe the overlay between two contrasting colors so the alert is
    /// hard to ignore from across the room. Off by default — some users find it
    /// distracting or uncomfortable.</summary>
    public bool FlashEnabled { get; set; } = false;

    /// <summary>Half-cycle duration of the flash, in milliseconds — the time the
    /// background takes to fade from one color to the other. A full on/off cycle
    /// is twice this value.</summary>
    public int FlashIntervalMs { get; set; } = 500;

    /// <summary>First color in the flash pair. Hex string (e.g. "#000000"). The text
    /// color over this background is picked automatically for max contrast.</summary>
    public string FlashColorA { get; set; } = "#000000";

    /// <summary>Second color in the flash pair. Hex string (e.g. "#FFFFFF").</summary>
    public string FlashColorB { get; set; } = "#FFFFFF";

    /// <summary>When true, the continuous alerts self-stop after
    /// <see cref="AlertMaxDurationSeconds"/>, even if the overlay is never dismissed.
    /// Shared by the looping sound and the visual flash so a single cap governs how
    /// long an unattended alert keeps running.</summary>
    public bool AlertAutoStopEnabled { get; set; } = false;

    /// <summary>Cap, in seconds, on how long continuous alerts run when
    /// <see cref="AlertAutoStopEnabled"/> is on. Applies to the looping sound, and
    /// to the visual flash when <see cref="AlertAutoStopIncludesVisual"/> is on.</summary>
    public int AlertMaxDurationSeconds { get; set; } = 60;

    /// <summary>When true, the auto-stop cap silences the looping sound after
    /// <see cref="AlertMaxDurationSeconds"/>; when false the sound keeps looping until
    /// the overlay is dismissed. Only consulted when <see cref="AlertAutoStopEnabled"/>
    /// is on. Defaults to true to preserve the original "stop everything" behavior.</summary>
    public bool AlertAutoStopIncludesSound { get; set; } = true;

    /// <summary>When true, the auto-stop cap also tears down the visual flash; when
    /// false the flash keeps strobing until the overlay is dismissed. Only consulted
    /// when <see cref="AlertAutoStopEnabled"/> is on. Defaults to true to preserve the
    /// original "stop everything" behavior.</summary>
    public bool AlertAutoStopIncludesVisual { get; set; } = true;

    /// <summary>When true, Slack pings whose content matches any entry in
    /// <see cref="KeywordFilterText"/> are silently dropped — no overlay, no sound,
    /// no flash. Lets the user mute noisy bots, channels, or topics while away.</summary>
    public bool KeywordFilterEnabled { get; set; } = false;

    /// <summary>Comma-separated keywords. A ping is muted when its sender, channel,
    /// or message text contains any of these (case-insensitive substring match).
    /// Blank entries are ignored. Only consulted when <see cref="KeywordFilterEnabled"/>.</summary>
    public string KeywordFilterText { get; set; } = string.Empty;
}
