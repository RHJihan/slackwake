// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Md. Rifat Hasan Jihan

using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace SlackWake.Services;

/// <summary>
/// Plays a WAV file on repeat with a fixed gap between repetitions, and an
/// optional hard cap on total looping duration.
///
/// Why MediaPlayer and not a fixed-cadence timer: the previous implementation
/// fired every N seconds regardless of how long the sound was, which either
/// overlapped a longer clip with itself or left an awkward silence after a
/// short one. MediaPlayer.MediaEnded lets us measure the gap from the *end*
/// of one play to the start of the next — that is what makes the alert sound
/// like a deliberate pattern instead of a stutter.
/// </summary>
public sealed class LoopingSoundPlayer
{
    // Gap between the end of one play and the start of the next.
    private const int GapMilliseconds = 500;

    private MediaPlayer? _player;
    private DispatcherTimer? _gapTimer;
    private DispatcherTimer? _maxTimer;
    private string _filePath = string.Empty;
    private bool _loop;
    private bool _stopped;

    /// <summary>
    /// Begin playback. When <paramref name="loop"/> is true, replay with a
    /// <see cref="GapMilliseconds"/> gap until <see cref="Stop"/> is called or
    /// <paramref name="maxDuration"/> elapses (if provided).
    /// </summary>
    public void Start(string filePath, bool loop, TimeSpan? maxDuration)
    {
        Stop();
        _stopped = false;

        var resolved = ResolvePath(filePath);
        if (resolved == null) return;

        _filePath = resolved;
        _loop = loop;

        if (loop && maxDuration is { } cap && cap > TimeSpan.Zero)
        {
            _maxTimer = new DispatcherTimer { Interval = cap };
            _maxTimer.Tick += (_, _) => Stop();
            _maxTimer.Start();
        }

        PlayOnce();
    }

    public void Stop()
    {
        _stopped = true;

        var current = _player;
        if (current != null)
        {
            _player = null;
            current.Stop();
            current.Close();
        }

        _gapTimer?.Stop();
        _gapTimer = null;
        _maxTimer?.Stop();
        _maxTimer = null;
    }

    private void PlayOnce()
    {
        if (_stopped) return;

        var player = new MediaPlayer();
        player.MediaEnded += (_, _) => OnEnded(player);
        player.MediaFailed += (_, _) => OnEnded(player);
        _player = player;
        player.Open(new Uri(_filePath));
        player.Play();
    }

    private void OnEnded(MediaPlayer source)
    {
        // Ignore callbacks from a player we've already torn down — a stale
        // MediaEnded must not resurrect playback after Stop().
        if (!ReferenceEquals(_player, source)) return;

        source.Close();
        _player = null;

        if (!_loop || _stopped) return;

        _gapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GapMilliseconds) };
        _gapTimer.Tick += (_, _) =>
        {
            _gapTimer?.Stop();
            _gapTimer = null;
            PlayOnce();
        };
        _gapTimer.Start();
    }

    /// <summary>
    /// Empty path means "system default" — map it to the canonical Exclamation
    /// wav so MediaPlayer has a real file to open and MediaEnded can fire.
    /// </summary>
    private static string? ResolvePath(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            return filePath;

        var fallback = Path.Combine(SoundLibrary.DefaultFolder, "Windows Exclamation.wav");
        return File.Exists(fallback) ? fallback : null;
    }
}
