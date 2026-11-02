using System;
using Godot;

namespace Wavebreak.Audio;

/// <summary>The puzzle maps' sounds (Game/Puzzle/PuzzleMap).</summary>
public enum PuzzleSound { Click, Thunk, Door, Bell, Wrong, Solved, Gust, Reward }

/// <summary>
/// Puzzle sounds, on the same 2D voice pool as the perk sounds. They are rare
/// (a plate, a door, a bell), so each kind only keeps a short minimum gap;
/// bells are pitched per bell so the order is heard as a tune as well as seen.
/// </summary>
public sealed partial class AudioDirector
{
    private static AudioStreamWav[]? _puzzleStreams;
    private readonly double[] _puzzleLast = new double[8];

    /// <summary>Puzzle sounds started this run (the self-check reads it).</summary>
    public static int PuzzleSoundsPlayed { get; private set; }

    public static void PlayPuzzle(PuzzleSound s, float pitch = 1f) => Instance?.PuzzleSoundNow(s, pitch);

    private void PuzzleSoundNow(PuzzleSound s, float pitch)
    {
        if (_puzzleStreams is null)
        {
            _puzzleStreams = new AudioStreamWav[_puzzleLast.Length];
            for (int i = 0; i < _puzzleStreams.Length; i++) _puzzleStreams[i] = SfxSynth.ToStream(SfxSynth.BuildPuzzle((PuzzleSound)i));
        }
        double now = Now();
        if (now - _puzzleLast[(int)s] < 0.08) return;
        Voice? voice = null;
        foreach (Voice v in _voices2D)
        {
            if (!IsPlaying(v.Player)) { voice = v; break; }
            if (voice is null || v.StartedAt < voice.StartedAt) voice = v;
        }
        if (voice?.Player is not AudioStreamPlayer p) return;
        p.Stream = _puzzleStreams[(int)s];
        p.PitchScale = Math.Clamp(pitch, 0.4f, 2.5f);
        p.VolumeDb = s is PuzzleSound.Gust ? -20f : s is PuzzleSound.Bell or PuzzleSound.Reward or PuzzleSound.Solved ? -11f : -14f;
        p.Play();
        voice.Id = (Sfx)(-1);
        voice.Priority = 4;
        voice.StartedAt = now;
        _puzzleLast[(int)s] = now;
        PuzzleSoundsPlayed++;
    }
}
