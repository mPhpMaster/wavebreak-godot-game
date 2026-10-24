using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>
/// The safe stop between two maps: a little sand island with a beach hut, the
/// shady merchant's stall, a campfire that heals to full, a microwave, and the
/// CONTINUE portal to the next map. No enemies, and the run clock is held while
/// the team is here (<see cref="Main"/> skips <see cref="RunState.Tick"/>), so
/// shopping costs nothing but the decision.
///
/// It is built by <see cref="StageDirector"/> in place of the map it replaces,
/// at the same origin, and the map's polygon clamp is pointed at
/// <see cref="Bounds"/> so the player cannot wander off the island. Everything
/// is a child of this node and leaves with it. The stations are handed to
/// <see cref="Interactables"/>, which prices and runs them exactly as it did
/// on the maps (merchant, microwave) or heals (campfire).
///
/// Layout, seen from the arrival spot looking north (-Z): the hut and the
/// merchant on the left, the campfire and microwave on the right, the portal
/// straight ahead. That is also the camera's view on arrival, so the first
/// frame shows everything there is to do.
/// </summary>
public sealed partial class RestRoom : Node3D
{
    /// <summary>The island's edge, for <see cref="Arena.ClampInside"/>.</summary>
    public static readonly StageDef Bounds = new()
    {
        Id = "restroom",
        DisplayName = "Rest Stop",
        Sides = 20,
        Radius = 15.5f,
        Theme = "meadow",
        Merchants = 0,
        Microwaves = 0,
        Breakables = 0,
    };

    public static readonly Vector3 SpawnPoint = new(0f, 0.3f, 8f);
    public static readonly Vector3 ExitPoint = new(0f, 0f, -9.5f);
    private static readonly Vector3 MerchantAt = new(-5.6f, 0f, -1.6f);
    private static readonly Vector3 HutAt = new(-9.2f, 0f, -5.4f);
    private static readonly Vector3 CampfireAt = new(5.2f, 0f, 0.6f);
    private static readonly Vector3 MicrowaveAt = new(7.4f, 0f, -4.6f);

    private static readonly Color Sand = new(0.96f, 0.86f, 0.64f);
    private static readonly Color SandWet = new(0.86f, 0.74f, 0.55f);
    private static readonly Color Sea = new(0.36f, 0.78f, 0.86f);
    private static readonly Color Straw = new(0.93f, 0.76f, 0.42f);
    private static readonly Color Plank = new(0.66f, 0.46f, 0.30f);
    private static readonly Color PlankDark = new(0.50f, 0.34f, 0.22f);

    // ------------------------------------------------------- co-op status
    // Written by the host's StageDirector (and, on clients, from its RestStatus
    // message); read by the HUD. Inside < 0 means single-player.

    public static int Inside { get; private set; } = -1;
    public static int Total { get; private set; }
    /// <summary>Seconds until the host moves on anyway, or below 0 when no
    /// countdown is running.</summary>
    public static float Countdown { get; private set; } = -1f;

    public static void SetStatus(int inside, int total, float countdown)
    {
        Inside = inside;
        Total = total;
        Countdown = countdown;
    }

    // ------------------------------------------------------------- parts

    public Merchant Merchant { get; private set; } = null!;
    public Campfire Campfire { get; private set; } = null!;
    public Microwave Microwave { get; private set; } = null!;
    public Portal Exit { get; private set; } = null!;

    /// <summary>1-based number of the map just cleared; prices follow it.</summary>
    public int ClearedPhase { get; }
    public string NextMapName { get; }

    public Vector3 ExitPosition => Exit is not null && IsInstanceValid(Exit) ? Exit.GlobalPosition : ExitPoint;

    public IEnumerable<Station> Stations
    {
        get
        {
            yield return Merchant;
            yield return Campfire;
            yield return Microwave;
            if (Trader is not null) yield return Trader;   // co-op only (RestRoom.Trade.cs)
        }
    }

    private StaticBody3D _body = null!;
    private MeshInstance3D? _sea;
    private float _t;

    public RestRoom() : this(1, "") { }

    public RestRoom(int clearedPhase, string nextMapName)
    {
        ClearedPhase = clearedPhase;
        NextMapName = nextMapName;
    }

    public override void _Ready()
    {
        Name = "RestRoom";
        SetStatus(Net.Coop.InRun ? 0 : -1, 0, -1f);

        PropTheme theme = PropTheme.Get("meadow");
        AddChild(PropFactory.MakeWorldEnvironment(theme));
        AddChild(PropFactory.MakeSun(theme));

        _body = new StaticBody3D { Name = "Solids" };
        AddChild(_body);
        BuildGround();
        BuildHut();
        BuildDecor();

        Merchant = Place(new Merchant(), MerchantAt);
        Campfire = Place(new Campfire(), CampfireAt);
        Microwave = Place(new Microwave(), MicrowaveAt);
        // A crate under the microwave, as on the maps.
        AddChild(Mesh(new BoxMesh { Size = new Vector3(1.3f, 0.12f, 1.1f) }, MicrowaveAt + new Vector3(0f, 0.06f, 0f), Flat(PlankDark)));

        Exit = new Portal { Name = "Exit" };
        AddChild(Exit);
        Exit.Position = ExitPoint;
        AddChild(new Label3D
        {
            Name = "ContinueSign",
            Text = Meta.Loc.T("CONTINUE"),
            FontSize = 64,
            PixelSize = 0.012f,
            OutlineSize = 16,
            Modulate = new Color(1f, 0.97f, 0.85f),
            OutlineModulate = new Color(0.10f, 0.20f, 0.30f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = ExitPoint + new Vector3(0f, 1.7f, 3.4f),
        });

        // The sign at the arrival spot, so the first frame says where you are.
        BuildSign(new Vector3(-2.4f, 0f, 5.2f));
        BuildSwapMeet();   // co-op: the trader's stall (RestRoom.Trade.cs)

        var layer = new CanvasLayer { Name = "RestRoomUI", Layer = 3 };
        AddChild(layer);
        layer.AddChild(new UI.RestRoomHud(NextMapName));
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_sea is not null) _sea.Position = new Vector3(0f, -0.32f + MathF.Sin(_t * 0.8f) * 0.04f, 0f);
    }

    private T Place<T>(T station, Vector3 at) where T : Station
    {
        AddChild(station);
        station.Position = at;
        Vector3 to = SpawnPoint - at;
        to.Y = 0f;
        station.Rotation = new Vector3(0f, MathF.Atan2(to.X, to.Z), 0f);
        return station;
    }

    // ------------------------------------------------------------ ground

    private void BuildGround()
    {
        // Collision: a flat disc a little wider than the clamp, so the edge of
        // the clamp is never the edge of the floor.
        _body.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = Bounds.Radius + 3f, Height = 1f },
            Position = new Vector3(0f, -0.5f, 0f),
        });

        AddChild(Mesh(new CylinderMesh { TopRadius = Bounds.Radius + 1.2f, BottomRadius = Bounds.Radius + 2.6f, Height = 0.5f, RadialSegments = 48 },
                      new Vector3(0f, -0.25f, 0f), Flat(SandWet), shadow: false));
        AddChild(Mesh(new CylinderMesh { TopRadius = Bounds.Radius, BottomRadius = Bounds.Radius + 1.2f, Height = 0.06f, RadialSegments = 48 },
                      new Vector3(0f, -0.02f, 0f), Flat(Sand), shadow: false));

        // The sea all round, with a soft foam ring at the shore.
        _sea = Mesh(new CylinderMesh { TopRadius = 140f, BottomRadius = 140f, Height = 0.1f, RadialSegments = 48 },
                    new Vector3(0f, -0.32f, 0f), new StandardMaterial3D
                    {
                        AlbedoColor = Sea,
                        Roughness = 0.15f,
                        Metallic = 0.1f,
                        EmissionEnabled = true,
                        Emission = Sea.Darkened(0.4f),
                        EmissionEnergyMultiplier = 0.25f,
                    }, shadow: false);
        AddChild(_sea);
        AddChild(Mesh(new TorusMesh { InnerRadius = Bounds.Radius + 2.4f, OuterRadius = Bounds.Radius + 3.1f, RingSegments = 48, Rings = 6 },
                      new Vector3(0f, -0.26f, 0f), Glow(new Color(1f, 1f, 1f), 0.4f), shadow: false));

        // A boardwalk from the arrival spot to the portal.
        for (int i = 0; i < 9; i++)
        {
            float z = 6.4f - i * 1.75f;
            AddChild(Mesh(new BoxMesh { Size = new Vector3(2.2f, 0.08f, 1.45f) }, new Vector3(0f, 0.04f, z), Flat(i % 2 == 0 ? Plank : PlankDark), shadow: false));
        }
    }

    // --------------------------------------------------------------- hut

    private void BuildHut()
    {
        var hut = new Node3D { Name = "Hut", Position = HutAt, Rotation = new Vector3(0f, 0.55f, 0f) };
        AddChild(hut);
        var wood = Flat(Plank);
        var dark = Flat(PlankDark);
        var straw = Flat(Straw);

        // Raised deck on stilts.
        hut.AddChild(Mesh(new BoxMesh { Size = new Vector3(4.6f, 0.25f, 4.0f) }, new Vector3(0f, 0.55f, 0f), wood));
        foreach (float x in new[] { -2.0f, 2.0f })
            foreach (float z in new[] { -1.7f, 1.7f })
            {
                hut.AddChild(Mesh(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.14f, Height = 3.4f, RadialSegments = 7 },
                                  new Vector3(x, 1.7f, z), dark));
            }
        // Three walls; the front stays open, with a little rail.
        hut.AddChild(Mesh(new BoxMesh { Size = new Vector3(4.0f, 2.2f, 0.12f) }, new Vector3(0f, 1.78f, -1.7f), wood));
        foreach (float x in new[] { -2.0f, 2.0f })
            hut.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.12f, 2.2f, 3.4f) }, new Vector3(x, 1.78f, 0f), wood));
        hut.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.5f, 0.1f, 0.1f) }, new Vector3(-1.25f, 1.25f, 1.7f), dark));
        hut.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.5f, 0.1f, 0.1f) }, new Vector3(1.25f, 1.25f, 1.7f), dark));
        // A round window with a warm glow.
        var window = Mesh(new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 0.05f, RadialSegments = 16 },
                          new Vector3(0f, 2.1f, -1.63f), Glow(new Color(1f, 0.85f, 0.5f), 1.4f));
        window.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
        hut.AddChild(window);

        // Thatched roof: two stacked cones, the lower one wider, for the fringe.
        hut.AddChild(Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 3.6f, Height = 1.9f, RadialSegments = 10 },
                          new Vector3(0f, 3.85f, 0f), straw));
        hut.AddChild(Mesh(new CylinderMesh { TopRadius = 3.2f, BottomRadius = 3.75f, Height = 0.25f, RadialSegments = 10 },
                          new Vector3(0f, 2.95f, 0f), Flat(Straw.Darkened(0.12f))));
        hut.AddChild(Mesh(new SphereMesh { Radius = 0.22f, Height = 0.44f }, new Vector3(0f, 4.85f, 0f), Flat(new Color(1f, 0.52f, 0.47f))));

        // Steps up to the deck.
        for (int i = 0; i < 2; i++)
            hut.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.4f, 0.12f, 0.45f) }, new Vector3(0f, 0.18f + i * 0.2f, 2.35f - i * 0.4f), dark));

        // One collider for the whole hut, so the player walks round it.
        _body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(4.6f, 4f, 4.0f) },
            Transform = new Transform3D(new Basis(Vector3.Up, 0.55f), HutAt + new Vector3(0f, 2f, 0f)),
        });
    }

    // ------------------------------------------------------------- decor

    private void BuildDecor()
    {
        // Palms round the shore, leaving the boardwalk and the portal clear.
        float[] angles = { 0.3f, 0.95f, 2.25f, 2.85f, 3.55f, 4.1f, 5.25f, 5.95f };
        for (int i = 0; i < angles.Length; i++)
        {
            float a = angles[i];
            float r = Bounds.Radius - 1.6f + (i % 2) * 0.6f;
            Palm(new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r), 3.6f + (i % 3) * 0.5f, a + 1.3f);
        }

        // Beach umbrella and two towels by the campfire.
        var umbrella = new Node3D { Name = "Umbrella", Position = new Vector3(8.6f, 0f, 3.6f) };
        AddChild(umbrella);
        umbrella.AddChild(Mesh(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 2.6f, RadialSegments = 6 },
                               new Vector3(0f, 1.3f, 0f), Flat(new Color(0.95f, 0.95f, 0.95f))));
        umbrella.AddChild(Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 1.7f, Height = 0.7f, RadialSegments = 8 },
                               new Vector3(0f, 2.55f, 0f), Flat(new Color(1f, 0.52f, 0.47f))));
        // A second, smaller cone on top gives it a cream crown.
        umbrella.AddChild(Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 1.1f, Height = 0.46f, RadialSegments = 8 },
                               new Vector3(0f, 2.72f, 0f), Flat(new Color(1f, 0.97f, 0.93f))));
        AddChild(Mesh(new BoxMesh { Size = new Vector3(0.9f, 0.03f, 1.9f) }, new Vector3(7.6f, 0.02f, 4.4f), Flat(new Color(0.55f, 0.80f, 1.00f)), shadow: false));
        AddChild(Mesh(new BoxMesh { Size = new Vector3(0.9f, 0.03f, 1.9f) }, new Vector3(9.4f, 0.02f, 4.9f), Flat(new Color(1.00f, 0.86f, 0.45f)), shadow: false));

        // A beach ball.
        var ball = Mesh(new SphereMesh { Radius = 0.35f, Height = 0.7f }, new Vector3(3.2f, 0.35f, 5.4f), Flat(new Color(1f, 0.6f, 0.72f)));
        AddChild(ball);
        AddChild(Mesh(new TorusMesh { InnerRadius = 0.33f, OuterRadius = 0.37f }, new Vector3(3.2f, 0.35f, 5.4f), Flat(new Color(1f, 0.97f, 0.93f))));

        // String lights between the hut and the stall.
        Vector3 from = HutAt + new Vector3(2.2f, 3.0f, 1.8f), to = MerchantAt + new Vector3(1.1f, 2.4f, 0.2f);
        Color[] bulbs = { new(1f, 0.86f, 0.45f), new(1f, 0.6f, 0.72f), new(0.55f, 0.9f, 0.74f), new(0.55f, 0.8f, 1f) };
        for (int i = 0; i <= 8; i++)
        {
            float t = i / 8f;
            Vector3 p = from.Lerp(to, t) + Vector3.Down * (MathF.Sin(t * Mathf.Pi) * 0.5f);
            AddChild(Mesh(new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 6, Rings = 3 }, p, Glow(bulbs[i % bulbs.Length], 2.2f), shadow: false));
        }

        // Kit flowers and bushes round the hut, and cube pets on the beach.
        string root = ArtAssets.Root + "kenney/nature-kit/";
        Kit(root + "plant_bushLarge.glb", 1.1f, HutAt + new Vector3(3.2f, 0f, -1.6f), 0.4f);
        Kit(root + "plant_bush.glb", 0.8f, HutAt + new Vector3(-0.6f, 0f, 3.1f), 1.2f);
        Kit(root + "flower_redA.glb", 0.5f, MerchantAt + new Vector3(1.9f, 0f, 1.2f), 0f);
        Kit(root + "flower_yellowA.glb", 0.5f, MerchantAt + new Vector3(2.3f, 0f, 0.6f), 1f);
        Kit(root + "flower_purpleA.glb", 0.5f, CampfireAt + new Vector3(-2.4f, 0f, 1.8f), 2f);
        Kit(root + "rock_smallA.glb", 0.5f, CampfireAt + new Vector3(2.0f, 0f, -1.8f), 0.7f);
        Kit(root + "rock_largeB.glb", 1.2f, new Vector3(4.5f, 0f, -11.5f), 0.7f);
        Kit(root + "rock_largeA.glb", 1.0f, new Vector3(-4.8f, 0f, -11.2f), 2.2f);
        string pets = ArtAssets.Root + "kenney/cube-pets/";
        Kit(pets + "animal-crab.glb", 0.55f, new Vector3(2.6f, 0f, 9.6f), MathF.PI * 0.85f);
        Kit(pets + "animal-parrot.glb", 0.6f, HutAt + new Vector3(1.0f, 0.68f, 0.6f), 0.9f);
        Kit(pets + "animal-chick.glb", 0.45f, CampfireAt + new Vector3(-1.4f, 0f, 1.6f), MathF.PI);
    }

    private void Palm(Vector3 at, float height, float lean)
    {
        var palm = new Node3D { Name = "Palm", Position = at, Rotation = new Vector3(0f, lean, 0f) };
        AddChild(palm);
        var trunk = Flat(new Color(0.62f, 0.45f, 0.30f));
        var ring = Flat(new Color(0.52f, 0.37f, 0.24f));
        // A trunk of stacked, slightly offset segments: the curve is the palm.
        Vector3 top = Vector3.Zero;
        int segments = 6;
        for (int i = 0; i < segments; i++)
        {
            float t = (i + 0.5f) / segments;
            Vector3 p = new(0f, t * height, t * t * 1.1f);
            palm.AddChild(Mesh(new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.21f, Height = height / segments + 0.05f, RadialSegments = 7 },
                               p, i % 2 == 0 ? trunk : ring));
            top = new Vector3(0f, height, 1.1f);
        }
        // Fronds: flattened, drooping leaves round the crown, two greens.
        Color[] greens = { new(0.32f, 0.70f, 0.36f), new(0.46f, 0.80f, 0.40f) };
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6f;
            var leaf = Mesh(new BoxMesh { Size = new Vector3(0.55f, 0.06f, 2.1f) }, Vector3.Zero, Flat(greens[i % 2]));
            leaf.Transform = new Transform3D(new Basis(Vector3.Up, a) * new Basis(Vector3.Right, 0.38f),
                                             top + new Vector3(MathF.Sin(a) * 0.85f, -0.25f, MathF.Cos(a) * 0.85f));
            palm.AddChild(leaf);
        }
        palm.AddChild(Mesh(new SphereMesh { Radius = 0.28f, Height = 0.56f }, top, Flat(greens[0].Darkened(0.2f))));
        // Two coconuts.
        palm.AddChild(Mesh(new SphereMesh { Radius = 0.15f, Height = 0.3f }, top + new Vector3(0.18f, -0.25f, 0.1f), Flat(new Color(0.45f, 0.30f, 0.18f))));
        palm.AddChild(Mesh(new SphereMesh { Radius = 0.15f, Height = 0.3f }, top + new Vector3(-0.15f, -0.28f, -0.08f), Flat(new Color(0.45f, 0.30f, 0.18f))));
    }

    private void BuildSign(Vector3 at)
    {
        var sign = new Node3D { Name = "Sign", Position = at, Rotation = new Vector3(0f, 0.25f, 0f) };
        AddChild(sign);
        sign.AddChild(Mesh(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.08f, Height = 1.6f, RadialSegments = 6 },
                           new Vector3(0f, 0.8f, 0f), Flat(PlankDark)));
        sign.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.9f, 0.7f, 0.1f) }, new Vector3(0f, 1.55f, 0f), Flat(Plank)));
        sign.AddChild(new Label3D
        {
            Text = Meta.Loc.T("REST STOP"),
            FontSize = 48,
            PixelSize = 0.006f,
            OutlineSize = 8,
            Modulate = new Color(1f, 0.97f, 0.9f),
            OutlineModulate = new Color(0.3f, 0.18f, 0.1f, 1f),
            Position = new Vector3(0f, 1.56f, 0.06f),
        });
    }

    // ----------------------------------------------------------- helpers

    private static MeshInstance3D Mesh(Mesh mesh, Vector3 at, Material mat, bool shadow = true) => new()
    {
        Mesh = mesh,
        Position = at,
        MaterialOverride = mat,
        CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private static StandardMaterial3D Flat(Color c) => new() { AlbedoColor = c, Roughness = 0.85f };

    private static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    /// <summary>An imported model, <paramref name="height"/> metres tall with
    /// its feet at <paramref name="at"/>. Skipped quietly when the file (or
    /// imported art as a whole) is missing: the room stands without it.</summary>
    private void Kit(string path, float height, Vector3 at, float yaw)
    {
        if (!ArtAssets.UseImported || !ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene) return;
        if (scene.Instantiate() is not Node3D model) return;
        Aabb? box = null;
        void Walk(Node n, Transform3D xf)
        {
            Transform3D here = n is Node3D n3 ? xf * n3.Transform : xf;
            if (n is MeshInstance3D { Mesh: not null } mi)
            {
                Aabb b = here * mi.GetAabb();
                box = box is { } acc ? acc.Merge(b) : b;
            }
            foreach (Node c in n.GetChildren()) Walk(c, here);
        }
        foreach (Node c in model.GetChildren()) Walk(c, Transform3D.Identity);
        float h = box is { } bb ? MathF.Max(0.05f, bb.Size.Y) : 1f;
        float s = height / h;
        float floor = box is { } fb ? fb.Position.Y * s : 0f;
        model.Scale = Vector3.One * s;
        model.Position = at - new Vector3(0f, floor, 0f);
        model.Rotation = new Vector3(0f, yaw, 0f);
        AddChild(model);
    }
}
