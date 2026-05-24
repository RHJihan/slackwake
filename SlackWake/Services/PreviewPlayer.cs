using System;
using System.IO;
using System.Windows.Media;

namespace SlackWake.Services;

/// <summary>
/// Stateful preview playback for a single WAV file. Wraps WPF's <see cref="MediaPlayer"/>
/// so callers can ask "is it currently playing?" and react to natural end-of-stream —
/// neither of which <see cref="System.Media.SoundPlayer"/> exposes.
///
/// Single responsibility: own the preview transport (play / stop / IsPlaying). Enumeration
/// and one-shot fire-and-forget playback for the overlay live in <see cref="SoundLibrary"/>.
/// </summary>
public sealed class PreviewPlayer
{
    // Per-play instance instead of a long-lived reused one. Reusing a single MediaPlayer
    // means Stop() has to race with whatever Open/buffering state the previous play left
    // behind; throwing the instance away on every transition makes "stop now" actually
    // mean now, and lets us safely ignore late MediaEnded callbacks from stale instances.
    private MediaPlayer? _player;
    private bool _isPlaying;

    public bool IsPlaying => _isPlaying;
    public event EventHandler? IsPlayingChanged;

    public void Play(string filePath)
    {
        var resolved = ResolvePath(filePath);
        if (resolved == null) return;

        StopInternal();

        var player = new MediaPlayer();
        player.MediaEnded  += (_, _) => OnPlaybackFinished(player);
        player.MediaFailed += (_, _) => OnPlaybackFinished(player);

        _player = player;
        player.Open(new Uri(resolved));
        player.Play();
        SetPlaying(true);
    }

    public void Stop() => StopInternal();

    /// <summary>
    /// Tear down the current instance — Stop() halts transport, Close() releases the file
    /// handle, dropping the reference lets the audio sink be reclaimed. After this returns
    /// the OS audio buffer drains to silence within a frame.
    /// </summary>
    private void StopInternal()
    {
        var current = _player;
        if (current != null)
        {
            _player = null;
            current.Stop();
            current.Close();
        }
        SetPlaying(false);
    }

    /// <summary>
    /// Natural end-of-stream / failure callback. Guard against stale instances:
    /// if the user already stopped or switched sounds, we've moved on and shouldn't
    /// flip IsPlaying back to false for an event from a player we no longer own.
    /// </summary>
    private void OnPlaybackFinished(MediaPlayer source)
    {
        if (!ReferenceEquals(_player, source)) return;
        StopInternal();
    }

    /// <summary>
    /// Empty path means "system default" — map it to the canonical Exclamation wav so
    /// MediaPlayer has a real file to open and MediaEnded can fire to flip the button back.
    /// </summary>
    private static string? ResolvePath(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            return filePath;

        var fallback = Path.Combine(SoundLibrary.DefaultFolder, "Windows Exclamation.wav");
        return File.Exists(fallback) ? fallback : null;
    }

    private void SetPlaying(bool value)
    {
        if (_isPlaying == value) return;
        _isPlaying = value;
        IsPlayingChanged?.Invoke(this, EventArgs.Empty);
    }
}
