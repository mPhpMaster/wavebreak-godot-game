using Godot;
using Wavebreak.Data;

namespace Wavebreak.Game;

public static class ReefCurrents
{
    public static readonly ReefCurrentDef[] All =
    {
        new(new Vector2(-38f, -62f), new Vector2(74f, -62f), 10f, 3.2f),
        new(new Vector2(-72f, 12f), new Vector2(-22f, 49f), 9f, 2.6f),
    };

    public static Vector3 VelocityAt(Vector3 at)
    {
        if (!Arena.Current.Underwater) return Vector3.Zero;
        Vector3 push = Vector3.Zero;
        foreach (ReefCurrentDef flow in All)
        {
            Vector2 line = flow.To - flow.From;
            Vector2 flat = new(at.X, at.Z);
            float t = Mathf.Clamp((flat - flow.From).Dot(line) / line.LengthSquared(), 0f, 1f);
            float distance = flat.DistanceTo(flow.From + line * t);
            float height = at.Y - UnderwaterTerrain.Height(at);
            float weight = Mathf.Clamp(1f - distance / flow.Radius, 0f, 1f)
                * Mathf.Clamp(1f - Mathf.Abs(height - 7f) / 8f, 0f, 1f);
            Vector2 direction = line.Normalized();
            push += new Vector3(direction.X, 0f, direction.Y) * (flow.Speed * weight);
        }
        return push.LimitLength(3.5f); // A base-speed swimmer can always escape against it.
    }
}
