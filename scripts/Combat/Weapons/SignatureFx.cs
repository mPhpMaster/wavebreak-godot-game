using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// Effects for the three signature weapons (<see cref="Signatures"/>): the
/// lingering ones (a moored anchor, a kite on its string) and the brief ones
/// (a lens glint, a chain flash).
///
/// Stepped from <see cref="Projectile.UpdateAll"/>, the same loop and the same
/// point in the frame as every shot, so they pause with the run, run inside the
/// content audit's fire test, and need no per-node process callback.
///
/// Every visual follows the FxGuard rules: thin lines and small meshes,
/// materials tracked for the near-camera fade, transparency through
/// <see cref="Meta.GraphicsSettings.FxFade"/>, and drawn sizes capped whatever
/// the area stat says (the hit areas are not capped).
/// </summary>
internal static class SignatureFx
{
    /// <summary>One live effect. <see cref="Step"/> returns false when done.</summary>
    internal interface IFx
    {
        bool Step(float dt);
        void Free();
    }

    /// <summary>Most brief flashes alive at once; past this a new one is dropped.</summary>
    private const int MaxFlashes = 14;

    private static Node? _parent;
    private static readonly List<IFx> Live = new(16);
    private static int _flashes;

    public static int LiveCount => Live.Count;

    public static void Initialize(Node parent)
    {
        Shutdown();
        _parent = parent;
        Signatures.ResetHits();
    }

    public static void Shutdown()
    {
        if (_parent is not null && Signatures.Hits > 0 && Meta.Launch.Automated)
            GD.Print($"[signature] hits={Signatures.Hits}");
        for (int i = 0; i < Live.Count; i++) Live[i].Free();
        Live.Clear();
        _flashes = 0;
        _parent = null;
        Signatures.ForgetRigs();
    }

    /// <summary>Drop every live effect but keep the root (the audit's fire
    /// test, between weapons: a moored anchor must not score on the next one).</summary>
    public static void Clear()
    {
        for (int i = 0; i < Live.Count; i++) Live[i].Free();
        Live.Clear();
        _flashes = 0;
        Signatures.ForgetRigs();
    }

    public static void UpdateAll(float dt)
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            IFx fx = Live[i];
            if (fx.Step(dt)) continue;
            fx.Free();
            if (fx is Flash) _flashes--;
            Live[i] = Live[^1];
            Live.RemoveAt(Live.Count - 1);
        }
    }

    public static bool Add(IFx fx)
    {
        if (fx is Flash)
        {
            if (_flashes >= MaxFlashes) { fx.Free(); return false; }
            _flashes++;
        }
        Live.Add(fx);
        return true;
    }

    /// <summary>A new visual node under the effect root, or null between runs
    /// (the audit's headless paths, a torn-down scene).</summary>
    public static MeshInstance3D? NewMesh(Mesh mesh, Material material)
    {
        if (_parent is null || !GodotObject.IsInstanceValid(_parent)) return null;
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel = true,
        };
        _parent.AddChild(mi);
        return mi;
    }

    public static void FreeNode(Node? n)
    {
        if (n is not null && GodotObject.IsInstanceValid(n)) n.QueueFree();
    }

    // ------------------------------------------------------------ helpers

    /// <summary>Lay a unit-box bar from <paramref name="a"/> to <paramref name="b"/>
    /// (any direction, including up), <paramref name="width"/> thick.</summary>
    public static void SetBar(MeshInstance3D bar, Vector3 a, Vector3 b, float width)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 0.02f) { bar.Visible = false; return; }
        bar.Visible = true;
        Vector3 z = d / len;
        Vector3 helper = MathF.Abs(z.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 x = helper.Cross(z).Normalized();
        Vector3 y = z.Cross(x).Normalized();
        bar.GlobalTransform = new Transform3D(new Basis(x * width, y * width, z * len), (a + b) * 0.5f);
    }

    public static float Fade(float own) => Meta.GraphicsSettings.FxFade(own);

    // ------------------------------------------------------------- meshes

    private static readonly Dictionary<string, ArrayMesh> Meshes = new();

    private static ArrayMesh Cached(string key, Action<MeshKit> build)
    {
        if (Meshes.TryGetValue(key, out ArrayMesh? m) && GodotObject.IsInstanceValid(m)) return m;
        var kit = new MeshKit();
        build(kit);
        m = kit.ToMesh();
        Meshes[key] = m;
        return m;
    }

    private static readonly Color Bright = new(1f, 1f, 1f);
    private static readonly Color Mid = new(0.70f, 0.70f, 0.72f);
    private static readonly Color Dark = new(0.30f, 0.30f, 0.34f);

    /// <summary>A four-point star, flat in XY, radius 1 (the lens glint).</summary>
    public static ArrayMesh Glint => Cached("glint", k =>
    {
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi * 0.5f;
            Vector3 dir = new(MathF.Cos(a), MathF.Sin(a), 0f);
            k.Cone(Vector3.Zero, dir * 1f, 0.16f, Bright, 0f, 4);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Pi * 0.25f + i * Mathf.Pi * 0.5f;
            Vector3 dir = new(MathF.Cos(a), MathF.Sin(a), 0f);
            k.Cone(Vector3.Zero, dir * 0.45f, 0.08f, Bright, 0f, 4);
        }
        k.Sphere(Vector3.Zero, 0.14f, Bright, 0f, 6, 4);
    });

    /// <summary>A ship's anchor, ring up, about 1 tall, origin at the crown.</summary>
    public static ArrayMesh Anchor => Cached("anchor", k =>
    {
        k.Tube(new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0.95f, 0f), 0.07f, 0.06f, Mid, 0f, 6);   // shank
        k.Box(new Transform3D(Basis.Identity, new Vector3(0f, 0.78f, 0f)), new Vector3(0.5f, 0.07f, 0.07f), Dark);   // stock
        for (int i = 0; i < 10; i++)                                                                   // ring
        {
            float a = i * Mathf.Tau / 10f;
            k.Sphere(new Vector3(MathF.Cos(a) * 0.12f, 1.05f + MathF.Sin(a) * 0.12f, 0f), 0.035f, Bright, 0f, 5, 3);
        }
        for (int side = -1; side <= 1; side += 2)                                                     // arms and flukes
        {
            Vector3 elbow = new(side * 0.30f, 0.16f, 0f);
            k.Tube(new Vector3(0f, 0.05f, 0f), elbow, 0.06f, 0.05f, Mid, 0f, 6);
            k.Tube(elbow, new Vector3(side * 0.42f, 0.42f, 0f), 0.05f, 0.03f, Mid, 0f, 6);
            k.Cone(new Vector3(side * 0.38f, 0.34f, 0f), new Vector3(side * 0.46f, 0.55f, 0f), 0.09f, Bright, 0f, 4);
        }
        k.Sphere(new Vector3(0f, 0.05f, 0f), 0.09f, Dark, 0f, 6, 4);
    });

    /// <summary>A thin rope ring, unit radius, flat on the ground, with four
    /// little bollards on it: the mooring zone's edge, not a filled disc.</summary>
    public static ArrayMesh RopeRing => Cached("ropering", k =>
    {
        const int n = 48;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n;
            var basis = new Basis(Vector3.Up, -a);
            var at = new Vector3(MathF.Cos(a), 0.02f, MathF.Sin(a));
            k.Box(new Transform3D(basis, at), new Vector3(0.022f, 0.03f, Mathf.Tau / n * 1.05f), i % 2 == 0 ? Bright : Mid);
        }
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Pi * 0.25f + i * Mathf.Pi * 0.5f;
            var at = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            k.Cylinder(new Transform3D(Basis.Identity, at), 0.05f, 0.04f, 0.14f, Dark, 0f, 6, Bright);
        }
    });

    /// <summary>A diamond kite with cross spars and a ribbon tail, facing +Z,
    /// about 0.8 tall.</summary>
    public static ArrayMesh Kite => Cached("kite", k =>
    {
        var diamond = new Basis(Vector3.Forward, Mathf.Pi * 0.25f);
        k.Box(new Transform3D(diamond.Scaled(new Vector3(1f, 1.35f, 1f)), Vector3.Zero), new Vector3(0.42f, 0.42f, 0.03f), Bright, 0f, Mid);
        k.Box(new Transform3D(Basis.Identity, new Vector3(0f, 0f, 0.025f)), new Vector3(0.025f, 0.78f, 0.02f), Dark);
        k.Box(new Transform3D(Basis.Identity, new Vector3(0f, 0.06f, 0.025f)), new Vector3(0.56f, 0.025f, 0.02f), Dark);
        for (int i = 0; i < 3; i++)
        {
            float y = -0.48f - i * 0.2f;
            float x = (i % 2 == 0 ? 0.04f : -0.04f);
            k.Box(new Transform3D(new Basis(Vector3.Forward, 0.6f), new Vector3(x, y, 0f)), new Vector3(0.09f, 0.05f, 0.02f), Bright);
            k.Box(new Transform3D(new Basis(Vector3.Forward, -0.6f), new Vector3(x, y, 0f)), new Vector3(0.09f, 0.05f, 0.02f), Bright);
        }
    });

    public static StandardMaterial3D Mat(Color tint) => ExpansionVisuals.Glow(tint);
    public static StandardMaterial3D Line(Color tint) => CombatVisuals.Glow(tint, additive: false);

    // ------------------------------------------------------------- flashes

    /// <summary>A small star that pops at a point and fades (lens glint, a
    /// moored hit). Faces the camera; never larger than 0.9 m.</summary>
    internal sealed class Flash : IFx
    {
        private readonly MeshInstance3D? _mi;
        private readonly float _life, _size;
        private readonly Vector3 _at;
        private float _age;

        public Flash(Vector3 at, float size, Color tint, float life)
        {
            _at = at;
            _size = MathF.Min(size, 0.9f);
            _life = MathF.Max(0.08f, life);
            _mi = NewMesh(Glint, Mat(tint));
            if (_mi is not null) Place(0f);
        }

        private void Place(float t)
        {
            if (_mi is null) return;
            float s = _size * (t < 0.25f ? 0.4f + 2.4f * t : 1f - 0.6f * (t - 0.25f) / 0.75f);
            Basis face = Basis.Identity;
            if (_mi.GetViewport()?.GetCamera3D() is { } cam)
                face = cam.GlobalBasis;
            face = face.Rotated(face.Z.Normalized(), t * 2.2f);
            _mi.GlobalTransform = new Transform3D(face.Scaled(Vector3.One * MathF.Max(0.02f, s)), _at);
            _mi.Transparency = Fade(t * t);
        }

        public bool Step(float dt)
        {
            _age += dt;
            if (_age >= _life) return false;
            Place(_age / _life);
            return true;
        }

        public void Free() => FreeNode(_mi);
    }

    /// <summary>A thin line between two points that fades out (a chain hauled
    /// in, a lightning drop from the kite).</summary>
    internal sealed class Wire : IFx
    {
        private readonly MeshInstance3D? _mi;
        private readonly float _life, _width;
        private readonly Func<Vector3>? _from;
        private Vector3 _a;
        private readonly Vector3 _b;
        private float _age;

        public Wire(Vector3 a, Vector3 b, float width, Color tint, float life, Func<Vector3>? followFrom = null)
        {
            _a = a; _b = b; _from = followFrom;
            _width = MathF.Min(width, 0.12f);
            _life = MathF.Max(0.05f, life);
            _mi = NewMesh(CombatVisuals.Slab, Line(tint));
            if (_mi is not null) { SetBar(_mi, a, b, _width); _mi.Transparency = Fade(0.15f); }
        }

        public bool Step(float dt)
        {
            _age += dt;
            if (_age >= _life) return false;
            if (_mi is null) return true;
            if (_from is not null) _a = _from();
            float t = _age / _life;
            SetBar(_mi, _a, _b, _width * (1f - 0.5f * t));
            _mi.Transparency = Fade(0.15f + 0.85f * t * t);
            return true;
        }

        public void Free() => FreeNode(_mi);
    }
}
