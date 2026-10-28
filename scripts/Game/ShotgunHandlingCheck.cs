using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public sealed partial class ShotgunHandlingCheck : Node
{
    private Vector3 _wish;
    public override void _Ready() => _ = Run();
    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool condition, string label)
    {
        GD.Print($"SHOTGUN HANDLING {(condition ? "OK" : "FAIL")} {label}");
        if (!condition) throw new InvalidOperationException(label);
    }

    private async Task Run()
    {
        try
        {
            await Wait(.7f);
            Main main = Main.Instance!; main.SetProcess(false); main.Enemies.ClearField(999); main.Enemies.Quiet = true;
            Player player = main.Player; player.MoveOverride = () => _wish;
            CharacterRig rig = ReefCheckHelpers.Find<CharacterRig>(player)!;
            Require(rig.HoldsShotgun, "Nori uses the two-hand shotgun rig");
            foreach (bool upgraded in new[] { false, true })
            foreach (Vector3 direction in new[] { Vector3.Forward, Vector3.Up, Vector3.Down })
            {
                player.Teleport(new Vector3(0, 42, 0));
                _wish = direction with { Y = 0 };
                if (direction.Y > 0) Input.ActionPress("jump");
                if (direction.Y < 0) Input.ActionPress("slide");
                await Wait(.35f);
                Vector3 start = player.GlobalPosition;
                var target = new ExpeditionWeaponTarget();
                Targeting.Source = into => { into.Add(target); return 1; };
                var weapon = new WeaponRuntime(Catalog.WeaponById("reef_shotgun")!);
                if (upgraded) weapon.LevelUp(new WeaponUpgrade(Rarity.Common, new[] { (UpgradeStat.AttackSpeed, 19f) }));
                int shots = player.ShotgunRecoilBursts;
                for (int frame = 0; frame < 150; frame++)
                {
                    target.Position = player.CenterPosition + direction * 8;
                    main.Weapons.Tick(.0001f); Targeting.Refresh(); weapon.Tick(1f / 60, main.Weapons);
                    RequireFinite(player, rig);
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                }
                float progress = (player.GlobalPosition - start).Dot(direction);
                Require(progress > 8, $"{(upgraded ? "20x fire rate" : "baseline")} swimming {direction} progresses {progress:0.00}m while firing");
                Require(player.ShotgunRecoilBursts - shots >= (upgraded ? 15 : 2), "actual weapon cooldown emits sustained shots");
                Require(rig.ShotgunForward.Dot(direction) > .99f, "muzzle follows the actual 3D firing direction");
                weapon.Dispose(); Projectile.ClearLiveCombat(); Input.ActionRelease("jump"); Input.ActionRelease("slide");
            }
            _wish = Vector3.Right; player.ShotgunFeedback(Vector3.Forward); await Wait(.5f);
            Require(player.Velocity.X > 4 && player.ShotgunRecoilSpeed < .01f, "steering remains responsive and the kick fully decays");
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--shotgun-shot=")) await Capture(player, rig, arg["--shotgun-shot=".Length..]);
            Arena.UseBounds(Stages.Run[0]); player.Teleport(new Vector3(0, 2, 0)); await Wait(.5f);
            player.ShotgunFeedback(Vector3.Forward); await Wait(.03f);
            Require(!player.IsSwimming && player.ShotgunRecoilSpeed <= .35f && rig.ShotgunGripError < .02f,
                "land uses the same two-hand grip with a smaller bounded kick");
            GD.Print("SHOTGUN HANDLING PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("SHOTGUN HANDLING FAIL " + error); GetTree().Quit(1); }
        finally { Targeting.Source = null; Input.ActionRelease("jump"); Input.ActionRelease("slide"); }
    }

    private static void RequireFinite(Player player, CharacterRig rig)
    {
        if (!player.Velocity.IsFinite() || player.ShotgunRecoilSpeed > .801f || rig.ShotgunGripError > .02f)
            throw new InvalidOperationException($"invalid recoil/pose: kick={player.ShotgunRecoilSpeed} gripError={rig.ShotgunGripError}");
    }

    private async Task Capture(Player player, CharacterRig rig, string path)
    {
        _wish = Vector3.Zero; player.Teleport(new Vector3(0, 42, 0)); await Wait(.2f);
        player.PrepareShotgunMuzzle(Vector3.Forward);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 3.8f };
        GetTree().CurrentScene.AddChild(camera); camera.GlobalPosition = player.GlobalPosition + new Vector3(3, 2.2f, -3);
        camera.LookAt(player.CenterPosition); camera.MakeCurrent(); await Wait(.2f);
        Require(rig.ShotgunGripError < .02f, "both palms stay on the weapon grips in the captured pose");
        await Screenshot.Capture(GetTree(), this, path);
    }
}
