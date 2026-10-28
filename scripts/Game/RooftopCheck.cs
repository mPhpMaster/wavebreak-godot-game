using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>Real controller acquisition plus isolated physical roof/occlusion checks.</summary>
public sealed partial class RooftopCheck : Node
{
    private readonly Player _player;
    public RooftopCheck(Player player) => _player = player;
    public override void _Ready() => _ = Run();
    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool valid, string label)
    {
        GD.Print($"ROOFTOP {(valid ? "OK" : "FAIL")} {label}");
        if (!valid) throw new InvalidOperationException(label);
    }
    private async Task Run()
    {
        try
        {
            await Wait(.7f);
            EnemyManager.Instance!.Quiet = true; EnemyManager.Instance.ClearField(999);
            _player.MoveOverride = static () => Vector3.Zero;
            Require(CityRooftops.Count > 240, "real city registers every building roof");
            _player.Teleport(Vector3.Up * .2f);
            PlayerCamera.Instance!.FaceDirection(Vector3.Forward);
            PlayerCamera.Instance.SetPitchDegrees(-60);
            Input.ActionPress("city_hook_left"); await Wait(.03f);
            Require(_player.LeftHookFiring && !_player.LeftHookAttached
                && _player.HookTip(HookSide.Left).DistanceTo(_player.HookOrigin(HookSide.Left)) > .1f
                && _player.HookTip(HookSide.Left).DistanceTo(_player.LeftHookAnchor) > .1f,
                "launch tip travels visibly before the tether attaches");
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--roof-shot=")) await Screenshot.Capture(GetTree(), this, arg["--roof-shot=".Length..]);
            await Wait(.14f);
            Require(_player.LeftHookAttached && _player.LeftHookAnchor.Y >= 12.9f, "low camera aim acquires a rooftop, not a low wall");
            Vector3 left = _player.LeftHookAnchor;
            PlayerCamera.Instance.SetPitchDegrees(75);
            Input.ActionPress("city_hook_right"); await Wait(.16f);
            Require(_player.RightHookAttached && _player.RightHookAnchor.Y >= 19.9f, "sky aim acquires the independent right rooftop");
            Require(_player.LeftHookAnchor == left && left.DistanceTo(_player.RightHookAnchor) > 10, "existing anchor remains stable while the other hook acquires");
            Input.ActionRelease("city_hook_left"); await Wait(.05f);
            Require(!_player.LeftHookAttached && _player.RightHookAttached, "independent release preserves the other roof");
            Input.ActionRelease("city_hook_right"); await Wait(.05f);

            var fixture = new Node3D(); GetTree().CurrentScene.AddChild(fixture);
            CityRooftops.Begin(fixture);
            Vector3 origin = new(1000, 2, 1000);
            AddRoof(fixture, origin + new Vector3(-20, -2, -32), 20);
            AddRoof(fixture, origin + new Vector3(-20, -2, -32), 26);
            await Wait(.05f);
            Require(CityRooftops.Find(_player, origin, Vector3.Forward, HookSide.Left, out Vector3 higher)
                && higher.Y > 25, "height preference selects a similarly near taller roof");
            Require(CityRooftops.LastVisibilityQueries <= CityRooftops.VisibilityBudget, "selection obeys the visibility-query budget");
            Require(!CityRooftops.Find(_player, origin + Vector3.Right * 200, Vector3.Forward, HookSide.Left, out _), "out-of-range roofs cannot attach");
            Require(!CityRooftops.Find(_player, origin, Vector3.Back, HookSide.Left, out _), "buildings behind the camera are excluded");
            var blocker = new StaticBody3D { Position = origin + new Vector3(0, 10, -12), CollisionLayer = 1 };
            blocker.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 100, 2) } });
            fixture.AddChild(blocker); await Wait(.05f);
            Require(!CityRooftops.Find(_player, origin, Vector3.Forward, HookSide.Left, out _), "occluded roofs are rejected");
            CityRooftops.End(fixture); fixture.QueueFree();
            Require(CityRooftops.Count == 0 && !CityRooftops.Find(_player, origin, Vector3.Forward, HookSide.Left, out _), "map teardown clears the roof registry");
            await Wait(.05f); GC.Collect(); GC.WaitForPendingFinalizers();
            GD.Print("ROOFTOP PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("ROOFTOP FAIL " + error); GetTree().Quit(1); }
        finally { Input.ActionRelease("city_hook_left"); Input.ActionRelease("city_hook_right"); }
    }

    private static void AddRoof(Node3D owner, Vector3 at, float height)
    {
        var body = new StaticBody3D { Position = at, CollisionLayer = 1 | Arena.HookLayer };
        body.AddChild(new CollisionShape3D { Position = Vector3.Up * height / 2,
            Shape = new BoxShape3D { Size = new Vector3(8, height, 8) } });
        owner.AddChild(body); CityRooftops.Register(body, new Vector2(at.X, at.Z), 8, 8, height);
    }
}
