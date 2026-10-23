using System.Collections.Generic;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Visuals;

/// <summary>
/// Stylised environment pieces and the code that scatters them.
///
/// Every prop is a merged, vertex-coloured mesh in a few seeded variants, placed
/// through MultiMesh: a whole arena of rocks, trees, ruins and thousands of grass
/// tufts costs a few dozen draw calls in total, independent of how many things
/// are on the ground. That is the budget that keeps the crowd affordable.
///
/// Usage from a map:
/// <code>
///   PropTheme theme = PropTheme.Get("ashlands");
///   arena.AddChild(PropFactory.BuildFloor(Arena.Radius, theme));            // visual floor
///   PropFactory.Scatter(arena, run.Rng, Arena.Radius - 4f, theme);          // props
///   arena.AddChild(PropFactory.MakeWorldEnvironment(theme));                 // sky, fog, ambient
///   arena.AddChild(PropFactory.MakeSun(theme));
/// </code>
/// Props are visual only by default: in a game about crowds, a rock that blocks
/// enemies reshapes every fight, and that is a level-design decision. Pass
/// <c>collide: true</c> to give the large ones simple static colliders.
/// </summary>
public static class PropFactory
{
    public enum Kind { Rock, Tree, Crystal, Pillar, Arch, Wall, Mushroom, Grass, Flower }

    private const int Variants = 4;
    private static readonly Dictionary<string, Mesh> Cache = new();

    // ============================================================== scatter

    /// <summary>
    /// Scatter a theme's props over a disc of <paramref name="radius"/> and add
    /// them to <paramref name="parent"/>. Returns the container node.
    /// <paramref name="clearRadius"/> keeps the spawn point open.
    /// </summary>
    public static Node3D Scatter(Node3D parent, Rng rng, float radius, PropTheme theme, float density = 1f,
                                 float clearRadius = 6f, bool collide = false)
    {
        Node3D node = Scatter(rng, radius, theme, density, clearRadius, collide);
        parent.AddChild(node);
        return node;
    }

    /// <inheritdoc cref="Scatter(Node3D, Rng, float, PropTheme, float, float, bool)"/>
    public static Node3D Scatter(Rng rng, float radius, PropTheme theme, float density = 1f,
                                 float clearRadius = 6f, bool collide = false)
    {
        var root = new Node3D { Name = "Props" };
        float area100 = Mathf.Pi * radius * radius / 100f * density;
        var placed = new List<(Vector2 p, float r)>();
        StaticBody3D? body = collide ? new StaticBody3D { Name = "PropColliders" } : null;

        // Large props first, with spacing; clumps of rocks around them.
        var big = new List<(Kind kind, int count, float minR, float maxR)>
        {
            (Kind.Tree, (int)(theme.Trees * area100), 0.9f, 1.4f),
            (Kind.Pillar, (int)(theme.Ruins * area100 * 0.5f), 0.9f, 1.3f),
            (Kind.Arch, (int)(theme.Ruins * area100 * 0.2f), 1f, 1.2f),
            (Kind.Wall, (int)(theme.Ruins * area100 * 0.3f), 0.9f, 1.3f),
            (Kind.Crystal, (int)(theme.Crystals * area100), 0.7f, 1.5f),
            (Kind.Rock, (int)(theme.Rocks * area100), 0.5f, 1.8f),
            (Kind.Mushroom, (int)(theme.Mushrooms * area100), 0.6f, 1.3f),
        };

        foreach (var (kind, count, minS, maxS) in big)
        {
            if (count <= 0) continue;
            var xforms = new List<Transform3D>[Variants];
            for (int v = 0; v < Variants; v++) xforms[v] = new List<Transform3D>();
            float footprint = Footprint(kind);
            bool kit = theme.UsesKit(kind);
            float kitScale = kit && theme.KitScale.TryGetValue(kind, out float ksv) ? ksv : 1f;
            int made = 0;
            for (int attempt = 0; attempt < count * 8 && made < count; attempt++)
            {
                float a = rng.Range(0f, Mathf.Tau);
                float d = Mathf.Sqrt(rng.Float()) * radius;
                if (d < clearRadius + footprint) continue;
                var p = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
                float scale = rng.Range(minS, maxS);
                float r = footprint * scale;
                bool clash = false;
                foreach (var (q, qr) in placed)
                    if (p.DistanceSquaredTo(q) < (r + qr) * (r + qr)) { clash = true; break; }
                if (clash) continue;
                placed.Add((p, r));
                int variant = rng.RangeInt(0, Variants);
                // Arches and walls face the centre so they frame the play space.
                float yaw = kind is Kind.Arch or Kind.Wall ? -a + Mathf.Pi * 0.5f + rng.Range(-0.3f, 0.3f) : rng.Range(0f, Mathf.Tau);
                var basis = new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale * kitScale);
                xforms[variant].Add(new Transform3D(basis, new Vector3(p.X, 0f, p.Y)));
                made++;

                if (body is not null && kind is not Kind.Mushroom)
                    body.AddChild(new CollisionShape3D
                    {
                        Shape = new CylinderShape3D { Radius = r * 0.55f, Height = 3f },
                        Position = new Vector3(p.X, 1.5f, p.Y),
                    });

                // A few pebbles around large rocks and crystals: clumps read as
                // terrain, evenly spread singles read as scatter.
                if (kind is Kind.Rock && scale > 1.3f)
                    for (int k = 0; k < 3; k++)
                    {
                        float pa = rng.Range(0f, Mathf.Tau);
                        Vector2 pp = p + new Vector2(Mathf.Cos(pa), Mathf.Sin(pa)) * r * rng.Range(1.1f, 1.6f);
                        xforms[rng.RangeInt(0, Variants)].Add(new Transform3D(
                            new Basis(Vector3.Up, rng.Range(0f, Mathf.Tau)).Scaled(Vector3.One * rng.Range(0.25f, 0.45f)),
                            new Vector3(pp.X, 0f, pp.Y)));
                    }
            }
            for (int v = 0; v < Variants; v++)
                if (xforms[v].Count > 0)
                    // Kit meshes keep the artist's materials; procedural ones share the toon prop material.
                    root.AddChild(Multi(PropMesh(kind, theme, v), xforms[v], kit ? null : ToonMaterials.Prop, $"{kind}{v}"));
        }

        // Ground cover: grass and flowers, one MultiMesh each, coloured per instance.
        int grass = (int)(theme.Grass * area100);
        if (grass > 0) root.AddChild(Cover(rng, radius, clearRadius * 0.5f, grass, PropMesh(Kind.Grass, theme, 0),
                                           theme.GrassA, theme.GrassB, null, 0.7f, 1.5f, "Grass"));
        int flowers = (int)(theme.Flowers * area100);
        if (flowers > 0) root.AddChild(Cover(rng, radius, clearRadius, flowers, PropMesh(Kind.Flower, theme, 0),
                                             Colors.White, Colors.White, theme.FlowerColors, 0.8f, 1.3f, "Flowers"));

        if (body is not null) root.AddChild(body);
        return root;
    }

    private static float Footprint(Kind k) => k switch
    {
        Kind.Tree => 1.6f, Kind.Arch => 2.6f, Kind.Wall => 2.2f, Kind.Pillar => 1.0f,
        Kind.Crystal => 1.0f, Kind.Mushroom => 0.7f, _ => 1.1f,
    };

    private static MultiMeshInstance3D Multi(Mesh mesh, List<Transform3D> xforms, Material? mat, string name)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = xforms.Count,
        };
        for (int i = 0; i < xforms.Count; i++) mm.SetInstanceTransform(i, xforms[i]);
        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = mm,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
    }

    private static MultiMeshInstance3D Cover(Rng rng, float radius, float clear, int count, Mesh mesh, Color a, Color b,
                                            Color[]? palette, float minS, float maxS, string name)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
            InstanceCount = count,
        };
        for (int i = 0; i < count; i++)
        {
            float ang = rng.Range(0f, Mathf.Tau);
            float d = clear + Mathf.Sqrt(rng.Float()) * (radius - clear);
            var basis = new Basis(Vector3.Up, rng.Range(0f, Mathf.Tau)).Scaled(Vector3.One * rng.Range(minS, maxS));
            mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(Mathf.Cos(ang) * d, 0f, Mathf.Sin(ang) * d)));
            Color c = palette is { Length: > 0 } ? palette[rng.RangeInt(0, palette.Length)] : a.Lerp(b, rng.Float());
            mm.SetInstanceCustomData(i, c.SrgbToLinear());
        }
        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = mm,
            MaterialOverride = ToonMaterials.Foliage,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Grass beyond this is a colour on the floor shader anyway.
            VisibilityRangeEnd = 70f,
        };
    }

    // ================================================================ meshes

    /// <summary>One prop mesh, cached per theme, kind and variant. Public so a
    /// map can place hero props by hand (e.g. an arch over the spawn).</summary>
    public static Mesh PropMesh(Kind kind, PropTheme theme, int variant)
    {
        string key = $"{theme.Id}:{kind}:{variant}";
        if (Cache.TryGetValue(key, out Mesh? m) && GodotObject.IsInstanceValid(m)) return m;
        if (theme.UsesKit(kind))
        {
            string[] files = theme.Kit[kind];
            if (KitMesh(ArtAssets.Root + files[variant % files.Length]) is { } kitMesh)
            {
                Cache[key] = kitMesh;
                return kitMesh;
            }
        }
        var k = new MeshKit();
        uint seed = (uint)(variant * 7919 + (int)kind * 104729 + theme.Id.Length * 31);
        switch (kind)
        {
            case Kind.Rock: Rock(k, theme, seed); break;
            case Kind.Tree: Tree(k, theme, seed, variant); break;
            case Kind.Crystal: Crystal(k, theme, seed); break;
            case Kind.Pillar: Pillar(k, theme, seed, variant); break;
            case Kind.Arch: Arch(k, theme, seed); break;
            case Kind.Wall: Wall(k, theme, seed); break;
            case Kind.Mushroom: Mushroom(k, theme, seed); break;
            case Kind.Grass: GrassTuft(k); break;
            case Kind.Flower: Flower(k); break;
        }
        m = k.ToMesh();
        Cache[key] = m;
        return m;
    }

    /// <summary>
    /// Flatten an imported prop scene into one mesh (surfaces and materials
    /// kept, node transforms baked in), so it can go through MultiMesh like the
    /// procedural props. Null if the file is missing.
    /// </summary>
    public static ArrayMesh? KitMesh(string path)
    {
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene) return null;
        Node root = scene.Instantiate();
        var mesh = new ArrayMesh();
        void Walk(Node n, Transform3D xf)
        {
            Transform3D here = n is Node3D n3 ? xf * n3.Transform : xf;
            if (n is MeshInstance3D { Mesh: { } src } mi)
                for (int surf = 0; surf < src.GetSurfaceCount(); surf++)
                {
                    var st = new SurfaceTool();
                    st.AppendFrom(src, surf, here);
                    st.Commit(mesh);
                    mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mi.GetActiveMaterial(surf));
                }
            foreach (Node c in n.GetChildren()) Walk(c, here);
        }
        foreach (Node c in root.GetChildren()) Walk(c, Transform3D.Identity);
        root.Free();
        return mesh.GetSurfaceCount() > 0 ? mesh : null;
    }

    private static float H(uint seed, int a, int b = 0) => MeshKit.Hash(seed, a, b);

    private static void Rock(MeshKit k, PropTheme t, uint seed)
    {
        int lumps = 1 + (int)(H(seed, 1) * 2.5f);
        for (int i = 0; i < lumps; i++)
        {
            float s = i == 0 ? 1f : 0.45f + H(seed, i, 2) * 0.3f;
            var at = i == 0 ? Vector3.Zero : new Vector3((H(seed, i, 3) - 0.5f) * 1.6f, 0f, (H(seed, i, 4) - 0.5f) * 1.6f);
            var radii = new Vector3(0.9f + H(seed, i, 5) * 0.4f, 0.6f + H(seed, i, 6) * 0.6f, 0.8f + H(seed, i, 7) * 0.4f) * s;
            var xf = new Transform3D(new Basis(Vector3.Up, H(seed, i, 8) * 6f), at + Vector3.Up * radii.Y * 0.3f);
            k.Lump(xf, radii, t.RockA, t.RockB, seed + (uint)i * 17u, 0.25f, 7, 5);
        }
    }

    private static void Tree(MeshKit k, PropTheme t, uint seed, int variant)
    {
        float h = 2.6f + H(seed, 1) * 1.8f;
        float lean = (H(seed, 2) - 0.5f) * 0.3f;
        Vector3 top = new(lean, h * 0.62f, 0f);
        switch (t.TreeKind)
        {
            case TreeStyle.Pine:
            {
                k.Tube(Vector3.Zero, new Vector3(0f, h * 0.5f, 0f), 0.22f, 0.14f, t.Trunk, 0f, 6);
                k.Faceted = true;
                k.Sway = 0.35f;
                int tiers = 3 + (int)(H(seed, 3) * 1.5f);
                for (int i = 0; i < tiers; i++)
                {
                    float f = (float)i / tiers;
                    float y = h * (0.28f + f * 0.6f);
                    float r = 1.35f * (1f - f * 0.62f);
                    Color c = t.FoliageA.Lerp(t.FoliageB, f);
                    k.Cone(new Vector3(0f, y, 0f), new Vector3(0f, y + r * 1.2f, 0f), r, c, 0f, 7,
                           t.SnowCaps ? new Color(0.95f, 0.97f, 1f) : c.Lightened(0.15f));
                }
                k.Faceted = false;
                k.Sway = 0f;
                break;
            }
            case TreeStyle.Dead:
            {
                k.Tube(Vector3.Zero, top, 0.28f, 0.12f, t.Trunk, 0f, 6);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 1.7f + H(seed, i, 4) * 1.2f;
                    Vector3 from = Vector3.Zero.Lerp(top, 0.45f + i * 0.13f);
                    Vector3 dir = new(Mathf.Cos(a), 0.7f + H(seed, i, 5) * 0.5f, Mathf.Sin(a));
                    Vector3 to = from + dir.Normalized() * (0.8f + H(seed, i, 6) * 0.7f);
                    k.Tube(from, to, 0.09f, 0.02f, t.Trunk.Lightened(0.1f), 0f, 5);
                    if (t.Crystals > 0f && i % 2 == 0)
                        k.Sphere(to, 0.08f, t.Crystal, 2f, 5, 3); // embers caught in the branches
                }
                break;
            }
            case TreeStyle.Kelp:
            {
                // Coral/kelp fronds: several wavy stalks ending in bulbs.
                k.Sway = 0f;
                int stalks = 3 + (int)(H(seed, 7) * 3f);
                for (int i = 0; i < stalks; i++)
                {
                    float a = i * Mathf.Tau / stalks + H(seed, i, 8);
                    Vector3 p0 = new(Mathf.Cos(a) * 0.3f, 0f, Mathf.Sin(a) * 0.3f);
                    Vector3 prev = p0;
                    float sh = h * (0.6f + H(seed, i, 9) * 0.5f);
                    for (int s = 1; s <= 4; s++)
                    {
                        k.Sway = s / 4f;
                        Vector3 next = p0 + new Vector3(Mathf.Sin(s * 1.3f + i) * 0.3f + Mathf.Cos(a) * s * 0.12f, sh * s / 4f,
                                                        Mathf.Cos(s * 1.1f + i) * 0.3f + Mathf.Sin(a) * s * 0.12f);
                        k.Tube(prev, next, 0.16f - s * 0.025f, 0.13f - s * 0.025f, t.Trunk.Lerp(t.FoliageA, s / 5f), 0f, 6);
                        prev = next;
                    }
                    k.Sphere(prev, 0.26f, t.FoliageB, 0.3f, 7, 5);
                }
                k.Sway = 0f;
                break;
            }
            default:
            {
                k.Tube(Vector3.Zero, top, 0.3f, 0.18f, t.Trunk, 0f, 7);
                k.Tube(top * 0.7f, top * 0.7f + new Vector3(0.7f, 0.6f, 0.2f), 0.12f, 0.06f, t.Trunk, 0f, 5);
                k.Faceted = true;
                k.Sway = 0.4f;
                int blobs = 3 + variant % 2;
                for (int i = 0; i < blobs; i++)
                {
                    float a = i * Mathf.Tau / blobs + H(seed, i, 3);
                    var c = top + new Vector3(Mathf.Cos(a) * 0.65f, 0.35f + H(seed, i, 4) * 0.5f, Mathf.Sin(a) * 0.65f);
                    float r = 0.95f + H(seed, i, 5) * 0.35f;
                    k.Ellipsoid(new Transform3D(Basis.Identity, c), new Vector3(r, r * 0.85f, r), t.FoliageA, 0f, 8, 5, t.FoliageB);
                }
                k.Ellipsoid(new Transform3D(Basis.Identity, top + new Vector3(0f, 1.05f, 0f)), new Vector3(1.05f, 0.9f, 1.05f),
                            t.FoliageA.Lerp(t.FoliageB, 0.4f), 0f, 8, 5, t.FoliageB.Lightened(0.1f));
                k.Faceted = false;
                k.Sway = 0f;
                break;
            }
        }
    }

    private static void Crystal(MeshKit k, PropTheme t, uint seed)
    {
        k.Lump(Transform3D.Identity, new Vector3(0.8f, 0.35f, 0.8f), t.RockA, t.RockB, seed, 0.2f, 6, 4);
        k.Faceted = true;
        int n = 3 + (int)(H(seed, 1) * 4f);
        for (int i = 0; i < n; i++)
        {
            float a = H(seed, i, 2) * Mathf.Tau;
            float tilt = i == 0 ? 0.05f : 0.25f + H(seed, i, 3) * 0.4f;
            Vector3 dir = new Vector3(Mathf.Cos(a) * tilt, 1f, Mathf.Sin(a) * tilt).Normalized();
            float len = (i == 0 ? 1.8f : 0.7f + H(seed, i, 4) * 0.9f);
            float r = len * 0.18f;
            Vector3 b0 = new(Mathf.Cos(a) * 0.25f * (i == 0 ? 0f : 1f), 0.1f, Mathf.Sin(a) * 0.25f * (i == 0 ? 0f : 1f));
            var xf = new Transform3D(MeshKit.BasisAlong(dir), b0);
            k.Cylinder(xf, r, r * 0.9f, len * 0.75f, t.Crystal.Darkened(0.2f), 0.9f, 6, t.Crystal);
            k.Cone(b0 + dir * len * 0.75f, b0 + dir * len, r * 0.9f, t.Crystal, 1.3f, 6, t.Crystal.Lightened(0.6f));
        }
        k.Faceted = false;
    }

    private static void Pillar(MeshKit k, PropTheme t, uint seed, int variant)
    {
        bool broken = variant % 2 == 1;
        float h = broken ? 1.6f + H(seed, 1) * 1.2f : 3.6f + H(seed, 1) * 0.8f;
        k.Box(new Vector3(0f, 0.2f, 0f), new Vector3(1.3f, 0.4f, 1.3f), t.RuinA.Darkened(0.1f), 0f, t.RuinA);
        k.Faceted = true;
        k.Cylinder(new Transform3D(Basis.Identity, new Vector3(0f, 0.4f, 0f)), 0.48f, 0.44f, h, t.RuinA, 0f, 8, t.RuinA.Lightened(0.08f));
        k.Faceted = false;
        if (!broken)
        {
            k.Box(new Vector3(0f, 0.4f + h + 0.18f, 0f), new Vector3(1.2f, 0.36f, 1.2f), t.RuinA, 0f, t.RuinB);
        }
        else
        {
            // A broken top: a jagged lump, and the fallen drum beside it.
            k.Lump(new Transform3D(Basis.Identity, new Vector3(0f, 0.4f + h, 0f)), new Vector3(0.46f, 0.3f, 0.46f), t.RuinA, t.RuinB, seed, 0.35f, 6, 4);
            var fallen = new Transform3D(new Basis(Vector3.Forward, Mathf.Pi * 0.5f).Rotated(Vector3.Up, H(seed, 5) * 3f),
                                         new Vector3(1.3f, 0.44f, 0.4f));
            k.Faceted = true;
            k.Cylinder(fallen, 0.44f, 0.44f, 1.1f, t.RuinA, 0f, 8, t.RuinB);
            k.Faceted = false;
        }
        // Moss / snow on the base.
        k.Lump(new Transform3D(Basis.Identity, new Vector3(0.5f, 0.05f, 0.4f)), new Vector3(0.45f, 0.25f, 0.4f), t.RuinB.Darkened(0.2f), t.RuinB, seed + 9u, 0.3f, 6, 4);
    }

    private static void Arch(MeshKit k, PropTheme t, uint seed)
    {
        float span = 3.2f, h = 3.4f;
        for (int side = -1; side <= 1; side += 2)
        {
            k.Box(new Vector3(side * span * 0.5f, h * 0.5f, 0f), new Vector3(0.8f, h, 0.8f), t.RuinA.Darkened(0.08f), 0f, t.RuinA);
            k.Box(new Vector3(side * span * 0.5f, 0.15f, 0f), new Vector3(1.1f, 0.3f, 1.1f), t.RuinA.Darkened(0.2f));
        }
        // Voussoirs along a half circle.
        const int stones = 7;
        float r = span * 0.5f;
        for (int i = 0; i < stones; i++)
        {
            if (i == 5 && H(seed, 1) > 0.4f) continue; // a missing stone, sometimes
            float a = Mathf.Pi * (i + 0.5f) / stones;
            Vector3 c = new(Mathf.Cos(a) * r, h + Mathf.Sin(a) * r * 0.75f, 0f);
            var xf = new Transform3D(new Basis(Vector3.Back, a - Mathf.Pi * 0.5f), c);
            k.Box(xf, new Vector3(0.85f, 0.6f, 0.8f), t.RuinA, 0f, t.RuinB);
        }
        k.Lump(new Transform3D(Basis.Identity, new Vector3(span * 0.8f, 0f, 0.8f)), new Vector3(0.5f, 0.35f, 0.45f), t.RuinA, t.RuinB, seed, 0.3f, 6, 4);
    }

    private static void Wall(MeshKit k, PropTheme t, uint seed)
    {
        // Crumbling wall: courses of blocks, each course shorter and more ragged.
        for (int row = 0; row < 4; row++)
        {
            int blocks = 5 - row - (H(seed, row) > 0.5f ? 1 : 0);
            float off = row % 2 == 0 ? 0f : 0.35f;
            for (int i = 0; i < blocks; i++)
            {
                if (H(seed, row, i + 10) < 0.12f * row) continue;
                float x = -1.75f + off + i * 0.75f;
                Color c = t.RuinA.Lerp(t.RuinA.Darkened(0.2f), H(seed, row, i));
                var xf = new Transform3D(new Basis(Vector3.Up, (H(seed, row, i + 20) - 0.5f) * 0.15f), new Vector3(x, 0.25f + row * 0.5f, 0f));
                k.Box(xf, new Vector3(0.7f, 0.46f, 0.6f), c, 0f, row == 3 || i == blocks - 1 ? t.RuinB : c.Lightened(0.06f));
            }
        }
        k.Lump(new Transform3D(Basis.Identity, new Vector3(1.6f, 0f, 0.6f)), new Vector3(0.4f, 0.3f, 0.35f), t.RuinA, t.RuinB, seed, 0.3f, 6, 4);
    }

    private static void Mushroom(MeshKit k, PropTheme t, uint seed)
    {
        int n = 1 + (int)(H(seed, 1) * 3f);
        for (int i = 0; i < n; i++)
        {
            float s = i == 0 ? 1f : 0.4f + H(seed, i, 2) * 0.35f;
            float a = H(seed, i, 3) * Mathf.Tau;
            Vector3 at = i == 0 ? Vector3.Zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.6f;
            float h = 1.1f * s;
            k.Tube(at, at + new Vector3(0.05f, h, 0f), 0.16f * s, 0.12f * s, new Color(0.92f, 0.88f, 0.8f), 0f, 7);
            Vector3 cap = at + new Vector3(0.05f, h, 0f);
            k.Ellipsoid(new Transform3D(Basis.Identity, cap), new Vector3(0.6f, 0.32f, 0.6f) * s, t.MushroomCap.Darkened(0.2f), 0.15f, 10, 5, t.MushroomCap);
            for (int d = 0; d < 4; d++)
            {
                float da = d * 1.6f + H(seed, i, d + 4);
                k.Sphere(cap + new Vector3(Mathf.Cos(da) * 0.33f * s, 0.24f * s, Mathf.Sin(da) * 0.33f * s), 0.07f * s,
                         new Color(1f, 0.97f, 0.9f), 0.4f, 5, 3);
            }
        }
    }

    private static void GrassTuft(MeshKit k)
    {
        // White geometry: the per-instance colour paints it; tips are lighter.
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.26f;
            Vector3 b0 = new(Mathf.Cos(a) * 0.06f, 0f, Mathf.Sin(a) * 0.06f);
            Vector3 tip = b0 + new Vector3(Mathf.Cos(a) * 0.14f, 0.32f + (i % 3) * 0.08f, Mathf.Sin(a) * 0.14f);
            k.Sway = 0f;
            var xf = new Transform3D(MeshKit.BasisAlong(tip - b0).Scaled(new Vector3(1f, 1f, 0.25f)), b0);
            k.Sway = 1f;
            k.Cylinder(xf, 0.045f, 0.002f, (tip - b0).Length(), new Color(0.75f, 0.75f, 0.75f), 0f, 3, Colors.White);
        }
        k.Sway = 0f;
    }

    private static void Flower(MeshKit k)
    {
        k.Sway = 0.6f;
        k.Tube(Vector3.Zero, new Vector3(0f, 0.3f, 0f), 0.015f, 0.012f, new Color(0.3f, 0.55f, 0.25f), 0f, 4, caps: false);
        k.Sway = 1f;
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.Tau / 5f;
            k.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(Mathf.Cos(a) * 0.06f, 0.31f, Mathf.Sin(a) * 0.06f)),
                        new Vector3(0.055f, 0.02f, 0.055f), Colors.White, 0.25f, 6, 3);
        }
        k.Sphere(new Vector3(0f, 0.32f, 0f), 0.035f, new Color(1f, 0.85f, 0.3f), 0.4f, 5, 3);
        k.Sway = 0f;
    }

    // ================================================================ floor

    /// <summary>
    /// A visual floor disc with the theme's ground shader. No collider: the map
    /// keeps its own (it already has one, and two would fight).
    /// </summary>
    public static MeshInstance3D BuildFloor(float radius, PropTheme theme)
    {
        var mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.2f, RadialSegments = 64, Rings = 1 };
        return new MeshInstance3D
        {
            Name = "Floor",
            Mesh = mesh,
            Position = new Vector3(0f, -0.1f + 0.003f, 0f),
            MaterialOverride = FloorMaterial(theme),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static readonly Dictionary<string, ShaderMaterial> Floors = new();

    /// <summary>The theme's floor material, for maps that build their own ground
    /// geometry (ramps, platforms) and want it to match. World-space, so it
    /// tiles seamlessly across separate meshes.</summary>
    public static ShaderMaterial FloorMaterial(PropTheme t)
    {
        if (Floors.TryGetValue(t.Id, out ShaderMaterial? m) && GodotObject.IsInstanceValid(m)) return m;
        m = new ShaderMaterial { Shader = ToonMaterials.FloorShader };
        m.SetShaderParameter("color_a", t.FloorA);
        m.SetShaderParameter("color_b", t.FloorB);
        m.SetShaderParameter("color_patch", t.FloorPatch);
        m.SetShaderParameter("color_line", t.FloorLine);
        m.SetShaderParameter("patch_amount", t.PatchAmount);
        m.SetShaderParameter("line_strength", t.LineStrength);
        m.SetShaderParameter("slab_size", t.SlabSize);
        // Ember cracks glow; stone joints do not.
        m.SetShaderParameter("line_glow", t.Id == "ashlands" ? 0.9f : 0f);
        Floors[t.Id] = m;
        return m;
    }

    // ============================================================ sky & light

    /// <summary>Sky, ambient, fog, glow and tonemapping for the theme.</summary>
    public static WorldEnvironment MakeWorldEnvironment(PropTheme t) => new()
    {
        Name = "Environment",
        Environment = MakeEnvironment(t),
    };

    public static Godot.Environment MakeEnvironment(PropTheme t) => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Sky,
        Sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = t.SkyTop,
                SkyHorizonColor = t.SkyHorizon,
                GroundHorizonColor = t.GroundHorizon,
                GroundBottomColor = t.GroundHorizon.Darkened(0.4f),
                SunAngleMax = 25f,
                SkyCurve = 0.12f,
            },
        },
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = t.Ambient,
        AmbientLightEnergy = t.AmbientEnergy,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        TonemapWhite = 6f,
        FogEnabled = t.FogDensity > 0f,
        FogLightColor = t.Fog,
        FogDensity = t.FogDensity,
        FogSkyAffect = 0.4f,
        FogAerialPerspective = 0.3f,
        GlowEnabled = true,
        GlowIntensity = t.Glow,
        GlowBloom = 0.05f,
        GlowHdrThreshold = 1.1f,
        AdjustmentEnabled = true,
        AdjustmentSaturation = 1.12f,
        AdjustmentContrast = 1.05f,
    };

    public static DirectionalLight3D MakeSun(PropTheme t) => new()
    {
        Name = "Sun",
        Rotation = new Vector3(Mathf.DegToRad(t.SunPitchDegrees), Mathf.DegToRad(t.SunYawDegrees), 0f),
        LightColor = t.Sun,
        LightEnergy = t.SunEnergy,
        ShadowEnabled = true,
        ShadowBlur = 1.5f,
        DirectionalShadowMaxDistance = 60f,
    };
}
