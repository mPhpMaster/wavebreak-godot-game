using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

public sealed partial class SwimmingDashCheck : Node
{
    private Vector3 _wish = Vector3.Forward;
    public override void _Ready() => _ = Run();
    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool value, string label)
    {
        GD.Print($"SWIM DASH {(value ? "OK" : "FAIL")} {label}");
        if (!value) throw new InvalidOperationException(label);
    }
    private async Task Run()
    {
        try
        {
            await Wait(.6f);
            Main main = Main.Instance!; main.SetProcess(false); main.Enemies.ClearField(999); main.Enemies.Quiet = true;
            Player player = main.Player;
            player.MoveOverride = () => _wish;
            player.Teleport(Vector3.Up * 42);
            await Wait(.1f);
            int before = player.Dashes;
            player.RequestDash(); await Wait(.05f);
            float peak = player.Velocity.Length();
            Require(player.IsDashing && player.Dashes == before + 1 && peak > 12, "dash launches a visible underwater burst");
            _wish = Vector3.Right;
            await Wait(.14f);
            Require(player.IsDashing && player.Velocity.X > 3, "right input steers during the same burst");
            _wish = Vector3.Left;
            await Wait(.2f);
            Require(player.IsDashing && player.Velocity.X < -1, "left input reverses steering during the burst");
            Require(player.Velocity.Length() < peak, "burst speed decays instead of staying at maximum");
            await Wait(.25f);
            Require(!player.IsDashing && player.IsSwimming && player.Velocity.Length() < peak * .8f, "burst returns smoothly to ordinary swimming");
            GD.Print("SWIM DASH PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("SWIM DASH FAIL " + error); GetTree().Quit(1); }
    }
}
