using System;
using System.IO;
using System.Text.Json;
using SlackWake.Models;

namespace SlackWake.Services;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as JSON in the per-user AppData folder.
/// Failures fall back to defaults rather than crashing — settings are advisory.
/// </summary>
public class SettingsService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SlackWake");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt or unreadable settings file — reset to defaults rather than
            // refusing to launch. The next Save() will overwrite the bad file.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch
        {
            // Persisting settings is best-effort. We do not want a transient disk
            // problem to take down the tray app.
        }
    }
}
