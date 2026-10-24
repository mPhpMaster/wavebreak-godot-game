using System.Collections.Generic;
using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// Shared meshes and materials for the expansion weapons.
///
/// Same rules as <see cref="CombatVisuals"/>: every shot of a kind is the same
/// shape, so each mesh is baked once with <see cref="MeshKit"/> and scaled per
/// instance, and materials are cached by tint. The difference is that these
/// meshes carry two-tone vertex colour (a bright edge, a darker body), so the
/// material multiplies the vertex colour by the weapon's tint instead of
/// painting the whole silhouette one flat colour.
///
/// All meshes are unit-sized: radius about 1, forward along -Z, so the
/// projectile's own transform and scale place them.
/// </summary>
internal static class ExpansionVisuals
{
    private static readonly Color Bright = new(1f, 1f, 1f);
    private static readonly Color Mid = new(0.72f, 0.72f, 0.72f);
    private static readonly Color Dark = new(0.32f, 0.32f, 0.36f);

    private static readonly Dictionary<Color, StandardMaterial3D> Materials = new();
    private static readonly Dictionary<string, ArrayMesh> Meshes = new();

    /// <summary>Unshaded, vertex colour times tint, blendable so the projectile
    /// can fade through <see cref="GeometryInstance3D.Transparency"/>.</summary>
    public static StandardMaterial3D Glow(Color tint)
    {
        if (Materials.TryGetValue(tint, out StandardMaterial3D? m) && GodotObject.IsInstanceValid(m)) return m;
        m = new StandardMaterial3D
        {
            AlbedoColor = new Color(tint.R * 1.35f, tint.G * 1.35f, tint.B * 1.35f, tint.A),
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        FxGuard.Track(m);   // fades out between the camera and the player
        Materials[tint] = m;
        return m;
    }

    private static ArrayMesh Cached(string key, System.Action<MeshKit> build)
    {
        if (Meshes.TryGetValue(key, out ArrayMesh? mesh) && GodotObject.IsInstanceValid(mesh)) return mesh;
        var kit = new MeshKit();
        build(kit);
        mesh = kit.ToMesh();
        Meshes[key] = mesh;
        return mesh;
    }

    /// <summary>Flat spinning disc with four hooked teeth (boomerang).</summary>
    public static ArrayMesh Chakram => Cached("chakram", k =>
    {
        k.Cylinder(new Transform3D(Basis.Identity, new Vector3(0f, -0.08f, 0f)), 0.75f, 0.75f, 0.16f, Mid, 0f, 12, Bright);
        k.Sphere(Vector3.Zero, 0.28f, Dark);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Tau / 4f;
            Vector3 root = new(Mathf.Sin(a) * 0.6f, 0f, Mathf.Cos(a) * 0.6f);
            Vector3 tip = new(Mathf.Sin(a + 0.6f) * 1.05f, 0f, Mathf.Cos(a + 0.6f) * 1.05f);
            k.Cone(root, tip, 0.18f, Bright, 0f, 5);
        }
    });

    /// <summary>A long curved fin blade, hub at the origin (orbiting blades).</summary>
    public static ArrayMesh Fin => Cached("fin", k =>
    {
        k.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, 0f, -0.35f)), new Vector3(0.22f, 0.07f, 0.85f), Bright, 0f, 8, 5);
        k.Cone(new Vector3(0f, 0f, -0.9f), new Vector3(0.12f, 0f, -1.35f), 0.12f, Bright, 0f, 5);
        k.Sphere(new Vector3(0f, 0f, 0.35f), 0.16f, Mid);
    });

    /// <summary>Low lumpy pool, height ~0.1 (trail hazards).</summary>
    public static ArrayMesh Pool => Cached("pool", k =>
    {
        k.Cylinder(Transform3D.Identity, 1f, 0.92f, 0.05f, Mid, 0f, 18, Bright);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.37f;
            float r = 0.25f + 0.12f * i;
            k.Sphere(new Vector3(Mathf.Sin(a) * r, 0.04f, Mathf.Cos(a) * r), 0.09f, Bright, 0f, 6, 4);
        }
    });

    /// <summary>Spiked shell (mines).</summary>
    public static ArrayMesh SpikedShell => Cached("mine", k =>
    {
        k.Sphere(Vector3.Zero, 0.7f, Mid, 0f, 10, 6);
        Vector3[] dirs =
        {
            Vector3.Up, new(1f, 0.3f, 0f), new(-1f, 0.3f, 0f), new(0f, 0.3f, 1f), new(0f, 0.3f, -1f),
            new(0.7f, 0.5f, 0.7f), new(-0.7f, 0.5f, -0.7f),
        };
        foreach (Vector3 d in dirs)
        {
            Vector3 n = d.Normalized();
            k.Cone(n * 0.6f, n * 1.05f, 0.14f, Bright, 0f, 5);
        }
        k.Sphere(new Vector3(0f, 0.55f, 0f), 0.16f, Bright);
    });

    /// <summary>Squat post with a swivel head and a barrel along -Z (turrets).</summary>
    public static ArrayMesh Sentry => Cached("sentry", k =>
    {
        k.Cylinder(new Transform3D(Basis.Identity, new Vector3(0f, -0.9f, 0f)), 0.55f, 0.35f, 0.7f, Dark, 0f, 8, Mid);
        k.Sphere(new Vector3(0f, 0f, 0f), 0.45f, Mid);
        k.Tube(new Vector3(0f, 0.05f, -0.2f), new Vector3(0f, 0.05f, -1.05f), 0.13f, 0.11f, Bright);
        k.Sphere(new Vector3(0f, 0.3f, 0.15f), 0.12f, Bright);
    });

    /// <summary>Stacked flattened rings around a dark core (vortex).</summary>
    public static ArrayMesh Whorl => Cached("whorl", k =>
    {
        k.Sphere(Vector3.Zero, 0.35f, Dark);
        k.Ellipsoid(Transform3D.Identity, new Vector3(1f, 0.08f, 1f), Mid, 0f, 16, 4);
        k.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, 0.12f, 0f)), new Vector3(0.7f, 0.07f, 0.7f), Bright, 0f, 14, 4);
        k.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, -0.12f, 0f)), new Vector3(0.55f, 0.06f, 0.55f), Bright, 0f, 12, 4);
    });

    /// <summary>A flat faceted pebble (ricochet).</summary>
    public static ArrayMesh Pebble => Cached("pebble", k =>
    {
        k.Faceted = true;
        k.Ellipsoid(Transform3D.Identity, new Vector3(0.8f, 0.32f, 1f), Mid, 0f, 7, 4, Bright);
        k.Faceted = false;
    });

    /// <summary>A rough falling stone (strikes).</summary>
    public static ArrayMesh Stone => Cached("stone", k =>
        k.Lump(Transform3D.Identity, new Vector3(1f, 1.1f, 1f), Mid, Bright, 0xA11u, 0.25f, 7, 5));

    /// <summary>A unit-radius ring of crest blocks (expanding wave).</summary>
    public static ArrayMesh Crest => Cached("crest", k =>
    {
        const int n = 36;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n;
            var basis = new Basis(Vector3.Up, a);
            var at = new Vector3(Mathf.Sin(a), 0.25f, Mathf.Cos(a));
            k.Box(new Transform3D(basis, at), new Vector3(0.2f, 0.5f, 0.06f), Mid, 0f, Bright);
        }
    });
}
