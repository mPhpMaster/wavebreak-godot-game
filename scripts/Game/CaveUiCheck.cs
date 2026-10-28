using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>Opt-in real discovery, health visibility and rendered map checks.</summary>
public sealed partial class CaveUiCheck : Node
{
    private readonly Player _player;
    private readonly string? _prefix;
    private bool _passed = true;
    private readonly NearbyEnemySelection _selection = new();
    public CaveUiCheck(Player player, string? prefix) { _player = player; _prefix = prefix; }
    public override void _Ready() => _ = Run();
    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
    private void Check(bool condition, string message)
    { _passed &= condition; GD.Print($"CAVEUI {(condition ? "OK" : "FAIL")} {message}"); }
    private async Task Capture(string name)
    { if (_prefix is not null) await Screenshot.Capture(GetTree(), this, _prefix + "-" + name + ".png"); }

    private async Task Run()
    {
        await Wait(0.8f);
        _player.MoveOverride = static () => Vector3.Zero;
        EnemyManager.Instance!.Quiet = true; EnemyManager.Instance.ClearField(999);
        ReefCaveDiscovery cave = ReefCaveDiscovery.Instance!;
        Check(cave.Mask == 0, "both cave portals start undiscovered");
        await Capture("hidden");
        FogOfWar.Instance!.Reveal(new Vector2(ReefCavePath.Entrance.X, ReefCavePath.Entrance.Z), 30);
        _player.Teleport(ReefCavePath.Entrance + Vector3.Up * 42);
        await Wait(0.4f);
        Check(cave.Mask == 0, "revealed XZ fog from above cannot reveal a portal at a different depth");
        var wall = Wall(ReefCavePath.Entrance + Vector3.Left * 5, new Vector3(0.6f, 12, 12));
        _player.Teleport(ReefCavePath.Entrance + Vector3.Left * 10);
        await Wait(0.4f);
        Check(cave.Mask == 0 && !cave.CanDiscover(_player.CenterPosition, CaveMapMarkerKind.Entrance), "solid world geometry blocks portal discovery");
        wall.GetParent().RemoveChild(wall); wall.QueueFree();
        await Wait(0.5f);
        Check(cave.Discovered(CaveMapMarkerKind.Entrance) && !cave.Discovered(CaveMapMarkerKind.Exit), "entrance discovery does not reveal the exit");
        _player.Teleport(ReefCavePath.Entrance + new Vector3(-12, -12, 0));
        await Wait(0.3f); PlayerCamera.Instance!.SnapToTarget();
        await Capture("entrance");
        _player.Teleport(ReefCavePath.Exit + Vector3.Up * 8);
        FogOfWar.Instance.Reveal(new Vector2(ReefCavePath.Exit.X, ReefCavePath.Exit.Z), 25);
        await Wait(0.5f);
        Check(cave.Mask == 3, "exit is independently discovered at its own visible location");
        Check(CaveMapPresentation.Elevation(Vector3.Up * 12, Vector3.Zero).Contains("+")
            && CaveMapPresentation.Elevation(Vector3.Down * 8, Vector3.Zero).Contains("-")
            && CaveMapPresentation.Elevation(Vector3.Zero, Vector3.Zero).Contains("="), "height labels distinguish above, below and level without color");
        _player.Teleport(Vector3.Up * 12); PlayerCamera.Instance.SnapToTarget();
        await Wait(0.25f); await Capture("both");
        if (_prefix is not null)
        {
            StatsPanel.ForceOpen = true;
            await Wait(0.4f); await Capture("fullmap");
            Check(ReefCheckHelpers.Find<BigMap>(GetTree().Root) is { CaveMarkersDrawn: 2 }, "the real Tab map draws both discovered portal labels");
            StatsPanel.ForceOpen = false;
            await Wait(0.1f);
        }
        await CheckHealthBars();
        cave.Merge(255); Check(cave.Mask == 3, "replicated discovery masks are bounded");
        Arena.UseBounds(Stages.Run[0]);
        await Wait(0.2f);
        _selection.Refresh(_player, GetViewport().GetCamera3D(), EnemyManager.Instance?.AliveList);
        Check(!Arena.Current.Underwater, "ordinary maps keep their existing bounds");
        if (_prefix is not null) Check(ReefCheckHelpers.Find<Minimap>(GetTree().Root) is { CaveMarkersDrawn: 0 }, "cave markers never leak into ordinary maps");
        GD.Print(_passed ? "CAVEUI PASS" : "CAVEUI FAIL"); GetTree().Quit(_passed ? 0 : 1);
    }

    private StaticBody3D Wall(Vector3 at, Vector3 size)
    {
        var wall = new StaticBody3D { Position = at, CollisionLayer = 1 };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        wall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Arena.Flat(new Color(0.2f, 0.3f, 0.4f)) });
        _player.GetParent().AddChild(wall); return wall;
    }
}
