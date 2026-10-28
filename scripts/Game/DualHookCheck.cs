using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Visuals;
using Wavebreak.Meta;

namespace Wavebreak.Game;

public sealed partial class DualHookCheck : Node
{
    private readonly Player _player;
    private bool _ok = true;
    public DualHookCheck(Player player) => _player = player;
    public override void _Ready() => _ = Run();
    private async Task Wait(float t) => await ToSignal(GetTree().CreateTimer(t, true, true), SceneTreeTimer.SignalName.Timeout);
    private void Check(bool ok, string text) { _ok &= ok; GD.Print($"DUALHOOK {(ok ? "OK" : "FAIL")} {text}"); }
    private void Look(Vector3 anchor)
    {
        Vector3 delta = anchor - (_player.CenterPosition + Vector3.Up * .7f);
        PlayerCamera.Instance!.FaceDirection(delta with { Y = 0 });
        PlayerCamera.Instance.SetPitchDegrees(Mathf.RadToDeg(Mathf.Atan2(delta.Y, new Vector2(delta.X, delta.Z).Length())));
    }
    private async Task Run()
    {
        try
        {
            await Wait(.8f);
            EnemyManager manager = EnemyManager.Instance!; manager.ClearField(999f); manager.Quiet = true;
            _player.MoveOverride = static () => Vector3.Zero;
            Check(InputMap.ActionGetEvents("city_hook_left").Any(e => e is InputEventMouseButton { ButtonIndex: MouseButton.Left }), "LMB is the left hook default");
            Check(InputMap.ActionGetEvents("city_hook_right").Any(e => e is InputEventMouseButton { ButtonIndex: MouseButton.Right }), "RMB is the right hook default");
            Check(InputMap.ActionGetEvents("city_hook_left").Any(e => e is InputEventJoypadButton { ButtonIndex: JoyButton.LeftShoulder })
                && InputMap.ActionGetEvents("city_hook_right").Any(e => e is InputEventJoypadButton { ButtonIndex: JoyButton.RightShoulder })
                && InputMap.ActionGetEvents("jump").Any(e => e is InputEventJoypadButton { ButtonIndex: JoyButton.A }),
                "controller has independent shoulders and attached boost on A");
            Check(Arena.Current.Radius == 350f && ReefCheckHelpers.Find<Arena>(GetTree().Root)!.CityBuildingCount > 240, "city expanded beyond three times the original area");
            _player.Teleport(Vector3.Up * .2f); await Wait(.1f);
            Look(new Vector3(18, 17, -16)); await Wait(.08f);
            Input.ActionPress("city_hook_left"); await Wait(.2f);
            Check(_player.LeftHookAttached && !_player.RightHookAttached, "left input fires and holds one structural anchor");
            Look(new Vector3(-18, 9, -16)); await Wait(.08f);
            int dashes = _player.Dashes;
            Input.ActionPress("city_hook_right"); await Wait(.2f);
            Check(_player.LeftHookAttached && _player.RightHookAttached, "right input attaches without replacing the left hook");
            Check(_player.LeftHookAnchor.DistanceTo(_player.RightHookAnchor) > 10f, "anchors are genuinely independent");
            Check(_player.Dashes == dashes, "RMB does not trigger the city dash");
            int boosts = _player.HookBoosts;
            float speed = _player.Velocity.Length();
            Input.ActionPress("jump"); await Wait(.04f); Input.ActionRelease("jump");
            Check(_player.LeftHookAttached && _player.RightHookAttached && _player.HookBoosts == boosts + 1, "Space boosts without releasing either hook");
            Check(_player.Velocity.Length() > speed + 2f && _player.Velocity.Length() <= GrapplePhysics.BoostSpeed + .1f, "boost is noticeable and speed-bounded");
            Input.ActionPress("jump"); await Wait(.04f); Input.ActionRelease("jump");
            Check(_player.HookBoosts == boosts + 1, "cooldown rejects boost spam");
            foreach (HookSide side in new[] { HookSide.Left, HookSide.Right })
            {
                var cable = _player.GetNode<GrappleTether>(side == HookSide.Left ? "GrappleCable" : "RightGrappleCable");
                Vector3 tip = cable.GlobalPosition + cable.GlobalBasis.Y * .5f;
                Vector3 anchor = side == HookSide.Left ? _player.LeftHookAnchor : _player.RightHookAnchor;
                Check(cable.Visible && tip.DistanceTo(anchor) < .1f, side + " cable reaches its own anchor");
            }
            Input.ActionRelease("city_hook_left"); await Wait(.08f);
            Check(!_player.LeftHookAttached && _player.RightHookAttached, "left release preserves the right tether");
            Input.ActionRelease("city_hook_right"); await Wait(.08f);
            Check(!_player.Grappling, "right release frees both after independent releases");
            boosts = _player.HookBoosts;
            _player.RequestJump(); await Wait(.05f);
            Check(_player.HookBoosts == boosts, "unanchored jump cannot create hook thrust");
            foreach (Vector3 at in new[] { new Vector3(0, 1, -250), new Vector3(0, 1, 250), new Vector3(250, 1, 0), new Vector3(-250, 1, 0) })
            {
                _player.Teleport(at); await Wait(.06f);
                Vector3 candidate = Mathf.Abs(at.Z) > 100 ? new Vector3(25, 14, at.Z) : new Vector3(at.X, 14, 25);
                Check(GrappleAnchors.Ray(_player, _player.CenterPosition, (candidate - _player.CenterPosition).Normalized(), _player.CenterPosition, out Vector3 anchor), "reachable structural anchor in district " + at);
                Input.ActionPress("city_hook_left");
                Check(_player.AttachHook(HookSide.Left, anchor), "district hook attaches");
                if (_player.HookBoostCooldown > 0) await Wait(_player.HookBoostCooldown + .03f);
                boosts = _player.HookBoosts; _player.RequestJump(); await Wait(.04f);
                Check(_player.Grappling && _player.HookBoosts > boosts, "attached traversal boost works in expanded district");
                Input.ActionRelease("city_hook_left"); await Wait(.03f);
            }
            _player.Teleport(new Vector3(Arena.Current.Radius + 20f, 5, 0)); await Wait(.05f);
            Check(_player.GlobalPosition.X < Arena.Current.InRadius, "expanded boundary contains fast traversal");
            _player.Teleport(Vector3.Up * .1f); await Wait(.08f);
            Vector3 c = _player.CenterPosition;
            GrappleAnchors.Ray(_player, c, (new Vector3(18, 17, -16) - c).Normalized(), c, out Vector3 finalAnchor);
            Input.ActionPress("city_hook_left"); _player.AttachHook(finalAnchor);
            _player.Health.Kill();
            Check(!_player.LeftHookAttached && !_player.RightHookAttached, "death immediately clears both hooks");
            using var leftBinding = new InputEventKey { PhysicalKeycode = Key.F9 };
            using var rightBinding = new InputEventKey { PhysicalKeycode = Key.F10 };
            Keybinds.Rebind("city_hook_left", 0, leftBinding);
            Check(Keybinds.Get("city_hook_left", 0) is InputEventKey { PhysicalKeycode: Key.F9 }
                && Keybinds.Get("city_hook_right", 0) is InputEventMouseButton { ButtonIndex: MouseButton.Right }
                && InputMap.ActionGetEvents("city_hook_left").Any(e => e is InputEventJoypadButton),
                "rebinding the left hook preserves the right hook and controller");
            Keybinds.Rebind("city_hook_right", 0, rightBinding);
            Check(Keybinds.Get("city_hook_right", 0) is InputEventKey { PhysicalKeycode: Key.F10 }, "right hook can be rebound independently");
            Keybinds.ResetAll();
            Check(!Keybinds.Actions.Any(a => a.Action == "core_strike")
                && InputMap.ActionGetEvents("dash").Any(e => e is InputEventJoypadButton { ButtonIndex: JoyButton.X }),
                "core strike shares the existing controller dash and has no separate F action");
            Keybinds.Rebind("dash", 0, leftBinding);
            Check(Keybinds.Get("dash", 0) is InputEventKey { PhysicalKeycode: Key.F9 }
                && InputMap.ActionGetEvents("dash").Any(e => e is InputEventJoypadButton), "rebinding dash also rebinds core strike while retaining controller");
            Keybinds.ResetAll();
        }
        catch (Exception e) { Check(false, e.ToString()); }
        finally
        {
            Input.ActionRelease("city_hook_left"); Input.ActionRelease("city_hook_right"); Input.ActionRelease("jump");
            GC.Collect(); GC.WaitForPendingFinalizers();
            GD.Print(_ok ? "DUALHOOK PASS" : "DUALHOOK FAIL"); GetTree().Quit(_ok ? 0 : 1);
        }
    }
}

