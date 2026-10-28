using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Game;

public sealed partial class PlatformPursuitCheck : Node
{
    public override void _Ready() => _ = Run();
    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
    private async Task Run()
    {
        try
        {
            await Wait(.7f);
            Main main = Main.Instance!;
            main.SetProcess(false); main.Player.SetPhysicsProcess(false); main.Player.Teleport(Vector3.Up * 100);
            main.Enemies.ClearField(999); main.Enemies.Quiet = false; EnemyManager.DevFrozen = true;
            PlatformRoute platform = PlatformRoutes.All.First();
            EnemyDef def = Catalog.Enemies.First(enemy => !enemy.IsBoss && !enemy.IsElite && enemy.Behavior == EnemyBehavior.Chaser);
            Enemy enemy = main.Enemies.SpawnAt(def, platform.Centre - platform.RampSide * (platform.HalfSize + 5))!;
            var target = new CityCheckTarget { Position = platform.Centre + Vector3.Up * platform.Height };
            for (int index = 0; index < 2400; index++) enemy.Step(1f / 60, target.Position, target, Vector3.Zero);
            GD.Print($"PLATFORM final={enemy.GlobalPosition} target={target.Position}");
            if (enemy.GlobalPosition.Y < platform.Height - .35f || enemy.GlobalPosition.DistanceTo(target.Position) > platform.HalfSize)
                throw new InvalidOperationException("Ground enemy did not route around the platform and climb its ramp");
            GD.Print("PLATFORM PURSUIT PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("PLATFORM PURSUIT FAIL " + error); GetTree().Quit(1); }
    }
}
