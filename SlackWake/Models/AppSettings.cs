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

    /// <summary>Register the exe under HKCU\...\Run when true. On by default so a fresh
    /// install keeps watching for pings across reboots without the user opting in.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>When true, skip showing the settings window on launch (used when auto-started).</summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>Play an audible alert in addition to showing the fullscreen overlay.</summary>
    public bool SoundEnabled { get; set; } = true;

    /// <summary>Seconds to wait after the overlay appears before the first sound plays. 0 = immediate.</summary>
    public int SoundDelaySeconds { get; set; } = 5;

    /// <summary>When true, keep replaying the alert sound until the user dismisses the overlay.
    /// On by default so the alert keeps sounding until acknowledged (bounded by the auto-stop
    /// cap when <see cref="AlertAutoStopEnabled"/> is on).</summary>
    public bool SoundLoop { get; set; } = true;

    /// <summary>
    /// The alert sound to play: a full path to an audio file. The settings UI lists files from
    /// C:\Windows\Media and lets the user browse for a custom file anywhere; any path playable
    /// by <see cref="System.Windows.Media.MediaPlayer"/> (wav, mp3, wma, m4a, …) works. Blank,
    /// or a saved path that no longer exists, falls back to SlackWake's default (Ring08.wav).
    /// </summary>
    public string SoundFilePath { get; set; } = string.Empty;

    /// <summary>Strobe the overlay between two contrasting colors so the alert is
    /// hard to ignore from across the room. On by default so the alert is maximally
    /// noticeable out of the box; users who find it distracting can turn it off.</summary>
    public bool FlashEnabled { get; set; } = true;

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
    /// long an unattended alert keeps running. On by default so an unattended alert
    /// can't run indefinitely.</summary>
    public bool AlertAutoStopEnabled { get; set; } = true;

    /// <summary>Cap, in seconds, on how long continuous alerts run when
    /// <see cref="AlertAutoStopEnabled"/> is on. Applies to the looping sound, and
    /// to the visual flash when <see cref="AlertAutoStopIncludesVisual"/> is on.
    /// Defaults to 10 minutes.</summary>
    public int AlertMaxDurationSeconds { get; set; } = 600;

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

    // ---- Keyword filters ----
    // Two independent, complementary filters. "Mute by keyword" is a block-list (drop
    // matching pings); "Alert only by keyword" is an allow-list (drop everything that
    // does NOT match). They can both be on at once: a ping then wakes the user only if it
    // is allowed AND not muted — muting wins on overlap (see MainViewModel). Each keeps
    // its own keyword list so the two never share or clobber state.

    /// <summary>Block-list switch. When true, Slack pings whose content matches any entry
    /// in <see cref="KeywordFilterText"/> are silently dropped — no overlay, no sound, no
    /// flash; everything else still alerts. Lets the user mute noisy bots, channels, or
    /// topics while away. Off by default.</summary>
    public bool KeywordFilterEnabled { get; set; } = false;

    /// <summary>Comma- or newline-separated block-list keywords. A ping is muted when its
    /// sender, channel, or message text contains any of these (case-insensitive substring).
    /// Wrap a phrase in double quotes to match it verbatim, including commas. Blank entries
    /// are ignored, as is any entry prefixed with <c>//</c> — a commented-out keyword that
    /// stays in the list but is skipped. Only consulted when <see cref="KeywordFilterEnabled"/>.</summary>
    public string KeywordFilterText { get; set; } = string.Empty;

    /// <summary>Allow-list switch. When true, ONLY pings whose content matches an entry in
    /// <see cref="KeywordAllowText"/> are allowed through; every other ping is silently
    /// dropped. Lets the user say "while I'm away, only wake me for on-call/incidents." Off
    /// by default. An empty <see cref="KeywordAllowText"/> makes this inert rather than
    /// muting everything (see MainViewModel) — enabling it before typing a keyword can never
    /// silently swallow every ping.</summary>
    public bool KeywordAllowEnabled { get; set; } = false;

    /// <summary>Comma- or newline-separated allow-list keywords. A ping is allowed through
    /// when its sender, channel, or message text contains any of these (case-insensitive
    /// substring). Wrap a phrase in double quotes to match it verbatim, including commas.
    /// Blank entries are ignored, as is any entry prefixed with <c>//</c> — a commented-out
    /// keyword that stays in the list but is skipped. Only consulted when <see cref="KeywordAllowEnabled"/>.</summary>
    public string KeywordAllowText { get; set; } = string.Empty;
}
