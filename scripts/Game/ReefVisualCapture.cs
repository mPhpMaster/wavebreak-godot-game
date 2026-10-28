using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

/// <summary>Opt-in captures of the four reef fixes; never installed in normal play.</summary>
public sealed partial class ReefVisualCapture : Node
{
    private readonly string _prefix;

    public ReefVisualCapture(string prefix) => _prefix = prefix;

    public override void _Ready() => _ = Capture();

    private async Task Wait(double seconds)
        => await ToSignal(GetTree().CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);

    private async Task Capture()
    {
        await Wait(4.5);
        if (Player.Instance is not { } player || PlayerCamera.Instance is not { } camera) return;
        player.MoveOverride = static () => Vector3.Zero;
        if (EnemyManager.Instance is { } enemies) { enemies.Quiet = true; enemies.ClearField(999f); }
        async Task View(Vector3 at, Vector3 direction, float pitch, string name)
        {
            player.Teleport(at);
            camera.FaceDirection(direction);
            camera.SetPitchDegrees(pitch);
            camera.SnapToTarget();
            await Wait(0.7);
            await Screenshot.Capture(GetTree(), this, _prefix + "-" + name + ".png");
        }
        await View(Vector3.Up * 4f, Vector3.Forward, -20f, "stroke-a");
        await Wait(0.65);
        await Screenshot.Capture(GetTree(), this, _prefix + "-stroke-b.png");
        await View(Vector3.Up * 4f, Vector3.Right, -15f, "swim-side");
        var valley = new Vector3(20f, 0f, -38f);
        valley.Y = UnderwaterTerrain.Height(valley) + 5f;
        await View(valley, Vector3.Forward, -30f, "valley");
        await View(new Vector3(105f, 14f, 10f), Vector3.Right, -5f, "cliffs");
        if (ReefCheckHelpers.Find<ReefLandmarks>(GetTree().Root) is { } landmarks)
        {
            await View(ReefCavePath.Entrance + new Vector3(-10, 0, 0), Vector3.Right, -5, "cave-entry");
            await View(ReefCavePath.Points[45], ReefCavePath.Points[49] - ReefCavePath.Points[45], -5, "cave-interior");
            await View(ReefCavePath.Exit + Vector3.Up * 9 + Vector3.Back * 8, Vector3.Forward, -45, "cave-exit");
            await View(landmarks.WreckRewardPosition + new Vector3(15f, 8f, 8f), new Vector3(-15f, 0f, -8f), -22f, "wreck");
        }
        await View(Visuals.ReefPlacement.Ledges[0] + new Vector3(-8, 5, 5), new Vector3(8, 0, -5), -20, "cliff-shrine");
        if (ReefHarvest.Instance is { Count: > 0 } harvest)
            await View(harvest.Definitions[0].Position + new Vector3(0, 3, 5), Vector3.Forward, -25, "harvest");
        if (EnemyManager.Instance is { } marine)
        {
            marine.Quiet = false;
            Vector3 at = new(-10, 9, -25);
            int offset = 0;
            foreach (string id in new[] { "brine_leech", "sidle_crab", "lantern_jelly", "undertow_oracle" })
                if (Data.Catalog.EnemyById(id) is { } def)
                { var creature = marine.SpawnAt(def, at + Vector3.Right * offset * 5); creature?.SetProcess(false); offset++; }
            EnemyManager.DevFrozen = true;
            Actors.Pickup.Spawn(Actors.PickupKind.Gold, at + new Vector3(3, 0, 5), 8);
            Actors.Pickup.Spawn(Actors.PickupKind.Xp, at + new Vector3(5, 0, 5), 8);
            await View(at + new Vector3(7, 4, 14), Vector3.Forward, -12, "marine-and-pickups");
            marine.Quiet = true;
        }
        var current = new Vector3(20f, 0f, -62f);
        current.Y = UnderwaterTerrain.Height(current) + 7f;
        await View(current, Vector3.Right, -15f, "current");
        if (ReefCheckHelpers.Find<Visuals.ReefFishSchools>(GetTree().Root) is { } fish)
            await View(fish.SchoolCentre(0), Vector3.Forward, -10f, "fish");
        await View(Vector3.Up * (WaterSurfaceHeight.At(Vector3.Zero) - 5f), Vector3.Forward, 55f, "surface-below");
        player.SetPhysicsProcess(false);
        await View(Vector3.Up * (WaterSurfaceHeight.At(Vector3.Zero) + 7f), Vector3.Forward, -28f, "surface-above");
        if (ReefCheckHelpers.Find<DirectionalLight3D>(GetTree().Root) is { } sun)
            await View(Vector3.Up * (WaterSurfaceHeight.BaseHeight + 14),
                (sun.GlobalBasis.Z with { Y = 0 }).Rotated(Vector3.Up, 0.4f), 62, "sky-sun");
        player.SetPhysicsProcess(true);
        player.Teleport(Vector3.Up * (WaterSurfaceHeight.At(Vector3.Zero) - Player.BodyHeight * 0.5f - 1.5f));
        camera.FaceDirection(Vector3.Right); camera.SetPitchDegrees(-12f); camera.SnapToTarget();
        int entries = player.WaterEntries;
        Input.ActionPress("jump"); player.RequestDash();
        await Wait(0.25); Input.ActionRelease("jump");
        await Wait(0.65);
        await Screenshot.Capture(GetTree(), this, _prefix + "-breach-apex.png");
        for (int i = 0; i < 30 && player.WaterEntries == entries; i++) await Wait(0.1);
        await Screenshot.Capture(GetTree(), this, _prefix + "-reentry.png");
        GD.Print("REEF CAPTURES COMPLETE");
        GetTree().Quit();
    }
}
