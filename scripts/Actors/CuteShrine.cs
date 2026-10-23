using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Actors;

/// <summary>
/// A Shrine of Balance or a Wishing Well standing in the world. Built from
/// rounded primitives in soft pastels (no imported model fits, and these are
/// meant to look friendly next to the stone shrines): a little pedestal with a
/// swaying set of scales and a floating heart, or a round stone well with a
/// peaked roof, a bucket and shimmering water.
///
/// Passive like <see cref="MapShrine"/>: <see cref="Game.CuteShrineSpawner"/>
/// decides reach, price and payout, and tells it to celebrate, deny or go dark.
/// No collision, for the same reason chests and map shrines have none.
/// </summary>
public sealed partial class CuteShrine : Node3D
{
    public CuteShrineKind Kind { get; private set; }

    public bool Spent { get; private set; }

    public int Uses { get; set; }

    public Color Tint => CuteShrines.Tint(Kind);

    public Vector3 Top => GlobalPosition + Vector3.Up * (_height * Size + 0.9f);

    /// <summary>Whole-model scale: a touch bigger than life, so it reads at a glance.</summary>
    private const float Size = 1.25f;

    private const float BeamHeight = 10f;

    private float _height = 2.4f;
    private float _phase;
    private float _flash;
    private Node3D _charm = null!;
    private Node3D? _beamPivot;
    private OmniLight3D _light = null!;
    private MeshInstance3D? _beam;
    private MeshInstance3D? _water;
    private StandardMaterial3D? _waterMat;
    private Label3D _name = null!;
    private CpuParticles3D _burst = null!;
    private CpuParticles3D? _sparkle;
    private readonly List<StandardMaterial3D> _paints = new();

    public CuteShrine() { }

    public CuteShrine(CuteShrineKind kind) => Kind = kind;

    public override void _Ready()
    {
        Name = $"CuteShrine_{Kind}";
        Scale = Vector3.One * Size;
        Color tint = Tint;

        if (Kind == CuteShrineKind.Balance) BuildBalance(tint);
        else BuildWell(tint);

        _light = new OmniLight3D
        {
            Position = new Vector3(0f, _height * 0.7f + 0.4f, 0f),
            LightColor = tint,
            LightEnergy = 1.2f,
            OmniRange = 6.5f,
            ShadowEnabled = false,
        };
        AddChild(_light);

        _beam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.28f, Height = BeamHeight, RadialSegments = 10 },
            Position = new Vector3(0f, BeamHeight * 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(tint, 0.12f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_beam);

        _name = new Label3D
        {
            Text = Meta.Loc.IsArabic ? Meta.Loc.T(CuteShrines.Name(Kind)) : CuteShrines.Name(Kind).ToUpperInvariant(),

            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40,
            PixelSize = 0.006f,
            OutlineSize = 10,
            OutlineModulate = new Color(0.12f, 0.08f, 0.22f, 0.9f),
            Modulate = tint.Lightened(0.4f),
            Position = new Vector3(0f, _height + 1.5f, 0f),
            NoDepthTest = true,
            Shaded = false,
            Visible = false,
        };
        AddChild(_name);

        _burst = new CpuParticles3D
        {
            Position = new Vector3(0f, _height * 0.6f, 0f),
            Emitting = false,
            OneShot = true,
            Amount = 48,
            Lifetime = 1.2f,
            Explosiveness = 0.9f,
            Direction = Vector3.Up,
            Spread = 55f,
            InitialVelocityMin = 2.5f,
            InitialVelocityMax = 6.5f,
            Gravity = new Vector3(0f, -6f, 0f),
            Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f, RadialSegments = 6, Rings = 3 },
            MaterialOverride = Glow(tint.Lightened(0.3f), 3f),
        };
        AddChild(_burst);

        _phase = Mathf.Abs(GetInstanceId().GetHashCode() % 628) / 100f;
    }

    // ------------------------------------------------------------ build

    /// <summary>Pedestal, post, a beam with two pans on strings, and a pink
    /// heart floating above: balance, but make it adorable.</summary>
    private void BuildBalance(Color tint)
    {
        Color stone = new(0.93f, 0.90f, 0.98f);
        Color gold = new(1.00f, 0.84f, 0.50f);
        Prim(new CylinderMesh { TopRadius = 0.85f, BottomRadius = 1.0f, Height = 0.35f, RadialSegments = 20 }, new Vector3(0f, 0.175f, 0f), stone);
        Prim(new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.7f, Height = 0.25f, RadialSegments = 20 }, new Vector3(0f, 0.47f, 0f), tint.Lightened(0.35f));
        Prim(new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.15f, Height = 1.7f, RadialSegments = 10 }, new Vector3(0f, 1.45f, 0f), gold);
        Prim(new SphereMesh { Radius = 0.17f, Height = 0.34f }, new Vector3(0f, 2.33f, 0f), gold);

        // The beam and pans sway together around the top of the post.
        _beamPivot = new Node3D { Name = "Beam", Position = new Vector3(0f, 2.3f, 0f) };
        AddChild(_beamPivot);
        PrimOn(_beamPivot, new CapsuleMesh { Radius = 0.07f, Height = 2.3f }, Vector3.Zero, gold, new Vector3(0f, 0f, Mathf.Pi / 2f));
        foreach (float x in new[] { -1.05f, 1.05f })
        {
            PrimOn(_beamPivot, new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.015f, Height = 0.8f }, new Vector3(x, -0.42f, 0f), gold, Vector3.Zero);
            PrimOn(_beamPivot, new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.26f, Height = 0.14f, RadialSegments = 18 },
                   new Vector3(x, -0.86f, 0f), x < 0f ? new Color(0.62f, 0.95f, 0.80f) : new Color(1.0f, 0.75f, 0.62f), Vector3.Zero);
            // A pearl in each pan.
            PrimOn(_beamPivot, new SphereMesh { Radius = 0.12f, Height = 0.24f }, new Vector3(x, -0.72f, 0f), new Color(1f, 0.97f, 0.95f), Vector3.Zero, 0.6f);
        }

        _charm = new Node3D { Name = "Charm", Position = new Vector3(0f, 3.05f, 0f) };
        AddChild(_charm);
        // Two spheres and a cone: a heart.
        var pink = Glow(new Color(1.00f, 0.55f, 0.75f), 1.6f);
        foreach (float x in new[] { -0.11f, 0.11f })
            _charm.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.15f, Height = 0.3f }, Position = new Vector3(x, 0.05f, 0f), MaterialOverride = pink });
        _charm.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.0f, Height = 0.32f, RadialSegments = 16 },
            Position = new Vector3(0f, -0.14f, 0f),
            MaterialOverride = pink,
        });
        _height = 2.6f;
    }

    /// <summary>A round stone well with a peaked roof on two posts, a bucket on
    /// a rope and sparkly water.</summary>
    private void BuildWell(Color tint)
    {
        Color stone = new(0.80f, 0.82f, 0.90f);
        Color wood = new(0.78f, 0.56f, 0.40f);
        Color roof = new(1.00f, 0.62f, 0.62f);
        // Ring of stones: an outer tube.
        Prim(new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.25f, Height = 0.95f, RadialSegments = 24 }, new Vector3(0f, 0.475f, 0f), stone);
        Prim(new TorusMesh { InnerRadius = 0.92f, OuterRadius = 1.28f, RingSegments = 24, Rings = 10 }, new Vector3(0f, 0.95f, 0f), stone.Lightened(0.25f));
        _waterMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.78f, 1.00f),
            Roughness = 0.05f,
            Metallic = 0.1f,
            EmissionEnabled = true,
            Emission = new Color(0.30f, 0.75f, 1.00f),
            EmissionEnergyMultiplier = 0.9f,
        };
        _water = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.95f, BottomRadius = 0.95f, Height = 0.05f, RadialSegments = 24 },
            Position = new Vector3(0f, 0.9f, 0f),
            MaterialOverride = _waterMat,
        };
        AddChild(_water);

        foreach (float x in new[] { -1.05f, 1.05f })
            Prim(new BoxMesh { Size = new Vector3(0.16f, 1.9f, 0.16f) }, new Vector3(x, 1.85f, 0f), wood);
        Prim(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 2.2f }, new Vector3(0f, 2.55f, 0f), wood, new Vector3(0f, 0f, Mathf.Pi / 2f));
        // Roof: a wide prism, rounded off with a ridge.
        Prim(new PrismMesh { Size = new Vector3(2.9f, 0.85f, 1.6f) }, new Vector3(0f, 3.25f, 0f), roof);
        Prim(new CapsuleMesh { Radius = 0.08f, Height = 1.7f }, new Vector3(0f, 3.68f, 0f), roof.Darkened(0.1f), new Vector3(Mathf.Pi / 2f, 0f, 0f));

        _charm = new Node3D { Name = "Bucket", Position = new Vector3(0f, 1.8f, 0f) };
        AddChild(_charm);
        PrimOn(_charm, new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.01f, Height = 0.7f }, new Vector3(0f, 0.38f, 0f), new Color(0.9f, 0.85f, 0.75f), Vector3.Zero);
        PrimOn(_charm, new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.17f, Height = 0.3f, RadialSegments = 14 }, Vector3.Zero, wood.Lightened(0.15f), Vector3.Zero);
        // A gold coin glinting on the rim.
        Prim(new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.13f, Height = 0.03f, RadialSegments = 14 },
             new Vector3(0.75f, 1.03f, 0.55f), new Color(1f, 0.82f, 0.30f), new Vector3(0.3f, 0f, 0.2f), 1.2f);

        _sparkle = new CpuParticles3D
        {
            Position = new Vector3(0f, 0.95f, 0f),
            Amount = 10,
            Lifetime = 1.6f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.7f,
            Direction = Vector3.Up,
            Spread = 15f,
            InitialVelocityMin = 0.3f,
            InitialVelocityMax = 0.7f,
            Gravity = Vector3.Zero,
            Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 6, Rings = 3 },
            MaterialOverride = Glow(new Color(0.85f, 0.97f, 1f), 3f),
        };
        AddChild(_sparkle);
        _height = 3.3f;
    }

    private void Prim(Mesh mesh, Vector3 at, Color color, Vector3? rot = null, float glow = 0f)
        => PrimOn(this, mesh, at, color, rot ?? Vector3.Zero, glow);

    private void PrimOn(Node3D parent, Mesh mesh, Vector3 at, Color color, Vector3 rot, float glow = 0f)
    {
        var mat = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.55f };
        if (glow > 0f) { mat.EmissionEnabled = true; mat.Emission = color; mat.EmissionEnergyMultiplier = glow; }
        _paints.Add(mat);
        parent.AddChild(new MeshInstance3D { Mesh = mesh, Position = at, Rotation = rot, MaterialOverride = mat });
    }

    // ------------------------------------------------------------ state

    public void ShowName(bool show) => _name.Visible = show && !Spent;

    public void Deny()
    {
        _flash = 1f;
        Tween t = CreateTween();
        foreach (float x in new[] { 0.08f, -0.07f, 0.05f, -0.03f, 0f })
            t.TweenProperty(_charm, "position:x", x, 0.045);
    }

    public void Celebrate()
    {
        _burst.Restart();
        _burst.Emitting = true;
        Tween t = CreateTween();
        t.TweenProperty(_light, "light_energy", 6f, 0.12);
        t.TweenProperty(_light, "light_energy", Spent ? 0.3f : 1.2f, 1.2);
        Tween hop = CreateTween();
        hop.TweenProperty(_charm, "scale", Vector3.One * 1.35f, 0.12);
        hop.TweenProperty(_charm, "scale", Vector3.One, 0.35).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Used up: soft grey, no beam, still and quiet.</summary>
    public void MarkSpent()
    {
        if (Spent) return;
        Spent = true;
        if (_beam is not null) _beam.Visible = false;
        if (_sparkle is not null) _sparkle.Emitting = false;
        foreach (StandardMaterial3D m in _paints) m.AlbedoColor = m.AlbedoColor.Lerp(new Color(0.55f, 0.55f, 0.6f), 0.55f);
        if (_waterMat is not null) _waterMat.EmissionEnergyMultiplier = 0.15f;
        _name.Visible = false;
        CreateTween().TweenProperty(_light, "light_energy", 0.25f, 1.2);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _phase += dt;
        if (Spent) return;

        if (_beamPivot is not null) _beamPivot.Rotation = new Vector3(0f, 0f, Mathf.Sin(_phase * 1.3f) * 0.16f);
        if (Kind == CuteShrineKind.Balance)
        {
            _charm.Position = new Vector3(_charm.Position.X, 3.05f + Mathf.Sin(_phase * 2f) * 0.12f, 0f);
            _charm.RotateY(dt * 1.2f);
        }
        else
        {
            _charm.Position = new Vector3(_charm.Position.X, 1.8f + Mathf.Sin(_phase * 1.1f) * 0.08f, 0f);
            if (_waterMat is not null) _waterMat.EmissionEnergyMultiplier = 0.8f + 0.25f * Mathf.Sin(_phase * 2.5f);
        }

        float energy = 1.2f * (0.85f + 0.15f * Mathf.Sin(_phase * 3f));
        if (_flash > 0f)
        {
            _flash = Mathf.Max(0f, _flash - dt * 3f);
            _light.LightColor = Tint.Lerp(new Color(1f, 0.3f, 0.3f), _flash);
            energy = Mathf.Max(energy, 1.2f + 3f * _flash);
        }
        else _light.LightColor = Tint;
        _light.LightEnergy = energy;
    }

    private static StandardMaterial3D Glow(Color color, float energy) => new()
    {
        AlbedoColor = color,
        Roughness = 0.4f,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
    };
}
