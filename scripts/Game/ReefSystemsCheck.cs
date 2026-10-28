using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Stats;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public sealed partial class ReefSystemsCheck : Node
{
    private readonly Player _player;
    private ReefLandmarks? _landmarks;
    private ReefFishSchools? _fish;
    private ReefWaterAudio? _audio;
    private SwimmerWake? _wake;
    private int _step, _reactions, _emitted;
    private float _time, _deepMix, _startX;
    private bool _passed = true;

    public ReefSystemsCheck(Player player) => _player = player;
    private void Check(bool value, string label)
    { _passed &= value; GD.Print($"REEF {(value ? "OK" : "FAIL")} {label}"); }

    public override void _PhysicsProcess(double delta)
    {
        _time += (float)delta;
        if (_step == 0 && _time > 0.7f)
        {
            _player.MoveOverride = static () => Vector3.Zero;
            _landmarks = ReefCheckHelpers.Find<ReefLandmarks>(GetTree().Root);
            _fish = ReefCheckHelpers.Find<ReefFishSchools>(GetTree().Root);
            _audio = ReefCheckHelpers.Find<ReefWaterAudio>(_player);
            _wake = ReefCheckHelpers.Find<SwimmerWake>(_player);
            Check(_landmarks is { WreckChestPlaced: true } && _landmarks.CaveCentres.Count == 1,
                "one winding cave and a registered gilded wreck chest exist");
            if (_landmarks is null || _fish is null || _audio is null || _wake is null)
            { Check(false, "all reef systems installed"); Finish(); return; }
            Check(ReefFoundations.SupportedStructures > 0, "ruins have footprint-sampled physical foundations");
            for (int i = 1; i < ReefCavePath.Points.Length; i++)
            {
                var query = PhysicsRayQueryParameters3D.Create(ReefCavePath.Points[i - 1], ReefCavePath.Points[i], 1u);
                Check(_player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0,
                    $"winding passage segment {i} is clear");
            }
            Vector3 flow = new(20f, 0f, -62f);
            flow.Y = UnderwaterTerrain.Height(flow) + 7f;
            float strength = ReefCurrents.VelocityAt(flow).Length();
            Check(strength > 2f && strength < _player.Stats.Get(Stat.MoveSpeed),
                "current is useful but weaker than ordinary swimming control");
            Check(ReefCurrents.VelocityAt(Vector3.Up * 60f) == Vector3.Zero, "current has a fair vertical escape");
            _player.Teleport(flow); _startX = flow.X;
            _step++;
        }
        else if (_step == 1 && _time > 1.3f)
        {
            Check(_player.GlobalPosition.X > _startX + 1f, "current physically carries the idle swimmer");
            _player.Teleport(_landmarks!.CaveCentres[0] + Vector3.Up * 1.2f); _step++;
        }
        else if (_step == 2 && _time > 1.8f)
        {
            Check(_landmarks!.CaveRewards == 1 && _landmarks.GuardsSpawned >= 2,
                "cave awards a small treasure once and has guarding risk");
            _step++;
        }
        else if (_step == 3 && _time > 2.2f)
        {
            Check(_landmarks!.CaveRewards == 1, "cave treasure cannot be farmed by standing still");
            _player.Teleport(_landmarks.WreckRewardPosition + Vector3.Up * 2f); _step++;
        }
        else if (_step == 4 && _time > 2.7f)
        {
            Check(_landmarks!.GuardsSpawned >= 3, "the wreck has its own guardian");
            _reactions = _fish!.TotalReactions;
            _player.Teleport(_fish.SchoolCentre(0)); _step++;
        }
        else if (_step == 5 && _time > 3.3f)
        {
            Check(_fish!.TotalReactions > _reactions && ReefFishSchools.FishCount == 144,
                "bounded fish schools react to the nearby swimmer");
            _emitted = _wake!.Emitted;
            _player.MoveOverride = static () => Vector3.Forward; _step++;
        }
        else if (_step == 6 && _time > 3.9f)
        {
            Check(_wake!.Emitted > _emitted && _audio!.MovementMix > 0.5f,
                "actual movement produces wake bubbles and movement audio");
            _deepMix = _audio!.SurfaceMix;
            _player.MoveOverride = static () => Vector3.Zero;
            _player.Teleport(Vector3.Up * (WaterSurfaceHeight.At(Vector3.Zero) - Player.BodyHeight * 0.5f - 1f)); _step++;
        }
        else if (_step == 7 && _time > 4.6f)
        {
            Check(_audio!.SurfaceMix > _deepMix + 0.3f && _audio.MovementMix < 0.1f,
                "near-surface waves replace the deep bed and idle bubbling falls quiet");
            int bus = AudioServer.GetBusIndex("ReefWater");
            Check(bus >= 0 && AudioServer.GetBusSend(bus).ToString() == AudioSettings.SfxBus,
                "all water audio respects the existing effects/master/mute buses");
            Check(System.Enum.GetValues<ReefAudioKind>().All(k => ReefAudioSynth.Stream(k).Data.Any(b => b != 0)),
                "all original water sound buffers contain audio");
            Finish();
        }
    }

    private void Finish()
    {
        GD.Print(_passed ? "REEF SYSTEMS PASS" : "REEF SYSTEMS FAIL");
        GetTree().Quit(_passed ? 0 : 1); _step = 99;
    }
}
