using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Audio;

public sealed partial class ReefWaterAudio : Node
{
    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[4];
    private AudioEffectLowPassFilter _filter = null!;
    private int _exits, _entries;
    private bool _initialized;
    public float SurfaceMix { get; private set; }
    public float MovementMix { get; private set; }

    private void InitializeAudio()
    {
        _initialized = true;
        AudioDirector.EnsureBuses();
        int bus = AudioServer.GetBusIndex("ReefWater");
        if (bus < 0)
        {
            AudioServer.AddBus(); bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, "ReefWater"); AudioServer.SetBusSend(bus, AudioSettings.SfxBus);
            AudioServer.AddBusEffect(bus, new AudioEffectLowPassFilter());
        }
        _filter = (AudioEffectLowPassFilter)AudioServer.GetBusEffect(bus, 0);
        for (int i = 0; i < _players.Length; i++)
        {
            _players[i] = new AudioStreamPlayer { Bus = "ReefWater", Stream = ReefAudioSynth.Stream((ReefAudioKind)i), VolumeDb = -80f };
            AddChild(_players[i]);
        }
    }

    public override void _Process(double delta)
    {
        if (Player.Instance is not { IsSwimming: true } player)
        { if (_initialized) foreach (AudioStreamPlayer voice in _players) voice.Stop(); return; }
        if (!_initialized) InitializeAudio();
        for (int i = 0; i < 3; i++) if (!_players[i].Playing) _players[i].Play();
        float depth = Mathf.Max(0f, WaterSurfaceHeight.At(player.GlobalPosition) - player.CenterPosition.Y);
        SurfaceMix = Mathf.Exp(-depth / 13f);
        MovementMix = player.IsBreaching ? 0f : Mathf.Clamp(player.Velocity.Length() / 6f, 0f, 1.7f);
        _players[0].VolumeDb = -31f + (1f - SurfaceMix) * 7f;
        _players[1].VolumeDb = -65f + SurfaceMix * 43f;
        _players[2].VolumeDb = MovementMix < 0.08f ? -80f : -39f + Mathf.Min(1.5f, MovementMix) * 11f;
        _players[2].PitchScale = 0.85f + MovementMix * 0.22f;
        _filter.CutoffHz = Mathf.Lerp(650f, 4200f, SurfaceMix);
        if (_exits != player.Breaches || _entries != player.WaterEntries)
        {
            _players[3].VolumeDb = -20f;
            _players[3].PitchScale = _entries != player.WaterEntries ? 0.85f : 1.15f;
            _players[3].Play();
            _exits = player.Breaches; _entries = player.WaterEntries;
        }
    }
}
