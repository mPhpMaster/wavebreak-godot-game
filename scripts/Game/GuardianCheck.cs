using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public sealed partial class GuardianCheck : Node
{
    private readonly Player _player;
    private readonly bool _boss;
    public GuardianCheck(Player player, bool boss = false) { _player = player; _boss = boss; }
    public override void _Ready() => _ = Run();
    private async Task Wait(float time) => await ToSignal(GetTree().CreateTimer(time, true, true), SceneTreeTimer.SignalName.Timeout);
    private void Require(bool value, string label)
    {
        GD.Print($"{(_boss ? "LEVIATHAN" : "GUARDIAN")} {(value ? "OK" : "FAIL")} {label}");
        if (!value) throw new InvalidOperationException(label);
    }
    private static void DumpMeshes(Node root)
    {
        if (root is MeshInstance3D mesh && mesh.Mesh is not null)
            GD.Print($"CREATURE mesh={mesh.Name} visible={mesh.IsVisibleInTree()} bounds={mesh.GlobalTransform * mesh.GetAabb()}");
        foreach (Node child in root.GetChildren()) DumpMeshes(child);
    }
    private async Task Run()
    {
        try
        {
            await Wait(.7f);
            Main.Instance!.SetProcess(false);
            EnemyManager enemies = EnemyManager.Instance!; enemies.ClearField(999); enemies.Quiet = false;
            EnemyManager.DevFrozen = true;
            _player.MoveOverride = static () => Vector3.Zero; _player.Teleport(new Vector3(0, _boss ? 42 : 10, 0));
            Enemy guardian;
            if (_boss)
            {
                Require(Main.Instance!.Stages.DevBossNow(), "real first Reef phase requests its authored boss before generic unlock time");
                guardian = null!;
                foreach (Enemy live in enemies.Alive) if (live.Def?.Id == ReefLeviathanModels.Id) guardian = live;
            }
            else guardian = enemies.SpawnAt(Catalog.EnemyById(ReefGuardianModels.Id)!, new Vector3(0, 10, -9))!;
            Require(guardian is { IsAlive: true }, _boss ? "Leviathan spawns through the real boss pool" : "new cave guardian spawns through the real enemy pool");
            CharacterRig rig = ReefCheckHelpers.Find<CharacterRig>(guardian)!;
            Require(rig.Spec.IsImported && rig.Spec.Id == (_boss ? "boss:wertt-leviathan" : "guardian:quaternius-shark"), _boss ? "licensed Leviathan replaces the old reef boss model" : "licensed shark replaces the procedural guardian model");
            AnimationPlayer animation = ReefCheckHelpers.Find<AnimationPlayer>(guardian)!;
            Require(animation is not null && rig.Spec.Clips.TryGetValue(AnimSlot.Walk, out string? swim) && animation.HasAnimation(swim), "import contains a playable swimming clip");
            string before = rig.DebugClip; await Wait(.35f);
            Require(before != rig.DebugClip && animation!.GetAnimation(animation.CurrentAnimation).LoopMode != Animation.LoopModeEnum.None,
                "swimming animation advances and loops");
            Require(rig.Spec.SceneScale > 0 && float.IsFinite(rig.Spec.SceneScale), "import is normalized to gameplay dimensions");
            if (_boss)
            {
                Require(guardian.Def!.Height >= 32 && guardian.Def.Radius >= 15 && guardian.Def.AttackRange >= 18,
                    "huge Leviathan uses enlarged model, collision, hit radius and attack reach");
                Require(guardian.GlobalPosition.Y > ReefCavePath.Floor(guardian.GlobalPosition)
                    && guardian.GlobalPosition.Y + guardian.Def.Height < WaterSurfaceHeight.At(guardian.GlobalPosition),
                    "the real boss spawns fully submerged above the seabed");
                LeviathanVerification.Combat(_player, guardian);
                float pursuitDistance = guardian.AimPoint.DistanceTo(_player.CenterPosition);
                EnemyManager.DevFrozen = false; await Wait(.8f); EnemyManager.DevFrozen = true;
                Require(guardian.GlobalPosition.IsFinite() && guardian.AimPoint.DistanceTo(_player.CenterPosition) <= pursuitDistance + .5f
                    && guardian.GlobalPosition.Y + guardian.Def.Height <= WaterSurfaceHeight.At(guardian.GlobalPosition) + .1f,
                    "huge boss navigates toward the swimmer while remaining submerged");
            }
            GD.Print($"CREATURE normalized_bounds={rig.Spec.SceneSourceBounds.Size * rig.Spec.SceneScale} scale={rig.Spec.SceneScale} offset={rig.Spec.SceneOffset}");
            GD.Print("GUARDIAN animation=" + rig.DebugClip);
            DumpMeshes(rig);
            PlayerCamera.Instance!.FaceDirection(Vector3.Forward); PlayerCamera.Instance.SetPitchDegrees(0); PlayerCamera.Instance.SnapToTarget();
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--guardian-shot=") || arg.StartsWith("--leviathan-shot="))
                {
                    var portrait = new Camera3D { Current = true, Position = guardian.GlobalPosition + new Vector3(_boss ? 45 : 8, _boss ? 25 : 4, _boss ? 52 : 10) };
                    portrait.Projection = Camera3D.ProjectionType.Orthogonal;
                    portrait.Size = _boss ? 55 : 10;
                    GetTree().CurrentScene.AddChild(portrait);
                    portrait.MakeCurrent();
                    GD.Print($"CREATURE camera={GetViewport().GetCamera3D()?.Name} at={portrait.GlobalPosition} fov={portrait.Fov}");
                    if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--creature-static") >= 0)
                    {
                        rig.SetProcess(false); animation!.Stop();
                        foreach (Node node in rig.FindChildren("*", "MeshInstance3D", true, false))
                            if (node is MeshInstance3D mesh)
                                for (int shape = 0; shape < mesh.GetBlendShapeCount(); shape++) mesh.SetBlendShapeValue(shape, 0);
                    }
                    portrait.LookAt(guardian.GlobalPosition + Vector3.Up * (_boss ? 12 : 1));
                    await Wait(3);
                    await Screenshot.Capture(GetTree(), this, arg[(arg.IndexOf('=') + 1)..]);
                }
            GD.Print(_boss ? "LEVIATHAN PASS" : "GUARDIAN PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print((_boss ? "LEVIATHAN FAIL " : "GUARDIAN FAIL ") + error); GetTree().Quit(1); }
    }
}





