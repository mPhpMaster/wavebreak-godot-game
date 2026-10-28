using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Game;

public sealed partial class HookTargetsCheck : Node
{
    private readonly Player _player;
    public HookTargetsCheck(Player player) => _player = player;
    public override void _Ready() => _ = Run();
    private async Task Wait(float t) => await ToSignal(GetTree().CreateTimer(t, true, true), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool value, string label)
    {
        GD.Print($"HOOK TARGETS {(value ? "OK" : "FAIL")} {label}");
        if (!value) throw new InvalidOperationException(label);
    }
    private async Task Run()
    {
        try
        {
            await Wait(.7f);
            EnemyManager enemies = EnemyManager.Instance!; enemies.ClearField(999);
            EnemyManager.DevFrozen = true; enemies.Quiet = false;
            Enemy giant = enemies.SpawnAt(Catalog.EnemyById("city_bellwarden")!, new Vector3(0, 0, -48))!;
            _player.MoveOverride = static () => Vector3.Zero;
            _player.Teleport(new Vector3(0, 11, -32)); await Wait(.05f);
            Vector3 from = _player.CenterPosition;
            bool hit = GrappleAnchors.RayTarget(_player, from, (giant.GlobalPosition + Vector3.Up * 13 - from).Normalized(),
                from, out Vector3 anchor, out CollisionObject3D? target);
            GD.Print($"HOOK TARGETS probe hit={hit} from={from} anchor={anchor} target={target?.Name} giant={giant.GlobalPosition} layer={giant.CollisionLayer}");
            Require(hit && target == giant, "ray accepts the giant body");
            Input.ActionPress("city_hook_left"); Require(_player.AttachHook(anchor), "controller binds a giant anchor");
            Vector3 local = giant.ToLocal(anchor);
            giant.GlobalPosition += Vector3.Right * 3;
            await Wait(.05f);
            Require(_player.LeftHookAttached && _player.LeftHookAnchor.DistanceTo(giant.ToGlobal(local)) < .05f,
                "tether follows the actual moving body-local point");
            giant.Spawn(giant.GlobalPosition); await Wait(.05f);
            Require(!_player.Grappling, "reusing a pooled giant invalidates its previous attachment");
            Input.ActionRelease("city_hook_left");
            _player.Teleport(new Vector3(0, 5, -Arena.Current.InRadius + 10)); await Wait(.05f);
            from = _player.CenterPosition;
            Require(GrappleAnchors.RayTarget(_player, from, Vector3.Forward, from, out anchor, out target)
                && target!.IsInGroup("CityBoundary"), "the visible city boundary wall accepts hooks");
            Input.ActionPress("city_hook_right"); Require(_player.AttachHook(HookSide.Right, anchor), "boundary attachment validates");
            Input.ActionRelease("city_hook_right");
            GD.Print("HOOK TARGETS PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("HOOK TARGETS FAIL " + error); GetTree().Quit(1); }
        finally { Input.ActionRelease("city_hook_left"); Input.ActionRelease("city_hook_right"); }
    }
}
