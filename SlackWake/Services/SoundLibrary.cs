// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;

namespace SlackWake.Services;

/// <summary>
/// Discovers audio files (default: C:\Windows\Media) and plays them. Also serves as
/// the single playback point used by both the settings preview button and the
/// overlay's actual alert, so behaviour stays consistent between the two.
/// </summary>
public static class SoundLibrary
{
    public static readonly string DefaultFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <summary>Full path to SlackWake's own default alert sound (Ring08 ships with every
    /// Windows install). This is what a fresh install selects and the recovery path when a
    /// chosen file goes missing.</summary>
    public static readonly string FallbackFilePath = Path.Combine(DefaultFolder, "Ring08.wav");

    /// <summary>Action entry pinned at the bottom of the dropdown. Selecting it opens a file
    /// picker rather than choosing a sound — the view-model intercepts it (see <see cref="SoundKind.Browse"/>).</summary>
    public const string BrowseLabel = "Browse for a file…";

    /// <summary>
    /// Audio extensions offered in the file picker and accepted as custom alert sounds.
    /// These are the formats WPF's <see cref="System.Windows.Media.MediaPlayer"/> can play
    /// (the engine behind both the preview and the overlay alert). Kept here as the single
    /// source of truth so the dialog filter and any validation never drift apart.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedExtensions =
        new[] { ".wav", ".mp3", ".wma", ".aac", ".m4a", ".flac", ".ogg" };

    /// <summary>Distinguishes the kinds of entry in the sound dropdown. Replaces guessing from
    /// the file path — the browse-action row has an empty path, so it can only be told apart
    /// from a real sound by intent.</summary>
    public enum SoundKind
    {
        /// <summary>A file discovered in <see cref="DefaultFolder"/>.</summary>
        BuiltIn,

        /// <summary>A file the user picked from an arbitrary location.</summary>
        Custom,

        /// <summary>The "Browse for a file…" action row; empty path.</summary>
        Browse,
    }

    public record SoundOption(string DisplayName, string FilePath, SoundKind Kind = SoundKind.BuiltIn)
    {
        /// <summary>True for a user-supplied file from outside <see cref="DefaultFolder"/>.</summary>
        public bool IsCustom => Kind == SoundKind.Custom;

        /// <summary>True for the "Browse for a file…" action row.</summary>
        public bool IsBrowse => Kind == SoundKind.Browse;
    }

    /// <summary>The always-present "Browse for a file…" action row (last entry of every enumeration).</summary>
    public static SoundOption BrowseAction() => new(BrowseLabel, string.Empty, SoundKind.Browse);

    /// <summary>Wraps a user-picked file as a custom entry, labelled by its file name.</summary>
    public static SoundOption CreateCustom(string filePath) =>
        new(Path.GetFileName(filePath), filePath, SoundKind.Custom);

    /// <summary>Case-insensitive path comparison, tolerant of null. Used to dedupe a picked
    /// file against entries already in the list. Falls back to comparing <see cref="Path.GetFullPath"/>
    /// normalizations so a file picked from C:\Windows\Media collapses onto its built-in entry
    /// even when the two paths differ only textually (casing aside — e.g. short 8.3 vs long form,
    /// or a "\.\"-style segment the dialog may return) rather than spawning a duplicate custom row.
    /// The plain ordinal check runs first so a blank or otherwise non-normalizable operand still
    /// compares safely.</summary>
    public static bool PathsEqual(string? a, string? b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // A non-path operand can't be normalized — already handled by the ordinal check
            // above, so an unequal, unnormalizable pair is a miss.
            return false;
        }
    }

    /// <summary>True when the given path already lives in <see cref="DefaultFolder"/>, so it is
    /// already represented by a built-in entry and needs no separate custom row.</summary>
    public static bool IsInDefaultFolder(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
            return dir != null && PathsEqual(dir.TrimEnd(Path.DirectorySeparatorChar),
                DefaultFolder.TrimEnd(Path.DirectorySeparatorChar));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>File-dialog filter string covering <see cref="SupportedExtensions"/> plus an
    /// "All files" fallback, built once from the extension list so it stays in sync.</summary>
    public static string FileDialogFilter
    {
        get
        {
            var patterns = string.Join(";", SupportedExtensions.Select(e => "*" + e));
            return $"Audio files ({patterns})|{patterns}|All files (*.*)|*.*";
        }
    }

    /// <summary>
    /// Builds the dropdown list: every WAV in <see cref="DefaultFolder"/>, an optional custom
    /// entry for a previously chosen file that lives elsewhere, and finally the
    /// "Browse for a file…" action row.
    /// </summary>
    /// <param name="customFilePath">A persisted custom sound path to surface as its own entry,
    /// so a file the user picked in a past session is shown and re-selectable. Ignored when
    /// blank, missing, or already covered by a built-in entry.</param>
    public static IReadOnlyList<SoundOption> Enumerate(string? customFilePath = null)
    {
        var list = new List<SoundOption>();

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
            // Folder unreadable for some reason — the dropdown may be empty apart from the
            // Browse row; playback still degrades to Ring08 via ResolvePlayablePath.
        }

        // Re-surface a file the user picked in a past session when it lives outside the
        // default folder; a file inside it is already listed as a built-in.
        if (!string.IsNullOrWhiteSpace(customFilePath)
            && File.Exists(customFilePath)
            && !list.Any(o => PathsEqual(o.FilePath, customFilePath)))
        {
            list.Add(CreateCustom(customFilePath));
        }

        list.Add(BrowseAction());
        return list;
    }

    /// <summary>
    /// Resolves a stored selection to an actual, playable file path — the single point every
    /// player routes through so their behaviour never drifts:
    /// <list type="bullet">
    /// <item>an existing file → itself;</item>
    /// <item>anything else (blank or missing) → Ring08.</item>
    /// </list>
    /// Returns null only if even Ring08 is unavailable.
    /// </summary>
    public static string? ResolvePlayablePath(string? selection)
    {
        if (!string.IsNullOrWhiteSpace(selection) && File.Exists(selection))
        {
            return selection;
        }

        return File.Exists(FallbackFilePath) ? FallbackFilePath : null;
    }

    /// <summary>
    /// Play the given selection once. Resolves through <see cref="ResolvePlayablePath"/>
    /// (the file / Ring08), and drops to the system beep only if even that can't be played,
    /// so callers don't need to special-case anything.
    /// </summary>
    public static void PlayOnce(string? filePath)
    {
        var path = ResolvePlayablePath(filePath);

        try
        {
            if (path != null)
            {
                // SoundPlayer.Play is async (PlaySound with SND_ASYNC). Do NOT wrap in
                // `using` — disposing before the OS finishes the buffer truncates the
                // sound. The instance is short-lived and GC will reclaim it.
                var player = new SoundPlayer(path);
                player.Play();
                return;
            }
        }
        catch
        {
            // fall through to the last-resort system beep
        }

        try { SystemSounds.Beep.Play(); } catch { /* audio device gone */ }
    }
}
