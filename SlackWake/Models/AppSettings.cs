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
}
