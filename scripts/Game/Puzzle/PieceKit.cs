using Godot;

namespace Wavebreak.Game.Puzzle;

/// <summary>A pressure plate's moving top, for <see cref="PieceKit.DrawPlate"/>.</summary>
public sealed class PlateView
{
    public Node3D Top = null!;
    public StandardMaterial3D Mat = null!;
    public float Shown;
}

/// <summary>
/// What a <see cref="PuzzleKind"/> builds its pieces with: nodes go into the
/// room being built (so they hide with it), colliders go on the pieces' body
/// (the player's and the guards' layers, never the camera's: a piece behind
/// the player never pulls the camera into their head), and the shared
/// palette, materials and stock pieces (plates, carved symbols, labels, a
/// reset post) keep every room in one style.
/// </summary>
public sealed class PieceKit
{
    public PuzzleMap Map { get; }

    public PieceKit(PuzzleMap map) => Map = map;

    // The hall palette (PuzzleMap.Build.cs uses the same).
    public static readonly Color Stone = new(0.86f, 0.79f, 0.64f);
    public static readonly Color Wall = new(0.50f, 0.62f, 0.66f);
    public static readonly Color WallCap = new(0.88f, 0.86f, 0.78f);
    public static readonly Color Bronze = new(0.74f, 0.53f, 0.30f);
    public static readonly Color BronzeDark = new(0.50f, 0.34f, 0.20f);
    public static readonly Color Teal = new(0.40f, 0.95f, 0.86f);
    public static readonly Color Warm = new(1f, 0.82f, 0.55f);
    public static readonly Color Flame = new(1f, 0.62f, 0.25f);
    public static readonly Color PlateIdle = new(0.70f, 0.66f, 0.58f);
    public static readonly Color Iron = new(0.30f, 0.29f, 0.28f);

    /// <summary>Add a node to the room being built.</summary>
    public void Put(Node n) => Map.PutPiece(n);

    /// <summary>A collider players and guards stop at (not the camera).</summary>
    public void Solid(Shape3D shape, Vector3 at) => Map.AddPieceCollider(new CollisionShape3D { Shape = shape, Position = at });

    /// <summary>An upright cylinder collider standing on <paramref name="at"/>.</summary>
    public void Post(Vector3 at, float radius, float height) => Solid(new CylinderShape3D { Radius = radius, Height = height }, at + Vector3.Up * (height * 0.5f));

    public static MeshInstance3D Mesh(Mesh mesh, Vector3 at, Material mat, bool shadow = true) => new()
    {
        Mesh = mesh,
        Position = at,
        MaterialOverride = mat,
        CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>A plain matte colour of its own (safe to animate).</summary>
    public static StandardMaterial3D Flat(Color c) => new() { AlbedoColor = c, Roughness = 0.85f };

    /// <summary>A glowing, unshaded colour of its own.</summary>
    public static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    /// <summary>Light a <see cref="Flat"/> material up (or put it out).</summary>
    public static void Lit(StandardMaterial3D m, bool on, Color glow, Color idle, float energy = 2f)
    {
        m.AlbedoColor = on ? glow : idle;
        m.EmissionEnabled = on;
        m.Emission = glow;
        m.EmissionEnergyMultiplier = energy;
    }

    /// <summary>A pressure plate: a bronze frame and a top that sinks when pressed.</summary>
    public PlateView Plate(Vector3 at, bool round = false)
    {
        var view = new PlateView { Mat = Flat(PlateIdle) };
        Put(Mesh(round ? new CylinderMesh { TopRadius = 1.0f, BottomRadius = 1.05f, Height = 0.1f, RadialSegments = 24 }
                            : new BoxMesh { Size = new Vector3(1.9f, 0.1f, 1.9f) }, at + Vector3.Up * 0.05f, Flat(BronzeDark), false));
        view.Top = new Node3D { Position = at + Vector3.Up * 0.16f };
        Put(view.Top);
        view.Top.AddChild(Mesh(round ? new CylinderMesh { TopRadius = 0.78f, BottomRadius = 0.82f, Height = 0.16f, RadialSegments = 24 }
                                     : new BoxMesh { Size = new Vector3(1.5f, 0.16f, 1.5f) }, Vector3.Zero, view.Mat, false));
        // A weight mark: a squat dome on the pad.
        view.Top.AddChild(Mesh(new SphereMesh { Radius = 0.3f, Height = 0.3f, RadialSegments = 12, Rings = 4 }, Vector3.Up * 0.08f, Flat(BronzeDark), false));
        return view;
    }

    /// <summary>Sink and light a plate (pressed), or raise it.</summary>
    public static void DrawPlate(PlateView p, bool pressed, float dt)
    {
        p.Shown = Mathf.MoveToward(p.Shown, pressed ? 1f : 0f, dt * 8f);
        Vector3 pos = p.Top.Position;
        p.Top.Position = new Vector3(pos.X, 0.16f - 0.11f * p.Shown, pos.Z);
        p.Mat.AlbedoColor = PlateIdle.Lerp(Teal, p.Shown);
        p.Mat.EmissionEnabled = p.Shown > 0.5f;
        p.Mat.Emission = Teal;
        p.Mat.EmissionEnergyMultiplier = 1.2f;
    }

    /// <summary>A carved symbol, standing up, about 0.8 m across.</summary>
    public static Node3D Symbol(RuneSymbol s, Material mat)
    {
        var n = new Node3D();
        MeshInstance3D Prism(int sides, float r, float rollZ = 0f)
        {
            var m = Mesh(new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.14f, RadialSegments = sides }, Vector3.Zero, mat);
            m.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, rollZ);
            n.AddChild(m);
            return m;
        }
        switch (s)
        {
            case RuneSymbol.Triangle: Prism(3, 0.45f, Mathf.Pi * 0.5f); break;
            case RuneSymbol.Circle: Prism(24, 0.36f); break;
            case RuneSymbol.Square: n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.62f, 0.62f, 0.14f) }, Vector3.Zero, mat)); break;
            case RuneSymbol.Diamond:
                var d = Mesh(new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.14f) }, Vector3.Zero, mat);
                d.Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.25f);
                n.AddChild(d);
                break;
            case RuneSymbol.Star:
                Prism(3, 0.45f, Mathf.Pi * 0.5f);
                Prism(3, 0.45f, -Mathf.Pi * 0.5f);
                break;
            default:   // Cross
                n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.7f, 0.2f, 0.14f) }, Vector3.Zero, mat));
                n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.2f, 0.7f, 0.14f) }, Vector3.Zero, mat));
                break;
        }
        return n;
    }

    /// <summary>A billboard label (numbers and letters on pieces).</summary>
    public Label3D Label(string text, Vector3 at, int size = 72, float pixel = 0.011f)
    {
        var l = new Label3D
        {
            Text = text,
            FontSize = size,
            PixelSize = pixel,
            OutlineSize = Mathf.Max(8, size / 5),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = at,
            Modulate = new Color(1f, 0.97f, 0.88f),
            OutlineModulate = new Color(0.15f, 0.12f, 0.08f),
        };
        Put(l);
        return l;
    }

    /// <summary>A reset bell: a short post with a circling arrow, solid.</summary>
    public void ResetPost(Vector3 at)
    {
        Put(Mesh(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.4f, Height = 1.1f, RadialSegments = 10 }, at + Vector3.Up * 0.55f, Flat(WallCap)));
        var ring = Mesh(new TorusMesh { InnerRadius = 0.32f, OuterRadius = 0.46f, Rings = 18, RingSegments = 6 }, at + Vector3.Up * 1.55f, Flat(Bronze));
        ring.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
        Put(ring);
        var head = Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.16f, Height = 0.3f, RadialSegments = 3 }, at + new Vector3(0.4f, 1.62f, 0f), Flat(Bronze));
        head.Rotation = new Vector3(0f, 0f, Mathf.Pi);
        Put(head);
        Post(at, 0.45f, 1.6f);
    }

    /// <summary>A thick straight line between two points (a beam, a pipe).</summary>
    public static MeshInstance3D? Segment(Node3D parent, Vector3 a, Vector3 b, Material mat, float thick = 0.24f)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 0.01f) return null;
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(thick, thick, len + thick) },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mi);
        mi.GlobalPosition = (a + b) * 0.5f;
        mi.LookAt(b, Mathf.Abs(d.Normalized().Y) > 0.9f ? Vector3.Right : Vector3.Up);
        return mi;
    }
}
