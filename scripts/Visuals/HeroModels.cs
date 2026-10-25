using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// Jointed limbs and a hat anchor for hand-built bodies (see
/// <see cref="HeroModels"/>). The limbs use the biped part roles, so
/// <see cref="CharacterRig"/>'s biped animator poses them exactly like the
/// procedural heroes: walk and run cycles, the air tuck, dash streak, slide,
/// slam, flip, the weapon-arm windup and strike, hit recoil and the death fall.
/// </summary>
public sealed partial class CustomBody
{
    internal readonly List<(PartRole role, float side, Vector3 pivot, MeshKit upper, MeshKit lower, Vector3 lowerPivot)> Limbs = new();

    /// <summary>Radians of arm and leg swing at full speed; 0 keeps the default.</summary>
    public float LimbSwing;

    /// <summary>The crown of the head in hips space, and the head's half-width:
    /// where and how big a hat goes (<see cref="CharacterRig.SetHat"/>).</summary>
    public Vector3? HatAnchor;
    public float HatRadius;

    /// <summary>An arm or a leg: <paramref name="upper"/> hangs from
    /// <paramref name="joint"/> (shoulder or hip), <paramref name="lower"/> from
    /// <paramref name="mid"/> (elbow or knee). Both points in hips space; each
    /// kit is built around its own pivot.</summary>
    internal void Limb(bool arm, int side, Vector3 joint, Vector3 mid, MeshKit upper, MeshKit lower)
    {
        PartRole role = arm ? (side < 0 ? PartRole.ArmL : PartRole.ArmR) : (side < 0 ? PartRole.LegL : PartRole.LegR);
        Limbs.Add((role, side, joint, upper, lower, mid - joint));
    }
}

/// <summary>
/// The heroes trio's bodies (Meta/Characters.Heroes.cs), built by hand from
/// <see cref="MeshKit"/> shapes rather than imported: no free model fitted
/// all three (an upright parrot in a captain's coat, a burly sailor dog, a
/// goggled fox with a kite), and the Kenney cube pets they used to wear are
/// the cute crew's. Each one is its own silhouette at gameplay distance:
///
///   * Sunny: a red macaw in a navy captain's coat with gold epaulettes and
///     coat tails, a crest of three colours, a long tail and a brass spyglass;
///   * Biscuit: a broad tan sea dog with floppy ears, a striped jersey, a
///     sailor collar, a red neckerchief and an iron anchor slung on his back;
///   * Flick: a slim orange fox with tall ears, flight goggles, a scarf whose
///     two ends stream behind, a huge white-tipped tail and a kite spool.
///
/// Chibi proportions (the head is about a third of the height) so the face
/// reads from the game camera. Skins repaint through <see cref="SkinRecolor"/>:
/// fur and plumage take the dye fully, clothes partly, and eyes, brass,
/// lenses and iron not at all (<see cref="MeshKit.Paint"/>).
/// </summary>
public static class HeroModels
{
    public const string Sunny = "hero-sunny", Biscuit = "hero-biscuit", Flick = "hero-flick";
    public static readonly string[] LookIds = { Sunny, Biscuit, Flick };

    public static bool Has(string id) => Array.IndexOf(LookIds, id) >= 0;

    /// <summary>A fresh spec for one of <see cref="LookIds"/>, or null.
    /// <see cref="PlayerLooks"/> caches it.</summary>
    public static ModelSpec? Spec(string id) => id switch
    {
        Sunny => Make(id, 1.50f, 1.00f, 1.05f, BuildSunny),
        Biscuit => Make(id, 1.52f, 1.25f, 0.92f, BuildBiscuit),
        Flick => Make(id, 1.50f, 0.92f, 1.10f, BuildFlick),
        _ => null,
    };

    private static ModelSpec Make(string id, float height, float bulk, float cadence, Action<CustomBody> build) => new()
    {
        Id = "hero:" + id,
        Body = BodyType.Biped,
        Height = height,
        Bulk = bulk,
        Jointed = true,
        Face = false,
        Cadence = cadence,
        LodDistance = 0f, // the hero is always close
        Custom = build,
    };

    // ================================================================ palette

    private static readonly Color White = new(1f, 1f, 1f);
    private static readonly Color Pupil = new(0.08f, 0.06f, 0.14f);
    private static readonly Color Ink = new(0.12f, 0.08f, 0.10f);
    private static readonly Color Blush = new(1.00f, 0.55f, 0.62f);
    private static readonly Color Brass = new(0.98f, 0.76f, 0.30f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.32f);
    private static readonly Color Leather = new(0.42f, 0.26f, 0.16f);
    private static readonly Color Navy = new(0.16f, 0.22f, 0.48f);

    // ================================================================= pen

    /// <summary>Draws into a kit in fractions of the model height, from a
    /// pivot given in feet space (metres): every shape below is written as if
    /// the model stood on the origin, and the pen moves it into the space of
    /// the part it belongs to (the hips for the body, a joint for a limb).</summary>
    private readonly struct Pen
    {
        public readonly MeshKit K;
        private readonly float _h;
        private readonly Vector3 _o;

        public Pen(MeshKit k, float h, Vector3 originMetres) { K = k; _h = h; _o = originMetres; }

        public float H => _h;
        public float Paint { set => K.Paint = value; }

        public Vector3 P(Vector3 f) => f * _h - _o;

        public void Ell(Vector3 c, Vector3 r, Color col, Color? top = null, float emit = 0f, int segs = 12, int rings = 8)
            => K.Ellipsoid(new Transform3D(Basis.Identity, P(c)), r * _h, col, emit, segs, rings, top);

        public void Ell(Basis b, Vector3 c, Vector3 r, Color col, Color? top = null, float emit = 0f, int segs = 12, int rings = 8)
            => K.Ellipsoid(new Transform3D(b, P(c)), r * _h, col, emit, segs, rings, top);

        public void Sph(Vector3 c, float r, Color col, float emit = 0f, int segs = 10, int rings = 7)
            => K.Sphere(P(c), r * _h, col, emit, segs, rings);

        public void Tube(Vector3 a, Vector3 b, float ra, float rb, Color col, Color? end = null, int segs = 9, float emit = 0f)
            => K.Tube(P(a), P(b), ra * _h, rb * _h, col, emit, segs, true, end);

        public void Cone(Vector3 bas, Vector3 tip, float r, Color col, Color? tipCol = null, int segs = 7, float emit = 0f)
            => K.Cone(P(bas), P(tip), r * _h, col, emit, segs, tipCol);

        /// <summary>A cylinder standing along <paramref name="axis"/> from <paramref name="at"/>.</summary>
        public void Cyl(Vector3 at, Vector3 axis, float rb, float rt, float len, Color col, Color? top = null, int segs = 12, float emit = 0f)
            => K.Cylinder(new Transform3D(MeshKit.BasisAlong(axis), P(at)), rb * _h, rt * _h, len * _h, col, emit, segs, top);

        public void Box(Basis b, Vector3 c, Vector3 size, Color col, Color? top = null)
            => K.Box(new Transform3D(b, P(c)), size * _h, col, 0f, top);

        /// <summary>A flattened leaf (feather, ear, scarf end) from
        /// <paramref name="a"/> to <paramref name="b"/>, <paramref name="width"/>
        /// wide and <paramref name="thick"/> thick, its flat side facing
        /// <paramref name="normal"/>.</summary>
        public void Leaf(Vector3 a, Vector3 b, float width, float thick, Vector3 normal, Color col, Color? tip = null)
        {
            Vector3 pa = P(a), pb = P(b);
            Vector3 along = pb - pa;
            float len = along.Length();
            if (len < 1e-5f) return;
            Vector3 y = along / len;
            Vector3 z = (normal - y * normal.Dot(y)).Normalized();
            Vector3 x = y.Cross(z).Normalized();
            var basis = new Basis(x, y, z);
            K.Ellipsoid(new Transform3D(basis, pa + along * 0.5f), new Vector3(width * _h * 0.5f, len * 0.5f, thick * _h * 0.5f),
                        col, 0f, 10, 6, tip);
        }
    }

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    /// <summary>Big glossy cartoon eyes on the front of a head: white, a tall
    /// pupil looking slightly in, two highlights. Never repainted.</summary>
    private static void Eyes(Pen p, Vector3 left, Vector3 right, float r, Color? iris = null, float yaw = 0.18f)
    {
        p.Paint = 0f;
        Color pupil = iris ?? Pupil;
        foreach (var (at, side) in new[] { (left, -1f), (right, 1f) })
        {
            Basis b = new(Vector3.Up, side * yaw);
            p.Ell(b, at, V(r, r * 1.15f, r * 0.55f), White, null, 0.12f, 12, 8);
            Vector3 q = at + b * V(-side * r * 0.10f, -r * 0.08f, -r * 0.36f);
            p.Ell(b, q, V(r * 0.66f, r * 0.84f, r * 0.34f), pupil, null, 0f, 12, 8);
            p.Sph(q + V(-r * 0.22f, r * 0.34f, -r * 0.30f), r * 0.27f, White, 1.3f, 8, 5);
            p.Sph(q + V(r * 0.22f, -r * 0.30f, -r * 0.28f), r * 0.12f, White, 1.0f, 6, 4);
        }
        p.Paint = 1f;
    }

    /// <summary>Hips space from feet space: every model below is written
    /// standing on the origin.</summary>
    private static Pen Body(CustomBody b, float hipFraction)
    {
        b.HipsOffset = new Vector3(0f, hipFraction * b.H, 0f);
        b.StrideLength = hipFraction * b.H * 3.3f / Mathf.Max(0.3f, b.Spec.Cadence);
        return new Pen(b.Kit, b.H, b.HipsOffset);
    }

    /// <summary>One limb from three feet-space points (fractions): the joint,
    /// the bend and the end. The two callbacks draw the upper and lower
    /// halves with pens already moved to their pivots.</summary>
    private static void Limb(CustomBody b, bool arm, int side, Vector3 joint, Vector3 bend,
                             Action<Pen> upper, Action<Pen> lower)
    {
        float H = b.H;
        var up = new MeshKit();
        var low = new MeshKit();
        upper(new Pen(up, H, joint * H));
        lower(new Pen(low, H, bend * H));
        b.Limb(arm, side, joint * H - b.HipsOffset, bend * H - b.HipsOffset, up, low);
    }

    private static void Hat(CustomBody b, Vector3 crown, float halfWidth)
    {
        b.HatAnchor = crown * b.H - b.HipsOffset;
        b.HatRadius = halfWidth * b.H;
    }

    // =============================================================== Sunny

    private static void BuildSunny(CustomBody b)
    {
        Color red = new(0.93f, 0.22f, 0.20f), redLight = new(1.00f, 0.42f, 0.32f);
        Color yellow = new(1.00f, 0.80f, 0.18f), blue = new(0.18f, 0.48f, 0.98f), green = new(0.25f, 0.78f, 0.42f);
        Color face = new(1.00f, 0.97f, 0.93f), beak = new(0.98f, 0.92f, 0.80f), beakTip = new(0.22f, 0.20f, 0.24f);
        Color coat = Navy, coatLight = Navy.Lightened(0.18f), cream = new(1.00f, 0.94f, 0.80f), legs = new(0.58f, 0.60f, 0.68f);
        const float hip = 0.25f;
        Pen p = Body(b, hip);
        b.LimbSwing = 0.72f;

        // Coat: a navy body, cream waistcoat front with gold buttons, a belt,
        // and two coat tails hanging behind past the knees.
        p.Paint = 0.3f;
        p.Ell(V(0f, 0.43f, 0f), V(0.17f, 0.17f, 0.14f), coat, coatLight);
        p.Ell(V(0f, 0.31f, 0.01f), V(0.15f, 0.08f, 0.13f), coat);
        for (int side = -1; side <= 1; side += 2)
        {
            p.Leaf(V(side * 0.07f, 0.33f, 0.10f), V(side * 0.12f, 0.10f, 0.17f), 0.12f, 0.035f, V(0f, 0.3f, 1f), coat, coatLight);
        }
        p.Paint = 0.2f;
        p.Ell(V(0f, 0.42f, -0.085f), V(0.10f, 0.14f, 0.07f), cream);
        p.Paint = 0f;
        for (int i = 0; i < 3; i++)
            p.Sph(V(0f, 0.48f - i * 0.055f, -0.155f + i * 0.004f), 0.016f, Gold, 0.5f, 6, 4);
        p.Cyl(V(0f, 0.30f, 0f), Vector3.Up, 0.152f, 0.152f, 0.035f, Leather, null, 14);
        p.Box(Basis.Identity, V(0f, 0.318f, -0.152f), V(0.06f, 0.045f, 0.02f), Gold);
        // Epaulettes: gold pads with a fringe, the captain's silhouette.
        for (int side = -1; side <= 1; side += 2)
        {
            p.Ell(V(side * 0.165f, 0.565f, 0f), V(0.07f, 0.03f, 0.065f), Gold, Gold.Lightened(0.2f));
            for (int i = -2; i <= 2; i++)
                p.Tube(V(side * 0.2f, 0.555f, i * 0.022f), V(side * 0.215f, 0.52f, i * 0.024f), 0.008f, 0.007f, Gold, null, 5);
        }
        // A red collar under the chin.
        p.Paint = 0.6f;
        p.Cyl(V(0f, 0.565f, 0f), Vector3.Up, 0.12f, 0.10f, 0.05f, red, redLight, 12);

        // Tail feathers out from under the coat: long, fanned, red with blue sides.
        p.Paint = 0.8f;
        p.Leaf(V(0f, 0.30f, 0.10f), V(0f, 0.06f, 0.34f), 0.08f, 0.02f, V(0f, 1f, 0.4f), red, redLight);
        p.Leaf(V(-0.03f, 0.30f, 0.10f), V(-0.09f, 0.09f, 0.29f), 0.06f, 0.02f, V(0f, 1f, 0.4f), blue, blue.Lightened(0.2f));
        p.Leaf(V(0.03f, 0.30f, 0.10f), V(0.09f, 0.09f, 0.29f), 0.06f, 0.02f, V(0f, 1f, 0.4f), blue, blue.Lightened(0.2f));

        // Head: round red, white cheek patches round the eyes, a big hooked beak.
        Vector3 hc = V(0f, 0.74f, -0.01f);
        p.Paint = 1f;
        p.Ell(hc, V(0.19f, 0.18f, 0.18f), red, redLight, 0f, 16, 10);
        p.Paint = 0.25f;
        for (int side = -1; side <= 1; side += 2)
            p.Ell(new Basis(Vector3.Up, side * 0.5f), hc + V(side * 0.09f, -0.005f, -0.13f), V(0.075f, 0.085f, 0.05f), face);
        Eyes(p, hc + V(-0.088f, 0.012f, -0.168f), hc + V(0.088f, 0.012f, -0.168f), 0.046f);
        p.Paint = 0f;
        // Upper beak: a rounded wedge curling down to a dark hook; small lower beak.
        p.Ell(hc + V(0f, -0.045f, -0.19f), V(0.055f, 0.06f, 0.06f), beak, beak.Lightened(0.1f));
        p.Tube(hc + V(0f, -0.02f, -0.235f), hc + V(0f, -0.10f, -0.245f), 0.032f, 0.012f, beak, beakTip, 8);
        p.Ell(hc + V(0f, -0.10f, -0.17f), V(0.04f, 0.028f, 0.035f), beakTip);
        p.Paint = 1f;
        Eyes2Blush(p, hc + V(-0.13f, -0.06f, -0.12f), hc + V(0.13f, -0.06f, -0.12f), 0.026f);
        // Crest: three swept feathers in three colours.
        p.Leaf(hc + V(0f, 0.15f, -0.04f), hc + V(0f, 0.31f, 0.06f), 0.06f, 0.025f, V(1f, 0f, 0f), red, yellow);
        p.Leaf(hc + V(-0.03f, 0.14f, 0.00f), hc + V(-0.07f, 0.27f, 0.12f), 0.05f, 0.022f, V(1f, 0f, 0.3f), yellow, green);
        p.Leaf(hc + V(0.03f, 0.14f, 0.00f), hc + V(0.07f, 0.27f, 0.12f), 0.05f, 0.022f, V(-1f, 0f, 0.3f), blue, blue.Lightened(0.3f));
        Hat(b, hc + V(0f, 0.165f, 0f), 0.175f);

        // Legs: thin grey bird legs, three forward toes and one back.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 0.07f;
            Limb(b, false, side, V(x, hip, 0f), V(x, hip * 0.5f, 0f),
                 u => { u.Paint = 0.3f; u.Tube(V(x, hip + 0.01f, 0f), V(x, hip * 0.5f, 0f), 0.045f, 0.03f, coat); },
                 l =>
                 {
                     l.Paint = 0f;
                     l.Tube(V(x, hip * 0.5f, 0f), V(x, 0.03f, 0f), 0.022f, 0.02f, legs, null, 7);
                     for (int t = -1; t <= 1; t++)
                         l.Tube(V(x, 0.02f, 0f), V(x + t * 0.035f, 0.012f, -0.075f), 0.016f, 0.01f, legs, null, 6);
                     l.Tube(V(x, 0.02f, 0f), V(x, 0.012f, 0.05f), 0.014f, 0.009f, legs, null, 6);
                 });
        }

        // Arms: coat sleeves with gold cuffs, wing feathers for hands; the right
        // one holds the brass spyglass out front, so the windup raises it.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 0.20f;
            int s = side;
            Limb(b, true, side, V(x, 0.53f, 0f), V(x + side * 0.015f, 0.43f, 0f),
                 u => { u.Paint = 0.3f; u.Tube(V(x, 0.53f, 0f), V(x + s * 0.015f, 0.43f, 0f), 0.05f, 0.045f, coat); },
                 l =>
                 {
                     Vector3 e = V(x + s * 0.015f, 0.43f, 0f), w = V(x + s * 0.02f, 0.335f, 0f);
                     l.Paint = 0.3f;
                     l.Tube(e, w, 0.045f, 0.042f, coat);
                     l.Paint = 0f;
                     l.Cyl(w + V(0f, -0.005f, 0f), Vector3.Up, 0.05f, 0.05f, 0.028f, Gold, null, 10);
                     // Feathered hand: a fan of three.
                     l.Paint = 1f;
                     l.Leaf(w, w + V(s * 0.01f, -0.10f, -0.01f), 0.05f, 0.025f, V(1f, 0f, 0f), red, yellow);
                     l.Leaf(w, w + V(s * 0.03f, -0.085f, 0.03f), 0.04f, 0.022f, V(1f, 0f, 0f), yellow, blue);
                     l.Leaf(w, w + V(-s * 0.005f, -0.08f, -0.045f), 0.04f, 0.022f, V(1f, 0f, 0f), blue, blue.Lightened(0.3f));
                     if (s > 0) Spyglass(l, w + V(0f, -0.04f, 0f));
                 });
        }
    }

    /// <summary>A brass telescope pointing forward from the hand: three
    /// stepped tubes, leather grip, a glinting lens.</summary>
    private static void Spyglass(Pen l, Vector3 hand)
    {
        l.Paint = 0f;
        Vector3 back = hand + V(0f, 0.005f, 0.07f);
        Vector3 a = hand + V(0f, 0.01f, -0.06f), c = hand + V(0f, 0.018f, -0.15f), d = hand + V(0f, 0.024f, -0.23f);
        l.Tube(back, a, 0.026f, 0.028f, Brass, null, 10);
        l.Tube(a, c, 0.032f, 0.032f, Brass.Darkened(0.08f), null, 10);
        l.Tube(c, d, 0.038f, 0.04f, Brass, null, 10);
        l.Tube(back + V(0f, 0f, -0.01f), a + V(0f, 0f, 0.02f), 0.031f, 0.031f, Leather, null, 10);
        l.Cyl(d, V(0f, 0.08f, -1f), 0.044f, 0.044f, 0.012f, Gold, null, 12);
        l.Sph(d + V(0f, 0.002f, -0.012f), 0.03f, new Color(0.55f, 0.92f, 1f), 1.2f, 8, 5);
        l.Paint = 1f;
    }

    private static void Eyes2Blush(Pen p, Vector3 left, Vector3 right, float r)
    {
        p.Paint = 0f;
        p.Ell(left, V(r, r * 0.6f, r * 0.35f), Blush, null, 0.1f, 8, 5);
        p.Ell(right, V(r, r * 0.6f, r * 0.35f), Blush, null, 0.1f, 8, 5);
        p.Paint = 1f;
    }

    // ============================================================= Biscuit

    private static void BuildBiscuit(CustomBody b)
    {
        Color fur = new(0.86f, 0.62f, 0.36f), furLight = new(0.95f, 0.74f, 0.48f);
        Color cream = new(1.00f, 0.93f, 0.80f), ear = new(0.48f, 0.30f, 0.18f), nose = new(0.10f, 0.08f, 0.09f);
        Color stripe = Navy, shirt = new(0.97f, 0.97f, 0.98f), trousers = new(0.20f, 0.26f, 0.52f);
        Color kerchief = new(0.92f, 0.20f, 0.20f), iron = new(0.36f, 0.40f, 0.48f), rope = new(0.86f, 0.74f, 0.52f);
        const float hip = 0.27f;
        Pen p = Body(b, hip);
        b.LimbSwing = 0.6f;

        // Barrel chest in a striped jersey: white with navy bands.
        p.Paint = 0.25f;
        p.Ell(V(0f, 0.44f, 0f), V(0.22f, 0.19f, 0.17f), shirt);
        for (int i = 0; i < 4; i++)
        {
            float y = 0.34f + i * 0.055f;
            float rr = Mathf.Sqrt(Mathf.Max(0.05f, 1f - Mathf.Pow((y - 0.44f) / 0.19f, 2f)));
            p.Cyl(V(0f, y, 0f), Vector3.Up, 0.222f * rr + 0.004f, 0.222f * rr + 0.004f, 0.022f, stripe, null, 16);
        }
        // Trousers and belt.
        p.Paint = 0.35f;
        p.Ell(V(0f, 0.30f, 0f), V(0.20f, 0.08f, 0.16f), trousers);
        p.Paint = 0f;
        p.Cyl(V(0f, 0.305f, 0f), Vector3.Up, 0.196f, 0.196f, 0.035f, Leather, null, 16);
        p.Box(Basis.Identity, V(0f, 0.322f, -0.195f), V(0.07f, 0.05f, 0.02f), Gold);
        // Sailor collar: a navy square flap on the back with white trim, and a
        // red neckerchief knotted under the chin.
        p.Paint = 0.3f;
        var collar = new Basis(Vector3.Right, 0.32f);
        p.Box(collar, V(0f, 0.55f, 0.15f), V(0.30f, 0.17f, 0.02f), stripe);
        p.Box(collar, V(0f, 0.55f, 0.16f), V(0.26f, 0.13f, 0.02f), shirt);
        p.Box(collar, V(0f, 0.55f, 0.165f), V(0.22f, 0.10f, 0.02f), stripe);
        p.Paint = 0.4f;
        p.Cyl(V(0f, 0.58f, 0f), Vector3.Up, 0.15f, 0.13f, 0.045f, kerchief, kerchief.Lightened(0.15f), 14);
        p.Sph(V(0f, 0.575f, -0.15f), 0.04f, kerchief);
        p.Leaf(V(-0.01f, 0.565f, -0.16f), V(-0.05f, 0.46f, -0.19f), 0.06f, 0.02f, V(0f, 0f, -1f), kerchief);
        p.Leaf(V(0.01f, 0.565f, -0.16f), V(0.05f, 0.47f, -0.185f), 0.06f, 0.02f, V(0f, 0f, -1f), kerchief);

        // The anchor slung across his back on a rope: shank, ring, stock,
        // curved arms and flukes. Iron, never repainted.
        p.Paint = 0f;
        var tilt = new Basis(Vector3.Back, 0.35f);
        Vector3 ac = V(0f, 0.43f, 0.21f);
        Vector3 Ax(float x, float y) => ac + tilt * V(x, y, 0f);
        p.Tube(Ax(0f, 0.20f), Ax(0f, -0.20f), 0.024f, 0.024f, iron, null, 8);
        p.Cyl(Ax(0f, 0.235f), V(0f, 0f, 1f), 0.045f, 0.045f, 0.02f, iron, null, 12);
        p.Tube(Ax(-0.10f, 0.15f), Ax(0.10f, 0.15f), 0.02f, 0.02f, iron, null, 7);
        for (int side = -1; side <= 1; side += 2)
        {
            p.Tube(Ax(0f, -0.20f), Ax(side * 0.10f, -0.17f), 0.024f, 0.022f, iron, null, 7);
            p.Tube(Ax(side * 0.10f, -0.17f), Ax(side * 0.15f, -0.08f), 0.022f, 0.016f, iron, null, 7);
            p.Cone(Ax(side * 0.14f, -0.10f), Ax(side * 0.17f, -0.03f), 0.04f, iron.Lightened(0.1f), null, 4);
        }
        p.Tube(V(-0.17f, 0.56f, -0.02f), V(0.20f, 0.30f, 0.10f), 0.018f, 0.018f, rope, null, 6);
        // A short wagging tail (static) under the anchor.
        p.Paint = 1f;
        p.Tube(V(0f, 0.30f, 0.15f), V(0f, 0.36f, 0.27f), 0.04f, 0.025f, fur, furLight, 8);

        // Head: broad, a big cream muzzle, a black nose, floppy dark ears and a
        // brown patch over one eye.
        Vector3 hc = V(0f, 0.76f, -0.01f);
        p.Ell(hc, V(0.21f, 0.18f, 0.18f), fur, furLight, 0f, 16, 10);
        p.Paint = 0.5f;
        p.Ell(hc + V(0f, -0.065f, -0.135f), V(0.12f, 0.085f, 0.085f), cream);
        p.Ell(hc + V(0f, -0.115f, -0.11f), V(0.07f, 0.04f, 0.06f), cream);
        p.Paint = 1f;
        p.Ell(new Basis(Vector3.Up, -0.3f), hc + V(0.085f, 0.035f, -0.12f), V(0.07f, 0.065f, 0.05f), ear);
        Eyes(p, hc + V(-0.085f, 0.03f, -0.155f), hc + V(0.085f, 0.03f, -0.16f), 0.04f);
        p.Paint = 0f;
        p.Ell(hc + V(0f, -0.03f, -0.225f), V(0.045f, 0.032f, 0.03f), nose, null, 0.1f);
        p.Sph(hc + V(-0.012f, -0.018f, -0.25f), 0.009f, White, 0.6f, 5, 3);
        // A little smile under the nose.
        p.Tube(hc + V(0f, -0.06f, -0.215f), hc + V(-0.03f, -0.085f, -0.205f), 0.007f, 0.007f, Ink, null, 5);
        p.Tube(hc + V(0f, -0.06f, -0.215f), hc + V(0.03f, -0.085f, -0.205f), 0.007f, 0.007f, Ink, null, 5);
        Eyes2Blush(p, hc + V(-0.14f, -0.05f, -0.11f), hc + V(0.14f, -0.05f, -0.11f), 0.028f);
        p.Paint = 1f;
        for (int side = -1; side <= 1; side += 2)
            p.Leaf(hc + V(side * 0.17f, 0.10f, 0.01f), hc + V(side * 0.25f, -0.12f, 0.02f), 0.12f, 0.035f,
                   V(side, 0f, 0f), ear, ear.Darkened(0.1f));
        Hat(b, hc + V(0f, 0.17f, 0f), 0.20f);

        // Legs: navy bell-bottoms over big dark paws.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 0.10f;
            Limb(b, false, side, V(x, hip, 0f), V(x, hip * 0.5f, 0f),
                 u => { u.Paint = 0.35f; u.Tube(V(x, hip + 0.02f, 0f), V(x, hip * 0.5f, 0f), 0.075f, 0.07f, trousers); },
                 l =>
                 {
                     l.Paint = 0.35f;
                     l.Tube(V(x, hip * 0.5f, 0f), V(x, 0.07f, 0f), 0.07f, 0.085f, trousers);
                     l.Paint = 0.6f;
                     l.Ell(V(x, 0.04f, -0.035f), V(0.075f, 0.045f, 0.10f), ear.Darkened(0.2f));
                 });
        }

        // Arms: thick, striped sleeves, big cream paws.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 0.25f;
            int s = side;
            Limb(b, true, side, V(x, 0.54f, 0f), V(x + side * 0.02f, 0.43f, 0f),
                 u =>
                 {
                     u.Paint = 0.25f;
                     u.Tube(V(x, 0.54f, 0f), V(x + s * 0.02f, 0.43f, 0f), 0.072f, 0.065f, shirt);
                     u.Cyl(V(x + s * 0.01f, 0.475f, 0f), Vector3.Up, 0.072f, 0.072f, 0.022f, stripe, null, 12);
                 },
                 l =>
                 {
                     Vector3 e = V(x + s * 0.02f, 0.43f, 0f), w = V(x + s * 0.025f, 0.32f, 0f);
                     l.Paint = 1f;
                     l.Tube(e, w, 0.062f, 0.058f, fur, furLight);
                     l.Paint = 0.5f;
                     l.Sph(w + V(0f, -0.04f, -0.01f), 0.07f, cream);
                 });
        }
    }

    // =============================================================== Flick

    private static void BuildFlick(CustomBody b)
    {
        Color fur = new(1.00f, 0.50f, 0.16f), furLight = new(1.00f, 0.66f, 0.30f);
        Color white = new(1.00f, 0.97f, 0.92f), sock = new(0.26f, 0.16f, 0.14f), innerEar = new(1.00f, 0.80f, 0.74f);
        Color scarf = new(0.14f, 0.74f, 0.78f), scarfBand = new(1.00f, 0.88f, 0.30f);
        Color vest = new(0.40f, 0.30f, 0.62f), shorts = new(0.46f, 0.34f, 0.24f);
        Color lens = new(0.45f, 0.92f, 1.00f), wood = new(0.70f, 0.48f, 0.28f);
        const float hip = 0.27f;
        Pen p = Body(b, hip);
        b.LimbSwing = 0.85f;

        // Slim body: a little purple flight vest over orange fur, white chest.
        p.Paint = 1f;
        p.Ell(V(0f, 0.43f, 0f), V(0.14f, 0.16f, 0.12f), fur, furLight);
        p.Paint = 0.3f;
        p.Ell(V(0f, 0.43f, 0.01f), V(0.145f, 0.13f, 0.125f), vest, vest.Lightened(0.12f));
        p.Paint = 0.5f;
        p.Ell(V(0f, 0.44f, -0.07f), V(0.075f, 0.12f, 0.07f), white);
        p.Paint = 0.3f;
        p.Ell(V(0f, 0.30f, 0f), V(0.13f, 0.07f, 0.11f), shorts);
        p.Paint = 0f;
        p.Cyl(V(0f, 0.31f, 0f), Vector3.Up, 0.128f, 0.128f, 0.03f, Leather, null, 14);
        p.Box(Basis.Identity, V(0.06f, 0.33f, -0.115f), V(0.07f, 0.06f, 0.03f), Leather.Lightened(0.15f));

        // The tail: big, bushy, curling up behind, white tip. Its own silhouette.
        p.Paint = 1f;
        Vector3[] spine = { V(0f, 0.30f, 0.10f), V(0f, 0.30f, 0.22f), V(0f, 0.38f, 0.32f), V(0f, 0.52f, 0.36f), V(0f, 0.64f, 0.33f) };
        float[] radius = { 0.05f, 0.085f, 0.105f, 0.095f, 0.07f };
        for (int i = 0; i < spine.Length; i++)
        {
            Color c = i == spine.Length - 1 ? white : fur;
            Vector3 dir = i + 1 < spine.Length ? spine[i + 1] - spine[i] : spine[i] - spine[i - 1];
            Basis bb = MeshKit.BasisAlong(dir.Normalized());
            float r = radius[i];
            p.Ell(bb, spine[i], V(r, r * 1.35f, r), c, i == spine.Length - 2 ? white : (i == spine.Length - 1 ? white : furLight));
        }
        p.Sph(V(0f, 0.70f, 0.30f), 0.045f, white);

        // Scarf: wrapped round the neck, two long ends streaming back.
        p.Paint = 0.3f;
        p.Cyl(V(0f, 0.55f, 0f), Vector3.Up, 0.125f, 0.115f, 0.06f, scarf, scarf.Lightened(0.15f), 14);
        p.Cyl(V(0f, 0.57f, 0f), Vector3.Up, 0.127f, 0.12f, 0.016f, scarfBand, null, 14);
        p.Leaf(V(0.05f, 0.56f, 0.10f), V(0.13f, 0.50f, 0.36f), 0.075f, 0.02f, V(0f, 1f, 0.2f), scarf, scarfBand);
        p.Leaf(V(-0.03f, 0.56f, 0.11f), V(-0.06f, 0.60f, 0.40f), 0.07f, 0.02f, V(0f, 1f, -0.1f), scarf, scarfBand);

        // Head: orange, a white muzzle running to a pointed snout, black nose,
        // tall ears with dark tips and pale insides.
        Vector3 hc = V(0f, 0.75f, -0.01f);
        p.Paint = 1f;
        p.Ell(hc, V(0.18f, 0.17f, 0.17f), fur, furLight, 0f, 16, 10);
        p.Paint = 0.5f;
        for (int side = -1; side <= 1; side += 2)
            p.Ell(new Basis(Vector3.Up, side * 0.4f), hc + V(side * 0.08f, -0.06f, -0.12f), V(0.08f, 0.07f, 0.06f), white);
        p.Ell(hc + V(0f, -0.06f, -0.17f), V(0.06f, 0.05f, 0.08f), white);
        p.Paint = 0f;
        p.Sph(hc + V(0f, -0.035f, -0.245f), 0.026f, Ink, 0.1f, 8, 5);
        p.Sph(hc + V(-0.008f, -0.028f, -0.265f), 0.007f, White, 0.6f, 5, 3);
        Eyes(p, hc + V(-0.078f, 0.02f, -0.15f), hc + V(0.078f, 0.02f, -0.15f), 0.04f, new Color(0.18f, 0.10f, 0.06f));
        Eyes2Blush(p, hc + V(-0.125f, -0.05f, -0.11f), hc + V(0.125f, -0.05f, -0.11f), 0.024f);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 root = hc + V(side * 0.10f, 0.12f, 0.01f), tip = hc + V(side * 0.16f, 0.34f, 0.03f);
            p.Paint = 1f;
            p.Leaf(root, tip, 0.10f, 0.04f, V(0f, 0f, -1f), fur, sock);
            p.Paint = 0.3f;
            p.Leaf(root + V(0f, 0.01f, -0.018f), tip + V(-side * 0.008f, -0.06f, -0.018f), 0.055f, 0.012f, V(0f, 0f, -1f), innerEar);
        }
        // Flight goggles pushed up on the forehead: a leather strap round the
        // head, two brass rims, glassy lenses.
        p.Paint = 0f;
        p.Cyl(hc + V(0f, 0.065f, 0f), V(0f, 1f, 0.25f), 0.182f, 0.176f, 0.035f, Leather, null, 16);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 g = hc + V(side * 0.062f, 0.10f, -0.145f);
            Vector3 face = V(side * 0.25f, 0.55f, -1f).Normalized();
            p.Cyl(g - face * 0.02f, face, 0.05f, 0.048f, 0.035f, Brass, Brass.Lightened(0.15f), 12);
            p.Cyl(g + face * 0.012f, face, 0.036f, 0.036f, 0.008f, lens, lens.Lightened(0.3f), 12, 0.5f);
        }
        p.Tube(hc + V(-0.02f, 0.11f, -0.17f), hc + V(0.02f, 0.11f, -0.17f), 0.012f, 0.012f, Brass, null, 6);
        Hat(b, hc + V(0f, 0.16f, 0f), 0.17f);

        // Legs: orange thighs, dark socks, white-toed feet.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 0.07f;
            Limb(b, false, side, V(x, hip, 0f), V(x, hip * 0.5f, 0f),
                 u => { u.Paint = 1f; u.Tube(V(x, hip + 0.015f, 0f), V(x, hip * 0.5f, 0f), 0.05f, 0.042f, fur, furLight); },
                 l =>
                 {
                     l.Paint = 0.4f;
                     l.Tube(V(x, hip * 0.5f, 0f), V(x, 0.05f, 0f), 0.042f, 0.036f, sock);
                     l.Ell(V(x, 0.03f, -0.03f), V(0.05f, 0.032f, 0.08f), sock);
                     l.Paint = 0.5f;
                     l.Sph(V(x, 0.03f, -0.095f), 0.03f, white, 0f, 8, 5);
                 });
        }

        // Arms: fur with dark gloves; the left paw holds the kite spool, its
        // string running up out of reach.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 0.17f;
            int s = side;
            Limb(b, true, side, V(x, 0.53f, 0f), V(x + side * 0.015f, 0.43f, 0f),
                 u => { u.Paint = 1f; u.Tube(V(x, 0.53f, 0f), V(x + s * 0.015f, 0.43f, 0f), 0.04f, 0.036f, fur, furLight); },
                 l =>
                 {
                     Vector3 e = V(x + s * 0.015f, 0.43f, 0f), w = V(x + s * 0.02f, 0.34f, 0f);
                     l.Paint = 0.4f;
                     l.Tube(e, w, 0.036f, 0.034f, sock);
                     l.Sph(w + V(0f, -0.03f, 0f), 0.045f, sock);
                     if (s < 0)
                     {
                         // Spool: a wooden reel across the paw, string wound on it.
                         l.Paint = 0f;
                         Vector3 c = w + V(0f, -0.035f, -0.055f);
                         l.Cyl(c - V(0.05f, 0f, 0f), V(1f, 0f, 0f), 0.045f, 0.045f, 0.012f, wood, null, 12);
                         l.Cyl(c + V(0.038f, 0f, 0f), V(1f, 0f, 0f), 0.045f, 0.045f, 0.012f, wood, null, 12);
                         l.Cyl(c - V(0.04f, 0f, 0f), V(1f, 0f, 0f), 0.028f, 0.028f, 0.08f, White, null, 10);
                         l.Tube(c + V(0f, 0.025f, -0.01f), c + V(0.02f, 0.30f, -0.22f), 0.004f, 0.004f, White, null, 4);
                         l.Paint = 1f;
                     }
                 });
        }
        // A small diamond kite strapped on the back, above the tail.
        p.Paint = 0.2f;
        var kb = new Basis(Vector3.Right, -0.25f) * new Basis(Vector3.Back, Mathf.Pi * 0.25f);
        p.Box(kb, V(0f, 0.47f, 0.16f), V(0.17f, 0.17f, 0.012f), new Color(1.00f, 0.32f, 0.36f));
        p.Box(kb, V(0f, 0.47f, 0.168f), V(0.085f, 0.085f, 0.012f), scarfBand);
        p.Paint = 0f;
        p.Tube(V(0f, 0.355f, 0.13f), V(0f, 0.59f, 0.19f), 0.006f, 0.006f, wood, null, 4);
        p.Tube(V(-0.12f, 0.475f, 0.165f), V(0.12f, 0.475f, 0.165f), 0.006f, 0.006f, wood, null, 4);
    }
}
