using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Meta;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public sealed partial class HookControlsCheck : Node
{
    private readonly Player _player;
    public HookControlsCheck(Player player) => _player = player;
    public override void _Ready() => _ = Run();
    private async Task Wait(float time) => await ToSignal(GetTree().CreateTimer(time, true, true), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool value, string label)
    {
        GD.Print($"HOOK CONTROLS {(value ? "OK" : "FAIL")} {label}");
        if (!value) throw new InvalidOperationException(label);
    }
    private async Task Click(string action)
    {
        Input.ActionPress(action); await Wait(.08f); Input.ActionRelease(action); await Wait(.08f);
    }
    private async Task Run()
    {
        try
        {
            await Wait(.7f); EnemyManager.Instance!.Quiet = true; EnemyManager.Instance.ClearField(999);
            _player.MoveOverride = static () => Vector3.Zero;
            _player.Teleport(Vector3.Up * .2f); PlayerCamera.Instance!.FaceDirection(Vector3.Forward);
            GameplaySettings.SetToggleCityHooks(true);
            await Click("city_hook_left");
            Require(_player.LeftHookAttached, "toggle retains left hook after button release");
            await Click("city_hook_right");
            Require(_player.LeftHookAttached && _player.RightHookAttached, "toggle sides remain independent");
            int boosts = _player.HookBoosts;
            _player.RequestDash(); await Wait(.05f);
            Require(_player.HookBoosts == boosts + 1 && _player.LeftHookAttached && _player.RightHookAttached,
                "dash boosts without detaching either tether");
            _player.RequestJump(); await Wait(.05f);
            Require(_player.HookBoosts == boosts + 1, "jump and dash share a boost cooldown");
            await Click("city_hook_left");
            Require(!_player.LeftHookAttached && _player.RightHookAttached, "second left click releases only the left hook");
            await Click("city_hook_right"); Require(!_player.Grappling, "second right click releases the remaining hook");
            _player.Teleport(Vector3.Up * .2f); GameplaySettings.SetToggleCityHooks(false);
            Input.ActionPress("city_hook_left"); await Wait(.25f);
            Require(_player.LeftHookAttached, "hold mode acquires while pressed");
            Input.ActionRelease("city_hook_left"); await Wait(.05f);
            Require(!_player.Grappling, "hold mode releases immediately on button release");
            GameplaySettings.SetToggleCityHooks(true); await Click("city_hook_left");
            _player.Teleport(new Vector3(250, 1, 0)); await Wait(.05f);
            Require(!_player.Grappling, "teleport clears retained toggle state");
            GD.Print("HOOK CONTROLS PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("HOOK CONTROLS FAIL " + error); GetTree().Quit(1); }
        finally { Input.ActionRelease("city_hook_left"); Input.ActionRelease("city_hook_right"); }
    }
}
