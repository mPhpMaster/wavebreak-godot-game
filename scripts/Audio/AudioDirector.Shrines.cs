using System;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Audio;

/// <summary>
/// Map shrine sounds, a partial like <see cref="AudioDirector"/>'s chest sounds
/// and built with the same synth helpers and voice pool: a rising chime when a
/// charge ring fills, a bell for a blessing, a horn for a challenge, a fanfare
/// or a fall for its end, a low drone for a curse, and the refusal blip.
/// </summary>
public sealed partial class AudioDirector
{
    private enum ShrineSound { Charged, Blessed, ChallengeStart, ChallengeWon, ChallengeFailed, Cursed, Denied }

    private static AudioStreamWav[]? _shrineStreams;
    private readonly double[] _shrineLast = new double[7];

    private static (float Db, int Priority, float MinInterval) ShrineSpec(ShrineSound s) => s switch
    {
        ShrineSound.Charged => (-9f, 6, 0.2f),
        ShrineSound.Blessed => (-10f, 6, 0.15f),
        ShrineSound.ChallengeStart => (-8f, 7, 0.3f),
        ShrineSound.ChallengeWon => (-7f, 7, 0.3f),
        ShrineSound.ChallengeFailed => (-9f, 7, 0.3f),
        ShrineSound.Cursed => (-8f, 6, 0.3f),
        _ => (-13f, 5, 0.25f),
    };

    private void SubscribeShrineSounds()
    {
        if (_shrineStreams is null)
        {
            _shrineStreams = new AudioStreamWav[7];
            for (int i = 0; i < _shrineStreams.Length; i++)
                _shrineStreams[i] = SfxSynth.ToStream(BuildShrine((ShrineSound)i));
        }
        EventBus.Subscribe<MapShrineUsed>(OnMapShrineUsed);
    }

    private void UnsubscribeShrineSounds() => EventBus.Unsubscribe<MapShrineUsed>(OnMapShrineUsed);

    private void OnMapShrineUsed(MapShrineUsed e)
    {
        ShrineSound s = e.Outcome switch
        {
            ShrineOutcome.Charged => ShrineSound.Charged,
            ShrineOutcome.Blessed => ShrineSound.Blessed,
            ShrineOutcome.ChallengeStarted => ShrineSound.ChallengeStart,
            ShrineOutcome.ChallengeWon => ShrineSound.ChallengeWon,
            ShrineOutcome.ChallengeFailed => ShrineSound.ChallengeFailed,
            ShrineOutcome.Cursed => ShrineSound.Cursed,
            _ => ShrineSound.Denied,
        };
        PlayShrine(s);
    }

    private void PlayShrine(ShrineSound s)
    {
        if (_shrineStreams is null) return;
        var (db, priority, minInterval) = ShrineSpec(s);
        double now = Now();
        if (now - _shrineLast[(int)s] < minInterval) { _dropped++; return; }

        Voice? voice = null;
        foreach (Voice v in _voices2D)
        {
            if (!IsPlaying(v.Player)) { voice = v; break; }
            if (v.Priority <= priority
                && (voice is null || v.Priority < voice.Priority
                    || (v.Priority == voice.Priority && v.StartedAt < voice.StartedAt)))
                voice = v;
        }
        if (voice?.Player is not AudioStreamPlayer p) { _dropped++; return; }
        if (IsPlaying(p)) _stolen++;

        p.Stream = _shrineStreams[(int)s];
        p.PitchScale = 1f;
        p.VolumeDb = db;
        p.Play();

        voice.Id = (Sfx)(-1);
        voice.Priority = priority;
        voice.StartedAt = now;
        _shrineLast[(int)s] = now;
    }

    private static float[] BuildShrine(ShrineSound s)
    {
        const int rate = SfxSynth.Rate;
        float seconds = s switch
        {
            ShrineSound.Charged => 1.1f,
            ShrineSound.Blessed => 0.9f,
            ShrineSound.ChallengeStart => 0.9f,
            ShrineSound.ChallengeWon => 1.3f,
            ShrineSound.ChallengeFailed => 0.9f,
            ShrineSound.Cursed => 1.2f,
            _ => 0.26f,
        };
        float[] b = new float[(int)(seconds * rate)];
        var noise = new SfxSynth.NoiseGen(0x5A1E5u + (uint)s);

        switch (s)
        {
            case ShrineSound.Charged:
                // A quick rising run into a held fifth: "full".
                float[] run = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f };
                for (int k = 0; k < run.Length; k++)
                    Tone(b, k * 0.05f, seconds - k * 0.05f, run[k], run[k], false, 0.42f, 0.005f, 3.5f);
                Noise(b, noise, 0f, 0.3f, 5000f, 0.08f, 8f);
                break;
            case ShrineSound.Blessed:
                Tone(b, 0f, 0.9f, 880f, 880f, false, 0.6f, 0.004f, 4.5f);
                Tone(b, 0.07f, 0.8f, 1318.5f, 1318.5f, false, 0.45f, 0.004f, 5f);
                Tone(b, 0f, 0.9f, 1760f, 1760f, true, 0.06f, 0.004f, 7f);
                break;
            case ShrineSound.ChallengeStart:
                // Two-note horn, low then a fourth up.
                Tone(b, 0f, 0.4f, 196f, 200f, true, 0.8f, 0.03f, 2.5f);
                Tone(b, 0.3f, 0.6f, 261.63f, 264f, true, 0.85f, 0.03f, 2.2f);
                Noise(b, noise, 0f, 0.12f, 600f, 0.3f, 25f);
                break;
            case ShrineSound.ChallengeWon:
                float[] fan = { 523.25f, 659.25f, 783.99f };
                for (int k = 0; k < fan.Length; k++)
                    Tone(b, k * 0.1f, 0.25f, fan[k], fan[k], true, 0.55f, 0.006f, 6f);
                Tone(b, 0.3f, 1.0f, 1046.5f, 1046.5f, false, 0.6f, 0.006f, 2.8f);
                Tone(b, 0.3f, 1.0f, 783.99f, 783.99f, false, 0.35f, 0.006f, 2.8f);
                break;
            case ShrineSound.ChallengeFailed:
                Tone(b, 0f, 0.3f, 392f, 370f, true, 0.7f, 0.01f, 6f);
                Tone(b, 0.25f, 0.6f, 293.66f, 260f, true, 0.7f, 0.01f, 3.5f);
                break;
            case ShrineSound.Cursed:
                // A low, slightly sour pair that swells in: something agreed to.
                Tone(b, 0f, seconds, 110f, 98f, true, 0.7f, 0.25f, 1.8f);
                Tone(b, 0f, seconds, 155.56f, 146.83f, false, 0.45f, 0.3f, 1.8f);
                Noise(b, noise, 0f, seconds, 300f, 0.25f, 2.5f);
                break;
            default:
                Tone(b, 0f, 0.11f, 220f, 210f, true, 0.8f, 0.005f, 14f);
                Tone(b, 0.12f, 0.14f, 165f, 150f, true, 0.8f, 0.005f, 12f);
                break;
        }

        float a = 1f - MathF.Exp(-MathF.Tau * 4200f / rate);
        float y = 0f, peak = 0f;
        for (int i = 0; i < b.Length; i++) { y += a * (b[i] - y); b[i] = y; peak = MathF.Max(peak, MathF.Abs(y)); }
        if (peak > 1e-6f) for (int i = 0; i < b.Length; i++) b[i] *= SfxSynth.Peak / peak;
        return b;
    }
}
