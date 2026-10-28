using System.Collections.Generic;
using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>
/// Hand-built landforms for maps with a <see cref="Data.StageDef.Layout"/>
/// (Stormwatch Harbour and Bellspire Ward). Built before the random terrain; every
/// blocker reserves its footprint, so mounds, shrines, scatter and hazards
/// keep out of it.
///
/// Physics: one box or cylinder shape per blocker on the arena's single
/// static body, so the whole old town is a few dozen Jolt shapes. Drawing:
/// house bodies, roofs, windows, doors and rock cores each go through one
/// MultiMesh, so the landforms cost a handful of draw calls.
///
/// Every blocker is also registered with <see cref="MapBlockers"/> (enemy
/// steering, spawn and placement rejection, the map views).
/// </summary>
public sealed partial class Arena
{
    /// <summary>Raised or solid features of the current map other than the
    /// registered blockers (mounds, platforms, ridges, landmarks, the
    /// lighthouse headland), for the map views: centre and radius.</summary>
    public static IReadOnlyList<(Vector2 At, float Radius)> Features => _features;
    private static readonly List<(Vector2 At, float Radius)> _features = new();
    public static int FeaturesVersion { get; private set; }

    /// <summary>Footprints that only reserve space (the plaza, the circles a
    /// box is covered with): not drawn as features.</summary>
    private readonly HashSet<int> _reserveOnly = new();

    /// <summary>Preferred hazard pool centres, used before random ones.</summary>
    private readonly List<Vector3> _hazardSpots = new();

    // Instanced dressing, flushed into MultiMeshes at the end.
    private readonly List<(Transform3D X, Color C)> _bodies = new();
    private readonly List<(Transform3D X, Color C)> _roofs = new();
    private readonly List<(Transform3D X, Color C)> _windows = new();
    private readonly List<(Transform3D X, Color C)> _trims = new();
    private readonly List<(Transform3D X, Color C)> _rounds = new();
    private readonly Dictionary<int, List<Transform3D>> _rocks = new();

    private static readonly Color[] HouseWalls =
    {
        new(0.94f, 0.88f, 0.76f), new(0.58f, 0.82f, 0.80f), new(0.95f, 0.64f, 0.56f),
        new(0.98f, 0.87f, 0.58f), new(0.64f, 0.77f, 0.94f), new(0.82f, 0.72f, 0.92f),
    };
    private static readonly Color[] Roofs =
    {
        new(0.78f, 0.36f, 0.30f), new(0.33f, 0.42f, 0.60f), new(0.24f, 0.55f, 0.58f), new(0.55f, 0.32f, 0.42f),
    };
    private static readonly Color Rock = new(0.42f, 0.44f, 0.49f);
    private static readonly Color Moss = new(0.40f, 0.56f, 0.50f);
    private static readonly Color WindowWarm = new(1.00f, 0.84f, 0.52f);
    private static readonly Color DoorWood = new(0.36f, 0.24f, 0.18f);
    private static readonly Color Hull = new(0.36f, 0.25f, 0.20f);

    public override void _ExitTree()
    {
        if (_restoreOcclusion) GetViewport().UseOcclusionCulling = _previousOcclusion;
        MapBlockers.End(this);
        CityNavigation.End(this);
        if (_stage.Layout == "city") GrappleAnchors.End();
    }

    private void BuildLandforms()
    {
        _features.Clear();
        FeaturesVersion++;
        if (_stage.Layout != "harbour" && _stage.Layout != "city") return;
        MapBlockers.Begin(this);
        if (_stage.Layout == "city") BuildCity();
        else BuildHarbour();
        FlushInstances();
    }

    /// <summary>Publish the drawn features (called after the random terrain,
    /// before hazards).</summary>
    private void SnapshotFeatures()
    {
        _features.Clear();
        for (int i = 0; i < _footprints.Count; i++)
            if (!_reserveOnly.Contains(i)) _features.Add(_footprints[i]);
        FeaturesVersion++;
    }

    /// <summary>The next hazard pool: a preferred spot if one is still free,
    /// else a random free one.</summary>
    private bool NextHazardSpot(out Vector3 at)
    {
        float r = _stage.HazardRadius + 0.5f;
        while (_hazardSpots.Count > 0)
        {
            Vector3 p = _hazardSpots[0];
            _hazardSpots.RemoveAt(0);
            var flat = new Vector2(p.X, p.Z);
            if (Overlaps(flat, r, 1f) || EdgeExcess(p) > -(r + 4f)) continue;
            _footprints.Add((flat, r));
            at = p;
            return true;
        }
        return TryPlace(r, out at);
    }

    // ================================================================ harbour

    private void BuildHarbour()
    {
        float Deg(float d) => Mathf.DegToRad(d);
        Vector2 Polar(float deg, float r) => new(Mathf.Cos(Deg(deg)) * r, Mathf.Sin(Deg(deg)) * r);

        // ---- Harbour Square: a paved disc, lamps, kept clear for the portal.
        Reserve(Vector2.Zero, 24f);
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 25f, BottomRadius = 25f, Height = 0.04f, RadialSegments = 48, Rings = 1 },
            Position = new Vector3(0f, 0.02f, 0f),
            MaterialOverride = Flat(new Color(0.62f, 0.60f, 0.58f)),
        });
        for (int i = 0; i < 6; i++) Lamp(Polar(30f + 60f * i, 21f));

        // ---- Old Town: six houses round the square, a second row behind
        // every street out of it. No straight line leaves the town.
        for (int i = 0; i < 6; i++)
            House(Polar(60f * i, 34f), Deg(60f * i + 90f), 22f, 10f, 15f + 1.5f * (i % 3), i);
        for (int i = 0; i < 6; i++)
            House(Polar(30f + 60f * i, 62f), Deg(30f + 60f * i + 90f), 26f, 10f, 17f + (i % 2) * 2f, i + 3);

        // ---- Four cliff spurs split the outer belt into four zones.
        foreach (float a in new[] { 45f, 135f, 225f, 315f })
            Cliff(Polar(a, 84f), Polar(a, 150f), 5.5f, 20f);

        // ---- Lighthouse Point (north): a raised headland, the lighthouse,
        // the keeper's cottage, sea stacks at its foot.
        var head = new Vector2(0f, -128f);
        Headland(head, 22f, 5f);
        Lighthouse(head + new Vector2(0f, -8f), 4.5f, 26f, 5f);
        House(head + new Vector2(12f, 8f), 0f, 9f, 7f, 7f, 2, baseY: 5f);
        SeaStack(new Vector2(-50f, -104f), 5f, 15f);
        SeaStack(new Vector2(52f, -110f), 5.5f, 17f);
        MapBlockers.AddZone(new MapBlockers.Zone("Lighthouse Point", head, 50f));

        // ---- Sunken Quarter (east): drowned houses in flooded streets.
        House(new Vector2(98f, -32f), Deg(15f), 16f, 10f, 12f, 0, sunken: true);
        House(new Vector2(126f, 18f), Deg(-12f), 12f, 12f, 13f, 1, sunken: true);
        House(new Vector2(148f, -22f), Deg(75f), 16f, 9f, 10f, 2, sunken: true);
        House(new Vector2(104f, 48f), Deg(40f), 10f, 10f, 11f, 3, sunken: true);
        House(new Vector2(132f, -58f), Deg(-30f), 12f, 8f, 9f, 4, sunken: true);
        foreach (Vector2 p in new Vector2[] { new(112f, -8f), new(138f, -2f), new(86f, 12f), new(116f, -44f), new(152f, 12f),
                                               new(124f, 42f), new(94f, -60f), new(150f, -44f), new(110f, 24f), new(140f, 60f) })
            _hazardSpots.Add(new Vector3(p.X, 0f, p.Y));
        MapBlockers.AddZone(new MapBlockers.Zone("Sunken Quarter", new Vector2(124f, 0f), 52f));

        // ---- Coral Canyon (south): two rock walls and coral spires.
        Cliff(new Vector2(-15f, 90f), new Vector2(-15f, 160f), 5f, 16f);
        Cliff(new Vector2(15f, 90f), new Vector2(15f, 160f), 5f, 16f);
        CoralSpire(new Vector2(-44f, 112f), 4f, 11f);
        CoralSpire(new Vector2(46f, 128f), 3.5f, 12f);
        CoralSpire(new Vector2(-38f, 150f), 3f, 9f);
        CoralSpire(new Vector2(40f, 96f), 3f, 10f);
        MapBlockers.AddZone(new MapBlockers.Zone("Coral Canyon", new Vector2(0f, 124f), 52f));

        // ---- The Docks (west): warehouses, moored ships, crates.
        House(new Vector2(-100f, -26f), Deg(90f), 22f, 13f, 13f, 1, warehouse: true);
        House(new Vector2(-100f, 28f), Deg(90f), 18f, 12f, 12f, 3, warehouse: true);
        Ship(new Vector2(-150f, -34f), Deg(90f), 26f, 8f);
        Ship(new Vector2(-152f, 30f), Deg(90f), 22f, 8f);
        Crates(new Vector2(-126f, 2f), 6f, 7f);
        MapBlockers.AddZone(new MapBlockers.Zone("The Docks", new Vector2(-124f, 0f), 52f));

        // Inner zones last: ZoneAt picks the nearest centre among those it is inside.
        MapBlockers.AddZone(new MapBlockers.Zone("Harbour Square", Vector2.Zero, 27f));
        MapBlockers.AddZone(new MapBlockers.Zone("Old Town", Vector2.Zero, 74f));
    }

    // ============================================================ landforms

    /// <summary>A house (or warehouse, or a drowned ruin): one collider and
    /// blocker, drawn as two facade halves with their own roofs, rows of lit
    /// windows on the long faces and a door facing the square.</summary>
    private void Blocker(Vector2 at, float yaw, float len, float depth, float top, MapBlockers.Shape look, Color tint)
    {
        MapBlockers.Add(new MapBlockers.Blocker(at, new Vector2(len * 0.5f, depth * 0.5f), yaw, top, look, tint));
        ReserveBox(at, yaw, len, depth);
    }

    private void Reserve(Vector2 at, float r)
    {
        _reserveOnly.Add(_footprints.Count);
        _footprints.Add((at, r));
    }

    /// <summary>Cover a box with circles along its long axis, for the sampler.</summary>
    private void ReserveBox(Vector2 at, float yaw, float len, float depth)
    {
        bool alongX = len >= depth;
        float longS = alongX ? len : depth, shortS = alongX ? depth : len;
        var axis = alongX ? new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw)) : new Vector2(-Mathf.Sin(yaw), Mathf.Cos(yaw));
        int n = Mathf.Max(1, Mathf.CeilToInt(longS / shortS));
        float step = longS / n;
        float r = Mathf.Sqrt(shortS * shortS + step * step) * 0.5f;
        for (int i = 0; i < n; i++) Reserve(at + axis * (-longS * 0.5f + step * (i + 0.5f)), r);
    }

    private void AddBoxCollider(Vector2 at, float yaw, Vector3 size, float baseY)
    {
        _body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = size },
            Transform = new Transform3D(new Basis(Vector3.Up, -yaw), new Vector3(at.X, baseY + size.Y * 0.5f, at.Y)),
        });
    }

    /// <summary>A unit-box instance transform: centre (at, y), local X along yaw, size.</summary>
    private static Transform3D BoxX(Vector2 at, float yaw, float y, Vector3 size)
    {
        var x = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
        var z = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        return new Transform3D(new Basis(x * size.X, Vector3.Up * size.Y, z * size.Z), new Vector3(at.X, y, at.Y));
    }

    /// <summary>A unit-prism roof with its ridge along local X, sitting on <paramref name="y"/>.</summary>
    private static Transform3D RoofX(Vector2 at, float yaw, float y, Vector3 size)
    {
        var x = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
        var z = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        // Prism: triangle across its X, extruded along its Z.
        return new Transform3D(new Basis(z * size.Z, Vector3.Up * size.Y, -x * size.X), new Vector3(at.X, y + size.Y * 0.5f, at.Y));
    }

    private static Transform3D CylX(Vector2 at, float y, float radius, float height)
        => new(Basis.Identity.Scaled(new Vector3(radius * 2f, height, radius * 2f)), new Vector3(at.X, y, at.Y));

    private void AddRock(Vector3 at, float size, int variant)
    {
        if (!_rocks.TryGetValue(variant, out List<Transform3D>? list)) _rocks[variant] = list = new List<Transform3D>();
        Mesh mesh = PropFactory.PropMesh(PropFactory.Kind.Rock, _theme, variant);
        Aabb box = mesh.GetAabb();
        float s = size * 2f / Mathf.Max(0.2f, Mathf.Max(box.Size.X, box.Size.Z));
        var basis = new Basis(Vector3.Up, _rng.RandfRange(0f, Mathf.Tau)).Scaled(new Vector3(s, s * _rng.RandfRange(0.8f, 1.2f), s));
        list.Add(new Transform3D(basis, at - Vector3.Up * (box.Position.Y * s)));
    }

    private void FlushInstances()
    {
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.88f };
        var glow = new StandardMaterial3D { VertexColorUseAsAlbedo = true, EmissionEnabled = true, Emission = WindowWarm, EmissionEnergyMultiplier = _stage.Layout == "city" ? 0.45f : 1.4f };
        Instanced(new BoxMesh(), _bodies, mat, "Houses");
        Instanced(new PrismMesh(), _roofs, mat, "Roofs");
        Instanced(new BoxMesh(), _trims, mat, "Trims");
        Instanced(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 1 }, _rounds, mat, "Rounds");
        Instanced(new BoxMesh(), _windows, glow, "Windows");
        if (_stage.Layout == "city")
        {
            glow.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            SpatialBatches.Build(this, new QuadMesh { Size = Vector2.One }, _cityWindows, glow, "CityWindows", 128f, 180f, false);
        }
        foreach ((int variant, List<Transform3D> xs) in _rocks)
        {
            Mesh mesh = PropFactory.PropMesh(PropFactory.Kind.Rock, _theme, variant);
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xs.Count };
            for (int i = 0; i < xs.Count; i++) mm.SetInstanceTransform(i, xs[i]);
            AddChild(new MultiMeshInstance3D
            {
                Name = "CliffRocks" + variant,
                Multimesh = mm,
                MaterialOverride = _theme.UsesKit(PropFactory.Kind.Rock) ? null : ToonMaterials.Prop,
            });
        }
    }

    private void Instanced(Mesh mesh, List<(Transform3D X, Color C)> list, Material material, string name)
    {
        if (list.Count == 0) return;
        if (_stage.Layout == "city")
        {
            SpatialBatches.Build(this, mesh, list, material, name, 128f, name == "Trims" ? 250f : 500f, name == "Houses");
            return;
        }
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = list.Count,
        };
        for (int i = 0; i < list.Count; i++)
        {
            mm.SetInstanceTransform(i, list[i].X);
            mm.SetInstanceColor(i, list[i].C);
        }
        AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mm, MaterialOverride = material });
    }
}

/// <summary>The lighthouse's sweeping beam: two long additive wedges turning
/// slowly. One node, one tiny _Process.</summary>
public sealed partial class LighthouseBeam : Node3D
{
    public override void _Ready()
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(1f, 0.92f, 0.6f, 0.16f),
            DisableFog = true,
        };
        foreach (float side in new[] { 0f, Mathf.Pi })
        {
            var cone = new CylinderMesh { TopRadius = 7f, BottomRadius = 0.6f, Height = 60f, RadialSegments = 10, Rings = 1, CapTop = false, CapBottom = false };
            AddChild(new MeshInstance3D
            {
                Mesh = cone,
                // Lying along +X (then turned by side), the wide end out.
                Transform = new Transform3D(new Basis(Vector3.Up, side) * new Basis(Vector3.Back, Mathf.Pi / 2f), Vector3.Zero)
                            * new Transform3D(Basis.Identity, new Vector3(0f, 30f, 0f)),
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    public override void _Process(double delta) => RotateY((float)delta * 0.35f);
}
