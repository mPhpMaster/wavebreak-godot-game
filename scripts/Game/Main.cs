using Godot;

namespace Wavebreak.Game;

/// <summary>
/// Scene root. Builds the world and owns the run.
///
/// Systems are constructed here in dependency order and ticked from one place
/// rather than each running its own _Process. That ordering is a real design
/// choice: with independent _Process callbacks the order between "weapons fire"
/// and "enemies move" is whatever the scene tree happens to be, and hit
/// registration quietly changes when you reparent a node.
/// </summary>
public sealed partial class Main : Node3D
{
    public static Main? Instance { get; private set; }

    public RunState Run { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;
        Screenshot.ParseArgs();

        Run = new RunState(Screenshot.Requested ? Screenshot.Seed : (ulong)GD.Randi());
        GD.Print($"[wavebreak] run seed {Run.Seed}");

        AddChild(new Arena());

        BuildWorld();

        if (Screenshot.Requested)
            _ = RunScreenshot();
    }

    /// <summary>Populated as the gameplay systems land; kept as one method so the
    /// construction order stays visible in a single place.</summary>
    private void BuildWorld()
    {
        // Until the player exists, give the frame something to look at from a
        // fixed vantage so the arena can be verified on its own.
        var cam = new Camera3D
        {
            Position = new Vector3(0f, 28f, 34f),
            Rotation = new Vector3(Mathf.DegToRad(-38f), 0f, 0f),
            Current = true,
            Fov = 60f,
        };
        AddChild(cam);
    }

    public override void _Process(double delta)
    {
        Run.Tick((float)delta);
    }

    private async System.Threading.Tasks.Task RunScreenshot()
    {
        var timer = GetTree().CreateTimer(Screenshot.AfterSeconds);
        await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
        await Screenshot.Capture(GetTree(), this, Screenshot.Path!);
        GD.Print($"[wavebreak] t={Run.Clock} kills={Run.Kills}");
        GetTree().Quit();
    }

    public override void _ExitTree()
    {
        Run?.Dispose();
        Core.EventBus.Reset();
        Instance = null;
    }
}
