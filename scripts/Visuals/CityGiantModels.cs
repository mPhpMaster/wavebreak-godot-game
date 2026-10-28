using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

/// <summary>Original articulated wardens: sculpted anatomy beneath layered ceramic armour.</summary>
public static class CityGiantModels
{
    private static readonly Dictionary<string, ModelSpec> Cache = new();
    public static bool IsGiant(EnemyDef? def) => def?.Id.StartsWith("city_") == true;
    public static ModelSpec For(EnemyDef def)
    {
        if (Cache.TryGetValue(def.Id, out ModelSpec? spec)) return spec;
        spec = new ModelSpec { Id = def.Id, Height = def.Height, Body = BodyType.Biped,
            Jointed = true, Face = false, Cadence = 0.24f, LodDistance = 130f, Custom = Build };
        Cache[def.Id] = spec; return spec;
    }

    private static CityGiantPalette Palette(string id) => id switch
    {
        "city_gatewarden" => new(new(0.54f, .31f, .24f), new(.84f, .67f, .43f), new(.17f, .21f, .24f), new(.33f, .42f, .46f), new(1f, .54f, .17f)),
        "city_archon" => new(new(.34f, .30f, .43f), new(.62f, .65f, .76f), new(.11f, .17f, .29f), new(.94f, .68f, .31f), new(1f, .66f, .19f)),
        _ => new(new(.40f, .52f, .56f), new(.80f, .83f, .73f), new(.13f, .24f, .29f), new(.60f, .43f, .25f), new(1f, .60f, .16f)),
    };

    private static void Build(CustomBody b)
    {
        b.HipsOffset = Vector3.Up * (b.H * .46f); b.LimbSwing = .42f; b.StrideLength = b.H * .4f;
        CityGiantPalette p = Palette(b.Spec.Id);
        bool gate = b.Spec.Id == "city_gatewarden", archon = b.Spec.Id == "city_archon";
        void Ell(Vector3 at, Vector3 radius, Color c, float glow = 0f) => b.Kit.Ellipsoid(
            new Transform3D(Basis.Identity, at * b.H - b.HipsOffset), radius * b.H, c, glow, 10, 7);
        void Tube(Vector3 a, Vector3 z, float ra, float rz, Color c) => b.Kit.Tube(
            a * b.H - b.HipsOffset, z * b.H - b.HipsOffset, ra * b.H, rz * b.H, c, segs: 8);
        // Tapered ribcage, individual pectorals and layered abdomen; no box torso.
        Ell(new(0, .61f, 0), new(.145f, .18f, .085f), p.Skin);
        Ell(new(0, .47f, 0), new(.125f, .075f, .09f), p.Cloth);
        for (int side = -1; side <= 1; side += 2)
        {
            Ell(new(side * .083f, .725f, -.018f), new(.092f, .105f, .086f), p.Skin.Lightened(.08f));
            Ell(new(side * .085f, .735f, -.052f), new(.081f, .073f, .055f), p.Plate);
            for (int rib = 0; rib < 3; rib++)
                Tube(new(side * .06f, .665f - rib * .033f, -.069f), new(side * .135f, .65f - rib * .032f, -.025f), .013f, .008f, p.Trim);
            Ell(new(side * .067f, .485f, -.05f), new(.065f, .085f, .05f), p.Plate.Darkened(.10f));
            // Rounded shoulder pauldrons and projecting overlapping plates.
            Ell(new(side * .183f, .775f, 0), new(gate ? .087f : .070f, .077f, .082f), p.Plate);
            if (gate || archon)
                for (int plate = 0; plate < 3; plate++)
                    Ell(new(side * (.185f + plate * .009f), .78f - plate * .027f, .015f), new(.078f, .026f, .092f), plate % 2 == 0 ? p.Trim : p.Plate);
            Arm(b, p, side, gate);
            Leg(b, p, side, gate);
        }
        for (int row = 0; row < 3; row++)
            Ell(new(0, .61f - row * .045f, -.071f), new(.058f - row * .006f, .026f, .027f), p.Plate.Darkened(row * .045f));
        // Neck, recognizable face, cheek planes, heavy brow, jaw and inset eyes.
        Tube(new(0, .78f, 0), new(0, .855f, 0), .045f, .042f, p.Skin);
        Ell(new(0, .9f, 0), new(.075f, .098f, .069f), p.Skin.Lightened(.08f));
        Ell(new(0, .855f, -.026f), new(.062f, .037f, .054f), p.Plate.Darkened(.14f));
        Ell(new(0, .938f, .005f), new(.078f, .066f, .067f), p.Plate);
        Ell(new(0, .9f, -.067f), new(.018f, .039f, .023f), p.Skin);
        for (int side = -1; side <= 1; side += 2)
        {
            Ell(new(side * .039f, .91f, -.062f), new(.025f, .010f, .012f), p.Cloth);
            Ell(new(side * .039f, .91f, -.071f), new(.014f, .006f, .005f), p.Glow, 1.3f);
            Tube(new(side * .014f, .927f, -.066f), new(side * .067f, .925f, -.048f), .014f, .019f, p.Trim);
            Ell(new(side * .06f, .877f, -.037f), new(.023f, .032f, .035f), p.Plate);
            Ell(new(side * .073f, .904f, .004f), new(.018f, .03f, .023f), p.Trim);
        }
        Tube(new(-.03f, .858f, -.066f), new(.03f, .858f, -.066f), .006f, .006f, p.Cloth);
        foreach (int side in new[] { -1, 1 })
        {
            // Raised frame and recessed luminous heart: unchanged strike centre/depth.
            Ell(new(0, .65f, side * .073f), new(.048f, .052f, .025f), p.Cloth);
            Ell(new(0, .65f, side * .086f), new(.037f, .039f, .021f), p.Trim);
            Ell(new(0, .65f, side * .102f), new(.026f, .029f, .016f), p.Glow, 2f);
        }
        for (int i = 0; i < 5; i++)
            Ell(new(0, .78f - i * .045f, .08f), new(.025f, .020f, .019f), p.Trim);
        if (archon)
        {
            for (int i = -2; i <= 2; i++)
                b.Kit.Cone(new Vector3(i * .027f, .965f, .01f) * b.H - b.HipsOffset,
                    new Vector3(i * .038f, 1.07f - Mathf.Abs(i) * .025f, .01f) * b.H - b.HipsOffset,
                    .013f * b.H, p.Trim, segs: 6);
            for (int side = -1; side <= 1; side += 2)
                b.Kit.Ellipsoid(new Transform3D(new Basis(Vector3.Forward, side * .18f), new Vector3(side * .10f, .64f, .105f) * b.H - b.HipsOffset),
                    new Vector3(.085f, .20f, .018f) * b.H, p.Cloth, segs: 8, rings: 5);
        }
    }

    private static void Arm(CustomBody b, CityGiantPalette p, int side, bool heavy)
    {
        Vector3 joint = new(side * .18f, .75f, 0), mid = new(side * .205f, .565f, .005f);
        var upper = new MeshKit(); var lower = new MeshKit();
        void Ell(MeshKit k, Vector3 at, Vector3 r, Color c) => k.Ellipsoid(new Transform3D(Basis.Identity, at * b.H), r * b.H, c, segs: 9, rings: 6);
        Ell(upper, new(side * .009f, -.085f, 0), new(.046f, .105f, .048f), p.Skin);
        Ell(upper, new(side * .005f, -.155f, -.005f), new(.044f, .037f, .045f), p.Trim);
        Ell(lower, new(0, -.065f, 0), new(heavy ? .057f : .043f, .085f, .049f), p.Plate);
        Ell(lower, new(0, -.15f, -.008f), new(.039f, .051f, .031f), p.Skin);
        Ell(lower, new(0, -.19f, -.014f), new(.042f, .041f, .032f), p.Skin.Lightened(.06f));
        for (int finger = 0; finger < 4; finger++)
        {
            float x = (finger - 1.5f) * .017f;
            lower.Tube(new Vector3(x, -.20f, -.02f) * b.H, new Vector3(x, -.254f + Mathf.Abs(finger - 1.5f) * .008f, -.031f) * b.H,
                .009f * b.H, .006f * b.H, p.Skin, segs: 6);
        }
        lower.Tube(new Vector3(-side * .03f, -.18f, -.015f) * b.H, new Vector3(-side * .06f, -.21f, -.04f) * b.H,
            .012f * b.H, .008f * b.H, p.Skin, segs: 6);
        b.Limb(true, side, joint * b.H - b.HipsOffset, mid * b.H - b.HipsOffset, upper, lower);
    }

    private static void Leg(CustomBody b, CityGiantPalette p, int side, bool heavy)
    {
        Vector3 joint = new(side * .079f, .46f, 0), mid = new(side * .086f, .245f, 0);
        var upper = new MeshKit(); var lower = new MeshKit();
        upper.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0, -.105f, 0) * b.H), new Vector3(.057f, .127f, .064f) * b.H, p.Skin, segs: 10, rings: 7);
        upper.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0, -.18f, -.032f) * b.H), new Vector3(.052f, .052f, .04f) * b.H, p.Plate, segs: 8, rings: 5);
        lower.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0, -.09f, .012f) * b.H), new Vector3(heavy ? .058f : .046f, .108f, .052f) * b.H, p.Plate, segs: 9, rings: 6);
        lower.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0, -.195f, -.025f) * b.H), new Vector3(.052f, .049f, .092f) * b.H, p.Cloth, segs: 9, rings: 6);
        for (int toe = 0; toe < 3; toe++)
            lower.Ellipsoid(new Transform3D(Basis.Identity, new Vector3((toe - 1) * .03f, -.211f, -.09f) * b.H), new Vector3(.022f, .025f, .037f) * b.H, p.Trim, segs: 6, rings: 4);
        b.Limb(false, side, joint * b.H - b.HipsOffset, mid * b.H - b.HipsOffset, upper, lower);
    }
}
