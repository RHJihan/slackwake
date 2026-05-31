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

    /// <summary>When true, the loop self-stops after <see cref="SoundLoopMaxSeconds"/>.</summary>
    public bool SoundLoopMaxEnabled { get; set; } = false;

    /// <summary>Cap on total looping duration when <see cref="SoundLoopMaxEnabled"/> is on.</summary>
    public int SoundLoopMaxSeconds { get; set; } = 60;

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
}
