using Godot;

namespace Wavebreak.Actors;

/// <summary>
/// The swap meet in the rest room (co-op): a beaver at a rug-and-crates
/// stall under a sea-green awning, with a crab helper on the counter and a
/// hanging "SWAP MEET" board with two arrows going round. Deliberately
/// unlike the merchant (purple stall, hooded keeper): this one does not
/// sell anything, it only lets teammates swap. Interacting opens the
/// teammate picker (<see cref="Game.Trade.SwapMeet"/>).
/// </summary>
public sealed partial class Trader : Station
{
    public static readonly Color TraderTint = new(0.45f, 0.92f, 0.80f);

    public override Color Tint => TraderTint;
    public override string Verb => "swap with a teammate";

    private Node3D? _arrows;
    private float _spin;

    public override void _Ready()
    {
        Name = "Trader";
        var wood = Flat(new Color(0.62f, 0.44f, 0.28f));
        var woodDark = Flat(new Color(0.46f, 0.31f, 0.20f));
        var sea = Flat(new Color(0.36f, 0.80f, 0.74f));
        var cream = Flat(new Color(1.00f, 0.96f, 0.88f));
        var coral = Flat(new Color(1.00f, 0.55f, 0.48f));

        // A low counter of two crates and a plank, a rug in front with the
        // wares laid out on it: a swap meet, not a shop.
        Part(new BoxMesh { Size = new Vector3(0.8f, 0.7f, 0.7f) }, new Vector3(-0.6f, 0.35f, 0.45f), wood);
        Part(new BoxMesh { Size = new Vector3(0.8f, 0.7f, 0.7f) }, new Vector3(0.6f, 0.35f, 0.45f), woodDark);
        Part(new BoxMesh { Size = new Vector3(2.3f, 0.07f, 0.85f) }, new Vector3(0f, 0.74f, 0.45f), cream);
        Part(new BoxMesh { Size = new Vector3(2.4f, 0.02f, 1.3f) }, new Vector3(0f, 0.01f, 1.55f), coral);
        Part(new BoxMesh { Size = new Vector3(2.0f, 0.021f, 0.9f) }, new Vector3(0f, 0.012f, 1.55f), Flat(new Color(1.00f, 0.86f, 0.45f)));
        // Wares on the rug and the counter: a book, a bottle, a coin heap, a box.
        Part(new BoxMesh { Size = new Vector3(0.34f, 0.08f, 0.26f) }, new Vector3(-0.6f, 0.07f, 1.5f), Flat(new Color(0.55f, 0.66f, 1.00f)));
        Part(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.09f, Height = 0.3f, RadialSegments = 8 }, new Vector3(0.1f, 0.16f, 1.7f), Glow(new Color(1f, 0.6f, 0.72f), 0.8f));
        Part(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.2f, Height = 0.12f, RadialSegments = 10 }, new Vector3(0.65f, 0.07f, 1.45f), Glow(new Color(1f, 0.82f, 0.3f), 0.9f));
        Part(new BoxMesh { Size = new Vector3(0.22f, 0.22f, 0.22f) }, new Vector3(-0.5f, 0.89f, 0.45f), Flat(new Color(0.55f, 0.90f, 0.74f)));
        Part(new SphereMesh { Radius = 0.11f, Height = 0.22f }, new Vector3(0.45f, 0.88f, 0.45f), Glow(new Color(0.55f, 0.8f, 1f), 0.9f));

        // Posts and a scalloped two-tone awning.
        foreach (float x in new[] { -1.1f, 1.1f })
            foreach (float z in new[] { 0.1f, -0.9f })
                Part(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 2.3f, RadialSegments = 6 },
                     new Vector3(x, 1.15f, z), woodDark);
        for (int i = 0; i < 5; i++)
        {
            float x = -1.0f + i * 0.5f;
            Part(new BoxMesh { Size = new Vector3(0.5f, 0.07f, 1.4f) }, new Vector3(x, 2.32f, -0.4f), i % 2 == 0 ? sea : cream);
            Part(new SphereMesh { Radius = 0.13f, Height = 0.18f, RadialSegments = 8, Rings = 4 }, new Vector3(x, 2.25f, 0.32f), i % 2 == 0 ? sea : cream);
        }

        // The sign board hanging from the awning's front edge.
        Part(new BoxMesh { Size = new Vector3(0.03f, 0.12f, 0.03f) }, new Vector3(-0.7f, 2.24f, 0.38f), woodDark);
        Part(new BoxMesh { Size = new Vector3(0.03f, 0.12f, 0.03f) }, new Vector3(0.7f, 2.24f, 0.38f), woodDark);
        Part(new BoxMesh { Size = new Vector3(1.8f, 0.42f, 0.07f) }, new Vector3(0f, 2.02f, 0.4f), wood);
        AddChild(new Label3D
        {
            Name = "SignText",
            Text = Meta.Loc.T("SWAP MEET"),
            FontSize = 44,
            PixelSize = 0.0062f,
            OutlineSize = 8,
            Modulate = new Color(1f, 0.97f, 0.9f),
            OutlineModulate = new Color(0.10f, 0.32f, 0.30f, 1f),
            Position = new Vector3(0f, 2.03f, 0.445f),
        });

        // Two arrows chasing each other round over the roof: "swap".
        _arrows = new Node3D { Name = "Arrows", Position = new Vector3(0f, 3.15f, -0.3f) };
        AddChild(_arrows);
        for (int k = 0; k < 2; k++)
        {
            float a = k * Mathf.Pi;
            var arm = new Node3D { Rotation = new Vector3(0f, a, 0f) };
            _arrows.AddChild(arm);
            var shaft = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.30f, OuterRadius = 0.38f, Rings = 12, RingSegments = 6 },
                MaterialOverride = Glow(k == 0 ? TraderTint : new Color(1f, 0.72f, 0.58f), 1.6f),
                Scale = new Vector3(1f, 1f, 1f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            // Half a ring each: the other half is hidden inside the sibling's colour.
            arm.AddChild(shaft);
            arm.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.13f, Height = 0.24f, RadialSegments = 6 },
                MaterialOverride = Glow(k == 0 ? TraderTint : new Color(1f, 0.72f, 0.58f), 1.6f),
                Position = new Vector3(0.34f, 0f, 0f),
                Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        _arrows.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);

        // The trader behind the counter, and a crab helper on it.
        if (Model("res://assets/kenney/cube-pets/animal-beaver.glb", 1.25f, new Vector3(0f, 0.35f, -0.2f), -Mathf.Pi * 0.5f) is null)
        {
            Part(new CapsuleMesh { Radius = 0.32f, Height = 1.1f }, new Vector3(0f, 0.55f, -0.3f), Flat(new Color(0.55f, 0.38f, 0.25f)));
            Part(new SphereMesh { Radius = 0.26f, Height = 0.52f }, new Vector3(0f, 1.25f, -0.3f), Flat(new Color(0.62f, 0.44f, 0.30f)));
        }
        Model("res://assets/kenney/cube-pets/animal-crab.glb", 0.32f, new Vector3(0.75f, 0.78f, 0.45f), 0.4f);

        BuildMarker(3.5f);
        Refresh();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_arrows is null) return;
        _spin += (float)delta;
        _arrows.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, _spin * 1.4f);
    }
}
