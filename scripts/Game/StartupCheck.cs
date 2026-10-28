using System.Threading.Tasks;
using Godot;

namespace Wavebreak.Game;

public sealed partial class StartupCheck : Node
{
    private readonly int _steps;
    public StartupCheck(int steps) => _steps = steps;
    public override void _Ready() => _ = Run();
    private async Task Run()
    {
        await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
        bool passed = _steps >= 4 && !GetTree().Paused && Main.Instance is { } main
            && main.Stages.Stage.Id == "thornmeadow" && main.Player.IsAlive
            && main.GetNodeOrNull<UI.RunLoadingPanel>("RunLoading") is null;
        GD.Print($"LOADING {(passed ? "PASS" : "FAIL")} steps={_steps} paused={GetTree().Paused}");
        GetTree().Quit(passed ? 0 : 1);
    }
}
