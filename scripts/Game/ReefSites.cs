using Godot;

namespace Wavebreak.Game;

public static class ReefSites
{
    public static readonly Vector2[] Caves = { new(80f, 16f) };
    public static readonly Vector2 Wreck = new(-51f, -58f);

    public static (float Low, float High) GroundRange(Vector2 centre, float halfWidth, float halfLength)
    {
        float low = float.MaxValue, high = float.MinValue;
        for (int x = 0; x <= 4; x++)
            for (int z = 0; z <= 8; z++)
            {
                var at = new Vector3(centre.X - halfWidth + halfWidth * x / 2f, 0f,
                    centre.Y - halfLength + halfLength * z / 4f);
                float y = UnderwaterTerrain.Height(at);
                low = Mathf.Min(low, y); high = Mathf.Max(high, y);
            }
        return (low, high);
    }
}
