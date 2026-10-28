using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

public sealed partial class DiveControlCheck : Node
{
    public override void _Ready() => _ = Run();
    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool condition, string label)
    {
        GD.Print($"DIVE CONTROL {(condition ? "OK" : "FAIL")} {label}");
        if (!condition) throw new InvalidOperationException(label);
    }
    private async Task Run()
    {
        try
        {
            await Wait(.7f); Main main = Main.Instance!; main.SetProcess(false);
            main.Enemies.ClearField(999); main.Enemies.Quiet = true;
            Player player = main.Player; player.MoveOverride = static () => Vector3.Zero;
            foreach (bool fire in new[] { false, true })
            {
                player.Teleport(new Vector3(0, WaterSurfaceHeight.At(Vector3.Zero) + 3, 0));
                Input.ActionPress("slide");
                for (int frame = 0; frame < 150; frame++)
                {
                    // A shot downwards used to produce a large upward shove; hammer it at 60 shots/s.
                    if (fire) player.ShotgunFeedback(Vector3.Down);
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                }
                float depth = WaterSurfaceHeight.At(player.GlobalPosition) - player.CenterPosition.Y;
                Require(!player.IsBreaching && depth > 7 && player.Velocity.Y < -3,
                    $"dive crosses the surface and reaches {depth:0.0}m depth, downward firing={fire}");
                Require(player.NetVelocity.DistanceTo(player.Velocity) < .001f, "network publishes actual swimming velocity");
                Input.ActionRelease("slide");
            }
            // Probe the high coastal wall that the floor fallback must never lift a swimmer onto.
            Vector3 coast = new(Arena.Current.InRadius - 7, 45, 0);
            Require(ReefCavePath.Floor(coast) > WaterSurfaceHeight.At(coast), "probe covers a cliff higher than the sea");
            player.Teleport(coast); Input.ActionPress("slide"); await Wait(.3f);
            Require(player.GlobalPosition.Y < WaterSurfaceHeight.At(player.GlobalPosition), "cliff recovery cannot teleport the player above the sea");
            GD.Print("DIVE CONTROL PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("DIVE CONTROL FAIL " + error); GetTree().Quit(1); }
        finally { Input.ActionRelease("slide"); }
    }
}
