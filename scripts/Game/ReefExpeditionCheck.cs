using System;
using System.IO;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>Real physics-space and controller checks, only installed by an explicit test flag.</summary>
public sealed partial class ReefExpeditionCheck : Node
{
    private readonly Player _player;
    private bool _passed = true;
    private float _time;
    private int _step, _entries;
    private Pickup? _coin;
    public ReefExpeditionCheck(Player player) => _player = player;
    private void Check(bool value, string label) { _passed &= value; GD.Print($"EXPEDITION {(value ? "OK" : "FAIL")} {label}"); }

    public override void _PhysicsProcess(double delta)
    {
        _time += (float)delta;
        if (_step == 0 && _time > 1)
        {
            _player.MoveOverride = static () => Vector3.Zero;
            if (EnemyManager.Instance is { } enemies) { enemies.Quiet = true; enemies.ClearField(999); }
            Geometry(); Harvest();
            ReefAudioSynth.Stream(ReefAudioKind.Bubbles).SaveToWav(Path.Combine(Path.GetTempPath(), "wavebreak-reef-water-strokes.wav"));
            ReefAudioSynth.Stream(ReefAudioKind.Splash).SaveToWav(Path.Combine(Path.GetTempPath(), "wavebreak-reef-water-entry.wav"));
            _entries = _player.WaterEntries;
            _player.Teleport(Vector3.Up * (WaterSurfaceHeight.BaseHeight + 24));
            _step++;
        }
        else if (_step == 1 && _time > 1.6f)
        {
            Check(_player.IsBreaching && _player.Velocity.Y < -3 && _player.GlobalPosition.Y > WaterSurfaceHeight.BaseHeight,
                "skyfall uses airborne gravity above water"); _step++;
        }
        else if (_step == 2 && _time > 4)
        {
            Check(!_player.IsBreaching && _player.IsSwimming && _player.WaterEntries == _entries + 1 && _player.IsAlive,
                "skyfall enters once and transitions safely to swimming");
            Vector3 chamber = ReefCavePath.Points[56];
            _player.Teleport(chamber);
            _step++;
        }
        else if (_step == 3 && _time > 4.8f)
        {
            Check(_player.GlobalPosition.DistanceTo(ReefCavePath.Points[56]) < 1,
                "underground swim and fall recovery preserve cave position");
            Vector3 exit = ReefCavePath.Exit;
            _player.Teleport(exit + Vector3.Down * 8);
            Input.ActionPress("jump"); _step++;
        }
        else if (_step == 4 && _time > 7)
        {
            Input.ActionRelease("jump");
            Check(_player.GlobalPosition.Y > ReefCavePath.Exit.Y + 2, "player swims upward through the genuine interior exit");
            _player.Teleport(Vector3.Up * 10);
            Vector3 at = ReefPlacement.Ledges[1] + new Vector3(2, 5, 0);
            _coin = Pickup.Spawn(PickupKind.Gold, at, 4);
            _step++;
        }
        else if (_step == 5 && _time > 9)
        {
            Check(_coin is { IsPooled: false } && Mathf.Abs(_coin.GlobalPosition.Y - ReefPlacement.Ledges[1].Y) < 0.15f,
                "gold falls and rests on supported elevated rock instead of Y=0");
            Check(ResourceLoader.Exists("res://assets/kenney/reef-pickups/jewel.glb")
                && ResourceLoader.Exists("res://assets/kenney/reef-pickups/coin-gold.glb"), "licensed XP/gold scenes import successfully");
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-terrain-cache") >= 0)
            { GC.Collect(); GC.WaitForPendingFinalizers(); }
            GD.Print(_passed ? "EXPEDITION PASS" : "EXPEDITION FAIL"); GetTree().Quit(_passed ? 0 : 1); _step++;
        }
    }

    private void Geometry()
    {
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-terrain-cache") >= 0) TerrainCacheCheck.Run(Check);
        var space = _player.GetWorld3D().DirectSpaceState;
        bool clear = true; float length = 0;
        var sphere = new SphereShape3D { Radius = 0.9f };
        for (int i = 1; i < ReefCavePath.Points.Length; i++)
        {
            Vector3 a = ReefCavePath.Points[i - 1], b = ReefCavePath.Points[i]; length += a.DistanceTo(b);
            var query = new PhysicsShapeQueryParameters3D { Shape = sphere, Transform = new Transform3D(Basis.Identity, a),
                Motion = b - a, CollisionMask = 1, Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() } };
            float[] travel = space.CastMotion(query);
            if (travel.Length > 0 && travel[0] < 0.999f) { clear = false; GD.Print($"EXPEDITION blocked cave segment {i} fraction={travel[0]}"); }
        }
        Check(clear && length > 230 && ReefSites.Caves.Length == 1, $"one continuous winding cave has body clearance along {length:0} metres");
        var entrance = new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D { Radius = .6f, Height = 1.8f },
            Transform = new Transform3D(Basis.Identity, ReefCavePath.Entrance + Vector3.Left * 12f),
            Motion = Vector3.Right * 12f, CollisionMask = 1,
            Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() },
        };
        float[] mouth = space.CastMotion(entrance);
        Check(mouth.Length > 0 && mouth[0] > .999f && ReefPortalMasonry.GroundContacts >= 5,
            "grounded entrance footings retain full player capsule clearance from open water");
        Check(ReefCavePath.Exit.DistanceTo(ReefCavePath.Entrance) > 90 && new Vector2(ReefCavePath.Exit.X, ReefCavePath.Exit.Z).Length() < 40,
            "cliff entrance and interior seabed exit are separate");
        bool support = true;
        foreach (Vector3 ledge in ReefPlacement.Ledges)
            support &= Mathf.Abs(ReefPlacement.SupportBelow(_player, ledge + Vector3.Up) - ledge.Y) < 0.1f;
        Check(support, "all elevated placements have real colliding support");
        bool marine = true;
        foreach (string id in Stages.Lumenreef.EnemyIds)
            if (Catalog.EnemyById(id) is { } def) marine &= EnemyVisuals.SpecFor(def).Id.StartsWith("reef:", StringComparison.Ordinal);
        ModelSpec boss = EnemyVisuals.SpecFor(Catalog.EnemyById(Stages.Lumenreef.BossId)!);
        marine &= boss.Id == "boss:wertt-leviathan" && boss.IsImported && boss.NativeSwimmingClip;
        Check(marine, "reef roster uses marine overrides and the licensed animated Leviathan boss");
    }

    private void Harvest()
    {
        if (ReefHarvest.Instance is not { Count: > 35 } reef) { Check(false, "resource field exists"); return; }
        ReefHarvestDef def = reef.Definitions[0];
        Check(!reef.TryHarvest(-1, def.Position) && !reef.TryHarvest(0, def.Position + Vector3.Up * 30), "harvest rejects invalid IDs and distant players");
        Check(reef.TryHarvest(0, def.Position + Vector3.Up) && !reef.TryHarvest(0, def.Position + Vector3.Up)
            && Array.IndexOf(reef.SpentIds, 0) >= 0, "resource grants once and preserves depletion state");
        bool crystal = false, plant = false, cave = false, elevated = false, wall = false;
        foreach (ReefHarvestDef item in reef.Definitions)
        { crystal |= item.Crystal; plant |= !item.Crystal; cave |= item.Position.Y < -20; elevated |= item.Position.Y > 20; wall |= item.Normal.Y < 0.5f; }
        Check(crystal && plant && cave && elevated && wall, "crystals and useful plants span floor, actual cliff walls, ledges and cave");
    }

    public override void _ExitTree() => Input.ActionRelease("jump");
}

