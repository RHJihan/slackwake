using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;

namespace SlackWake.Services;

/// <summary>
/// Discovers WAV files (default: C:\Windows\Media) and plays them. Also serves as
/// the single playback point used by both the settings preview button and the
/// overlay's actual alert, so behaviour stays consistent between the two.
/// </summary>
public static class SoundLibrary
{
    public static readonly string DefaultFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <summary>Sentinel "use the built-in system sound" entry shown at the top of the dropdown.</summary>
    public const string SystemDefaultLabel = "System default (Exclamation)";

    public record SoundOption(string DisplayName, string FilePath)
    {
        /// <summary>True for the synthetic entry that maps to <see cref="SystemSounds.Exclamation"/>.</summary>
        public bool IsSystemDefault => string.IsNullOrEmpty(FilePath);
    }

    public static IReadOnlyList<SoundOption> Enumerate()
    {
        var list = new List<SoundOption>
        {
            new(SystemDefaultLabel, string.Empty)
        };

        try
        {
            if (Directory.Exists(DefaultFolder))
            {
                var wavs = Directory.EnumerateFiles(DefaultFolder, "*.wav")
                    .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase);
                foreach (var path in wavs)
                {
                    list.Add(new SoundOption(Path.GetFileNameWithoutExtension(path), path));
                }
            }
        }
        catch
        {
            // Folder unreadable for some reason — we still return the system-default entry
            // so the dropdown is never empty.
        }

        return list;
    }

    /// <summary>
    /// Play the given path once. Empty/missing path falls back to the system Exclamation
    /// sound so callers don't need to special-case it.
    /// </summary>
    public static void PlayOnce(string? filePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                // SoundPlayer.Play is async (PlaySound with SND_ASYNC). Do NOT wrap in
                // `using` — disposing before the OS finishes the buffer truncates the
                // sound. The instance is short-lived and GC will reclaim it.
                var player = new SoundPlayer(filePath);
                player.Play();
                return;
            }
        }
        catch
        {
            // fall through to system default
        }

        try { SystemSounds.Exclamation.Play(); } catch { /* audio device gone */ }
    }
}
