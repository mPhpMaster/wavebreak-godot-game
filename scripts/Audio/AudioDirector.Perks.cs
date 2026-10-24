using System;
using Godot;

namespace Wavebreak.Audio;

/// <summary>The sounds perk procs make (Combat/Weapons/PerkProcs).</summary>
public enum PerkSound { Chain, Bubble, Stomp, Heal, Mote, Coin, LastLight, Lighthouse, Pulse, Surge, Wake }

/// <summary>
/// Perk sounds, a partial like the chest and shrine sounds and built with the
/// same helpers and voice pool. They are quiet on purpose and rate-limited
/// twice: each sound has its own minimum gap, and all perk sounds together
/// share a budget of <see cref="PerkBudget"/> starts per second, because a late
/// build fires several perks every second and none of them should become the
/// thing the player hears.
/// </summary>
public sealed partial class AudioDirector
{
    private const int PerkBudget = 4;

    private static AudioStreamWav[]? _perkStreams;
    private readonly double[] _perkLast = new double[11];
    private double _perkWindowStart;
    private int _perkWindowCount;

    /// <summary>Perk sounds started this run (the self-check reads it).</summary>
    public static int PerkSoundsPlayed { get; private set; }

    private static (float Db, int Priority, float MinInterval) PerkSpec(PerkSound s) => s switch
    {
        PerkSound.Chain => (-19f, 2, 0.35f),
        PerkSound.Bubble => (-16f, 3, 0.4f),
        PerkSound.Stomp => (-15f, 3, 0.3f),
        PerkSound.Heal => (-22f, 1, 1.0f),
        PerkSound.Mote => (-26f, 1, 0.5f),
        PerkSound.Coin => (-18f, 2, 0.8f),
        PerkSound.LastLight => (-9f, 6, 1.0f),
        PerkSound.Lighthouse => (-17f, 3, 2.0f),
        PerkSound.Pulse => (-19f, 2, 1.0f),
        PerkSound.Surge => (-15f, 4, 2.0f),
        _ => (-24f, 1, 0.6f),
    };

    /// <summary>Play a perk sound, unless it or the perk budget is spent.</summary>
    public static void PlayPerk(PerkSound s, float pitch = 1f) => Instance?.PerkSoundNow(s, pitch);

    private void PerkSoundNow(PerkSound s, float pitch)
    {
        if (_perkStreams is null)
        {
            _perkStreams = new AudioStreamWav[_perkLast.Length];
            for (int i = 0; i < _perkStreams.Length; i++) _perkStreams[i] = SfxSynth.ToStream(BuildPerk((PerkSound)i));
        }
        var (db, priority, minInterval) = PerkSpec(s);
        double now = Now();
        if (now - _perkLast[(int)s] < minInterval) { _dropped++; return; }
        if (now - _perkWindowStart >= 1.0) { _perkWindowStart = now; _perkWindowCount = 0; }
        if (_perkWindowCount >= PerkBudget && priority < 5) { _dropped++; return; }

        Voice? voice = null;
        foreach (Voice v in _voices2D)
        {
            if (!IsPlaying(v.Player)) { voice = v; break; }
            if (v.Priority < priority
                && (voice is null || v.Priority < voice.Priority
                    || (v.Priority == voice.Priority && v.StartedAt < voice.StartedAt)))
                voice = v;
        }
        if (voice?.Player is not AudioStreamPlayer p) { _dropped++; return; }
        if (IsPlaying(p)) _stolen++;

        p.Stream = _perkStreams[(int)s];
        p.PitchScale = Math.Clamp(pitch * (1f + (NextRand() * 2f - 1f) * 0.04f), 0.5f, 2f);
        p.VolumeDb = db;
        p.Play();

        voice.Id = (Sfx)(-1);
        voice.Priority = priority;
        voice.StartedAt = now;
        _perkLast[(int)s] = now;
        _perkWindowCount++;
        PerkSoundsPlayed++;
    }

    private static float[] BuildPerk(PerkSound s)
    {
        const int rate = SfxSynth.Rate;
        float seconds = s switch
        {
            PerkSound.LastLight => 1.1f,
            PerkSound.Lighthouse => 0.9f,
            PerkSound.Surge => 0.6f,
            PerkSound.Pulse => 0.45f,
            PerkSound.Bubble => 0.22f,
            PerkSound.Coin => 0.3f,
            PerkSound.Stomp => 0.25f,
            _ => 0.16f,
        };
        float[] b = new float[(int)(seconds * rate)];
        var noise = new SfxSynth.NoiseGen(0x9E3Bu + (uint)s * 31u);

        switch (s)
        {
            case PerkSound.Chain:
                // A short dry crackle with a high glint: lightning, but small.
                Noise(b, noise, 0f, 0.12f, 2400f, 0.6f, 30f);
                Tone(b, 0f, 0.1f, 1800f, 1100f, true, 0.25f, 0.004f, 35f);
                break;
            case PerkSound.Bubble:
                // A rising "bloop".
                Tone(b, 0f, 0.2f, 320f, 900f, false, 0.8f, 0.006f, 14f);
                break;
            case PerkSound.Stomp:
                Tone(b, 0f, 0.22f, 150f, 60f, false, 1f, 0.004f, 18f);
                Noise(b, noise, 0f, 0.1f, 900f, 0.4f, 35f);
                break;
            case PerkSound.Heal:
                Tone(b, 0f, 0.15f, 1318.5f, 1318.5f, false, 0.6f, 0.004f, 22f);
                break;
            case PerkSound.Mote:
                Tone(b, 0f, 0.12f, 1567.98f, 1760f, false, 0.5f, 0.004f, 40f);
                break;
            case PerkSound.Coin:
                Tone(b, 0f, 0.12f, 1975.5f, 1975.5f, true, 0.4f, 0.004f, 20f);
                Tone(b, 0.07f, 0.2f, 2637f, 2637f, false, 0.4f, 0.004f, 16f);
                break;
            case PerkSound.LastLight:
                // A swell into a bright chord: you are not done yet.
                float[] chord = { 523.25f, 783.99f, 1046.5f };
                for (int k = 0; k < chord.Length; k++)
                    Tone(b, k * 0.04f, seconds - k * 0.04f, chord[k], chord[k], false, 0.45f, 0.12f, 3f);
                break;
            case PerkSound.Lighthouse:
                // Two soft low notes, a distant horn.
                Tone(b, 0f, 0.45f, 196f, 196f, false, 0.7f, 0.05f, 5f);
                Tone(b, 0.38f, 0.5f, 261.63f, 261.63f, false, 0.6f, 0.05f, 5f);
                break;
            case PerkSound.Pulse:
                Tone(b, 0f, 0.42f, 110f, 70f, false, 1f, 0.02f, 8f);
                Noise(b, noise, 0f, 0.3f, 600f, 0.3f, 10f);
                break;
            case PerkSound.Surge:
                Tone(b, 0f, 0.55f, 392f, 784f, false, 0.6f, 0.03f, 5f);
                Tone(b, 0.1f, 0.45f, 587.33f, 1174.66f, false, 0.35f, 0.03f, 6f);
                break;
            default:
                Noise(b, noise, 0f, 0.15f, 700f, 0.6f, 18f);
                break;
        }

        float max = 0f;
        foreach (float x in b) max = MathF.Max(max, MathF.Abs(x));
        if (max > 1e-6f) for (int i = 0; i < b.Length; i++) b[i] *= SfxSynth.Peak / max;
        return b;
    }
}
