using Godot;
using System.Linq;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>City-only street navigation, inflated for the largest warden capsule.</summary>
public static class CityNavigation
{
    private const float Cell = 4f;
    private const float Clearance = 5.1f;
    private static AStarGrid2D? _grid;
    private static Arena? _owner;
    private static Rect2I Region;

    public static void End(Arena owner)
    {
        if (!ReferenceEquals(owner, _owner)) return;
        _grid?.Dispose(); _grid = null; _owner = null;
    }

    public static void Build(Arena owner)
    {
        using var timing = Prof.Scope("Startup.cityNavigation");
        _grid?.Dispose();
        _owner = owner;
        int cells = Mathf.CeilToInt(Arena.Current.Radius / Cell);
        Region = new Rect2I(-cells, -cells, cells * 2 + 1, cells * 2 + 1);
        _grid = new AStarGrid2D
        {
            Region = Region, CellSize = Vector2.One * Cell,
            DiagonalMode = AStarGrid2D.DiagonalModeEnum.OnlyIfNoObstacles,
        };
        _grid.Update();
        for (int x = Region.Position.X; x < Region.End.X; x++)
        for (int z = Region.Position.Y; z < Region.End.Y; z++)
        {
            var id = new Vector2I(x, z);
            Vector3 at = World(id, -1f);
            Vector3 clamped = Arena.ClampInside(at, Clearance, out _);
            bool solid = clamped.DistanceSquaredTo(at) > 0.01f || MapBlockers.Inside(at, Clearance);
            _grid.SetPointSolid(id, solid);
        }
    }

    public static Vector3 Next(Vector3 from, Vector3 target, CityPathState state, float dt)
    {
        using var timing = Prof.Scope("City.path");
        if (_grid is null) return from;
        state.Repath -= dt;
        var cell = new Vector2I(Mathf.RoundToInt(target.X / Cell), Mathf.RoundToInt(target.Z / Cell));
        if (state.Repath <= 0f || state.TargetCell.DistanceSquaredTo(cell) > 4)
        {
            Vector2I goal = FreeCell(target);
            state.TargetCell = cell;
            state.Goal = goal;
            state.Repath = 0.8f;
            state.Points = _grid.GetIdPath(FreeCell(from), goal, true).ToArray();
            state.Next = state.Points.Length > 1 ? 1 : 0;
        }
        if (state.Points.Length == 0) return from;
        while (state.Next < state.Points.Length - 1
            && from.DistanceSquaredTo(World(state.Points[state.Next], from.Y)) < 0.8f)
            state.Next++;
        return World(state.Points[state.Next], from.Y);
    }

    private static Vector2I FreeCell(Vector3 at)
    {
        var center = new Vector2I(Mathf.RoundToInt(at.X / Cell), Mathf.RoundToInt(at.Z / Cell));
        Vector2I best = Vector2I.Zero;
        float bestDistance = float.MaxValue;
        for (int radius = 0; radius <= 24; radius++)
        {
            for (int x = -radius; x <= radius; x++)
            for (int z = -radius; z <= radius; z++)
            {
                if (Mathf.Abs(x) != radius && Mathf.Abs(z) != radius) continue;
                var id = center + new Vector2I(x, z);
                if (!Region.HasPoint(id) || _grid!.IsPointSolid(id)) continue;
                float distance = World(id, at.Y).DistanceSquaredTo(at);
                if (distance < bestDistance) { best = id; bestDistance = distance; }
            }
            if (bestDistance < float.MaxValue) return best;
        }
        return best;
    }

    private static Vector3 World(Vector2I id, float y) => new(id.X * Cell, y, id.Y * Cell);
}
