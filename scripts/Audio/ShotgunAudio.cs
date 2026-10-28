using Godot;

namespace Wavebreak.Audio;

public sealed partial class ShotgunAudio : AudioStreamPlayer3D
{
    private static AudioStreamWav? _sample;
    public override void _Ready()
    {
        AudioDirector.EnsureBuses(); Bus = AudioSettings.SfxBus;
        _sample ??= Build(); Stream = _sample; VolumeDb = -13; MaxDistance = 65;
    }
    private static AudioStreamWav Build()
    {
        const int rate = 22050, count = 7056;
        byte[] data = new byte[count * 2]; uint seed = 81923; float filtered = 0;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            seed = seed * 1664525 + 1013904223;
            filtered += (((seed >> 8) / 8388608f - 1) - filtered) * .18f;
            float envelope = Mathf.Min(1, t / .004f) * Mathf.Exp(-t * 18) * Mathf.Clamp((.32f - t) / .025f, 0, 1);
            float bass = Mathf.Sin(Mathf.Tau * (120 * t - 85 * t * t));
            short sample = (short)(Mathf.Clamp((bass * .42f + filtered * .8f) * envelope, -.8f, .8f) * short.MaxValue);
            data[i * 2] = (byte)sample; data[i * 2 + 1] = (byte)(sample >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
    }
}
