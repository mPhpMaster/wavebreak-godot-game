using System;
using Godot;

namespace Wavebreak.Audio;

/// <summary>Original short gas burst and metallic reel chirp, synthesized without samples.</summary>
public sealed partial class HookLaunchAudio : AudioStreamPlayer3D
{
    private static AudioStreamWav? _stream;
    public override void _Ready()
    {
        AudioDirector.EnsureBuses(); Bus = AudioSettings.SfxBus;
        _stream ??= Build(); Stream = _stream; VolumeDb = -16;
        MaxDistance = 70;
    }
    private static AudioStreamWav Build()
    {
        const int rate = 22050;
        const int count = 4410;
        var data = new byte[count * 2]; uint noise = 17431; float filtered = 0;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            noise = noise * 1664525 + 1013904223;
            filtered += (((noise >> 8) / 8388608f - 1) - filtered) * .24f;
            float envelope = Mathf.Min(1, t / .006f) * Mathf.Exp(-t * 24) * Mathf.Clamp((.2f - t) / .02f, 0, 1);
            float chirp = Mathf.Sin(Mathf.Tau * (950 * t - 1400 * t * t));
            short sample = (short)(Mathf.Clamp((filtered * .75f + chirp * .18f) * envelope, -.7f, .7f) * short.MaxValue);
            data[i * 2] = (byte)sample; data[i * 2 + 1] = (byte)(sample >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
    }
}
