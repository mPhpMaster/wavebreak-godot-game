using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// Part of <c>--check-boundary</c> for maps with big blockers
/// (<see cref="MapBlockers"/>, Stormwatch Harbour): prints BLOCKERS lines and
/// returns whether all held.
///
/// - <b>solid</b>: the player runs, jumps and dashes straight at five
///   blockers (houses of both rings, a cliff spur, the lighthouse) and must
///   never end up inside one.
/// - <b>spawn ring</b>: 300 real spawn-point searches round six spots of the
///   map; none may land in a blocker or on its roof.
/// - <b>finds</b>: no chest, map shrine or friendly shrine stands in one.
/// - <b>pathing</b>: twelve enemies dropped behind the second row of houses,
///   with the player waiting in the square, must find their way round to
///   within 12 m of the player in 30 s (at least 10 of 12). Straight-line
///   steering would leave them pressed against the walls.
/// </summary>
public sealed partial class StageDirector
{
    private async Task<bool> CheckBlockers(StageDef stage)
    {
        SceneTree tree = _root.GetTree();
        bool ok = true;
        void Line(string what, bool pass, string detail)
        {
            GD.Print($"BLOCKERS map={stage.Id} {what} {detail} {(pass ? "PASS" : "FAIL")}");
            ok &= pass;
        }

        // ---- solid: run into the walls.
        var blockers = MapBlockers.All;
        int[] picks = { 0, 3, 7, 12, blockers.Count > 0 ? IndexOfRound() : 0 };
        float worst = float.MinValue;
        foreach (int idx in picks.Distinct())
        {
            if (idx < 0 || idx >= blockers.Count) continue;
            MapBlockers.Blocker b = blockers[idx];
            // Start 8 m off the face nearer the middle of the map.
            var z = new Vector2(-b.Sin, b.Cos);
            Vector2 face = (b.Centre + z).LengthSquared() < (b.Centre - z).LengthSquared() ? z : -z;
            Vector2 start = b.Centre + face * (b.Half.Y + 8f);
            // On the floor under the start (the lighthouse stands on its headland).
            var down = PhysicsRayQueryParameters3D.Create(new Vector3(start.X, 60f, start.Y), new Vector3(start.X, -5f, start.Y), 1u);
            var hit = _root.GetWorld3D().DirectSpaceState.IntersectRay(down);
            float floorY = hit.Count > 0 ? ((Vector3)hit["position"]).Y : 0f;
            _player.Teleport(new Vector3(start.X, floorY + 0.5f, start.Y));
            Vector3 dir = new Vector3(-face.X, 0f, -face.Y);
            _player.MoveOverride = () => dir;
            float mine = float.MinValue;
            for (int frame = 0; frame < 150; frame++)
            {
                if (frame % 24 == 0) _player.RequestJump();
                if (frame % 70 == 10) _player.RequestDash();
                await _root.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
                Vector3 p = _player.GlobalPosition;
                Vector2 l = b.ToLocal(new Vector2(p.X, p.Z));
                float depth = Mathf.Min(b.Half.X - Mathf.Abs(l.X), b.Half.Y - Mathf.Abs(l.Y));
                if (p.Y < b.Top - 0.5f) { worst = Mathf.Max(worst, depth); mine = Mathf.Max(mine, depth); }
            }
            Vector3 end = _player.GlobalPosition;
            GD.Print($"[blockers] solid #{idx} at=({b.Centre.X:0},{b.Centre.Y:0}) half=({b.Half.X:0.#},{b.Half.Y:0.#}) deepest={mine:0.00} end=({end.X:0.0},{end.Y:0.0},{end.Z:0.0})");
        }
        _player.MoveOverride = null;
        Line("solid", worst < 0f, $"deepest={worst:0.00}m (negative = never inside)");

        // ---- spawn ring.
        int found = 0, inside = 0;
        foreach (Vector3 around in new[] { Vector3.Zero, new(0f, 0f, -48f), new(48f, 0f, 0f), new(-70f, 0f, 60f), new(110f, 0f, -10f), new(0f, 0f, 120f) })
        {
            _player.Teleport(around + Vector3.Up * 0.5f);
            await _root.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            (int f, int i) = _enemies.ProbeSpawnRing(around, 50);
            found += f;
            inside += i;
        }
        Line("spawn-ring", inside == 0 && found > 200, $"found={found}/300 inside={inside}");

        // ---- finds.
        int bad = 0, total = 0;
        if (ChestSpawner.Instance is { } chests)
            foreach (Chest c in chests.Chests) { total++; if (MapBlockers.Inside(c.GlobalPosition with { Y = -1f }, 0.5f)) bad++; }
        if (ShrineSpawner.Instance is { } shrines)
            foreach (MapShrine s in shrines.Shrines) { total++; if (MapBlockers.Inside(s.GlobalPosition with { Y = -1f }, 0.5f)) bad++; }
        if (CuteShrineSpawner.Instance is { } cute)
            foreach (CuteShrine s in cute.Shrines) { total++; if (MapBlockers.Inside(s.GlobalPosition with { Y = -1f }, 0.5f)) bad++; }
        Line("finds", bad == 0 && total > 0, $"checked={total} inside={bad}");

        // ---- pathing round the old town: once with steering off (the
        // baseline: straight at the player, into the walls), once with it.
        bool wasInvulnerable = Screenshot.Invulnerable;
        Screenshot.Invulnerable = true;
        Meta.Challenges.WeaponsSilenced = true;   // count arrivals, not kills
        (int baseSpawned, int baseArrived) = await PathingRound(off: true);
        (int spawned, int arrived) = await PathingRound(off: false);
        Line("pathing", spawned == 12 && arrived >= 10,
             $"arrived={arrived}/{spawned} in 30s (steering off: {baseArrived}/{baseSpawned})");
        Screenshot.Invulnerable = wasInvulnerable;
        Meta.Challenges.WeaponsSilenced = false;
        return ok;
    }

    /// <summary>Twelve runners behind the second row of houses, the player
    /// waiting in the square: how many come within 12 m in 30 s.</summary>
    private async Task<(int Spawned, int Arrived)> PathingRound(bool off)
    {
        SceneTree tree = _root.GetTree();
        MapBlockers.SteeringOff = off;
        _player.Teleport(Vector3.Up * 0.5f);
        _enemies.ClearField(9999f);
        EnemyDef? runner = Catalog.Enemies.FirstOrDefault(e => e.Id == "skitterling");   // 4.4 m/s
        int spawned = 0;
        if (runner is not null)
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.DegToRad(30f + 60f * i);
                // Behind the second row (outer face at 67 m), inside the cull radius.
                foreach (float r in new[] { 71f, 75f })
                    if (_enemies.SpawnAt(runner, new Vector3(Mathf.Cos(a) * r, 0.3f, Mathf.Sin(a) * r)) is not null) spawned++;
            }
        var reached = new System.Collections.Generic.HashSet<Enemy>();
        for (float waited = 0f; waited < 30f && reached.Count < spawned; waited += 0.5f)
        {
            await _root.ToSignal(tree.CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
            Vector3 me = _player.GlobalPosition;
            foreach (Enemy e in _enemies.AliveList)
                if (e.IsAlive && new Vector2(e.GlobalPosition.X - me.X, e.GlobalPosition.Z - me.Z).Length() < 12f) reached.Add(e);
        }
        MapBlockers.SteeringOff = false;
        _enemies.ClearField(9999f);
        return (spawned, reached.Count);
    }

    private static int IndexOfRound()
    {
        for (int i = 0; i < MapBlockers.All.Count; i++)
            if (MapBlockers.All[i].Look == MapBlockers.Shape.Round) return i;
        return 0;
    }
}
