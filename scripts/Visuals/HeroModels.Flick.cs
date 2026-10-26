using System;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// Flick the Kite Fox, the trio's movement hero. A round chibi fox (the head
/// is about 40% of his height) in a three-colour palette: warm orange fur,
/// cream face, belly, paws and tail tip, and one sky-blue accent (scarf,
/// goggle lenses, kite). The pilot look is a leather aviator cap with ear
/// flaps and brass goggles, fleece-cuffed flying boots and a small diamond
/// kite worn on his back; the white-tipped tail curls up beside him so the
/// fox still reads from the front.
///
/// The first Flick (a slim fox with tall leaf ears, dark socks and gloves, a
/// purple vest, a striped scarf, goggles worn as a headband and a spool on a
/// string) read as a rabbit at game distance, and his small head and thin
/// limbs looked spindly beside Sunny and Biscuit.
/// </summary>
public static partial class HeroModels
{
    private static void BuildFlick(CustomBody b)
    {
        Color fur = new(1.00f, 0.52f, 0.18f), furLight = new(1.00f, 0.66f, 0.32f);
        Color cream = new(1.00f, 0.95f, 0.86f);
        Color earTip = new(0.42f, 0.22f, 0.14f), earInner = new(1.00f, 0.86f, 0.78f);
        Color sky = new(0.30f, 0.72f, 1.00f), skyLight = new(0.55f, 0.85f, 1.00f);
        Color cap = new(0.52f, 0.32f, 0.20f), capLight = new(0.64f, 0.42f, 0.27f);
        Color lens = new(0.50f, 0.88f, 1.00f), nose = new(0.14f, 0.09f, 0.10f);
        const float hip = 0.22f;
        Pen p = Body(b, hip);
        b.LimbSwing = 0.85f;

        // ---- body: a round little pear, cream belly.
        p.Paint = 1f;
        p.Ell(V(0f, 0.375f, 0f), V(0.165f, 0.16f, 0.145f), fur, furLight, 0f, 14, 9);
        p.Paint = 0.6f;
        p.Ell(V(0f, 0.35f, -0.075f), V(0.11f, 0.115f, 0.085f), cream, null, 0f, 16, 10);

        // ---- tail: big and fluffy, curling up past his right side so the
        // silhouette says "fox" from the front too; a cream tip.
        p.Paint = 1f;
        Vector3[] spine = { V(0f, 0.25f, 0.11f), V(0.07f, 0.25f, 0.22f), V(0.15f, 0.31f, 0.28f), V(0.21f, 0.42f, 0.28f), V(0.235f, 0.54f, 0.23f) };
        float[] radius = { 0.06f, 0.095f, 0.115f, 0.11f, 0.085f };
        for (int i = 0; i < spine.Length; i++)
        {
            Vector3 dir = i + 1 < spine.Length ? spine[i + 1] - spine[i] : spine[i] - spine[i - 1];
            Basis bb = MeshKit.BasisAlong(dir.Normalized());
            float r = radius[i];
            bool tip = i == spine.Length - 1;
            p.Paint = tip ? 0.5f : 1f;
            p.Ell(bb, spine[i], V(r, r * 1.3f, r), tip ? cream : fur, tip ? cream : (i == spine.Length - 2 ? cream : furLight), 0f, 12, 8);
        }
        p.Paint = 0.5f;
        p.Ell(V(0.245f, 0.61f, 0.20f), V(0.06f, 0.07f, 0.06f), cream, null, 0f, 10, 7);

        // ---- the kite on his back: a sky-blue diamond with cream panels and
        // spars, tipped a little so its top corner peeks over his shoulder.
        var kb = new Basis(Vector3.Back, -0.30f) * new Basis(Vector3.Right, -0.18f) * new Basis(Vector3.Back, Mathf.Pi * 0.25f);
        Vector3 kc = V(-0.04f, 0.45f, 0.165f);
        p.Paint = 0.25f;
        p.Box(kb, kc, V(0.20f, 0.20f, 0.016f), sky, skyLight);
        p.Box(kb, kc + kb * V(0.045f, 0.045f, 0.006f), V(0.09f, 0.09f, 0.012f), cream);
        p.Box(kb, kc + kb * V(-0.045f, -0.045f, 0.006f), V(0.09f, 0.09f, 0.012f), cream);
        p.Paint = 0f;
        Vector3 d1 = kb * V(0.10f, 0.10f, 0f), d2 = kb * V(-0.10f, 0.10f, 0f), off = kb * V(0f, 0f, 0.016f);
        p.Tube(kc - d1 + off, kc + d1 + off, 0.006f, 0.006f, cap, null, 5);
        p.Tube(kc - d2 + off, kc + d2 + off, 0.006f, 0.006f, cap, null, 5);

        // ---- scarf: a puffy sky-blue roll round the neck, a knot on the
        // left and one short end fluttering back.
        p.Paint = 0.35f;
        p.Ell(V(0f, 0.53f, 0f), V(0.145f, 0.042f, 0.135f), sky, skyLight, 0f, 16, 7);
        p.Sph(V(-0.075f, 0.515f, -0.115f), 0.036f, sky, 0f, 10, 6);
        p.Leaf(V(-0.09f, 0.52f, -0.10f), V(-0.17f, 0.43f, -0.07f), 0.06f, 0.022f, V(-0.4f, 0f, -1f), sky, skyLight);
        p.Leaf(V(-0.06f, 0.535f, 0.11f), V(-0.14f, 0.52f, 0.29f), 0.065f, 0.02f, V(0f, 1f, 0.15f), sky, skyLight);

        // ---- head: big and round, soft cream cheeks, a short muzzle and a
        // button nose.
        Vector3 hc = V(0f, 0.74f, -0.01f);
        p.Paint = 1f;
        p.Ell(hc, V(0.225f, 0.20f, 0.195f), fur, furLight, 0f, 18, 12);
        p.Paint = 0.5f;
        p.Ell(hc + V(0f, -0.10f, -0.085f), V(0.165f, 0.085f, 0.12f), cream, null, 0f, 16, 9);
        p.Tube(hc + V(0f, -0.07f, -0.14f), hc + V(0f, -0.058f, -0.222f), 0.068f, 0.036f, cream, null, 14);
        p.Paint = 0f;
        p.Ell(hc + V(0f, -0.045f, -0.25f), V(0.032f, 0.024f, 0.022f), nose, null, 0.1f, 10, 6);
        p.Sph(hc + V(-0.01f, -0.035f, -0.268f), 0.008f, White, 0.6f, 5, 3);
        Eyes(p, hc + V(-0.088f, 0.005f, -0.165f), hc + V(0.088f, 0.005f, -0.165f), 0.05f, new Color(0.24f, 0.13f, 0.07f));
        Eyes2Blush(p, hc + V(-0.14f, -0.065f, -0.125f), hc + V(0.14f, -0.065f, -0.125f), 0.026f);

        // ---- ears: proper fox triangles, short and wide, dark tips, cream
        // insides, poking out of the cap.
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 root = hc + V(side * 0.12f, 0.13f, 0.01f), tip = hc + V(side * 0.20f, 0.30f, 0.025f);
            p.Paint = 1f;
            FlatCone(p, root, tip, 0.078f, 0.45f, V(0f, 0f, -1f), fur, earTip);
            p.Paint = 0.4f;
            Vector3 inset = V(0f, 0.0f, -0.022f);
            FlatCone(p, root + inset + V(0f, 0.015f, 0f), Lerp(root, tip, 0.82f) + inset, 0.05f, 0.30f, V(0f, 0f, -1f), earInner, earInner);
        }

        // ---- the aviator cap: a leather dome over the crown, tipped back so
        // the forehead stays open, a seam, round ear flaps with fleece edges.
        var tipBack = new Basis(Vector3.Right, 0.22f);
        p.Paint = 0.3f;
        p.K.Dome(new Transform3D(tipBack, p.P(hc + V(0f, 0.004f, 0.006f))), V(0.236f, 0.212f, 0.207f) * p.H, 0.12f, cap, 0f, 20, 7, capLight);
        p.Tube(hc + tipBack * V(0f, 0.214f, -0.02f), hc + tipBack * V(0f, 0.13f, -0.17f), 0.008f, 0.008f, capLight, null, 5);
        p.Tube(hc + tipBack * V(0f, 0.214f, -0.02f), hc + tipBack * V(0f, 0.12f, 0.18f), 0.008f, 0.008f, capLight, null, 5);
        for (int side = -1; side <= 1; side += 2)
        {
            var flap = new Basis(Vector3.Back, side * 0.10f);
            p.Paint = 0.5f;
            p.Ell(flap, hc + V(side * 0.214f, -0.068f, 0.025f), V(0.026f, 0.09f, 0.075f), cream, null, 0f, 12, 8);
            p.Paint = 0.3f;
            p.Ell(flap, hc + V(side * 0.222f, -0.05f, 0.025f), V(0.026f, 0.09f, 0.075f), cap, capLight, 0f, 12, 8);
        }
        // Goggles resting on the cap's brow: brass rims, sky-blue lenses, a
        // strap round the cap.
        p.Paint = 0f;
        p.Cyl(hc + V(0f, 0.095f, 0f), V(0f, 1f, -0.42f), 0.218f, 0.212f, 0.028f, earTip, null, 20);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 g = hc + V(side * 0.072f, 0.135f, -0.165f);
            Vector3 face = V(side * 0.30f, 0.55f, -1f).Normalized();
            p.Cyl(g - face * 0.022f, face, 0.054f, 0.050f, 0.036f, Brass, Brass.Lightened(0.2f), 14);
            p.Cyl(g + face * 0.012f, face, 0.040f, 0.040f, 0.008f, lens, lens.Lightened(0.3f), 14, 0.25f);
            p.Sph(g + face * 0.02f + V(-0.012f, 0.014f, 0f), 0.011f, White, 1.2f, 6, 4);
        }
        p.Tube(hc + V(-0.022f, 0.14f, -0.19f), hc + V(0.022f, 0.14f, -0.19f), 0.013f, 0.013f, Brass, null, 6);
        p.Paint = 1f;
        Hat(b, hc + V(0f, 0.205f, 0.01f), 0.205f);

        // ---- legs: short chubby fur legs in leather flying boots with
        // fleece cuffs.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 0.08f;
            Limb(b, false, side, V(x, hip, 0f), V(x, hip * 0.5f, 0f),
                 u => { u.Paint = 1f; u.Tube(V(x, hip + 0.02f, 0f), V(x, hip * 0.5f, 0f), 0.068f, 0.058f, fur, furLight, 10); },
                 l =>
                 {
                     l.Paint = 1f;
                     l.Tube(V(x, hip * 0.5f, 0f), V(x, 0.075f, 0f), 0.058f, 0.055f, fur, null, 10);
                     l.Paint = 0.3f;
                     l.Ell(V(x, 0.045f, -0.03f), V(0.066f, 0.048f, 0.092f), cap, capLight, 0f, 12, 8);
                     l.Paint = 0.5f;
                     l.Ell(V(x, 0.085f, -0.005f), V(0.07f, 0.024f, 0.07f), cream, null, 0f, 12, 6);
                 });
        }

        // ---- arms: soft fur arms, cream mitten paws. The left paw holds a
        // little kite spool wound with sky-blue string.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 0.175f;
            int s = side;
            Limb(b, true, side, V(x, 0.49f, 0f), V(x + side * 0.02f, 0.405f, 0f),
                 u => { u.Paint = 1f; u.Tube(V(x, 0.49f, 0f), V(x + s * 0.02f, 0.405f, 0f), 0.05f, 0.045f, fur, furLight, 10); },
                 l =>
                 {
                     Vector3 e = V(x + s * 0.02f, 0.405f, 0f), w = V(x + s * 0.028f, 0.32f, -0.005f);
                     l.Paint = 1f;
                     l.Tube(e, w, 0.045f, 0.042f, fur, null, 10);
                     l.Paint = 0.5f;
                     l.Sph(w + V(0f, -0.03f, -0.005f), 0.056f, cream, 0f, 12, 8);
                     if (s < 0)
                     {
                         l.Paint = 0f;
                         Vector3 c = w + V(0.0f, -0.035f, -0.07f);
                         l.Cyl(c - V(0.045f, 0f, 0f), V(1f, 0f, 0f), 0.034f, 0.034f, 0.012f, capLight, null, 12);
                         l.Cyl(c + V(0.033f, 0f, 0f), V(1f, 0f, 0f), 0.034f, 0.034f, 0.012f, capLight, null, 12);
                         l.Cyl(c - V(0.034f, 0f, 0f), V(1f, 0f, 0f), 0.024f, 0.024f, 0.068f, sky, null, 12);
                         l.Paint = 1f;
                     }
                 });
        }
    }

    private static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

    /// <summary>A cone from <paramref name="bas"/> to <paramref name="tip"/>
    /// flattened to <paramref name="thin"/> of its width across
    /// <paramref name="normal"/>: a triangular ear with soft edges.</summary>
    private static void FlatCone(Pen p, Vector3 bas, Vector3 tip, float r, float thin, Vector3 normal, Color col, Color tipCol)
    {
        Vector3 pa = p.P(bas), pb = p.P(tip);
        Vector3 along = pb - pa;
        float len = along.Length();
        if (len < 1e-5f) return;
        Vector3 y = along / len;
        Vector3 z = (normal - y * normal.Dot(y)).Normalized();
        Vector3 x = y.Cross(z).Normalized();
        var t = new MeshKit { Paint = p.K.Paint };
        t.Tube(Vector3.Zero, Vector3.Up * len, r * p.H, r * p.H * 0.12f, col, 0f, 12, true, tipCol);
        p.K.Append(t, new Transform3D(new Basis(x, y, z * thin), pa));
    }
}
