using Godot;

namespace Wavebreak.Game;

public sealed partial class Arena
{
    public const uint HookLayer = 1u << 5;
    public static bool IsCity => Current.Layout == "city";
    private readonly System.Collections.Generic.List<(Transform3D X, Color C)> _cityWindows = new();
    public int CityBuildingCount { get; private set; }
    internal System.Collections.Generic.IReadOnlyList<(Transform3D X, Color C)> CityWallInstances => _bodies;
    internal System.Collections.Generic.IReadOnlyList<(Transform3D X, Color C)> CityTrimInstances => _trims;
    private bool _restoreOcclusion, _previousOcclusion;

    private void BuildCity()
    {
        using var timing = Prof.Scope("Startup.cityGeometry");
        _previousOcclusion = GetViewport().UseOcclusionCulling;
        _restoreOcclusion = true;
        GetViewport().UseOcclusionCulling = true;
        CityRooftops.Begin(this);
        Reserve(Vector2.Zero, 15f);
        // Regular avenues remain wide enough for a giant's full capsule.
        for (int x = -10; x <= 10; x++)
        for (int z = -10; z <= 10; z++)
        {
            if (x == 0 || z == 0) continue;
            Vector2 at = new(x * 32f, z * 32f);
            if (at.Length() > 316f) continue;
            float height = 20f + ((x * x * 7 + z * z * 11 + x * z * 3 + 103) % 8) * 5f;
            CityTower(at, 13f + (x * x % 3), 13f + (z * z % 3), height,
                Mathf.Abs(x * 3 + z) % HouseWalls.Length);
        }
        CityTower(new Vector2(-18f, -22f), 12f, 12f, 13f, 1);
        CityTower(new Vector2(18f, -22f), 12f, 12f, 20f, 2);
        CityTower(new Vector2(-18f, 22f), 12f, 12f, 26f, 3);
        CityTower(new Vector2(18f, 22f), 12f, 12f, 33f, 4);
        CityNavigation.Build(this);
        foreach (var zone in new[] { (new Vector2(0, -230), "North Bellspire"), (new Vector2(230, 0), "Foundry Quarter"),
            (new Vector2(-230, 0), "Westgate Heights"), (new Vector2(0, 230), "Southward Terraces") })
            MapBlockers.AddZone(new MapBlockers.Zone(zone.Item2, zone.Item1, 130f));
        MapBlockers.AddZone(new MapBlockers.Zone("Bellspire Ward", new Vector2(0f, -55f), 90f));
    }

    private void WindowQuad(Vector3 position, float yaw)
    {
        Basis b = new(Vector3.Up, yaw);
        _cityWindows.Add((new Transform3D(new Basis(b.X * 1.1f, b.Y * 1.5f, b.Z), position), WindowWarm));
    }

    private void CityTower(Vector2 at, float width, float depth, float height, int palette)
    {
        CityBuildingCount++;
        Color wall = new Color(.77f, .72f, .62f).Lightened(palette * .015f);
        float roofRise = Mathf.Abs(at.X) < 24 && Mathf.Abs(at.Y) < 24 ? 0 : 3f;
        float masonryHeight = height - roofRise;
        Blocker(at, 0f, width, depth, height, MapBlockers.Shape.Box, wall);
        var solid = new StaticBody3D
        {
            Name = "HookTower" + CityBuildingCount,
            CollisionLayer = 1u | HookLayer, CollisionMask = 0,
            Position = new Vector3(at.X, 0f, at.Y),
        };
        solid.AddChild(new CollisionShape3D
        {
            Position = Vector3.Up * masonryHeight * 0.5f,
            Shape = new BoxShape3D { Size = new Vector3(width, masonryHeight, depth) },
        });
        if (roofRise > 0)
            solid.AddChild(new CollisionShape3D
            {
                Position = Vector3.Up * (height - roofRise * .5f),
                Shape = new PrismMesh { Size = new Vector3(width, roofRise, depth) }.CreateConvexShape(),
            });
        AddChild(solid);
        CityRooftops.Register(solid, at, width, depth, height, roofRise);
        AddChild(new OccluderInstance3D
        {
            Name = "TowerOccluder" + CityBuildingCount,
            Position = new Vector3(at.X, masonryHeight * .5f, at.Y),
            Occluder = new BoxOccluder3D { Size = new Vector3(width - .12f, masonryHeight - .12f, depth - .12f) },
        });
        float wallHeight = masonryHeight - (roofRise > 0 ? 0 : .25f);
        _bodies.Add((BoxX(at, 0f, wallHeight * 0.5f, new Vector3(width, wallHeight, depth)), wall));
        // A flush roof plate: its top and the physical roof are identical.
        if (roofRise == 0)
            _trims.Add((BoxX(at, 0f, height - .125f, new Vector3(width, .25f, depth)), Roofs[palette % Roofs.Length]));
        else _roofs.Add((BoxX(at, 0f, height - roofRise * .5f, new Vector3(width, roofRise, depth)), new Color(.48f, .21f, .13f)));
        foreach (float x in new[] { -width * .5f, width * .5f })
            foreach (float z in new[] { -depth * .5f, depth * .5f })
                _trims.Add((BoxX(at + new Vector2(x, z), 0, masonryHeight * .5f, new Vector3(.24f, masonryHeight, .24f)), DoorWood));
        for (float y = 3f; y < masonryHeight - 1f; y += 4f)
        {
            _trims.Add((BoxX(at, 0f, y - 0.3f, new Vector3(width + 0.12f, 0.2f, depth + 0.12f)), wall.Darkened(0.22f)));
            for (int side = -1; side <= 1; side += 2)
            for (float offset = -width * 0.5f + 2f; offset < width * 0.5f - 1f; offset += 3f)
            {
                WindowQuad(new Vector3(at.X + offset, y + .75f, at.Y + side * (depth * .5f + .03f)),
                    side < 0 ? 0f : Mathf.Pi);
                WindowQuad(new Vector3(at.X + side * (width * .5f + .03f), y + .75f, at.Y + offset),
                    side < 0 ? -Mathf.Pi / 2f : Mathf.Pi / 2f);
            }
        }
        _trims.Add((BoxX(at + new Vector2(0f, -depth * 0.5f - 0.03f), 0f, 1.35f,
            new Vector3(2f, 2.7f, 0.08f)), DoorWood));
    }
}

