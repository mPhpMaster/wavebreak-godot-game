using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// The cute hats (<c>Meta/Hats.Cute.cs</c>): bunny ears, a flower crown, cat
/// ears, a tiny tilted crown, a propeller cap and a seashell bow. Same units
/// as the rest of <see cref="HatModels"/>: head half-width 1, origin on the
/// crown, +Y up, front -Z. Soft pastel colours and round shapes throughout.
/// </summary>
public static partial class HatModels
{
    private static readonly Color Blush = new(1.00f, 0.66f, 0.74f);
    private static readonly Color Cream = new(1.00f, 0.97f, 0.92f);
    private static readonly Color Butter = new(1.00f, 0.88f, 0.45f);
    private static readonly Color Lilac = new(0.80f, 0.70f, 1.00f);
    private static readonly Color Mint = new(0.62f, 0.92f, 0.76f);
    private static readonly Color Sky = new(0.58f, 0.80f, 1.00f);

    private static bool BuildCute(string id, MeshKit k)
    {
        switch (id)
        {
            case "bunny_ears":
            {
                // One ear straight up, one flopped over: the flop is the cute part.
                for (int side = -1; side <= 1; side += 2)
                {
                    var ear = new MeshKit();
                    ear.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, 0.62f, 0f)), new Vector3(0.22f, 0.66f, 0.12f), Cream, 0f, 12, 8);
                    ear.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, 0.60f, -0.07f)), new Vector3(0.12f, 0.50f, 0.07f), Blush, 0f, 10, 6);
                    ear.Sphere(new Vector3(0f, 0.02f, 0f), 0.2f, Cream, 0f, 10, 6);
                    // Left ear leans out a little; the right one leans out and flops forward.
                    Basis basis = side < 0
                        ? new Basis(Vector3.Back, 0.18f)
                        : new Basis(Vector3.Back, -0.30f) * new Basis(Vector3.Right, -0.60f);
                    k.Append(ear, new Transform3D(basis, new Vector3(side * 0.40f, -0.08f, 0.05f)));
                }
                return true;
            }

            case "flower_crown":
            {
                Color[] petals = { Blush, Butter, Lilac, Sky, new(1.00f, 0.78f, 0.62f) };
                const int flowers = 9;
                for (int i = 0; i < flowers; i++)
                {
                    float a = i / (float)flowers * Mathf.Tau;
                    Vector3 n = new(Mathf.Sin(a), 0f, -Mathf.Cos(a));     // outward, i = 0 at the front
                    Vector3 t = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 c = n * 0.92f + Vector3.Up * 0.02f;
                    Color pc = petals[i % petals.Length];
                    for (int p = 0; p < 5; p++)
                    {
                        float b = p / 5f * Mathf.Tau;
                        Vector3 off = (t * Mathf.Cos(b) + Vector3.Up * Mathf.Sin(b)) * 0.13f;
                        k.Sphere(c + off + n * 0.02f, 0.095f, pc, 0.05f, 8, 5);
                    }
                    k.Sphere(c + n * 0.08f, 0.07f, Butter.Lightened(0.2f), 0.2f, 8, 5);
                    // a leaf between this flower and the next
                    float la = (i + 0.5f) / flowers * Mathf.Tau;
                    Vector3 ln = new(Mathf.Sin(la), 0f, -Mathf.Cos(la));
                    k.Ellipsoid(new Transform3D(new Basis(Vector3.Up, -la), ln * 0.90f + Vector3.Up * -0.02f),
                                new Vector3(0.14f, 0.05f, 0.08f), Mint, 0f, 8, 4);
                }
                return true;
            }

            case "cat_ears":
            {
                Color fur = new(1.00f, 0.74f, 0.48f);
                for (int side = -1; side <= 1; side += 2)
                {
                    var ear = new MeshKit();
                    ear.Cone(Vector3.Zero, new Vector3(0f, 0.62f, 0f), 0.32f, fur, 0f, 10);
                    ear.Cone(new Vector3(0f, 0.04f, -0.10f), new Vector3(0f, 0.50f, -0.10f), 0.20f, Blush, 0f, 10);
                    // Flatten front-to-back so it reads as an ear, tip it outward a touch.
                    var basis = new Basis(Vector3.Back, -side * 0.28f) * Basis.FromScale(new Vector3(1f, 1f, 0.45f));
                    k.Append(ear, new Transform3D(basis, new Vector3(side * 0.55f, -0.10f, 0.05f)));
                }
                return true;
            }

            case "tiny_crown":
            {
                var crown = new MeshKit();
                crown.Cylinder(new Transform3D(Basis.Identity, Vector3.Zero), 0.36f, 0.40f, 0.20f, Gold, 0.3f, 16);
                for (int i = 0; i < 5; i++)
                {
                    float a = i / 5f * Mathf.Tau;
                    Vector3 b = new(Mathf.Sin(a) * 0.36f, 0.18f, -Mathf.Cos(a) * 0.36f);
                    crown.Cone(b, b + new Vector3(Mathf.Sin(a) * 0.04f, 0.24f, -Mathf.Cos(a) * 0.04f), 0.10f, Gold, 0.3f, 6);
                    crown.Sphere(b + new Vector3(Mathf.Sin(a) * 0.04f, 0.27f, -Mathf.Cos(a) * 0.04f), 0.05f, Cream, 0.5f, 6, 4);
                }
                crown.Sphere(new Vector3(0f, 0.10f, -0.39f), 0.075f, Blush, 0.8f, 8, 6);
                crown.Sphere(new Vector3(0.27f, 0.10f, -0.27f), 0.055f, Sky, 0.8f, 8, 6);
                crown.Sphere(new Vector3(-0.27f, 0.10f, -0.27f), 0.055f, Mint, 0.8f, 8, 6);
                // Worn at a jaunty angle, a little off to one side.
                k.Append(crown, new Transform3D(new Basis(Vector3.Back, -0.28f).Scaled(Vector3.One * 1.25f), new Vector3(0.22f, -0.04f, 0f)));
                return true;
            }

            case "propeller_cap":
            {
                Color red = new(1.00f, 0.52f, 0.52f);
                // Beanie with a sunny top, a band round the rim and a brim.
                k.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, -0.16f, 0f)), new Vector3(0.90f, 0.58f, 0.90f), red, 0f, 16, 8, red.Lightened(0.25f));
                k.Cylinder(new Transform3D(Basis.Identity, new Vector3(0f, -0.22f, 0f)), 0.92f, 0.90f, 0.12f, Sky, 0f, 16);
                k.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, -0.18f, -0.78f)), new Vector3(0.55f, 0.06f, 0.38f), Butter, 0f, 12, 4);
                k.Tube(new Vector3(0f, 0.40f, 0f), new Vector3(0f, 0.58f, 0f), 0.04f, 0.04f, Cream, 0f, 6);
                k.Sphere(new Vector3(0f, 0.60f, 0f), 0.07f, Butter, 0.2f, 8, 5);
                k.Ellipsoid(new Transform3D(new Basis(Vector3.Up, 0.5f), new Vector3(0f, 0.62f, 0f)), new Vector3(0.62f, 0.035f, 0.13f), Sky, 0.1f, 10, 4);
                k.Ellipsoid(new Transform3D(new Basis(Vector3.Up, 0.5f + Mathf.Pi / 2f), new Vector3(0f, 0.62f, 0f)), new Vector3(0.62f, 0.035f, 0.13f), Blush, 0.1f, 10, 4);
                return true;
            }

            case "shell_bow":
            {
                Color shell = new(1.00f, 0.76f, 0.70f);
                var bow = new MeshKit();
                for (int side = -1; side <= 1; side += 2)
                {
                    // A scallop loop: a flattened fan with ribs.
                    Vector3 c = new(side * 0.30f, 0.05f, 0f);
                    bow.Ellipsoid(new Transform3D(new Basis(Vector3.Back, side * 0.25f), c), new Vector3(0.30f, 0.22f, 0.12f), shell, 0f, 12, 7);
                    for (int r = 0; r < 5; r++)
                    {
                        float a = Mathf.Lerp(-0.9f, 0.9f, r / 4f);
                        Vector3 tip = c + new Vector3(side * Mathf.Cos(a) * 0.26f, Mathf.Sin(a) * 0.19f, -0.10f);
                        bow.Tube(new Vector3(side * 0.06f, 0.05f, -0.09f), tip, 0.025f, 0.02f, shell.Darkened(0.12f), 0f, 5);
                    }
                    // ribbon tails
                    bow.Tube(new Vector3(side * 0.05f, -0.02f, 0f), new Vector3(side * 0.20f, -0.32f, 0.02f), 0.06f, 0.04f, Blush, 0f, 6);
                }
                bow.Sphere(new Vector3(0f, 0.05f, -0.04f), 0.12f, Cream, 0.5f, 10, 7);
                // Pinned on the side of the head, the way a hair bow sits.
                k.Append(bow, new Transform3D(new Basis(Vector3.Back, -0.30f).Scaled(Vector3.One * 1.45f), new Vector3(0.42f, 0.22f, -0.30f)));
                return true;
            }
        }
        return false;
    }
}
