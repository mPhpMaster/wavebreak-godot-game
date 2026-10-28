using Godot;

namespace Wavebreak.Visuals;

/// <summary>Original jointed bodies. The normal rig supplies land and swim poses.</summary>
public static class ExpeditionModels
{
    public static ModelSpec? Resolve(string id) => id switch
    {
        "expedition-nori" => Make(id, BuildFish),
        "expedition-vesper" => Make(id, BuildScout),
        _ => null,
    };

    private static ModelSpec Make(string id, System.Action<CustomBody> build) => new()
    {
        Id = id, Height = 1.8f, Body = BodyType.Biped, Jointed = true,
        Face = false, LodDistance = 0f, Custom = build,
    };

    private static void Box(CustomBody b, Vector3 center, Vector3 size, Color color)
        => b.Kit.Box(new Transform3D(Basis.Identity, center * b.H - b.HipsOffset), size * b.H, color);

    private static void Ell(CustomBody b, Vector3 center, Vector3 size, Color color, float glow = 0f)
        => b.Kit.Ellipsoid(new Transform3D(Basis.Identity, center * b.H - b.HipsOffset), size * b.H, color, glow);

    private static void Skeleton(CustomBody b, Color coat, Color hands, Color boots, bool fish)
    {
        b.HipsOffset = Vector3.Up * b.H * 0.46f;
        b.HatAnchor = Vector3.Up * b.H * 0.56f;
        b.HatRadius = b.H * 0.19f;
        foreach (int side in new[] { -1, 1 })
        {
            Limb(b, true, side, new Vector3(side * 0.23f, 0.71f, 0f),
                new Vector3(side * 0.27f, 0.54f, -0.015f), fish ? .26f : .17f, coat, hands, fish);
            Limb(b, false, side, new Vector3(side * 0.11f, 0.46f, 0f),
                new Vector3(side * 0.11f, 0.25f, 0f), 0.22f, coat.Darkened(0.25f), boots, false);
        }
    }

    private static void Limb(CustomBody b, bool arm, int side, Vector3 joint, Vector3 bend,
        float lowerLength, Color upperColor, Color lowerColor, bool fin)
    {
        var upper = new MeshKit();
        var lower = new MeshKit();
        Vector3 end = bend - Vector3.Up * lowerLength;
        float width = arm ? 0.065f : 0.075f;
        upper.Tube(Vector3.Zero, (bend - joint) * b.H, width * b.H, width * b.H, upperColor);
        lower.Tube(Vector3.Zero, (end - bend) * b.H, width * b.H, width * b.H, lowerColor);
        lower.Ellipsoid(new Transform3D(Basis.Identity, (end - bend) * b.H),
            new Vector3(fin ? .07f : width, fin ? .07f : .055f, arm ? .07f : .13f) * b.H, lowerColor);
        if (arm && fin)
            lower.Ellipsoid(new Transform3D(Basis.Identity, ((end - bend) + new Vector3(side * .05f, .01f, .025f)) * b.H),
                new Vector3(.035f, .05f, .035f) * b.H, lowerColor);
        if (arm) ExpeditionEquipment.Add(lower, (end - bend) * b.H, b.H, fin, side);
        b.Limb(arm, side, joint * b.H - b.HipsOffset, bend * b.H - b.HipsOffset, upper, lower);
    }

    private static void BuildFish(CustomBody b)
    {
        Color teal = new(0.08f, 0.73f, 0.71f), coral = new(1f, 0.46f, 0.27f);
        Skeleton(b, teal, coral, teal.Darkened(0.18f), true);
        Ell(b, new(0f, 0.64f, 0f), new(0.22f, 0.26f, 0.14f), teal);
        Ell(b, new(0f, 0.85f, -0.045f), new(0.245f, 0.17f, 0.18f), teal.Lightened(0.1f));
        Ell(b, new(0f, 0.62f, -0.13f), new(0.15f, 0.19f, 0.035f), new Color(0.86f, 0.97f, 0.75f));
        // An unmistakable dorsal sail and split fish tail, rather than a recoloured human.
        b.Kit.Cone(new Vector3(0f, 0.7f, 0.12f) * b.H - b.HipsOffset,
            new Vector3(0f, 0.82f, 0.36f) * b.H - b.HipsOffset, 0.12f * b.H, coral);
        foreach (int side in new[] { -1, 1 })
        {
            b.Kit.Cone(new Vector3(0f, 0.46f, 0.13f) * b.H - b.HipsOffset,
                new Vector3(side * 0.18f, 0.47f, 0.42f) * b.H - b.HipsOffset, 0.08f * b.H, coral);
            Ell(b, new(side * 0.14f, 0.88f, -0.197f), new(0.065f, 0.07f, 0.035f), Colors.White);
            Ell(b, new(side * 0.14f, 0.88f, -0.227f), new(0.031f, 0.043f, 0.014f), new Color(0.04f, 0.08f, 0.15f));
            for (int gill = 0; gill < 3; gill++)
                Box(b, new(side * 0.222f, 0.8f - gill * 0.025f, -0.035f), new(0.018f, 0.012f, 0.07f), teal.Darkened(0.4f));
        }
        Box(b, new(0f, 0.48f, -0.02f), new(0.43f, 0.06f, 0.3f), new Color(0.18f, 0.25f, 0.3f));
        Box(b, new(0f, 0.48f, -0.18f), new(0.07f, 0.07f, 0.035f), new Color(1f, 0.8f, 0.24f));
    }

    private static void BuildScout(CustomBody b)
    {
        Color leather = new(0.55f, 0.31f, 0.16f), skin = new(0.91f, 0.7f, 0.5f);
        Color strap = new(0.15f, 0.19f, 0.24f), scarf = new(0.12f, 0.43f, 0.48f);
        Skeleton(b, leather, skin, strap, false);
        Box(b, new(0f, 0.65f, 0f), new(0.4f, 0.29f, 0.24f), leather);
        Box(b, new(0f, 0.61f, -0.13f), new(0.22f, 0.28f, 0.025f), new Color(0.86f, 0.85f, 0.72f));
        Ell(b, new(0f, 0.87f, -0.02f), new(0.17f, 0.15f, 0.14f), skin);
        Ell(b, new(0f, 0.95f, 0.005f), new(0.18f, 0.08f, 0.15f), strap);
        Box(b, new(0f, 0.77f, 0f), new(0.39f, 0.07f, 0.28f), scarf);
        Box(b, new(-0.12f, 0.65f, 0.15f), new(0.16f, 0.28f, 0.035f), scarf);
        foreach (int side in new[] { -1, 1 })
        {
            b.Kit.Tube(new Vector3(side * 0.17f, 0.75f, -0.15f) * b.H - b.HipsOffset,
                new Vector3(-side * 0.13f, 0.5f, -0.15f) * b.H - b.HipsOffset, 0.014f * b.H, 0.014f * b.H, strap);
            Box(b, new(side * 0.11f, 0.39f, -0.07f), new(0.13f, 0.035f, 0.08f), strap);
            Box(b, new(side * 0.26f, 0.49f, 0.02f), new(0.13f, 0.16f, 0.17f), new Color(0.52f, 0.59f, 0.62f));
            Ell(b, new(side * 0.33f, 0.49f, 0.02f), new(0.022f, 0.056f, 0.056f), strap);
            b.Kit.Tube(new Vector3(side * 0.28f, 0.54f, -0.09f) * b.H - b.HipsOffset,
                new Vector3(side * 0.28f, 0.7f, -0.12f) * b.H - b.HipsOffset, 0.018f * b.H, 0.008f * b.H, Colors.Silver);
            Ell(b, new(side * 0.067f, 0.88f, -0.155f), new(0.024f, 0.03f, 0.012f), strap);
        }
        Box(b, new(0f, 0.49f, 0f), new(0.4f, 0.045f, 0.28f), strap);
    }
}
