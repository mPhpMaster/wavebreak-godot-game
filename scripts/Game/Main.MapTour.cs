using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

/// <summary>
/// <c>--map-tour=&lt;prefix&gt;</c>: walks the player round the current map and
/// photographs it, for checking a map's look and its lines of sight without
/// playing it, then quits:
///
///   &lt;prefix&gt;-start-N/E/S/W   from the start, the camera facing each way
///   &lt;prefix&gt;-&lt;zone&gt;          from each named zone of a hand-built map
///   &lt;prefix&gt;-overhead        straight down from 420 m, the whole layout
///   &lt;prefix&gt;-tab             the Tab sheet (big map) once the tour has
///                               revealed part of the map
///
/// Enemies are held off (the field is quiet), so the frames show the place.
/// Use with <c>--route=stormwatch,...</c> to tour a map that is not first in
/// the classic route.
/// </summary>
public sealed partial class Main
{
    private async Task MapTour(string prefix)
    {
        SceneTree tree = GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        async Task Shot(string name) => await Screenshot.Capture(tree, this, $"{prefix}-{name}.png");
        PlayerCamera? cam = PlayerCamera.Instance;

        async Task Stand(Vector3 at, Vector3 facing, string name)
        {
            _enemies.ClearField(999f);
            _player.Teleport(at + Vector3.Up * 0.4f);
            cam?.FaceDirection(facing);
            cam?.SnapToTarget();
            await Wait(0.9);
            await Shot(name);
        }

        await Wait(2.0);
        _enemies.Quiet = true;
        _enemies.ClearField(999f);
        await Shot("start-minimap");

        await Stand(Vector3.Zero, Vector3.Forward, "start-N");
        await Stand(Vector3.Zero, Vector3.Right, "start-E");
        await Stand(Vector3.Zero, Vector3.Back, "start-S");
        await Stand(Vector3.Zero, Vector3.Left, "start-W");

        // Each zone, looking back toward the middle and then outward.
        foreach (MapBlockers.Zone z in new List<MapBlockers.Zone>(MapBlockers.Zones))
        {
            if (z.Radius > 70f) continue;
            var at = new Vector3(z.Centre.X, 0f, z.Centre.Y);
            if (at.LengthSquared() < 1f) continue;
            // Stand a little in front of the zone centre, on open floor.
            Vector3 spot = at * 0.82f;
            if (MapBlockers.Inside(spot, 2f)) spot = at * 0.7f;
            string slug = z.Name.ToLowerInvariant().Replace(' ', '-');
            await Stand(spot, -spot, slug + "-in");
            await Stand(spot, spot, slug + "-out");
        }

        // The tour revealed a good part of the map: the Tab sheet now.
        _player.Teleport(Vector3.Up * 0.4f);
        await Wait(0.5);
        UI.StatsPanel.ForceOpen = true;
        await Wait(0.8);
        await Shot("tab");
        UI.StatsPanel.ForceOpen = false;
        await Wait(0.3);
        await Shot("minimap-later");

        // Overhead: a temporary camera straight down over the whole map.
        var top = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = Arena.Current.Radius * 2.15f,
            Far = 900f,
        };
        AddChild(top);
        top.GlobalTransform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2f), new Vector3(0f, 420f, 0f));
        top.MakeCurrent();
        foreach (Node n in tree.Root.FindChildren("*", "WorldEnvironment", true, false))
            if (n is WorldEnvironment { Environment: { } env }) env.FogEnabled = false;
        await Wait(0.6);
        await Shot("overhead");
        tree.Quit();
    }
}
