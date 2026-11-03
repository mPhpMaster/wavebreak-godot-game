using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// The shapes that tell look-alike weapons apart (docs/weapon-audit.md,
/// <see cref="WeaponLooks"/>). Same habits as <see cref="ExpansionVisuals"/>:
/// baked once with <see cref="MeshKit"/>, unit-sized, forward along -Z, grey
/// two-tone vertex colour that the material multiplies by the weapon's tint, so
/// a silhouette carries the identity and the colour stays the weapon's own.
/// A few carry a fixed accent (a fuse, wasp stripes) because the shape alone is
/// not enough at play distance.
/// </summary>
internal static class LookMeshes
{
    private static readonly Color Bright = new(1f, 1f, 1f);
    private static readonly Color Mid = new(0.72f, 0.72f, 0.74f);
    private static readonly Color Dark = new(0.30f, 0.30f, 0.34f);
    private static readonly Color Ink = new(0.12f, 0.12f, 0.14f);

    private static readonly Dictionary<string, ArrayMesh> Meshes = new();

    private static ArrayMesh Cached(string key, Action<MeshKit> build)
    {
        if (Meshes.TryGetValue(key, out ArrayMesh? mesh) && GodotObject.IsInstanceValid(mesh)) return mesh;
        var kit = new MeshKit();
        build(kit);
        mesh = kit.ToMesh();
        Meshes[key] = mesh;
        return mesh;
    }

    // ------------------------------------------------------------ shots

    /// <summary>A long faceted glass splinter (Splinter Rail).</summary>
    public static ArrayMesh Shard => Cached("shard", k =>
    {
        k.Faceted = true;
        k.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, 0f, -0.1f)), new Vector3(0.22f, 0.14f, 1.1f), Bright, 0f, 4, 3, Mid);
        k.Ellipsoid(new Transform3D(new Basis(Vector3.Up, 0.5f), new Vector3(0.18f, 0f, 0.75f)), new Vector3(0.08f, 0.06f, 0.3f), Mid, 0f, 4, 3);
        k.Faceted = false;
    });

    /// <summary>A thin drawn-glass needle with an eye (Glasswire Needles).</summary>
    public static ArrayMesh Needle => Cached("needle", k =>
    {
        k.Tube(new Vector3(0f, 0f, 1.0f), new Vector3(0f, 0f, -1.3f), 0.07f, 0.005f, Mid, 0f, 5, true, Bright);
        k.Sphere(new Vector3(0f, 0f, 1.05f), 0.11f, Bright, 0f, 6, 4);
    });

    /// <summary>A six-point spark star (Fan of Sparks).</summary>
    public static ArrayMesh Spark => Cached("spark", k =>
    {
        k.Sphere(Vector3.Zero, 0.32f, Bright, 0f, 6, 4);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.Tau / 6f + 0.3f;
            var dir = new Vector3(MathF.Sin(a), (i % 2 == 0 ? 0.35f : -0.35f), MathF.Cos(a)).Normalized();
            k.Cone(dir * 0.2f, dir * (i % 2 == 0 ? 1.1f : 0.75f), 0.13f, Bright, 0f, 4);
        }
    });

    /// <summary>A barbed salt spike on a short shaft (Brinespike).</summary>
    public static ArrayMesh Barb => Cached("barb", k =>
    {
        k.Faceted = true;
        k.Cone(new Vector3(0f, 0f, 0.2f), new Vector3(0f, 0f, -1.2f), 0.34f, Bright, 0f, 5, Bright);
        k.Faceted = false;
        k.Tube(new Vector3(0f, 0f, 0.15f), new Vector3(0f, 0f, 1.25f), 0.12f, 0.09f, Mid, 0f, 6);
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.Tau / 3f;
            var side = new Vector3(MathF.Sin(a), MathF.Cos(a), 0f);
            k.Cone(new Vector3(0f, 0f, -0.35f) + side * 0.18f, new Vector3(0f, 0f, 0.15f) + side * 0.5f, 0.09f, Mid, 0f, 4);
        }
    });

    /// <summary>A slender white-hot tracer with a bright core (Searing Longshot).</summary>
    public static ArrayMesh Tracer => Cached("tracer", k =>
    {
        k.Ellipsoid(Transform3D.Identity, new Vector3(0.16f, 0.16f, 1.5f), Mid, 0f, 6, 5);
        k.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, 0f, -0.6f)), new Vector3(0.12f, 0.12f, 0.6f), Bright, 0f, 6, 4);
    });

    /// <summary>A small round pellet (Reefbreaker Shotgun).</summary>
    public static ArrayMesh Pellet => Cached("pellet", k =>
    {
        k.Sphere(Vector3.Zero, 0.8f, Mid, 0f, 8, 5);
        k.Sphere(new Vector3(0.25f, 0.3f, -0.25f), 0.3f, Bright, 0f, 6, 4);
    });

    /// <summary>A soft comet: bright head and a trailing glow (Wisp Seeker).</summary>
    public static ArrayMesh Wisp => Cached("wisp", k =>
    {
        k.Sphere(Vector3.Zero, 0.5f, Bright, 0f, 10, 6);
        k.Sphere(Vector3.Zero, 0.78f, Mid, 0f, 10, 6);
        k.Tube(new Vector3(0f, 0f, 0.35f), new Vector3(0f, 0f, 1.9f), 0.5f, 0.02f, Mid, 0f, 8, false);
    });

    /// <summary>A long spear with a broad head and a guard (Judgement Lance).</summary>
    public static ArrayMesh Lance => Cached("lance", k =>
    {
        k.Tube(new Vector3(0f, 0f, 2.0f), new Vector3(0f, 0f, -0.5f), 0.08f, 0.08f, Mid, 0f, 6);
        k.Faceted = true;
        k.Cone(new Vector3(0f, 0f, -0.45f), new Vector3(0f, 0f, -1.9f), 0.32f, Bright, 0f, 4, Bright);
        k.Faceted = false;
        k.Box(new Transform3D(Basis.Identity, new Vector3(0f, 0f, -0.4f)), new Vector3(0.75f, 0.08f, 0.1f), Bright);
    });

    /// <summary>A striped wasp with two wings and a sting (Blightwasp Swarm).</summary>
    public static ArrayMesh Wasp => Cached("wasp", k =>
    {
        for (int i = 0; i < 4; i++)
            k.Sphere(new Vector3(0f, 0f, -0.45f + i * 0.3f), 0.3f - i * 0.03f, i % 2 == 0 ? Bright : Ink, 0f, 8, 5);
        k.Cone(new Vector3(0f, 0f, 0.5f), new Vector3(0f, 0f, 0.95f), 0.1f, Ink, 0f, 4);
        for (int s = -1; s <= 1; s += 2)
            k.Ellipsoid(new Transform3D(new Basis(Vector3.Forward, s * 0.35f), new Vector3(s * 0.42f, 0.22f, -0.15f)),
                new Vector3(0.38f, 0.04f, 0.18f), Mid, 0f, 8, 4);
    });

    /// <summary>A double-cone brass spindle with a thread band (Tempest Spindle).</summary>
    public static ArrayMesh Spindle => Cached("spindle", k =>
    {
        k.Cone(Vector3.Zero, new Vector3(0f, 0f, -1.1f), 0.38f, Mid, 0f, 8, Bright);
        k.Cone(Vector3.Zero, new Vector3(0f, 0f, 1.1f), 0.38f, Mid, 0f, 8, Dark);
        k.Cylinder(new Transform3D(new Basis(Vector3.Right, Mathf.Pi * 0.5f), new Vector3(0f, 0f, -0.06f)), 0.48f, 0.48f, 0.12f, Bright, 0f, 12, Bright);
    });

    /// <summary>A dark iron shell with a band and a lit fuse (Mortar Shell).</summary>
    public static ArrayMesh MortarShell => Cached("mortar", k =>
    {
        k.Sphere(Vector3.Zero, 0.85f, Dark, 0f, 10, 7);
        k.Cylinder(new Transform3D(Basis.Identity, new Vector3(0f, -0.08f, 0f)), 0.88f, 0.88f, 0.16f, Mid, 0f, 12, Mid);
        k.Tube(new Vector3(0f, 0.75f, 0f), new Vector3(0.1f, 1.15f, 0f), 0.08f, 0.06f, Mid, 0f, 5);
        k.Sphere(new Vector3(0.12f, 1.22f, 0f), 0.2f, new Color(1f, 0.95f, 0.55f), 1f, 6, 4);
    });

    /// <summary>A fat spotted pod with a cap (Bloomcap Spores).</summary>
    public static ArrayMesh Pod => Cached("pod", k =>
    {
        k.Ellipsoid(Transform3D.Identity, new Vector3(0.75f, 0.85f, 0.75f), Mid, 0f, 10, 6);
        k.Dome(new Transform3D(Basis.Identity, new Vector3(0f, 0.35f, 0f)), new Vector3(0.95f, 0.7f, 0.95f), 0f, Bright);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.26f;
            k.Sphere(new Vector3(MathF.Sin(a) * 0.62f, 0.62f + 0.12f * (i % 2), MathF.Cos(a) * 0.62f), 0.14f, Dark, 0f, 6, 4);
        }
    });

    /// <summary>A small sprouting seed (Bloomcap's split pods).</summary>
    public static ArrayMesh Seedling => Cached("seedling", k =>
    {
        k.Sphere(Vector3.Zero, 0.55f, Mid, 0f, 8, 5);
        k.Cone(new Vector3(0f, 0.4f, 0f), new Vector3(0.35f, 1.05f, 0f), 0.2f, Bright, 0f, 4);
        k.Cone(new Vector3(0f, 0.4f, 0f), new Vector3(-0.3f, 0.95f, 0.1f), 0.16f, Bright, 0f, 4);
    });

    /// <summary>A chunk of old ice with crystal points (Hailstone Cluster).</summary>
    public static ArrayMesh Hailstone => Cached("hailstone", k =>
    {
        k.Lump(Transform3D.Identity, new Vector3(0.75f, 0.7f, 0.75f), Mid, Bright, 0x4A11u, 0.2f, 6, 4);
        Vector3[] dirs = { new(1f, 0.4f, 0f), new(-0.6f, 0.7f, 0.5f), new(0.1f, 0.5f, -1f), new(-0.4f, -0.6f, -0.7f) };
        foreach (Vector3 d in dirs)
        {
            Vector3 n = d.Normalized();
            k.Cone(n * 0.5f, n * 1.15f, 0.2f, Bright, 0f, 4);
        }
    });

    /// <summary>A black cannonball with a smoking fuse hole (Broadside Barrage).</summary>
    public static ArrayMesh Cannonball => Cached("cannonball", k =>
    {
        k.Sphere(Vector3.Zero, 0.9f, Ink, 0f, 10, 7);
        k.Sphere(new Vector3(-0.35f, 0.4f, 0.3f), 0.3f, Mid, 0f, 6, 4);
        k.Sphere(new Vector3(0f, 0.95f, 0f), 0.18f, new Color(1f, 0.75f, 0.35f), 1f, 6, 4);
    });

    /// <summary>A burning chunk with flame tongues trailing up (Sunken Sun).</summary>
    public static ArrayMesh Ember => Cached("ember", k =>
    {
        k.Lump(Transform3D.Identity, new Vector3(0.7f, 0.7f, 0.7f), Dark, Mid, 0xE3B3u, 0.3f, 6, 4);
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.Tau / 5f;
            var at = new Vector3(MathF.Sin(a) * 0.4f, 0.3f, MathF.Cos(a) * 0.4f);
            k.Cone(at, at + new Vector3(MathF.Sin(a) * 0.2f, 1.1f + 0.3f * (i % 2), MathF.Cos(a) * 0.2f), 0.24f, Bright, 0f, 5);
        }
    });

    /// <summary>A jagged molten spike rising from the ground, base at the origin
    /// (Worldforge Maul's quake).</summary>
    public static ArrayMesh MagmaSpike => Cached("magma", k =>
    {
        k.Faceted = true;
        k.Cone(Vector3.Zero, new Vector3(0.1f, 1.7f, 0f), 0.5f, Dark, 0f, 5, Bright);
        k.Cone(new Vector3(0.35f, 0f, 0.2f), new Vector3(0.55f, 0.9f, 0.3f), 0.25f, Mid, 0f, 4, Bright);
        k.Cone(new Vector3(-0.3f, 0f, -0.25f), new Vector3(-0.5f, 1.0f, -0.35f), 0.25f, Mid, 0f, 4, Bright);
        k.Faceted = false;
    });

    /// <summary>A layered paper hive with a dark entrance (Plague Hive).</summary>
    public static ArrayMesh Hive => Cached("hive", k =>
    {
        for (int i = 0; i < 4; i++)
            k.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, -0.7f + i * 0.32f, 0f)),
                new Vector3(0.7f - i * 0.12f, 0.2f, 0.7f - i * 0.12f), i % 2 == 0 ? Mid : Bright, 0f, 10, 5);
        k.Sphere(new Vector3(0f, -0.45f, -0.55f), 0.18f, Ink, 0f, 6, 4);
    });

    /// <summary>A barbed harpoon head on a long shaft (Deepwater Harpoon).</summary>
    public static ArrayMesh Harpoon => Cached("harpoon", k =>
    {
        k.Tube(new Vector3(0f, 0f, 1.6f), new Vector3(0f, 0f, -0.4f), 0.07f, 0.07f, Mid, 0f, 6);
        k.Faceted = true;
        k.Cone(new Vector3(0f, 0f, -0.35f), new Vector3(0f, 0f, -1.3f), 0.25f, Bright, 0f, 4, Bright);
        k.Faceted = false;
        for (int s = -1; s <= 1; s += 2)
            k.Cone(new Vector3(s * 0.08f, 0f, -0.5f), new Vector3(s * 0.38f, 0f, -0.05f), 0.07f, Bright, 0f, 4);
        k.Cylinder(new Transform3D(new Basis(Vector3.Right, Mathf.Pi * 0.5f), new Vector3(0f, 0f, 1.6f)), 0.14f, 0.14f, 0.1f, Dark, 0f, 8);
    });

    /// <summary>A fan of three needles (Glass Gale).</summary>
    public static ArrayMesh NeedleFan => Cached("needlefan", k =>
    {
        for (int i = -1; i <= 1; i++)
        {
            var b = new Basis(Vector3.Up, i * 0.35f);
            Vector3 tail = b * new Vector3(0f, 0f, 0.9f), tip = b * new Vector3(0f, 0f, -1.1f);
            k.Tube(tail, tip, 0.07f, 0.005f, i == 0 ? Bright : Mid, 0f, 5, true, Bright);
        }
        k.Sphere(Vector3.Zero, 0.16f, Bright, 0f, 6, 4);
    });

    /// <summary>A storm fang: a slim swept blade with a forked tail, flat
    /// (Stormfang Blades).</summary>
    public static ArrayMesh Fang => Cached("fang", k =>
    {
        k.Faceted = true;
        k.Ellipsoid(new Transform3D(new Basis(Vector3.Up, 0.5f), new Vector3(0.15f, 0f, -0.25f)), new Vector3(0.22f, 0.05f, 0.95f), Bright, 0f, 4, 3, Mid);
        k.Ellipsoid(new Transform3D(new Basis(Vector3.Up, -0.5f), new Vector3(-0.15f, 0f, -0.25f)), new Vector3(0.22f, 0.05f, 0.95f), Bright, 0f, 4, 3, Mid);
        k.Faceted = false;
        k.Sphere(new Vector3(0f, 0f, 0.35f), 0.18f, Dark, 0f, 6, 4);
        for (int s = -1; s <= 1; s += 2)
            k.Cone(new Vector3(0f, 0f, 0.4f), new Vector3(s * 0.32f, 0f, 1.05f), 0.08f, Mid, 0f, 4);
    });

    /// <summary>Eight crystal shards wheeling round a dark core, flat (Frozen Gyre).</summary>
    public static ArrayMesh ShardWheel => Cached("shardwheel", k =>
    {
        k.Sphere(Vector3.Zero, 0.3f, Dark, 0f, 8, 5);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f;
            var at = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * 0.7f;
            var tangent = new Vector3(MathF.Cos(a), 0.1f, -MathF.Sin(a));
            k.Faceted = true;
            k.Cone(at - tangent * 0.2f, at + tangent * 0.45f, 0.14f, i % 2 == 0 ? Bright : Mid, 0f, 4);
            k.Faceted = false;
        }
    });

    /// <summary>Spore patch: a low disc with mushroom caps (Bloomtide).</summary>
    public static ArrayMesh SporePatch => Cached("sporepatch", k =>
    {
        k.Cylinder(Transform3D.Identity, 1f, 0.9f, 0.04f, Dark, 0f, 16, Mid);
        for (int i = 0; i < 6; i++)
        {
            float a = i * 1.05f;
            float r = 0.3f + 0.1f * i;
            var at = new Vector3(MathF.Sin(a) * r, 0f, MathF.Cos(a) * r);
            k.Tube(at, at + Vector3.Up * 0.25f, 0.05f, 0.04f, Mid, 0f, 5, false);
            k.Dome(new Transform3D(Basis.Identity, at + Vector3.Up * 0.25f), new Vector3(0.16f, 0.12f, 0.16f), 0f, Bright);
        }
    });

    /// <summary>A unit ring of upward ice spikes (Winter's Keep).</summary>
    public static ArrayMesh IceRing => Cached("icering", k =>
    {
        const int n = 22;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n;
            var at = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a));
            k.Faceted = true;
            k.Cone(at, at + new Vector3(0f, 0.75f + 0.35f * (i % 3 == 0 ? 1 : 0), 0f), 0.11f, Bright, 0f, 4, Bright);
            k.Faceted = false;
        }
    });

    /// <summary>A third of a ring of crest blocks, centred on -Z (Breakwater
    /// Cleaver's wave: it rolls forward, not all round).</summary>
    public static ArrayMesh CrestArc => Cached("crestarc", k =>
    {
        const int n = 14;
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Pi + (i / (float)(n - 1) - 0.5f) * Mathf.Tau / 3f;
            var basis = new Basis(Vector3.Up, a);
            var at = new Vector3(MathF.Sin(a), 0.3f, MathF.Cos(a));
            k.Box(new Transform3D(basis, at), new Vector3(0.24f, 0.6f, 0.07f), Mid, 0f, Bright);
            k.Sphere(at + new Vector3(0f, 0.32f, 0f), 0.08f, Bright, 0f, 5, 3);
        }
    });

    // ------------------------------------------------------- melee and fields

    /// <summary>A flat crescent sweep, unit radius, arc centred on -Z and
    /// spanning +-60 degrees, thick at the middle and thin at the tips
    /// (Cleaver Arc, Skyline Twin Swords). Scaled per swing.</summary>
    public static ArrayMesh Crescent => Cached("crescent", k =>
    {
        const int n = 16;
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1) - 0.5f;
            float a = Mathf.Pi + u * Mathf.Pi * 2f / 3f;
            float thick = 0.06f + 0.22f * (1f - MathF.Abs(u) * 2f);
            var at = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * (1f - thick * 0.5f);
            k.Box(new Transform3D(new Basis(Vector3.Up, a), at), new Vector3(0.15f, 0.03f, thick), i % 3 == 0 ? Bright : Mid);
        }
    });

    /// <summary>A hair-thin steel arc with a glint at each tip, same span as
    /// <see cref="Crescent"/> (Skyline Twin Swords: two of these cross).</summary>
    public static ArrayMesh BladeArc => Cached("bladearc", k =>
    {
        const int n = 18;
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1) - 0.5f;
            float a = Mathf.Pi + u * Mathf.Pi * 2f / 3f;
            var at = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a));
            k.Box(new Transform3D(new Basis(Vector3.Up, a), at), new Vector3(0.13f, 0.03f, 0.05f), Bright);
        }
        for (int s = -1; s <= 1; s += 2)
        {
            float a = Mathf.Pi + s * Mathf.Pi / 3f;
            k.Sphere(new Vector3(MathF.Sin(a), 0.02f, MathF.Cos(a)), 0.07f, Bright, 0f, 6, 4);
        }
    });

    /// <summary>Two thin crossed slashes (Riptide Blades), unit length along -Z.</summary>
    public static ArrayMesh CrossCut => Cached("crosscut", k =>
    {
        for (int s = -1; s <= 1; s += 2)
            k.Box(new Transform3D(new Basis(Vector3.Up, s * 0.35f), new Vector3(0f, 0f, -0.5f)), new Vector3(0.06f, 0.03f, 1f), Bright);
    });

    /// <summary>A cracked crater: a jagged rim and radiating cracks, unit radius
    /// (Forgefall Maul).</summary>
    public static ArrayMesh Crater => Cached("crater", k =>
    {
        k.Faceted = true;
        for (int i = 0; i < 10; i++)
        {
            float a = i * Mathf.Tau / 10f;
            var at = new Vector3(MathF.Sin(a), 0.05f, MathF.Cos(a)) * 0.85f;
            k.Lump(new Transform3D(Basis.Identity, at), new Vector3(0.18f, 0.12f + 0.06f * (i % 2), 0.18f), Mid, Bright, (uint)(0xC0 + i), 0.3f, 5, 3);
        }
        k.Faceted = false;
        for (int i = 0; i < 7; i++)
        {
            float a = i * Mathf.Tau / 7f + 0.2f;
            var at = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * 0.45f;
            k.Box(new Transform3D(new Basis(Vector3.Up, a), at), new Vector3(0.05f, 0.02f, 0.8f), Bright);
        }
    });

    /// <summary>Ten flame tongues standing on a unit circle (Ember Halo).</summary>
    public static ArrayMesh FlameRing => Cached("flamering", k =>
    {
        const int n = 10;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n;
            var at = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a));
            // Thin: the ring is scaled out to the field's radius in X and Z only.
            k.Cone(at, at + new Vector3(0f, 0.45f + 0.25f * (i % 2), 0f), 0.045f, Mid, 0f, 5, Bright);
        }
    });

    /// <summary>A flat six-armed snowflake, unit radius (Rimeheart).</summary>
    public static ArrayMesh Snowflake => Cached("snowflake", k =>
    {
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.Tau / 6f;
            var dir = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a));
            k.Box(new Transform3D(new Basis(Vector3.Up, a), dir * 0.5f), new Vector3(0.05f, 0.02f, 1f), Bright);
            for (int s = -1; s <= 1; s += 2)
            {
                var branch = new Basis(Vector3.Up, a + s * 0.7f);
                k.Box(new Transform3D(branch, dir * 0.62f + branch * new Vector3(0f, 0f, 0.1f)), new Vector3(0.04f, 0.02f, 0.25f), Mid);
            }
        }
    });

    /// <summary>A zigzag ring of crackle, unit radius (Static Field).</summary>
    public static ArrayMesh SparkRing => Cached("sparkring", k =>
    {
        const int n = 28;
        for (int i = 0; i < n; i++)
        {
            float a = (i + 0.5f) * Mathf.Tau / n;
            float r = i % 2 == 0 ? 0.92f : 1.05f;
            var at = new Vector3(MathF.Sin(a), 0.04f, MathF.Cos(a)) * r;
            k.Box(new Transform3D(new Basis(Vector3.Up, a + (i % 2 == 0 ? 0.8f : -0.8f)), at), new Vector3(0.025f, 0.03f, 0.2f), Bright);
        }
    });

    /// <summary>A soft puff of three spheres (breath, spores, smoke).</summary>
    public static ArrayMesh Puff => Cached("puff", k =>
    {
        k.Sphere(Vector3.Zero, 0.6f, Mid, 0f, 8, 5);
        k.Sphere(new Vector3(0.45f, 0.15f, 0.1f), 0.42f, Bright, 0f, 8, 5);
        k.Sphere(new Vector3(-0.35f, 0.25f, -0.2f), 0.4f, Bright, 0f, 8, 5);
    });

    /// <summary>A flat eight-point star, unit radius (frost and glass impacts).</summary>
    public static ArrayMesh Star => Cached("star", k =>
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f;
            var dir = new Vector3(MathF.Sin(a), 0f, MathF.Cos(a));
            k.Cone(dir * 0.1f, dir * (i % 2 == 0 ? 1f : 0.55f), 0.1f, i % 2 == 0 ? Bright : Mid, 0f, 4);
        }
    });

    /// <summary>A thin open ring, unit radius (telegraphs).</summary>
    public static ArrayMesh Hoop => Cached("hoop", k =>
    {
        const int n = 40;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n;
            var at = new Vector3(MathF.Sin(a), 0.02f, MathF.Cos(a));
            k.Box(new Transform3D(new Basis(Vector3.Up, a), at), new Vector3(0.05f, 0.02f, Mathf.Tau / n * 1.1f), i % 4 == 0 ? Bright : Mid);
        }
    });
}
