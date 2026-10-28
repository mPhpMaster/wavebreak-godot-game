using Godot;

namespace Wavebreak.Game;

/// <summary>One connected submerged passage, from cliff mouth to an interior chimney.</summary>
public static class ReefCavePath
{
    public const float Radius = 6.5f;
    public static readonly Vector3[] Points = Build();
    public static Vector3 Entrance => Points[0];
    public static Vector3 Exit => Points[^1];
    public static float Width(int index) => Radius + 2.5f * Mathf.Exp(-Mathf.Pow((index - 36) / 8f, 2))
        + 3f * Mathf.Exp(-Mathf.Pow((index - 69) / 9f, 2));

    private static Vector3[] Build()
    {
        Vector3[] control = {
            new(120, 24, 30), new(125, 19, 30), new(130, 8, 48),
            new(111, -7, 63), new(92, -17, 51), new(101, -22, 30),
            new(80, -24, 16), new(62, -25, 29), new(51, -23, 9),
            new(39, -21, -9), new(22, -20, 1), new(20, -13, 20),
            new(20, -5, 20), new(20, 3.4f, 20),
        };
        var points = new Vector3[(control.Length - 1) * 8 + 1];
        for (int i = 0; i < control.Length - 1; i++)
            for (int j = 0; j < 8; j++)
                points[i * 8 + j] = control[i].CubicInterpolate(control[i + 1],
                    control[Mathf.Max(0, i - 1)], control[Mathf.Min(control.Length - 1, i + 2)], j / 8f);
        points[^1] = control[^1];
        return points;
    }

    public static Vector3 Nearest(Vector3 at, out float distance)
        => Nearest(at, out distance, out _);

    private static Vector3 Nearest(Vector3 at, out float distance, out int index)
    {
        Vector3 best = Points[0];
        index = 0;
        float square = float.MaxValue;
        for (int i = 1; i < Points.Length; i++)
        {
            Vector3 edge = Points[i] - Points[i - 1];
            float t = Mathf.Clamp((at - Points[i - 1]).Dot(edge) / edge.LengthSquared(), 0f, 1f);
            Vector3 p = Points[i - 1] + edge * t;
            float d = at.DistanceSquaredTo(p);
            if (d < square) { square = d; best = p; index = i; }
        }
        distance = Mathf.Sqrt(square);
        return best;
    }

    public static bool Contains(Vector3 at, float margin = 0f)
    { Nearest(at, out float d, out int i); return d < Width(i) + margin; }

    public static float Floor(Vector3 at)
    {
        Vector3 near = Nearest(at, out float distance, out int i);
        return distance < Width(i) + 1.5f ? Mathf.Min(UnderwaterTerrain.Height(at), near.Y - Width(i) + 0.1f)
            : UnderwaterTerrain.Height(at);
    }

    public static float TerrainDistance(Vector3 at) { Nearest(at, out float d, out int i); return d - Width(i) + 0.2f; }
}
