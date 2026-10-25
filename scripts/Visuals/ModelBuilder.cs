using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>Which joint a moving part hangs from; the animator poses by role.</summary>
internal enum PartRole { LegL, LegR, ArmL, ArmR, LegFL, LegFR, LegBL, LegBR, WingL, WingR, Segment }

/// <summary>One moving part: a mesh hung from a pivot in hips space, optionally
/// with a second segment (shin, forearm) hung from <see cref="LowerPivot"/>.</summary>
internal sealed class PartDef
{
    public PartRole Role;
    public int Index;              // serpent segment number
    public Mesh Upper = null!;
    public Mesh? Lower;
    public Vector3 Pivot;          // in hips space
    public Vector3 LowerPivot;     // in the upper part's space
    public float Scale = 1f;       // serpent taper
    public float Side;             // -1 left, +1 right, 0 centre
}

/// <summary>Everything instancing a model needs, built once per spec.</summary>
internal sealed class BuiltModel
{
    public Mesh Body = null!;
    public Mesh Impostor = null!;
    public readonly List<PartDef> Parts = new();
    public Vector3 HipsOffset;     // hips node position in rig space
    public float AuraRadius;
    public float Height;
    public float StrideLength = 1f; // metres per full walk cycle
    public float LimbSwing = 0.7f;  // radians at full speed
    /// <summary>Hand-built heroes (HeroModels): the crown of the head in hips
    /// space and the head's half-width, so a hat sits exactly on it.</summary>
    public Vector3? HatAnchor;
    public float HatRadius;
}

/// <summary>
/// Turns a <see cref="ModelSpec"/> into meshes. Pure geometry: no nodes, no
/// state, and the result is cached on the spec, so the cost is paid once per
/// kind of model rather than once per enemy.
///
/// Conventions: origin at the feet, -Z forward (Godot's), +Y up. Every
/// dimension is a fraction of <see cref="ModelSpec.Height"/>, so a spec scales
/// by changing one number and a tall enemy is not just a stretched short one —
/// head size, limb thickness and stride all follow.
///
/// Parts that move together share a mesh; parts that animate get their own,
/// built around their pivot so the animator only ever has to rotate them.
/// </summary>
public static class ModelBuilder
{
    internal static BuiltModel Get(ModelSpec spec)
    {
        if (spec.Built is { } b && GodotObject.IsInstanceValid(b.Body)) return b;
        spec.Built = Build(spec);
        return spec.Built;
    }

    private sealed class Ctx
    {
        public ModelSpec S = null!;
        public float H;
        public MeshKit Body = new();
        public readonly List<(PartDef def, MeshKit upper, MeshKit? lower)> Parts = new();
        public BuiltModel Out = new();
    }

    private static BuiltModel Build(ModelSpec s)
    {
        var c = new Ctx { S = s, H = Mathf.Max(0.2f, s.Height) };
        c.Out.Height = c.H;

        if (s.Custom is not null) CustomBuild(c);   // CuteModels and friends
        else switch (s.Body)
        {
            case BodyType.Quadruped: Quadruped(c); break;
            case BodyType.Blob: Blob(c); break;
            case BodyType.Flyer: Flyer(c); break;
            case BodyType.Serpent: Serpent(c); break;
            default: Biped(c, brute: s.Body == BodyType.Brute); break;
        }

        // Far LOD: the whole model at rest, in one mesh. Built from the same kits,
        // so it can never drift out of sync with the near model.
        var imp = new MeshKit();
        imp.Append(c.Body, new Transform3D(Basis.Identity, c.Out.HipsOffset));
        foreach (var (def, upper, lower) in c.Parts)
        {
            var xf = new Transform3D(Basis.Identity.Scaled(Vector3.One * def.Scale), c.Out.HipsOffset + def.Pivot);
            imp.Append(upper, xf);
            if (lower is not null) imp.Append(lower, xf * new Transform3D(Basis.Identity, def.LowerPivot));
        }

        c.Out.Body = c.Body.ToMesh();
        c.Out.Impostor = imp.ToMesh();
        foreach (var (def, upper, lower) in c.Parts)
        {
            def.Upper = upper.ToMesh();
            def.Lower = lower?.ToMesh();
            c.Out.Parts.Add(def);
        }
        if (s.Aura || s.Crown) c.Out.AuraRadius = Mathf.Max(0.8f, c.H * 0.45f * s.Bulk);
        return c.Out;
    }

    private static void CustomBuild(Ctx c)
    {
        var b = new CustomBody(c.S, c.H, c.Body);
        c.S.Custom!(b);
        c.Out.HipsOffset = b.HipsOffset;
        c.Out.StrideLength = b.StrideLength;
        foreach (var (role, side, pivot, kit) in b.Parts)
            c.Parts.Add((new PartDef { Role = role, Side = side, Pivot = pivot }, kit, null));
        // Jointed arms and legs (HeroModels): posed by the biped animator.
        foreach (var (role, side, pivot, upper, lower, lowerPivot) in b.Limbs)
            c.Parts.Add((new PartDef { Role = role, Side = side, Pivot = pivot, LowerPivot = lowerPivot }, upper, lower));
        if (b.LimbSwing > 0f) c.Out.LimbSwing = b.LimbSwing;
        c.Out.HatAnchor = b.HatAnchor;
        c.Out.HatRadius = b.HatRadius;
    }

    // ================================================================= biped

    private static void Biped(Ctx c, bool brute)
    {
        ModelSpec s = c.S;
        float H = c.H;
        float hipY = (brute ? 0.30f : 0.40f) * H;
        float shoulder = (brute ? 0.68f : 0.70f) * H - hipY;   // relative to hips
        float w = (brute ? 0.24f : 0.16f) * H * s.Bulk;
        c.Out.HipsOffset = new Vector3(0f, hipY, 0f);
        c.Out.StrideLength = hipY * 3.2f / Mathf.Max(0.3f, s.Cadence);
        c.Out.LimbSwing = brute ? 0.55f : 0.75f;
        MeshKit k = c.Body;

        Color prim = s.Primary, sec = s.Secondary;
        // Pelvis and torso. The torso is wider at the chest than the waist, which
        // is the one curve that makes a stack of ellipsoids read as a body.
        k.Ellipsoid(At(0f, 0.03f * H, 0f), new Vector3(w * 0.85f, 0.09f * H, w * 0.68f), sec);
        float hunch = brute ? 0.06f * H : 0f;
        k.Ellipsoid(At(0f, shoulder * 0.42f, hunch * 0.3f), new Vector3(w * 0.82f, shoulder * 0.45f, w * 0.66f), prim);
        k.Ellipsoid(At(0f, shoulder * 0.72f, hunch), new Vector3(w * 1.02f, shoulder * 0.38f, w * 0.74f), prim,
                    topColor: prim.Lightened(0.12f));
        if (s.Belly)
            k.Ellipsoid(At(0f, shoulder * 0.42f, -w * 0.38f + hunch * 0.3f), new Vector3(w * 0.62f, shoulder * 0.36f, w * 0.38f),
                        brute ? prim.Lightened(0.22f) : s.Skin.Lerp(prim, 0.45f));
        // Belt.
        if (!brute) k.Cylinder(At(0f, 0.04f * H, 0f), w * 0.86f, w * 0.84f, 0.04f * H, sec.Darkened(0.35f), 0f, 10);

        float hr = (brute ? 0.105f : 0.155f) * H * s.HeadScale;
        Vector3 hc = brute
            ? new Vector3(0f, shoulder + hr * 0.25f, -w * 0.55f)
            : new Vector3(0f, shoulder + hr * 0.85f, 0f);
        Head(c, hc, hr, prim);

        if (s.ShoulderPads)
            for (int side = -1; side <= 1; side += 2)
            {
                var at = new Vector3(side * w * 1.02f, shoulder * 0.95f, hunch);
                k.Ellipsoid(At(at), new Vector3(w * 0.55f, w * 0.38f, w * 0.55f), sec, topColor: sec.Lightened(0.2f));
                k.Cone(at + new Vector3(side * w * 0.15f, w * 0.25f, 0f), at + new Vector3(side * w * 0.45f, w * 0.75f, 0f),
                       w * 0.16f, s.Accent);
            }
        if (s.Scarf)
        {
            k.Cylinder(At(0f, shoulder * 0.98f, 0f), w * 0.72f, w * 0.62f, 0.06f * H, s.Accent, 0f, 10);
            k.Tube(new Vector3(w * 0.3f, shoulder, w * 0.5f), new Vector3(w * 0.55f, shoulder * 0.55f, w * 1.2f),
                   0.035f * H, 0.025f * H, s.Accent);
        }
        if (s.Cape)
        {
            var xf = new Transform3D(new Basis(Vector3.Right, 0.18f), new Vector3(0f, shoulder * 0.45f, w * 0.85f));
            k.Box(xf, new Vector3(w * 1.9f, shoulder * 1.05f, 0.025f * H), sec.Darkened(0.15f));
        }
        Back(c, new Vector3(0f, shoulder * 0.6f, w * 0.7f + hunch), w, shoulder);
        if (s.Tail) Tail(k, new Vector3(0f, 0.02f * H, w * 0.6f), H * (brute ? 0.35f : 0.42f), w * 0.4f, prim, s.Accent);

        // Legs: thigh + shin + foot, hung from the hip.
        float legR = (brute ? 0.095f : 0.065f) * H * Mathf.Sqrt(s.Bulk);
        float footH = 0.05f * H;
        float thigh = (hipY - footH) * 0.5f, shin = hipY - footH - thigh;
        for (int side = -1; side <= 1; side += 2)
        {
            var up = new MeshKit();
            up.Tube(Vector3.Zero, new Vector3(0f, -thigh, 0f), legR * 1.1f, legR * 0.95f, sec);
            var lowKit = s.Jointed ? new MeshKit() : up;
            Vector3 knee = new(0f, -thigh, 0f);
            Vector3 o = s.Jointed ? Vector3.Zero : knee;
            lowKit.Tube(o, o + new Vector3(0f, -shin, 0f), legR * 0.95f, legR * 0.8f, sec.Darkened(0.1f));
            // Boot: a rounded box pushed forward so the stance reads.
            Color boot = brute ? sec.Darkened(0.35f) : s.Accent.Lerp(sec, 0.55f).Darkened(0.35f);
            lowKit.Ellipsoid(At(o + new Vector3(0f, -shin - footH * 0.35f, -legR * 0.55f)),
                             new Vector3(legR * 1.15f, footH * 0.9f, legR * 1.75f), boot);
            c.Parts.Add((new PartDef
            {
                Role = side < 0 ? PartRole.LegL : PartRole.LegR,
                Pivot = new Vector3(side * w * 0.48f, 0f, 0f),
                LowerPivot = knee,
                Side = side,
            }, up, s.Jointed ? lowKit : null));
        }

        // Arms: upper + forearm + hand, hung from the shoulder; held items go in
        // the hand so they swing with it.
        float armR = (brute ? 0.085f : 0.05f) * H * Mathf.Sqrt(s.Bulk);
        float ua = (brute ? 0.2f : 0.16f) * H, fa = (brute ? 0.2f : 0.14f) * H;
        for (int side = -1; side <= 1; side += 2)
        {
            var up = new MeshKit();
            up.Tube(Vector3.Zero, new Vector3(0f, -ua, 0f), armR * 1.1f, armR, prim.Lerp(sec, 0.3f));
            var lowKit = s.Jointed ? new MeshKit() : up;
            Vector3 elbow = new(0f, -ua, 0f);
            Vector3 o = s.Jointed ? Vector3.Zero : elbow;
            lowKit.Tube(o, o + new Vector3(0f, -fa, 0f), armR, armR * 0.9f, prim.Lerp(sec, 0.3f));
            Vector3 hand = o + new Vector3(0f, -fa - armR * 0.6f, 0f);
            lowKit.Sphere(hand, armR * (brute ? 1.7f : 1.35f), brute ? prim.Darkened(0.25f) : s.Skin);
            Held(lowKit, side < 0 ? s.LeftHand : s.RightHand, hand, H, armR, s, side);
            c.Parts.Add((new PartDef
            {
                Role = side < 0 ? PartRole.ArmL : PartRole.ArmR,
                Pivot = new Vector3(side * (w * 0.98f + armR * 0.6f), shoulder * 0.86f, hunch),
                LowerPivot = elbow,
                Side = side,
            }, up, s.Jointed ? lowKit : null));
        }

        if (s.Wings) Wings(c, new Vector3(0f, shoulder * 0.8f, w * 0.6f + hunch), w, H * 0.55f, flyer: false);
    }

    // ============================================================ quadruped

    private static void Quadruped(Ctx c)
    {
        ModelSpec s = c.S;
        float H = c.H;
        float legH = 0.36f * H;
        float bw = 0.19f * H * s.Bulk, bh = 0.18f * H, bl = 0.42f * H;
        c.Out.HipsOffset = new Vector3(0f, legH, 0f);
        c.Out.StrideLength = legH * 3.8f / Mathf.Max(0.3f, s.Cadence);
        c.Out.LimbSwing = 0.6f;
        MeshKit k = c.Body;

        k.Ellipsoid(At(0f, bh * 0.7f, 0.04f * H), new Vector3(bw, bh, bl), s.Primary, topColor: s.Primary.Lightened(0.1f));
        k.Ellipsoid(At(0f, bh * 0.45f, -0.04f * H), new Vector3(bw * 0.7f, bh * 0.6f, bl * 0.8f), s.Secondary.Lightened(0.15f));
        float hr = 0.16f * H * s.HeadScale;
        Vector3 hc = new(0f, H - legH - hr, -bl * 1.05f);
        k.Tube(new Vector3(0f, bh * 0.9f, -bl * 0.6f), hc + new Vector3(0f, -hr * 0.3f, hr * 0.3f), bw * 0.5f, hr * 0.6f, s.Primary);
        Head(c, hc, hr, s.Primary, snout: true);
        Back(c, new Vector3(0f, bh * 1.55f, 0f), bw, bl, along: true);
        if (s.Tail)
            Tail(k, new Vector3(0f, bh * 1.0f, bl * 0.9f), H * 0.4f, bw * 0.35f, s.Primary, s.Accent);

        float legR = 0.055f * H * Mathf.Sqrt(s.Bulk);
        (PartRole role, float x, float z)[] legs =
        {
            (PartRole.LegFL, -1f, -bl * 0.6f), (PartRole.LegFR, 1f, -bl * 0.6f),
            (PartRole.LegBL, -1f, bl * 0.6f), (PartRole.LegBR, 1f, bl * 0.6f),
        };
        foreach (var (role, x, z) in legs)
        {
            var up = new MeshKit();
            up.Tube(new Vector3(0f, bh * 0.4f, 0f), new Vector3(0f, -legH * 0.55f, 0f), legR * 1.5f, legR, s.Primary.Lerp(s.Secondary, 0.5f));
            up.Tube(new Vector3(0f, -legH * 0.55f, 0f), new Vector3(0f, -legH + legR * 0.6f, -legR * 0.4f), legR, legR * 0.75f, s.Secondary);
            up.Ellipsoid(At(0f, -legH + legR * 0.5f, -legR * 0.8f), new Vector3(legR * 1.1f, legR * 0.6f, legR * 1.5f), s.Secondary.Darkened(0.3f));
            c.Parts.Add((new PartDef { Role = role, Pivot = new Vector3(x * bw * 0.68f, 0f, z), Side = x }, up, null));
        }
        if (s.Wings) Wings(c, new Vector3(0f, bh * 1.4f, -bl * 0.2f), bw, H * 0.6f, flyer: false);
    }

    // ================================================================= blob

    private static void Blob(Ctx c)
    {
        ModelSpec s = c.S;
        float H = c.H;
        c.Out.HipsOffset = Vector3.Zero; // squash pivots at the floor so it stays planted
        c.Out.StrideLength = H * 1.6f / Mathf.Max(0.3f, s.Cadence);
        MeshKit k = c.Body;
        float rx = 0.52f * H * s.Bulk, ry = 0.5f * H, rz = 0.5f * H * s.Bulk;
        var centre = new Vector3(0f, ry * 0.98f, 0f);
        k.Ellipsoid(At(centre), new Vector3(rx, ry, rz), s.Primary.Darkened(0.15f), segs: 12, rings: 8,
                    topColor: s.Primary.Lightened(0.18f));
        // A lighter belly and two stubby feet keep it from reading as a ball.
        k.Ellipsoid(At(0f, ry * 0.7f, -rz * 0.45f), new Vector3(rx * 0.68f, ry * 0.55f, rz * 0.55f), s.Secondary.Lightened(0.25f));
        for (int side = -1; side <= 1; side += 2)
            k.Ellipsoid(At(side * rx * 0.45f, 0.06f * H, -rz * 0.35f), new Vector3(rx * 0.28f, 0.08f * H, rz * 0.36f), s.Secondary.Darkened(0.2f));

        // Eyes on the front-top of the shell.
        float hr = Mathf.Min(rx, ry) * 0.9f;
        Vector3 hc = centre + new Vector3(0f, ry * 0.18f, -rz * 0.12f);
        Head(c, hc, hr, s.Primary, shell: true);
        Back(c, centre + new Vector3(0f, ry * 0.55f, rz * 0.2f), rx * 0.8f, ry * 0.8f);
    }

    // ================================================================ flyer

    private static void Flyer(Ctx c)
    {
        ModelSpec s = c.S;
        float H = c.H;
        float r = 0.24f * H * s.Bulk;
        c.Out.HipsOffset = new Vector3(0f, 0.58f * H, 0f);
        c.Out.StrideLength = H * 2f;
        MeshKit k = c.Body;

        if (s.Head == HeadGear.Hood)
        {
            // A hooded wraith: robe tapering to tatters instead of a round body.
            k.Cone(new Vector3(0f, 0.05f * H, 0f), new Vector3(0f, -0.52f * H, 0.08f * H), r * 1.05f, s.Secondary,
                   0f, 9, tipColor: s.Secondary.Darkened(0.5f));
            k.Ellipsoid(At(0f, 0.08f * H, 0f), new Vector3(r * 1.0f, r * 0.75f, r * 0.9f), s.Secondary);
            Head(c, new Vector3(0f, 0.24f * H, -0.02f * H), r * 0.82f, s.Primary);
        }
        else
        {
            k.Ellipsoid(At(Vector3.Zero), new Vector3(r, r * 1.05f, r), s.Primary, segs: 12, rings: 8,
                        topColor: s.Primary.Lightened(0.2f));
            k.Ellipsoid(At(0f, -r * 0.2f, -r * 0.4f), new Vector3(r * 0.7f, r * 0.65f, r * 0.6f), s.Secondary.Lightened(0.2f));
            Head(c, new Vector3(0f, r * 0.15f, 0f), r, s.Primary, shell: true);
            // Dangling tendrils: hint at a creature without legs to animate.
            for (int i = -1; i <= 1; i++)
                k.Tube(new Vector3(i * r * 0.4f, -r * 0.8f, 0f), new Vector3(i * r * 0.55f, -r * 1.8f, r * 0.35f),
                       r * 0.14f, r * 0.04f, s.Secondary, 0f, 6, endColor: s.Accent);
        }
        Back(c, new Vector3(0f, r * 0.6f, r * 0.6f), r, r);
        Wings(c, new Vector3(0f, r * 0.25f, r * 0.35f), r * 0.85f, H * 0.62f, flyer: true);
    }

    // ============================================================== serpent

    private static void Serpent(Ctx c)
    {
        ModelSpec s = c.S;
        float H = c.H;
        bool rearing = H > 2.2f;
        float hr = (rearing ? 0.15f : 0.4f) * H * s.HeadScale;
        float sr = (rearing ? 0.13f : 0.3f) * H * s.Bulk;
        float headY = rearing ? H - hr * 1.2f : hr * 0.95f;
        c.Out.HipsOffset = Vector3.Zero;
        c.Out.StrideLength = sr * 9f / Mathf.Max(0.3f, s.Cadence);
        MeshKit k = c.Body;

        Vector3 hc = new(0f, headY, 0f);
        Head(c, hc, hr, s.Primary, snout: true);
        if (rearing)
        {
            // The raised neck is part of the head mesh so the rearing pose holds
            // still while the coils behind it slither.
            k.Tube(hc + new Vector3(0f, -hr * 0.6f, hr * 0.4f), new Vector3(0f, sr * 1.1f, hr * 1.5f), sr * 1.05f, sr * 1.15f,
                   s.Primary, 0f, 10, endColor: s.Primary.Darkened(0.1f));
            k.Tube(hc + new Vector3(0f, -hr * 0.5f, -hr * 0.1f), new Vector3(0f, sr * 1.2f, hr * 1.1f), sr * 0.7f, sr * 0.8f,
                   s.Secondary.Lightened(0.2f), 0f, 8);
            // A frill/hood behind the head.
            k.Ellipsoid(new Transform3D(new Basis(Vector3.Right, -0.25f), hc + new Vector3(0f, -hr * 0.2f, hr * 0.7f)),
                        new Vector3(hr * 2.0f, hr * 1.5f, hr * 0.25f), s.Secondary, 0f, 10, 6, s.Accent);
        }

        var seg = new MeshKit();
        seg.Ellipsoid(At(Vector3.Zero), new Vector3(sr, sr * 0.9f, sr * 1.15f), s.Primary, 0f, 10, 6, s.Primary.Lightened(0.12f));
        seg.Ellipsoid(At(0f, -sr * 0.35f, 0f), new Vector3(sr * 0.8f, sr * 0.55f, sr * 1.05f), s.Secondary.Lightened(0.15f));
        if (s.BackSpikes)
            seg.Cone(new Vector3(0f, sr * 0.7f, 0f), new Vector3(0f, sr * 1.5f, sr * 0.4f), sr * 0.28f, s.Accent, 0.2f, 5);
        if (s.Crystals)
            seg.Cone(new Vector3(0f, sr * 0.7f, 0f), new Vector3(0f, sr * 1.7f, sr * 0.2f), sr * 0.22f, s.Accent, 1.4f, 6);

        int n = Mathf.Clamp(s.Segments, 2, 9);
        float z = rearing ? hr * 1.5f + sr * 0.3f : hr * 0.75f + sr * 0.6f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float scale = 1f - t * 0.55f;
            float y = sr * scale * 0.95f;
            c.Parts.Add((new PartDef { Role = PartRole.Segment, Index = i, Pivot = new Vector3(0f, y, z), Scale = scale },
                         i == 0 ? seg : CloneKit(seg), null));
            z += sr * 1.55f * scale;
        }
        // Tail tip.
        c.Body.Faceted = false;
    }

    private static MeshKit CloneKit(MeshKit src)
    {
        var k = new MeshKit();
        k.Append(src, Transform3D.Identity);
        return k;
    }

    // ============================================================ head & co

    /// <summary>
    /// Head with face, eyes, mouth and head gear, into the body mesh.
    /// <paramref name="shell"/>: the head is the body (blobs, flyers) — only the
    /// face features are added. <paramref name="snout"/>: a muzzle out front.
    /// </summary>
    private static void Head(Ctx c, Vector3 hc, float hr, Color col, bool shell = false, bool snout = false)
    {
        ModelSpec s = c.S;
        MeshKit k = c.Body;
        float front = hr * 0.93f;

        if (!shell)
        {
            if (s.Head == HeadGear.Hood)
            {
                k.Ellipsoid(At(hc + new Vector3(0f, hr * 0.12f, hr * 0.1f)), new Vector3(hr * 1.12f, hr * 1.12f, hr * 1.08f),
                            s.Secondary, topColor: s.Secondary.Lightened(0.1f));
                k.Cone(hc + new Vector3(0f, hr * 0.7f, hr * 0.5f), hc + new Vector3(0f, hr * 1.1f, hr * 1.6f), hr * 0.45f, s.Secondary);
                // The face is a dark hollow; only the eyes glow out of it.
                k.Ellipsoid(At(hc + new Vector3(0f, -hr * 0.05f, -hr * 0.55f)), new Vector3(hr * 0.78f, hr * 0.72f, hr * 0.5f),
                            new Color(0.04f, 0.03f, 0.06f));
                front = hr * 1.0f;
            }
            else
            {
                k.Ellipsoid(At(hc), new Vector3(hr, hr * 0.96f, hr * 0.94f), col, segs: 12, rings: 8, topColor: col.Lightened(0.1f));
                if (s.Face)
                {
                    k.Ellipsoid(At(hc + new Vector3(0f, -hr * 0.12f, -hr * 0.36f)), new Vector3(hr * 0.8f, hr * 0.68f, hr * 0.66f), s.Skin);
                    front = hr * 1.0f;
                }
            }
            if (snout)
            {
                k.Ellipsoid(At(hc + new Vector3(0f, -hr * 0.3f, -hr * 0.85f)), new Vector3(hr * 0.55f, hr * 0.42f, hr * 0.6f),
                            col.Lightened(0.08f));
                k.Sphere(hc + new Vector3(0f, -hr * 0.18f, -hr * 1.42f), hr * 0.13f, new Color(0.08f, 0.06f, 0.08f));
            }
        }

        // Eyes. Glowing, because at nine metres a pupil is one pixel and a glow
        // is what tells you which way a thing is looking.
        int n = s.Eyes;
        float er = hr * (n >= 3 ? 0.13f : n == 1 ? 0.3f : 0.18f) * (shell ? 0.8f : 1f);
        float ey = hc.Y + hr * (snout ? 0.22f : 0.08f);
        for (int i = 0; i < n; i++)
        {
            float x;
            float yOff = 0f;
            if (n == 1) x = 0f;
            else if (n == 2) x = (i == 0 ? -1f : 1f) * hr * 0.36f;
            else
            {
                // A row of three, or two rows for four-plus: spider faces.
                int perRow = n <= 3 ? n : (n + 1) / 2;
                int row = n <= 3 ? 0 : i / perRow;
                int col2 = n <= 3 ? i : i % perRow;
                x = (col2 - (perRow - 1) * 0.5f) * hr * 0.33f;
                yOff = row * -hr * 0.3f + (n <= 3 ? 0f : hr * 0.12f);
            }
            float z = -FrontZ(front, hr, x, ey + yOff - hc.Y) + hc.Z;
            var at = new Vector3(hc.X + x, ey + yOff, z);
            if (s.Head == HeadGear.Visor) continue;
            if (s.Face && !shell)
            {
                // Hero eyes: dark ovals with a bright glint, not lamps.
                k.Ellipsoid(At(at), new Vector3(er * 0.75f, er * 1.15f, er * 0.45f), new Color(0.06f, 0.05f, 0.1f));
                k.Sphere(at + new Vector3(er * 0.2f, er * 0.35f, -er * 0.3f), er * 0.28f, s.Eye, 1.5f, 6, 4);
            }
            else
            {
                k.Ellipsoid(At(at), new Vector3(er, er * 1.05f, er * 0.5f), s.Eye, s.EyeGlow * 1.6f, 8, 5);
            }
        }

        if (s.Mouth || s.Tusks)
        {
            Vector3 m = new(hc.X, hc.Y - hr * 0.42f, hc.Z - FrontZ(front, hr, 0f, -hr * 0.42f) * 0.98f);
            if (snout) m += new Vector3(0f, -hr * 0.05f, -hr * 0.55f);
            if (s.Mouth)
            {
                k.Ellipsoid(At(m), new Vector3(hr * 0.5f, hr * 0.17f, hr * 0.2f), new Color(0.15f, 0.03f, 0.05f));
                for (int i = -2; i <= 2; i++)
                    k.Cone(m + new Vector3(i * hr * 0.17f, hr * 0.13f, -hr * 0.08f), m + new Vector3(i * hr * 0.17f, -hr * 0.05f, -hr * 0.1f),
                           hr * 0.06f, new Color(0.95f, 0.92f, 0.8f), 0f, 4);
            }
            if (s.Tusks)
                for (int side = -1; side <= 1; side += 2)
                    k.Cone(m + new Vector3(side * hr * 0.32f, -hr * 0.02f, 0f), m + new Vector3(side * hr * 0.42f, hr * 0.42f, -hr * 0.2f),
                           hr * 0.1f, new Color(0.95f, 0.9f, 0.78f), 0f, 5);
        }

        Vector3 top = hc + new Vector3(0f, hr * 0.85f, 0f);
        switch (s.Head)
        {
            case HeadGear.Horns:
                for (int side = -1; side <= 1; side += 2)
                    k.Cone(hc + new Vector3(side * hr * 0.55f, hr * 0.55f, hr * 0.05f),
                           hc + new Vector3(side * hr * 0.95f, hr * 1.45f, hr * 0.25f), hr * 0.2f, s.Accent, 0f, 6);
                break;
            case HeadGear.CurvedHorns:
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 a = hc + new Vector3(side * hr * 0.6f, hr * 0.5f, 0f);
                    Vector3 b = a + new Vector3(side * hr * 0.55f, hr * 0.45f, hr * 0.25f);
                    Vector3 d = b + new Vector3(side * hr * 0.25f, -hr * 0.1f, hr * 0.55f);
                    Vector3 e = d + new Vector3(side * -hr * 0.05f, -hr * 0.45f, hr * 0.1f);
                    k.Tube(a, b, hr * 0.2f, hr * 0.16f, s.Accent, 0f, 6);
                    k.Tube(b, d, hr * 0.16f, hr * 0.11f, s.Accent, 0f, 6);
                    k.Tube(d, e, hr * 0.11f, 0.004f, s.Accent.Lightened(0.2f), 0f, 6);
                }
                break;
            case HeadGear.Antlers:
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 a = hc + new Vector3(side * hr * 0.4f, hr * 0.7f, hr * 0.1f);
                    Vector3 b = a + new Vector3(side * hr * 0.5f, hr * 0.9f, hr * 0.1f);
                    k.Tube(a, b, hr * 0.09f, hr * 0.06f, s.Accent, 0f, 5);
                    k.Tube(b, b + new Vector3(side * hr * 0.45f, hr * 0.35f, 0f), hr * 0.06f, 0.004f, s.Accent, 0f, 5);
                    k.Tube(b, b + new Vector3(0f, hr * 0.55f, -hr * 0.1f), hr * 0.06f, 0.004f, s.Accent, 0f, 5);
                    k.Tube(a.Lerp(b, 0.5f), a.Lerp(b, 0.5f) + new Vector3(0f, hr * 0.35f, -hr * 0.3f), hr * 0.05f, 0.004f, s.Accent, 0f, 5);
                }
                break;
            case HeadGear.Ears:
            case HeadGear.LongEars:
                float len = s.Head == HeadGear.LongEars ? 1.25f : 0.7f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 b0 = hc + new Vector3(side * hr * 0.55f, hr * 0.6f, hr * 0.05f);
                    Vector3 tip = b0 + new Vector3(side * hr * 0.35f, hr * len, hr * 0.15f);
                    var xf = new Transform3D(MeshKit.BasisAlong(tip - b0).Scaled(new Vector3(1f, 1f, 0.45f)), b0);
                    k.Cylinder(xf, hr * 0.3f, hr * 0.02f, (tip - b0).Length(), col, 0f, 6, s.Accent);
                }
                break;
            case HeadGear.Antennae:
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 a = hc + new Vector3(side * hr * 0.3f, hr * 0.8f, 0f);
                    Vector3 b = a + new Vector3(side * hr * 0.45f, hr * 0.9f, hr * 0.35f);
                    k.Tube(a, b, hr * 0.06f, hr * 0.04f, s.Secondary, 0f, 5);
                    k.Sphere(b, hr * 0.14f, s.Accent, 1.4f, 6, 4);
                }
                break;
            case HeadGear.Crest:
                for (int i = 0; i < 5; i++)
                {
                    float t = i / 4f;
                    Vector3 b0 = hc + new Vector3(0f, hr * (0.9f - t * 0.25f), hr * (-0.45f + t * 1.0f));
                    k.Cone(b0, b0 + new Vector3(0f, hr * (0.55f - Mathf.Abs(t - 0.35f) * 0.4f), hr * 0.35f), hr * 0.17f, s.Accent, 0.25f, 5);
                }
                break;
            case HeadGear.Helmet:
                k.Ellipsoid(At(hc + new Vector3(0f, hr * 0.18f, hr * 0.02f)), new Vector3(hr * 1.08f, hr * 0.85f, hr * 1.04f), s.Secondary,
                            topColor: s.Secondary.Lightened(0.25f));
                k.Box(new Transform3D(Basis.Identity, hc + new Vector3(0f, hr * 0.05f, -hr * 0.98f)),
                      new Vector3(hr * 1.3f, hr * 0.14f, hr * 0.12f), s.Eye, s.EyeGlow * 1.8f);
                k.Tube(top + new Vector3(0f, hr * 0.15f, -hr * 0.3f), top + new Vector3(0f, hr * 0.45f, hr * 0.9f), hr * 0.12f, hr * 0.05f,
                       s.Accent, 0.15f, 6);
                break;
            case HeadGear.WizardHat:
                k.Cylinder(At(top + new Vector3(0f, -hr * 0.12f, 0f)), hr * 1.45f, hr * 1.35f, hr * 0.08f, s.Secondary, 0f, 12);
                k.Tube(top, top + new Vector3(0f, hr * 1.1f, hr * 0.15f), hr * 0.8f, hr * 0.35f, s.Secondary, 0f, 10, caps: false);
                k.Tube(top + new Vector3(0f, hr * 1.1f, hr * 0.15f), top + new Vector3(0f, hr * 1.55f, hr * 0.75f), hr * 0.35f, 0.004f,
                       s.Secondary, 0f, 8, caps: false);
                k.Cylinder(At(top + new Vector3(0f, -hr * 0.02f, 0f)), hr * 0.82f, hr * 0.78f, hr * 0.16f, s.Accent, 0.3f, 12);
                break;
            case HeadGear.Visor:
                k.Ellipsoid(At(hc + new Vector3(0f, hr * 0.05f, -hr * 0.62f)), new Vector3(hr * 0.86f, hr * 0.3f, hr * 0.42f),
                            s.Eye, s.EyeGlow * 1.3f);
                break;
        }

        if (s.Crown)
        {
            float cr = hr * 0.62f;
            Vector3 cc = top + new Vector3(0f, hr * (s.Head is HeadGear.WizardHat ? 1.7f : 0.35f), 0f);
            k.Cylinder(At(cc), cr, cr * 1.08f, hr * 0.2f, s.AuraColor.Lerp(new Color(1f, 0.85f, 0.3f), 0.5f), 0.9f, 10);
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.Tau;
                Vector3 b0 = cc + new Vector3(Mathf.Cos(a) * cr, hr * 0.18f, Mathf.Sin(a) * cr);
                k.Cone(b0, b0 + new Vector3(0f, hr * 0.42f, 0f), hr * 0.12f, new Color(1f, 0.88f, 0.35f), 1.1f, 4);
            }
        }
    }

    /// <summary>Depth of the head surface at (x, y) from its centre, so features
    /// sit on the face instead of floating in front or sinking in.</summary>
    private static float FrontZ(float front, float hr, float x, float y)
    {
        float u = x / hr, v = y / hr;
        float d = 1f - u * u - v * v;
        return front * Mathf.Sqrt(Mathf.Max(0.15f, d));
    }

    /// <summary>Spikes and crystals along the back.</summary>
    private static void Back(Ctx c, Vector3 at, float w, float len, bool along = false)
    {
        ModelSpec s = c.S;
        MeshKit k = c.Body;
        if (s.BackSpikes)
        {
            for (int i = 0; i < 4; i++)
            {
                float t = i / 3f - 0.5f;
                Vector3 b0 = along ? at + new Vector3(0f, 0f, t * len * 1.3f) : at + new Vector3(0f, -t * len * 0.8f, 0f);
                Vector3 dir = along ? new Vector3(0f, 1f, 0.35f) : new Vector3(0f, 0.45f, 1f);
                float size = w * (0.55f - Mathf.Abs(t) * 0.35f);
                k.Cone(b0, b0 + dir.Normalized() * size * 1.6f, size * 0.42f, s.Accent, 0.1f, 5);
            }
        }
        if (s.Crystals)
        {
            uint seed = s.Seed;
            for (int i = 0; i < 5; i++)
            {
                float a = MeshKit.Hash(seed, i, 1) - 0.5f;
                float b = MeshKit.Hash(seed, i, 2);
                Vector3 b0 = at + new Vector3(a * w * 1.6f, (b - 0.5f) * len * 0.5f, 0f);
                Vector3 dir = new Vector3(a * 1.2f, 1f, along ? 0.2f : 0.6f).Normalized();
                float size = w * (0.35f + b * 0.35f);
                k.Cone(b0 - dir * size * 0.2f, b0 + dir * size * 1.8f, size * 0.3f, s.Accent, 1.5f, 6, s.Accent.Lightened(0.5f));
            }
        }
    }

    private static void Tail(MeshKit k, Vector3 root, float length, float r, Color col, Color tip)
    {
        Vector3 a = root;
        Vector3 b = root + new Vector3(0f, length * 0.1f, length * 0.5f);
        Vector3 d = b + new Vector3(0f, length * 0.35f, length * 0.4f);
        k.Tube(a, b, r, r * 0.75f, col, 0f, 7);
        k.Tube(b, d, r * 0.75f, r * 0.3f, col, 0f, 7, endColor: tip);
    }

    private static void Wings(Ctx c, Vector3 root, float w, float span, bool flyer)
    {
        ModelSpec s = c.S;
        for (int side = -1; side <= 1; side += 2)
        {
            var kit = new MeshKit();
            float sx = side;
            // A bony leading edge and a membrane: two shapes, one silhouette.
            Vector3 elbow = new(sx * span * 0.45f, span * 0.22f, span * 0.05f);
            Vector3 tip = new(sx * span, span * 0.05f, span * 0.2f);
            kit.Tube(Vector3.Zero, elbow, w * 0.12f, w * 0.08f, s.Secondary, 0f, 6);
            kit.Tube(elbow, tip, w * 0.08f, 0.004f, s.Secondary, 0f, 6);
            var mem = new Transform3D(new Basis(Vector3.Forward, sx * 0.12f), new Vector3(sx * span * 0.5f, span * 0.03f, span * 0.18f));
            kit.Ellipsoid(mem, new Vector3(span * 0.5f, span * 0.025f, span * 0.24f), s.Accent.Lerp(s.Primary, 0.35f),
                          s.Crystals ? 0.8f : 0.15f, 10, 4);
            c.Parts.Add((new PartDef
            {
                Role = side < 0 ? PartRole.WingL : PartRole.WingR,
                Pivot = root + new Vector3(sx * w * 0.6f, 0f, 0f),
                Side = side,
            }, kit, null));
        }
        if (flyer) c.Out.LimbSwing = 0.8f;
    }

    /// <summary>Weapons and shields, built in hand space with the arm hanging
    /// down, so a held sword points forward and the swing carries it.</summary>
    private static void Held(MeshKit k, HeldItem item, Vector3 hand, float H, float armR, ModelSpec s, int side)
    {
        Color metal = new(0.82f, 0.85f, 0.9f), wood = new(0.45f, 0.3f, 0.18f);
        switch (item)
        {
            case HeldItem.Sword:
                k.Tube(hand + new Vector3(0f, 0f, 0.06f * H), hand + new Vector3(0f, 0f, -0.04f * H), armR * 0.4f, armR * 0.4f, wood, 0f, 5);
                k.Box(new Transform3D(Basis.Identity, hand + new Vector3(0f, 0f, -0.05f * H)), new Vector3(0.14f * H, armR * 0.6f, armR * 0.6f), s.Accent);
                k.Box(new Transform3D(new Basis(Vector3.Right, -0.15f), hand + new Vector3(0f, 0.03f * H, -0.24f * H)),
                      new Vector3(0.055f * H, 0.012f * H, 0.36f * H), metal, 0.25f);
                break;
            case HeldItem.Club:
                k.Tube(hand + new Vector3(0f, 0f, 0.05f * H), hand + new Vector3(0f, 0.08f * H, -0.32f * H), armR * 0.5f, armR * 1.3f, wood, 0f, 7);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.Tau / 4f;
                    Vector3 b0 = hand + new Vector3(Mathf.Cos(a) * armR * 1.1f, 0.07f * H + Mathf.Sin(a) * armR * 1.1f, -0.28f * H);
                    k.Cone(b0, b0 + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * armR * 1.2f, armR * 0.3f, metal, 0f, 4);
                }
                break;
            case HeldItem.Staff:
                k.Tube(hand + new Vector3(0f, -0.25f * H, 0.05f * H), hand + new Vector3(0f, 0.45f * H, -0.08f * H), armR * 0.35f, armR * 0.3f, wood, 0f, 6);
                k.Sphere(hand + new Vector3(0f, 0.5f * H, -0.09f * H), 0.05f * H, s.Accent, 2.2f, 8, 6);
                k.Tube(hand + new Vector3(-0.03f * H, 0.44f * H, -0.08f * H), hand + new Vector3(-0.04f * H, 0.56f * H, -0.1f * H), armR * 0.2f, 0.003f, wood, 0f, 4);
                k.Tube(hand + new Vector3(0.03f * H, 0.44f * H, -0.08f * H), hand + new Vector3(0.04f * H, 0.56f * H, -0.1f * H), armR * 0.2f, 0.003f, wood, 0f, 4);
                break;
            case HeldItem.Spear:
                k.Tube(hand + new Vector3(0f, -0.3f * H, 0.12f * H), hand + new Vector3(0f, 0.55f * H, -0.15f * H), armR * 0.3f, armR * 0.3f, wood, 0f, 5);
                k.Cone(hand + new Vector3(0f, 0.55f * H, -0.15f * H), hand + new Vector3(0f, 0.72f * H, -0.2f * H), armR * 0.7f, metal, 0.3f, 4);
                break;
            case HeldItem.Shield:
            {
                var xf = new Transform3D(new Basis(Vector3.Forward, Mathf.Pi * 0.5f) * new Basis(Vector3.Up, 0f),
                                         hand + new Vector3(side * armR * 1.2f, 0.06f * H, -armR * 0.4f));
                xf.Basis = MeshKit.BasisAlong(new Vector3(side, 0f, 0f));
                k.Cylinder(xf, 0.16f * H, 0.15f * H, 0.035f * H, s.Secondary, 0f, 8, s.Accent);
                k.Sphere(hand + new Vector3(side * (armR * 1.2f + 0.04f * H), 0.06f * H, -armR * 0.4f), 0.04f * H, s.Accent, 0.4f, 6, 4);
                break;
            }
            case HeldItem.Blades:
            {
                // A scything blade growing out of the forearm, curving forward.
                Vector3 a = hand + new Vector3(0f, 0.05f * H, 0f);
                Vector3 b = a + new Vector3(0f, -0.08f * H, -0.18f * H);
                Vector3 d = b + new Vector3(0f, 0.12f * H, -0.16f * H);
                k.Tube(a, b, armR * 0.9f, armR * 0.55f, s.Accent.Lerp(metal, 0.4f), 0.3f, 5);
                k.Tube(b, d, armR * 0.55f, 0.004f, s.Accent.Lerp(metal, 0.6f), 0.6f, 5);
                break;
            }
            case HeldItem.Claws:
                for (int i = -1; i <= 1; i++)
                    k.Cone(hand + new Vector3(i * armR * 0.6f, -armR * 0.5f, -armR * 0.5f),
                           hand + new Vector3(i * armR * 0.8f, -armR * 2.2f, -armR * 1.6f), armR * 0.3f, s.Accent.Lightened(0.3f), 0.2f, 4);
                break;
            case HeldItem.Bow:
            {
                Vector3 g = hand + new Vector3(0f, 0f, -armR * 0.5f);
                Vector3 top = g + new Vector3(0f, 0.28f * H, 0.06f * H), bot = g + new Vector3(0f, -0.28f * H, 0.06f * H);
                k.Tube(g, top, armR * 0.4f, armR * 0.2f, wood, 0f, 5);
                k.Tube(g, bot, armR * 0.4f, armR * 0.2f, wood, 0f, 5);
                k.Tube(top, bot, armR * 0.07f, armR * 0.07f, new Color(0.9f, 0.9f, 0.85f), 0f, 3, caps: false);
                break;
            }
        }
    }

    private static Transform3D At(float x, float y, float z) => new(Basis.Identity, new Vector3(x, y, z));
    private static Transform3D At(Vector3 p) => new(Basis.Identity, p);
}
