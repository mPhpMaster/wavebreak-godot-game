using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

/// <summary>
/// What a <see cref="ModelSpec.Custom"/> builder gets: the body kit (in hips
/// space), the model height, and a place to hang flapping fins. Everything else
/// — outline, far LOD, flash, dissolve, squash and bob — comes from the normal
/// rig, so a hand-built body behaves exactly like a generated one.
/// </summary>
public sealed class CustomBody
{
    public ModelSpec Spec { get; }
    public float H { get; }
    public MeshKit Kit { get; }

    /// <summary>Where the body pivots. Zero (the feet) for hoppers, so the
    /// squash keeps them planted; the body centre for flyers, so the bob and
    /// tilt turn round the middle.</summary>
    public Vector3 HipsOffset = Vector3.Zero;
    public float StrideLength;

    internal readonly List<(PartRole role, float side, Vector3 pivot, MeshKit kit)> Parts = new();

    internal CustomBody(ModelSpec spec, float h, MeshKit kit)
    {
        Spec = spec;
        H = h;
        Kit = kit;
        StrideLength = h * 1.6f / Mathf.Max(0.3f, spec.Cadence);
    }

    /// <summary>A fin or wing on one side, built around its pivot. Flyers flap
    /// these; hoppers carry them still.</summary>
    public void Fin(int side, Vector3 pivot, MeshKit kit)
        => Parts.Add((side < 0 ? PartRole.WingL : PartRole.WingR, side, pivot, kit));
}

/// <summary>
/// Models for the cute wave (<c>Data/Catalog.Enemies3.cs</c>): hand-built
/// procedural bodies in the same chunky toon style as the rest of the roster,
/// but rounder, with big glossy eyes, blush and soft pastel colours. They are
/// hostile; they just do not look it, which is the joke and also the reason
/// each one keeps its mechanic in its silhouette (a pot, a bubble on the head,
/// spines, claws, a tether, a shell, a lure).
///
/// Registered through <see cref="EnemyVisuals.Register"/> from
/// <see cref="BehaviourModels.EnsureRegistered"/>, so nothing else in the
/// visuals layer has to know about them.
/// </summary>
public static class CuteModels
{
    private static bool _registered;

    private static readonly Color White = new(1f, 1f, 1f);
    private static readonly Color Pupil = new(0.10f, 0.08f, 0.18f);
    private static readonly Color Blush = new(1.00f, 0.55f, 0.62f);
    private static readonly Color Ink = new(0.22f, 0.10f, 0.14f);
    private static readonly Color Cream = new(1.00f, 0.95f, 0.86f);

    public static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;

        Register("sidle_crab", BodyType.Blob, 1.8f, b => Crab(b, mother: false));
        Register("mother_crab", BodyType.Blob, 1.1f, b => Crab(b, mother: true));
        Register("glimmer_snail", BodyType.Blob, 0.8f, Snail);
        Register("crock_mimic", BodyType.Blob, 1.4f, Mimic);
        Register("bubblegill", BodyType.Blob, 1.2f, Bubblegill);
        Register("roly_puffer", BodyType.Blob, 1.3f, Puffer);
        Register("linkling", BodyType.Flyer, 1f, Jelly);
        Register("lanternfin", BodyType.Flyer, 1f, Lanternfin);
    }

    /// <summary>True when <paramref name="id"/> wears one of these bodies.</summary>
    public static bool Has(EnemyDef def) => EnemyVisuals.SpecFor(def).Custom is not null;

    private static void Register(string id, BodyType body, float cadence, Action<CustomBody> build)
    {
        if (Catalog.EnemyById(id) is not { } def) return;
        var s = new ModelSpec
        {
            Id = "enemy:" + id,
            Body = body,
            Primary = def.Tint,
            Secondary = def.Tint.Darkened(0.3f),
            Accent = def.Tint.Lightened(0.45f),
            Eye = White,
            Face = false,
            Cadence = cadence,
            Height = Mathf.Max(0.4f, def.Height),
            Seed = (uint)id.GetHashCode(),
            Custom = build,
        };
        if (def.IsElite || def.IsBoss)
        {
            s.Crown = def.IsElite;
            s.Aura = true;
            s.AuraColor = def.Tint.Lightened(0.25f);
            s.LodDistance = def.IsBoss ? 0f : 55f;
        }
        EnemyVisuals.Register(id, s, def.IsElite, def.IsBoss);
    }

    // ================================================================ helpers

    private static Transform3D At(Vector3 p) => new(Basis.Identity, p);
    private static Transform3D At(float x, float y, float z) => new(Basis.Identity, new Vector3(x, y, z));

    /// <summary>A point on the front (-Z) of an ellipsoid at (x, y) from its
    /// centre, pushed out by <paramref name="lift"/>.</summary>
    private static Vector3 Front(Vector3 c, Vector3 r, float x, float y, float lift = 0f)
    {
        float u = x / r.X, v = y / r.Y;
        float w = Mathf.Sqrt(Mathf.Max(0.02f, 1f - u * u - v * v));
        return c + new Vector3(x, y, -(r.Z * w + lift));
    }

    /// <summary>Big glossy cartoon eyes: white, a tall dark pupil looking
    /// slightly in, two highlights. <paramref name="at"/> is the pair's centre.</summary>
    private static void Eyes(MeshKit k, Vector3 at, float spacing, float r, Color? iris = null)
    {
        Color pupil = iris ?? Pupil;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 p = at + new Vector3(side * spacing * 0.5f, 0f, 0f);
            k.Ellipsoid(At(p), new Vector3(r, r * 1.12f, r * 0.62f), White, 0.15f, 12, 8);
            Vector3 q = p + new Vector3(-side * r * 0.08f, -r * 0.06f, -r * 0.40f);
            k.Ellipsoid(At(q), new Vector3(r * 0.68f, r * 0.82f, r * 0.36f), pupil, 0f, 12, 8);
            k.Sphere(q + new Vector3(-r * 0.24f, r * 0.34f, -r * 0.32f), r * 0.26f, White, 1.3f, 8, 5);
            k.Sphere(q + new Vector3(r * 0.22f, -r * 0.30f, -r * 0.30f), r * 0.12f, White, 1.0f, 6, 4);
        }
    }

    private static void Cheeks(MeshKit k, Vector3 left, Vector3 right, float r)
    {
        k.Ellipsoid(At(left), new Vector3(r, r * 0.6f, r * 0.35f), Blush, 0.1f, 8, 5);
        k.Ellipsoid(At(right), new Vector3(r, r * 0.6f, r * 0.35f), Blush, 0.1f, 8, 5);
    }

    /// <summary>A small curved smile of beads, centred at <paramref name="at"/>.</summary>
    private static void Smile(MeshKit k, Vector3 at, float width, float r)
    {
        const int n = 5;
        Vector3 prev = default;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1) * 2f - 1f;
            var p = at + new Vector3(t * width * 0.5f, (t * t - 1f) * width * 0.22f, -Mathf.Abs(t) * r * 0.2f);
            if (i > 0) k.Tube(prev, p, r, r, Ink, 0f, 5);
            prev = p;
        }
    }

    // ================================================================= crabs

    private static void Crab(CustomBody b, bool mother)
    {
        MeshKit k = b.Kit;
        ModelSpec s = b.Spec;
        float H = b.H;
        Color shell = s.Primary, top = s.Primary.Lightened(0.2f);
        Color claw = s.Primary.Lightened(0.12f), leg = s.Primary.Darkened(0.12f);

        var c = new Vector3(0f, 0.40f * H, 0f);
        var r = new Vector3(0.60f * H, 0.33f * H, 0.45f * H);
        k.Ellipsoid(At(c), r, shell, 0f, 16, 10, top);
        k.Ellipsoid(At(c + new Vector3(0f, -0.10f * H, -0.04f * H)), new Vector3(r.X * 0.86f, r.Y * 0.62f, r.Z * 0.86f), Cream);
        // Freckles on the shell.
        for (int i = 0; i < 5; i++)
        {
            float a = -1.1f + i * 0.55f;
            k.Sphere(c + new Vector3(Mathf.Sin(a) * r.X * 0.62f, r.Y * 0.80f, Mathf.Cos(a) * r.Z * 0.35f),
                     0.045f * H, top.Lightened(0.25f), 0f, 6, 4);
        }

        // Legs: three a side, splayed, little round feet.
        for (int side = -1; side <= 1; side += 2)
        for (int i = 0; i < 3; i++)
        {
            float z = (-0.12f + i * 0.17f) * H;
            var hip = new Vector3(side * 0.45f * H, 0.30f * H, z);
            var knee = new Vector3(side * 0.72f * H, 0.24f * H, z + 0.04f * H);
            var foot = new Vector3(side * 0.82f * H, 0.03f * H, z + 0.07f * H);
            k.Tube(hip, knee, 0.055f * H, 0.045f * H, leg, 0f, 6);
            k.Tube(knee, foot, 0.045f * H, 0.035f * H, leg, 0f, 6);
        }

        // Claws: round mitts held up in front, pincers a little open.
        for (int side = -1; side <= 1; side += 2)
        {
            var shoulder = new Vector3(side * 0.42f * H, 0.40f * H, -0.22f * H);
            var wrist = new Vector3(side * 0.62f * H, 0.50f * H, -0.48f * H);
            k.Tube(shoulder, wrist, 0.07f * H, 0.06f * H, leg, 0f, 7);
            var palm = wrist + new Vector3(side * 0.04f * H, 0.03f * H, -0.12f * H);
            k.Ellipsoid(At(palm), new Vector3(0.15f * H, 0.13f * H, 0.17f * H), claw, 0f, 10, 7, claw.Lightened(0.15f));
            var upper = new Transform3D(new Basis(Vector3.Right, 0.35f), palm + new Vector3(0f, 0.07f * H, -0.15f * H));
            var lower = new Transform3D(new Basis(Vector3.Right, -0.35f), palm + new Vector3(0f, -0.05f * H, -0.14f * H));
            k.Ellipsoid(upper, new Vector3(0.09f * H, 0.06f * H, 0.13f * H), claw, 0f, 8, 5, claw.Lightened(0.2f));
            k.Ellipsoid(lower, new Vector3(0.07f * H, 0.045f * H, 0.11f * H), claw.Darkened(0.05f), 0f, 8, 5);
        }

        // Eyes on stalks.
        float er = (mother ? 0.12f : 0.14f) * H;
        float stalkTop = (mother ? 0.88f : 0.92f) * H;
        for (int side = -1; side <= 1; side += 2)
            k.Tube(new Vector3(side * 0.16f * H, 0.62f * H, -0.18f * H), new Vector3(side * 0.17f * H, stalkTop - er * 0.6f, -0.22f * H),
                   0.045f * H, 0.04f * H, shell, 0f, 6);
        var eyeAt = new Vector3(0f, stalkTop, -0.24f * H);
        Eyes(k, eyeAt, 0.34f * H, er);
        if (mother)
        {
            // Lashes: she is somebody's mum.
            for (int side = -1; side <= 1; side += 2)
            for (int i = -1; i <= 1; i++)
            {
                var root = eyeAt + new Vector3(side * 0.17f * H + i * er * 0.45f, er * 0.95f, -er * 0.2f);
                k.Cone(root, root + new Vector3(i * er * 0.25f + side * er * 0.15f, er * 0.45f, -er * 0.1f), er * 0.1f, Ink, 0f, 4);
            }
        }

        Cheeks(k, Front(c, r, -0.30f * H, 0.02f * H, 0.005f), Front(c, r, 0.30f * H, 0.02f * H, 0.005f), 0.07f * H);
        Smile(k, Front(c, r, 0f, -0.02f * H, 0.01f), 0.15f * H, 0.012f * H);

        if (mother)
        {
            // A pearl necklace round the front, and a little coral comb on top.
            for (int i = 0; i < 9; i++)
            {
                float x = (i - 4) * 0.075f * H;
                k.Sphere(Front(c, r, x, -0.14f * H - MathF.Abs(x) * 0.12f, 0.02f * H), 0.035f * H, new Color(1f, 0.97f, 0.92f), 0.6f, 6, 4);
            }
        }
        if (s.Crown)
        {
            // The elite marker every elite wears, sized for a crab: a little
            // gold tiara on the shell, behind the eyes.
            float cr = 0.13f * H;
            Vector3 cc = c + new Vector3(0f, r.Y * 0.93f, 0.12f * H);
            Color gold = s.AuraColor.Lerp(new Color(1f, 0.85f, 0.3f), 0.6f);
            k.Cylinder(At(cc), cr, cr * 1.1f, 0.05f * H, gold, 0.9f, 12);
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.Tau;
                Vector3 b0 = cc + new Vector3(Mathf.Cos(a) * cr, 0.045f * H, Mathf.Sin(a) * cr);
                k.Cone(b0, b0 + new Vector3(0f, 0.09f * H, 0f), 0.03f * H, new Color(1f, 0.88f, 0.35f), 1.1f, 4);
                k.Sphere(b0 + new Vector3(0f, 0.1f * H, 0f), 0.018f * H, new Color(1f, 0.7f, 0.8f), 1.2f, 5, 3);
            }
        }
    }

    // ================================================================= snail

    private static void Snail(CustomBody b)
    {
        MeshKit k = b.Kit;
        ModelSpec s = b.Spec;
        float H = b.H;
        Color body = s.Primary, bodyTop = s.Primary.Lightened(0.2f);
        Color shellA = new(0.86f, 0.70f, 0.98f), shellB = new(1.00f, 0.88f, 0.80f), shellC = new(1.00f, 0.66f, 0.78f);

        // The foot: a long soft slug, and the neck rising out of its front.
        k.Ellipsoid(At(0f, 0.13f * H, 0.06f * H), new Vector3(0.30f * H, 0.14f * H, 0.60f * H), body, 0f, 12, 7, bodyTop);
        k.Tube(new Vector3(0f, 0.18f * H, -0.30f * H), new Vector3(0f, 0.50f * H, -0.44f * H), 0.17f * H, 0.19f * H, body, 0f, 10);
        var hc = new Vector3(0f, 0.56f * H, -0.46f * H);
        var hr = new Vector3(0.23f * H, 0.21f * H, 0.21f * H);
        k.Ellipsoid(At(hc), hr, body, 0f, 12, 8, bodyTop);

        // Eye stalks with the eyes on top.
        for (int side = -1; side <= 1; side += 2)
            k.Tube(hc + new Vector3(side * 0.08f * H, 0.12f * H, 0f), hc + new Vector3(side * 0.13f * H, 0.36f * H, -0.03f * H),
                   0.032f * H, 0.028f * H, body, 0f, 6);
        Eyes(k, hc + new Vector3(0f, 0.40f * H, -0.06f * H), 0.27f * H, 0.1f * H);
        Cheeks(k, Front(hc, hr, -0.13f * H, -0.03f * H, 0.004f), Front(hc, hr, 0.13f * H, -0.03f * H, 0.004f), 0.05f * H);
        Smile(k, Front(hc, hr, 0f, -0.07f * H, 0.006f), 0.09f * H, 0.009f * H);

        // The shell: a big round swirl, side-on, in pastel bands.
        var sc = new Vector3(0f, 0.50f * H, 0.16f * H);
        k.Ellipsoid(At(sc), new Vector3(0.24f * H, 0.36f * H, 0.38f * H), shellA, 0f, 14, 9, shellA.Lightened(0.15f));
        for (int side = -1; side <= 1; side += 2)
        {
            k.Ellipsoid(At(sc + new Vector3(side * 0.17f * H, 0.01f * H, 0.01f * H)), new Vector3(0.08f * H, 0.27f * H, 0.28f * H), shellB, 0f, 12, 7);
            k.Ellipsoid(At(sc + new Vector3(side * 0.22f * H, 0.03f * H, 0.03f * H)), new Vector3(0.06f * H, 0.17f * H, 0.18f * H), shellA, 0f, 12, 7);
            k.Ellipsoid(At(sc + new Vector3(side * 0.26f * H, 0.05f * H, 0.05f * H)), new Vector3(0.04f * H, 0.08f * H, 0.08f * H), shellC, 0.2f, 10, 6);
        }
        // Glimmer: a few sparkles on the shell.
        k.Sphere(sc + new Vector3(0.05f * H, 0.34f * H, -0.05f * H), 0.03f * H, White, 1.6f, 6, 4);
        k.Sphere(sc + new Vector3(-0.08f * H, 0.28f * H, 0.16f * H), 0.022f * H, White, 1.6f, 6, 4);
        k.Sphere(sc + new Vector3(0.1f * H, 0.15f * H, 0.3f * H), 0.02f * H, White, 1.6f, 6, 4);
    }

    // ================================================================= mimic

    private static void Mimic(CustomBody b)
    {
        MeshKit k = b.Kit;
        ModelSpec s = b.Spec;
        float H = b.H;
        Color clay = s.Primary, clayTop = s.Primary.Lightened(0.15f), dark = s.Primary.Darkened(0.25f);
        Color band = new(0.40f, 0.78f, 0.78f);

        // Pot belly, neck and rim.
        var c = new Vector3(0f, 0.38f * H, 0f);
        var r = new Vector3(0.42f * H, 0.38f * H, 0.42f * H);
        k.Ellipsoid(At(c), r, clay, 0f, 16, 10, clayTop);
        k.Ellipsoid(At(c + new Vector3(0f, 0.03f * H, 0f)), new Vector3(r.X * 1.02f, 0.055f * H, r.Z * 1.02f), band, 0.1f, 16, 4);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f;
            k.Sphere(c + new Vector3(Mathf.Sin(a) * r.X * 1.03f, -0.07f * H, Mathf.Cos(a) * r.Z * 1.03f), 0.03f * H, band.Lightened(0.3f), 0.1f, 6, 4);
        }
        k.Cylinder(At(0f, 0.66f * H, 0f), 0.27f * H, 0.30f * H, 0.12f * H, clay, 0f, 14, clayTop);
        k.Ellipsoid(At(0f, 0.78f * H, 0f), new Vector3(0.33f * H, 0.05f * H, 0.33f * H), clayTop, 0f, 14, 4);

        // The open mouth under the lid, and the tongue lolling over the rim.
        k.Ellipsoid(At(0f, 0.80f * H, -0.02f * H), new Vector3(0.27f * H, 0.07f * H, 0.25f * H), new Color(0.20f, 0.06f, 0.10f));
        k.Ellipsoid(new Transform3D(new Basis(Vector3.Right, 0.9f), new Vector3(0f, 0.74f * H, -0.33f * H)),
                    new Vector3(0.09f * H, 0.035f * H, 0.13f * H), new Color(1f, 0.48f, 0.58f), 0.1f, 10, 6);

        // The lid, tipped up at the front like a hat pushed back.
        var lid = new Transform3D(new Basis(Vector3.Right, 0.55f), new Vector3(0f, 0.93f * H, 0.08f * H));
        k.Cylinder(lid, 0.34f * H, 0.30f * H, 0.07f * H, dark, 0f, 14, clayTop);
        k.Sphere(lid * new Vector3(0f, 0.12f * H, 0f), 0.07f * H, band, 0.1f, 8, 5);

        // Eyes peeking out between pot and lid.
        Eyes(k, new Vector3(0f, 0.92f * H, -0.18f * H), 0.26f * H, 0.11f * H);
        Cheeks(k, Front(c, r, -0.22f * H, 0.17f * H, 0.004f), Front(c, r, 0.22f * H, 0.17f * H, 0.004f), 0.06f * H);

        // Stubby feet.
        for (int side = -1; side <= 1; side += 2)
            k.Ellipsoid(At(side * 0.17f * H, 0.04f * H, -0.12f * H), new Vector3(0.09f * H, 0.05f * H, 0.12f * H), dark);
    }

    // ============================================================ bubblegill

    private static void Bubblegill(CustomBody b)
    {
        MeshKit k = b.Kit;
        ModelSpec s = b.Spec;
        float H = b.H;
        Color body = s.Primary, top = s.Primary.Lightened(0.15f);
        Color fin = new(0.80f, 0.70f, 1.00f);

        var c = new Vector3(0f, 0.46f * H, 0f);
        var r = new Vector3(0.40f * H, 0.42f * H, 0.38f * H);
        k.Ellipsoid(At(c), r, body, 0f, 16, 10, top);
        k.Ellipsoid(At(c + new Vector3(0f, -0.12f * H, -0.12f * H)), new Vector3(r.X * 0.72f, r.Y * 0.62f, r.Z * 0.7f), Cream);

        // Fins: a crest on top, a fin each side, a tail behind.
        k.Ellipsoid(At(c + new Vector3(0f, r.Y * 0.95f, 0.06f * H)), new Vector3(0.035f * H, 0.15f * H, 0.22f * H), fin, 0.15f, 10, 6);
        for (int side = -1; side <= 1; side += 2)
            k.Ellipsoid(new Transform3D(new Basis(Vector3.Forward, side * 0.5f), c + new Vector3(side * r.X * 0.98f, -0.04f * H, 0.04f * H)),
                        new Vector3(0.035f * H, 0.15f * H, 0.11f * H), fin, 0.15f, 10, 6);
        for (int side = -1; side <= 1; side += 2)
            k.Ellipsoid(new Transform3D(new Basis(Vector3.Forward, side * 0.6f), new Vector3(side * 0.09f * H, 0.16f * H, r.Z + 0.06f * H)),
                        new Vector3(0.035f * H, 0.16f * H, 0.09f * H), fin, 0.15f, 10, 6);

        Eyes(k, Front(c, r, 0f, 0.13f * H, -0.05f * H), 0.34f * H, 0.14f * H);
        Cheeks(k, Front(c, r, -0.23f * H, -0.02f * H, 0.004f), Front(c, r, 0.23f * H, -0.02f * H, 0.004f), 0.065f * H);

        // The pursed "o" mouth it blows through.
        Vector3 m = Front(c, r, 0f, -0.08f * H, 0f);
        k.Ellipsoid(At(m), new Vector3(0.085f * H, 0.08f * H, 0.06f * H), new Color(1f, 0.55f, 0.65f), 0.05f, 10, 6);
        k.Ellipsoid(At(m + new Vector3(0f, 0f, -0.03f * H)), new Vector3(0.045f * H, 0.042f * H, 0.04f * H), new Color(0.3f, 0.08f, 0.15f));

        // A couple of bubbles floating off the top: the tell for what it does.
        Color bub = new(0.80f, 0.95f, 1.00f);
        k.Sphere(new Vector3(0.16f * H, 1.02f * H, -0.12f * H), 0.07f * H, bub, 0.7f, 10, 6);
        k.Sphere(new Vector3(0.26f * H, 1.16f * H, -0.08f * H), 0.045f * H, bub, 0.7f, 8, 5);
        k.Sphere(new Vector3(0.13f * H, 1.05f * H, -0.18f * H), 0.018f * H, White, 1.5f, 6, 4);
    }

    // ================================================================ puffer

    private static void Puffer(CustomBody b)
    {
        MeshKit k = b.Kit;
        ModelSpec s = b.Spec;
        float H = b.H;
        Color body = s.Primary, top = s.Primary.Lightened(0.15f);
        Color spine = new(1.00f, 0.66f, 0.38f), fin = new(1.00f, 0.72f, 0.55f);

        var c = new Vector3(0f, 0.47f * H, 0f);
        float R = 0.45f * H;
        var r = new Vector3(R, R, R);
        k.Ellipsoid(At(c), r, body, 0f, 18, 12, top);
        k.Ellipsoid(At(c + new Vector3(0f, -0.14f * H, -0.10f * H)), new Vector3(R * 0.76f, R * 0.6f, R * 0.74f), Cream);

        // Soft spines all over, except across the face.
        const int n = 34;
        float golden = Mathf.Pi * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < n; i++)
        {
            float y = 1f - (i + 0.5f) / n * 2f;
            float rad = Mathf.Sqrt(1f - y * y);
            float th = golden * i;
            var dir = new Vector3(Mathf.Cos(th) * rad, y, Mathf.Sin(th) * rad);
            if (dir.Z < -0.45f && dir.Y > -0.55f) continue;          // keep the face clear
            if (dir.Y < -0.8f) continue;                              // and the bottom it sits on
            Vector3 root = c + dir * R * 0.94f;
            k.Cone(root, root + dir * 0.13f * H, 0.045f * H, spine, 0f, 5, spine.Lightened(0.3f));
        }

        for (int side = -1; side <= 1; side += 2)
            k.Ellipsoid(new Transform3D(new Basis(Vector3.Forward, side * 0.7f), c + new Vector3(side * R * 1.0f, -0.02f * H, 0.02f * H)),
                        new Vector3(0.03f * H, 0.12f * H, 0.09f * H), fin, 0.15f, 8, 5);
        for (int side = -1; side <= 1; side += 2)
            k.Ellipsoid(new Transform3D(new Basis(Vector3.Forward, side * 0.6f), c + new Vector3(side * 0.08f * H, 0f, R + 0.07f * H)),
                        new Vector3(0.03f * H, 0.14f * H, 0.08f * H), fin, 0.15f, 8, 5);

        Eyes(k, Front(c, r, 0f, 0.10f * H, -0.05f * H), 0.36f * H, 0.15f * H);
        Cheeks(k, Front(c, r, -0.25f * H, -0.05f * H, 0.004f), Front(c, r, 0.25f * H, -0.05f * H, 0.004f), 0.07f * H);
        // A little pout.
        Vector3 m = Front(c, r, 0f, -0.11f * H, 0f);
        k.Ellipsoid(At(m), new Vector3(0.06f * H, 0.04f * H, 0.04f * H), new Color(1f, 0.5f, 0.55f), 0.05f, 8, 5);
    }

    // ================================================================= jelly

    private static void Jelly(CustomBody b)
    {
        MeshKit k = b.Kit;
        ModelSpec s = b.Spec;
        float H = b.H;
        b.HipsOffset = new Vector3(0f, 0.55f * H, 0f);
        Color bell = s.Primary, bellTop = s.Primary.Lightened(0.3f), frill = s.Primary.Lightened(0.15f);
        Color tentA = new(0.85f, 0.75f, 1.00f), tentB = new(1.00f, 0.75f, 0.90f);

        var c = new Vector3(0f, 0.04f * H, 0f);
        var r = new Vector3(0.38f * H, 0.32f * H, 0.38f * H);
        k.Ellipsoid(At(c), r, bell, 0.35f, 16, 10, bellTop);
        k.Ellipsoid(At(c + new Vector3(0f, -0.16f * H, 0f)), new Vector3(r.X * 0.92f, 0.07f * H, r.Z * 0.92f), bell.Darkened(0.1f), 0.3f, 14, 4);
        // Spots on the dome.
        k.Sphere(c + new Vector3(0.12f * H, r.Y * 0.88f, 0.05f * H), 0.05f * H, White, 0.6f, 6, 4);
        k.Sphere(c + new Vector3(-0.14f * H, r.Y * 0.80f, 0.10f * H), 0.04f * H, White, 0.6f, 6, 4);
        k.Sphere(c + new Vector3(0.02f * H, r.Y * 0.75f, 0.2f * H), 0.035f * H, White, 0.6f, 6, 4);

        // A frilly skirt round the rim.
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.Tau / 12f;
            k.Sphere(c + new Vector3(Mathf.Sin(a) * r.X * 0.95f, -0.16f * H, Mathf.Cos(a) * r.Z * 0.95f), 0.075f * H, frill, 0.35f, 8, 5);
        }

        // Tentacles: curly ribbons hanging under the bell.
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.Tau / 6f + 0.3f;
            var root = c + new Vector3(Mathf.Sin(a) * 0.20f * H, -0.18f * H, Mathf.Cos(a) * 0.20f * H);
            var mid = root + new Vector3(Mathf.Sin(a + 1f) * 0.06f * H, -0.2f * H, Mathf.Cos(a + 1f) * 0.06f * H);
            var tip = mid + new Vector3(Mathf.Sin(a - 0.8f) * 0.07f * H, -0.18f * H, Mathf.Cos(a - 0.8f) * 0.07f * H);
            Color col = i % 2 == 0 ? tentA : tentB;
            k.Tube(root, mid, 0.04f * H, 0.03f * H, col, 0.3f, 6);
            k.Tube(mid, tip, 0.03f * H, 0.012f * H, col, 0.3f, 6);
        }

        Eyes(k, Front(c, r, 0f, 0.02f * H, -0.045f * H), 0.30f * H, 0.115f * H);
        Cheeks(k, Front(c, r, -0.21f * H, -0.08f * H, 0.004f), Front(c, r, 0.21f * H, -0.08f * H, 0.004f), 0.06f * H);
        Smile(k, Front(c, r, 0f, -0.10f * H, 0.006f), 0.09f * H, 0.012f * H);
    }

    // ============================================================ lanternfin

    private static void Lanternfin(CustomBody b)
    {
        MeshKit k = b.Kit;
        ModelSpec s = b.Spec;
        float H = b.H;
        b.HipsOffset = new Vector3(0f, 0.5f * H, 0f);
        Color body = s.Primary, top = s.Primary.Darkened(0.15f), belly = s.Primary.Lightened(0.5f);
        Color fin = new(0.55f, 0.85f, 1.00f), glow = new(1.00f, 0.88f, 0.45f);

        var c = Vector3.Zero;
        var r = new Vector3(0.36f * H, 0.34f * H, 0.38f * H);
        k.Ellipsoid(At(c), r, body, 0f, 16, 10, top);
        k.Ellipsoid(At(c + new Vector3(0f, -0.12f * H, -0.06f * H)), new Vector3(r.X * 0.75f, r.Y * 0.6f, r.Z * 0.78f), belly);

        // The lure: a curly stalk off the forehead with a warm glowing bulb.
        var s0 = c + new Vector3(0f, r.Y * 0.85f, -0.08f * H);
        var s1 = s0 + new Vector3(0f, 0.22f * H, -0.06f * H);
        var s2 = s1 + new Vector3(0f, 0.06f * H, -0.2f * H);
        var s3 = s2 + new Vector3(0f, -0.08f * H, -0.08f * H);
        k.Tube(s0, s1, 0.03f * H, 0.025f * H, top, 0f, 6);
        k.Tube(s1, s2, 0.025f * H, 0.022f * H, top, 0f, 6);
        k.Tube(s2, s3, 0.022f * H, 0.02f * H, top, 0f, 6);
        k.Sphere(s3 + new Vector3(0f, -0.06f * H, 0f), 0.1f * H, glow, 3.2f, 12, 8);
        k.Sphere(s3 + new Vector3(-0.03f * H, -0.03f * H, -0.06f * H), 0.03f * H, White, 3f, 6, 4);

        // Fins on the sides flap (they are the rig's wings), a tail fin behind.
        for (int side = -1; side <= 1; side += 2)
        {
            var kit = new MeshKit();
            kit.Ellipsoid(new Transform3D(new Basis(Vector3.Forward, side * 0.25f), new Vector3(side * 0.13f * H, 0f, 0.04f * H)),
                          new Vector3(0.15f * H, 0.025f * H, 0.11f * H), fin, 0.3f, 10, 5);
            b.Fin(side, c + new Vector3(side * r.X * 0.9f, -0.04f * H, 0.02f * H), kit);
        }
        for (int side = -1; side <= 1; side += 2)
            k.Ellipsoid(new Transform3D(new Basis(Vector3.Forward, side * 0.55f), c + new Vector3(side * 0.08f * H, 0f, r.Z + 0.08f * H)),
                        new Vector3(0.03f * H, 0.15f * H, 0.09f * H), fin, 0.3f, 8, 5);
        k.Ellipsoid(At(c + new Vector3(0f, r.Y * 0.92f, 0.1f * H)), new Vector3(0.03f * H, 0.1f * H, 0.16f * H), fin, 0.3f, 8, 5);

        Eyes(k, Front(c, r, 0f, 0.07f * H, -0.05f * H), 0.34f * H, 0.15f * H);
        Cheeks(k, Front(c, r, -0.22f * H, -0.07f * H, 0.004f), Front(c, r, 0.22f * H, -0.07f * H, 0.004f), 0.06f * H);
        // A wide grin with two tiny teeth.
        Vector3 m = Front(c, r, 0f, -0.15f * H, 0.002f);
        k.Ellipsoid(At(m), new Vector3(0.14f * H, 0.045f * H, 0.04f * H), new Color(0.18f, 0.05f, 0.12f));
        for (int side = -1; side <= 1; side += 2)
            k.Cone(m + new Vector3(side * 0.06f * H, 0.03f * H, -0.03f * H), m + new Vector3(side * 0.06f * H, -0.015f * H, -0.035f * H),
                   0.018f * H, White, 0.2f, 4);
    }

    // ======================================================= mimic disguise

    private static Mesh? _pot;
    private static StandardMaterial3D? _eyeWhite, _eyeDark;

    /// <summary>
    /// The pot a dormant <c>crock_mimic</c> hides as: the same Kenney urn the
    /// map's breakable pots use, at the same 0.9 m, plus a pair of eyes that
    /// peek out over the rim now and then (returned in
    /// <paramref name="eyes"/>, hidden by default). Faces +Z, the way enemies turn.
    /// </summary>
    public static Node3D MimicDisguise(float height, out Node3D eyes)
    {
        var root = new Node3D { Name = "Disguise" };
        root.AddChild(new MeshInstance3D
        {
            Name = "Pot",
            Mesh = PotMesh(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        });

        _eyeWhite ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = White };
        _eyeDark ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Pupil };
        var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 6 };
        eyes = new Node3D { Name = "Peek", Visible = false, Position = new Vector3(0f, 0.86f, 0.06f) };
        for (int side = -1; side <= 1; side += 2)
        {
            var w = new MeshInstance3D { Mesh = sphere, MaterialOverride = _eyeWhite, Position = new Vector3(side * 0.085f, 0f, 0f), Scale = new Vector3(0.07f, 0.08f, 0.05f) };
            w.AddChild(new MeshInstance3D { Mesh = sphere, MaterialOverride = _eyeDark, Position = new Vector3(-side * 0.1f, -0.1f, 0.65f), Scale = new Vector3(0.6f, 0.7f, 0.5f) });
            eyes.AddChild(w);
        }
        root.AddChild(eyes);
        _ = height;
        return root;
    }

    private static Mesh PotMesh()
    {
        if (_pot is not null && GodotObject.IsInstanceValid(_pot)) return _pot;
        ArrayMesh? kit = PropFactory.KitMesh("res://assets/kenney/graveyard-kit/urn-round.glb");
        if (kit is not null)
        {
            Aabb box = kit.GetAabb();
            float h = MathF.Max(0.05f, box.Size.Y);
            float sc = 0.9f / h;
            var scaled = new ArrayMesh();
            var xf = new Transform3D(Basis.Identity.Scaled(Vector3.One * sc), new Vector3(0f, -box.Position.Y * sc, 0f));
            for (int surf = 0; surf < kit.GetSurfaceCount(); surf++)
            {
                var st = new SurfaceTool();
                st.AppendFrom(kit, surf, xf);
                st.Commit(scaled);
                scaled.SurfaceSetMaterial(scaled.GetSurfaceCount() - 1, kit.SurfaceGetMaterial(surf));
            }
            if (scaled.GetSurfaceCount() > 0) return _pot = scaled;
        }
        _pot = new SphereMesh
        {
            Radius = 0.38f, Height = 0.8f, RadialSegments = 10, Rings = 6,
            Material = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.60f, 0.56f), Roughness = 0.85f },
        };
        return _pot;
    }
}
