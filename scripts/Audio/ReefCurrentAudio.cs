using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Audio;

/// <summary>
/// A soft rushing loop for the nearest water current: placed at the closest
/// point of its axis, louder while it pushes, and loudest inside it. Original
/// sound, made from filtered noise like the rest of the reef ambience, and on
/// the reef water bus (so it follows the effects volume and mute).
/// </summary>
public sealed partial class ReefCurrentAudio : Node3D
{
    private const int Rate = 22050;
    private static AudioStreamWav? _stream;
    private readonly ReefCurrentField _field;
    private AudioStreamPlayer3D _voice = null!;

    /// <summary>Loudness 0..1 the loop was set to last frame (the self-check reads it).</summary>
    public float Level { get; private set; }

    public ReefCurrentAudio(ReefCurrentField field) => _field = field;

    public override void _Ready()
    {
        Name = "ReefCurrentAudio";
        AudioDirector.EnsureBuses();
        _voice = new AudioStreamPlayer3D
        {
            Stream = Stream(), VolumeDb = -80f, UnitSize = 14f, MaxDistance = 90f,
            Bus = AudioServer.GetBusIndex("ReefWater") >= 0 ? "ReefWater" : AudioSettings.SfxBus,
            AttenuationFilterCutoffHz = 3000f,
        };
        AddChild(_voice);
    }

    public override void _Process(double delta)
    {
        Level = 0f;
        if (Player.Instance is not { IsSwimming: true } player) { if (_voice.Playing) _voice.Stop(); return; }
        Vector3 ear = player.CenterPosition;
        ReefCurrentFlow? nearest = null;
        Vector3 nearestPoint = ear;
        float best = float.MaxValue;
        foreach (ReefCurrentFlow flow in _field.Live)
        {
            Vector3 line = flow.To - flow.From;
            float s = Mathf.Clamp((ear - flow.From).Dot(line) / Mathf.Max(0.01f, line.LengthSquared()), 0f, 1f);
            Vector3 point = flow.From + line * s;
            float d = point.DistanceTo(ear);
            if (d < best) { best = d; nearest = flow; nearestPoint = point; }
        }
        if (nearest is null) { if (_voice.Playing) _voice.Stop(); return; }
        float clock = _field.Clock;
        Level = Mathf.Clamp(0.3f * nearest.Visibility(clock) + 0.7f * nearest.Push(clock), 0f, 1f);
        if (_voice.Bus != "ReefWater" && AudioServer.GetBusIndex("ReefWater") >= 0) _voice.Bus = "ReefWater";
        // Inside the flow the sound sits on the swimmer, not on the axis a few metres off.
        _voice.GlobalPosition = best < nearest.Radius ? ear : nearestPoint;
        _voice.VolumeDb = Level < 0.02f ? -80f : Mathf.LinearToDb(Level) - 14f;
        _voice.PitchScale = 0.85f + 0.25f * nearest.Push(clock);
        if (!_voice.Playing) _voice.Play();
    }

    /// <summary>Eight seconds of rushing water: band-limited noise with a slow swell.</summary>
    public static AudioStreamWav Stream()
    {
        if (_stream is not null) return _stream;
        const float duration = 8f;
        int count = (int)(Rate * duration);
        byte[] data = new byte[count * 2];
        var random = new Random(51703);
        float low = 0f, lower = 0f, band = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)random.NextDouble() * 2f - 1f;
            low += (noise - low) * 0.18f;
            lower += (noise - lower) * 0.02f;
            band = low - lower;
            // Whole cycles of each swell across the loop, so the seam is silent.
            float swell = 0.65f + 0.2f * MathF.Sin(t * Mathf.Tau / duration * 3f) + 0.15f * MathF.Sin(t * Mathf.Tau / duration * 7f);
            float sample = (band * 0.55f + lower * 0.35f) * swell;
            float edge = MathF.Min(1f, MathF.Min(t * 40f, (duration - t) * 40f));
            short pcm = (short)(Math.Clamp(sample * edge, -0.95f, 0.95f) * 32767f);
            data[i * 2] = (byte)(pcm & 255); data[i * 2 + 1] = (byte)((pcm >> 8) & 255);
        }
        _stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = count,
        };
        return _stream;
    }
}
