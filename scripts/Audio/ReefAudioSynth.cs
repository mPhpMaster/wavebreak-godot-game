using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Audio;

/// <summary>Original quiet water loops made from filtered noise and bubble chirps.</summary>
public static class ReefAudioSynth
{
    private const int Rate = 22050;
    private static readonly Dictionary<ReefAudioKind, AudioStreamWav> Cache = new();

    public static AudioStreamWav Stream(ReefAudioKind kind)
    {
        if (Cache.TryGetValue(kind, out AudioStreamWav? found)) return found;
        float duration = kind == ReefAudioKind.Splash ? 1.3f : 12f;
        int count = (int)(Rate * duration);
        byte[] data = new byte[count * 2];
        var random = new Random(74219 + (int)kind);
        float low = 0f, slow = 0f, envelope = 0f, target = 0f;
        int until = 0;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)random.NextDouble() * 2f - 1f;
            low += (noise - low) * 0.07f;
            slow += (noise - slow) * 0.006f;
            if (--until <= 0) { target = 0.12f + (float)random.NextDouble() * 0.75f; until = random.Next(Rate / 8, Rate * 3 / 4); }
            envelope += (target - envelope) * 0.0008f;
            float swish = low * envelope + (low - slow) * 0.25f;
            float sample = kind switch
            {
                ReefAudioKind.Deep => slow * 0.38f + low * 0.08f,
                ReefAudioKind.Surface => low * (0.22f + 0.13f * MathF.Sin(t * Mathf.Pi * 0.5f)) + noise * 0.025f,
                ReefAudioKind.Bubbles => swish * 0.65f,
                _ => (low * 0.8f + noise * 0.1f) * (1f - MathF.Exp(-t * 70f)) * MathF.Exp(-t * 4f),
            };
            float edge = MathF.Min(1f, MathF.Min(t * 30f, (duration - t) * 30f));
            short pcm = (short)(Math.Clamp(sample * edge, -0.95f, 0.95f) * 32767f);
            data[i * 2] = (byte)(pcm & 255); data[i * 2 + 1] = (byte)((pcm >> 8) & 255);
        }
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Data = data,
            LoopMode = kind == ReefAudioKind.Splash ? AudioStreamWav.LoopModeEnum.Disabled : AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0, LoopEnd = count,
        };
        Cache[kind] = stream;
        return stream;
    }
}
