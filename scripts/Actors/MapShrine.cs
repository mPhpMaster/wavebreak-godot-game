using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Actors;

/// <summary>
/// A map shrine in the world: a Kenney model (or primitives if it is missing),
/// a tint, a floating gem, a soft beam for long range, and for the charge shrine
/// the ring on the ground and its fill.
///
/// Passive like <see cref="Chest"/>: <see cref="Game.ShrineSpawner"/> decides who
/// is in reach, what it costs and what it pays, and only tells the shrine to
/// show a charge, flash, celebrate or go dark. No collision, for the same reason
/// chests have none: a solid shrine is a pillar for the swarm to bunch behind.
/// </summary>
public sealed partial class MapShrine : Node3D
{
    public MapShrineKind Kind { get; private set; }

    /// <summary>Used up: no prompt, no marker, dimmed.</summary>
    public bool Spent { get; private set; }

    /// <summary>Times used (statue blessings bought, greed stacks taken).</summary>
    public int Uses { get; set; }

    /// <summary>Charge shrines: 0..1 fill.</summary>
    public float Charge { get; private set; }

    /// <summary>A challenge is running at this shrine.</summary>
    public bool Active { get; set; }

    public Color Tint => MapShrines.Tint(Kind);

    /// <summary>Anchor for prompts and popups.</summary>
    public Vector3 Top => GlobalPosition + Vector3.Up * (_height + 0.9f);

    private const float GemLift = 0.9f;
    private const float BeamHeight = 10f;

    private float _height = 2.4f;
    private float _phase;
    private float _flash;
    private Node3D _gem = null!;
    private OmniLight3D _light = null!;
    private MeshInstance3D? _beam;
    private MeshInstance3D? _fill;
    private StandardMaterial3D? _ringMat;
    private Label3D _name = null!;
    private CpuParticles3D _burst = null!;
    private readonly List<MeshInstance3D> _bodies = new();
    private StandardMaterial3D _overlay = null!;

    public MapShrine() { }

    public MapShrine(MapShrineKind kind) => Kind = kind;

    public override void _Ready()
    {
        Name = $"MapShrine_{Kind}";
        Color tint = Tint;

        _overlay = new StandardMaterial3D
        {
            AlbedoColor = new Color(tint, 0.18f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };

        BuildBody(tint);

        _gem = new Node3D { Name = "Gem", Position = new Vector3(0f, _height + GemLift, 0f) };
        AddChild(_gem);
        Mesh gemMesh = Kind switch
        {
            MapShrineKind.Statue => new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.22f, Height = 0.08f, RadialSegments = 12 },
            MapShrineKind.Greed => new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 6, Rings = 3 },
            MapShrineKind.Challenge => new PrismMesh { Size = new Vector3(0.4f, 0.5f, 0.12f) },
            _ => new SphereMesh { Radius = 0.24f, Height = 0.62f, RadialSegments = 4, Rings = 2 },
        };
        var gem = new MeshInstance3D { Mesh = gemMesh, MaterialOverride = Glow(tint, 2.6f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        if (Kind == MapShrineKind.Statue) gem.Rotation = new Vector3(Mathf.Pi / 2f, 0f, 0f);
        _gem.AddChild(gem);

        _light = new OmniLight3D
        {
            Position = new Vector3(0f, _height * 0.6f + 0.6f, 0f),
            LightColor = tint,
            LightEnergy = 1.1f,
            OmniRange = 6f,
            ShadowEnabled = false,
        };
        AddChild(_light);

        // A faint column visible across the map: the "marker" for a map with
        // no minimap. Unshaded and additive so it reads as light.
        _beam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.3f, Height = BeamHeight, RadialSegments = 10 },
            Position = new Vector3(0f, BeamHeight * 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(tint, 0.13f),
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
            Text = MapShrines.Name(Kind).ToUpperInvariant(),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40,
            PixelSize = 0.006f,
            OutlineSize = 10,
            OutlineModulate = new Color(0.03f, 0.03f, 0.05f, 0.9f),
            Modulate = tint.Lightened(0.35f),
            Position = new Vector3(0f, _height + GemLift + 0.6f, 0f),
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
            Amount = 64,
            Lifetime = 1.1f,
            Explosiveness = 0.9f,
            Direction = Vector3.Up,
            Spread = 50f,
            InitialVelocityMin = 3f,
            InitialVelocityMax = 8f,
            Gravity = new Vector3(0f, -8f, 0f),
            Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.09f, 0.09f) },
            MaterialOverride = Glow(tint, 3.5f),
        };
        AddChild(_burst);

        _phase = Mathf.Abs(GetInstanceId().GetHashCode() % 628) / 100f;
    }

    // ------------------------------------------------------------ build

    /// <summary>One silhouette per kind from Kenney pieces, normalised by height
    /// so the import scale of each pack does not matter.</summary>
    private void BuildBody(Color tint)
    {
        switch (Kind)
        {
            case MapShrineKind.Charge:
                // A standing stone ring on a low plinth, inside a ring on the ground.
                Plinth(1.0f, 0.3f);
                if (!Kit("kenney/nature-kit/statue_ring.glb", 2.2f, new Vector3(0f, 0.3f, 0f)))
                    Prim(new TorusMesh { InnerRadius = 0.6f, OuterRadius = 0.85f }, new Vector3(0f, 1.4f, 0f), new Vector3(Mathf.Pi / 2f, 0f, 0f));
                _height = 2.5f;
                BuildRing(tint);
                break;

            case MapShrineKind.Challenge:
                // An obelisk flanked by two fire baskets: a call to arms.
                if (!Kit("kenney/graveyard-kit/pillar-obelisk.glb", 3.0f, Vector3.Zero))
                    Prim(new PrismMesh { Size = new Vector3(0.9f, 3f, 0.9f) }, new Vector3(0f, 1.5f, 0f), Vector3.Zero);
                foreach (float x in new[] { -1.4f, 1.4f })
                    if (!Kit("kenney/graveyard-kit/fire-basket.glb", 1.2f, new Vector3(x, 0f, 0.3f)))
                        Prim(new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.15f, Height = 1.1f }, new Vector3(x, 0.55f, 0.3f), Vector3.Zero);
                _height = 3.0f;
                break;

            case MapShrineKind.Statue:
                // A block statue on a pedestal column, gilded by the overlay.
                if (!Kit("kenney/nature-kit/statue_column.glb", 1.1f, Vector3.Zero))
                    Plinth(0.7f, 1.1f);
                if (!Kit("kenney/nature-kit/statue_block.glb", 1.9f, new Vector3(0f, 1.1f, 0f)))
                    Prim(new BoxMesh { Size = new Vector3(0.9f, 1.8f, 0.7f) }, new Vector3(0f, 2.0f, 0f), Vector3.Zero);
                _height = 3.0f;
                break;

            default:
                // A grave marker between two urns and a candle: an offering you
                // pay for later.
                if (!Kit("kenney/graveyard-kit/gravestone-decorative.glb", 2.2f, Vector3.Zero))
                    Prim(new BoxMesh { Size = new Vector3(1.1f, 2.1f, 0.35f) }, new Vector3(0f, 1.05f, 0f), Vector3.Zero);
                foreach (float x in new[] { -1.1f, 1.1f })
                    if (!Kit("kenney/graveyard-kit/urn-round.glb", 0.8f, new Vector3(x, 0f, 0.5f)))
                        Prim(new SphereMesh { Radius = 0.35f, Height = 0.7f }, new Vector3(x, 0.35f, 0.5f), Vector3.Zero);
                Kit("kenney/graveyard-kit/lantern-candle.glb", 0.6f, new Vector3(0f, 0f, 0.8f));
                _height = 2.3f;
                break;
        }
    }

    private void BuildRing(Color tint)
    {
        float r = MapShrines.ChargeRadius;
        _ringMat = Glow(tint, 1.4f);
        // Thick enough (not flat) that the chest placer treats it as an obstacle
        // and keeps chests out of the ring.
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = r - 0.16f, OuterRadius = r + 0.16f, RingSegments = 8, Rings = 48 },
            Position = new Vector3(0f, 0.05f, 0f),
            MaterialOverride = _ringMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(ring);

        _fill = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.02f, RadialSegments = 40 },
            Position = new Vector3(0f, 0.04f, 0f),
            Scale = new Vector3(0.01f, 1f, 0.01f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(tint, 0.35f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_fill);
    }

    private bool Kit(string relative, float height, Vector3 at)
    {
        ArrayMesh? mesh = PropFactory.KitMesh(ArtAssets.Root + relative);
        if (mesh is null) return false;
        Aabb box = mesh.GetAabb();
        float scale = box.Size.Y > 0.01f ? height / box.Size.Y : 1f;
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            Scale = Vector3.One * scale,
            // Feet on the ground whatever the model's own origin.
            Position = at - new Vector3(0f, box.Position.Y * scale, 0f),
            MaterialOverlay = _overlay,
        };
        AddChild(mi);
        _bodies.Add(mi);
        return true;
    }

    private void Plinth(float radius, float height)
        => Prim(new CylinderMesh { TopRadius = radius * 0.9f, BottomRadius = radius, Height = height, RadialSegments = 10 },
                new Vector3(0f, height * 0.5f, 0f), Vector3.Zero, new Color(0.42f, 0.42f, 0.46f));

    private void Prim(Mesh mesh, Vector3 at, Vector3 rot, Color? color = null)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh, Position = at, Rotation = rot,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color ?? Tint.Darkened(0.45f), Roughness = 0.8f },
            MaterialOverlay = _overlay,
        };
        AddChild(mi);
        _bodies.Add(mi);
    }

    // ------------------------------------------------------------ state

    public void SetCharge(float charge)
    {
        Charge = Mathf.Clamp(charge, 0f, 1f);
        if (_fill is not null)
        {
            float s = Mathf.Max(0.01f, Charge);
            _fill.Scale = new Vector3(s, 1f, s);
        }
    }

    /// <summary>Show or hide the name over the shrine (the spawner decides by distance).</summary>
    public void ShowName(bool show) => _name.Visible = show && !Spent;

    /// <summary>Refused: red flash and a shake.</summary>
    public void Deny()
    {
        _flash = 1f;
        Tween t = CreateTween();
        foreach (float x in new[] { 0.08f, -0.07f, 0.05f, -0.03f, 0f })
            t.TweenProperty(_gem, "position:x", x, 0.045);
    }

    /// <summary>Success: a burst and a flare of light.</summary>
    public void Celebrate()
    {
        _burst.Restart();
        _burst.Emitting = true;
        Tween t = CreateTween();
        t.TweenProperty(_light, "light_energy", 7f, 0.12);
        t.TweenProperty(_light, "light_energy", Spent ? 0f : 1.1f, 1.2);
    }

    /// <summary>Used up: grey, no gem, no beam.</summary>
    public void MarkSpent()
    {
        if (Spent) return;
        Spent = true;
        Active = false;
        _overlay.AlbedoColor = new Color(0.1f, 0.1f, 0.12f, 0.25f);
        _overlay.BlendMode = BaseMaterial3D.BlendModeEnum.Mix;
        if (_beam is not null) _beam.Visible = false;
        if (_ringMat is not null) { _ringMat.Emission = new Color(0.3f, 0.3f, 0.35f); _ringMat.EmissionEnergyMultiplier = 0.2f; _ringMat.AlbedoColor = new Color(0.3f, 0.3f, 0.35f); }
        _name.Visible = false;
        Tween t = CreateTween();
        t.TweenProperty(_gem, "scale", Vector3.One * 0.01f, 0.4);
        t.Parallel().TweenProperty(_light, "light_energy", 0f, 1.2);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _phase += dt;
        if (Spent) return;

        _gem.Position = new Vector3(_gem.Position.X, _height + GemLift + Mathf.Sin(_phase * 2f) * 0.14f, 0f);
        _gem.RotateY(dt * (Active ? 5f : 1.5f));

        float pulse = 0.8f + 0.2f * Mathf.Sin(_phase * 3f);
        float energy = (Active ? 2.4f : 1.1f + Charge * 2.5f) * pulse;
        if (_flash > 0f)
        {
            _flash = Mathf.Max(0f, _flash - dt * 3f);
            _light.LightColor = Tint.Lerp(new Color(1f, 0.2f, 0.2f), _flash);
            energy = Mathf.Max(energy, 1.1f + 3f * _flash);
        }
        else _light.LightColor = Tint;
        _light.LightEnergy = energy;

        if (_ringMat is not null)
            _ringMat.EmissionEnergyMultiplier = 1.2f + Charge * 3f + (Charge > 0f ? 0.6f * Mathf.Sin(_phase * 10f) : 0f);
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
