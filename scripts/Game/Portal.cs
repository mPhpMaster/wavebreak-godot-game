using Godot;

namespace Wavebreak.Game;

/// <summary>
/// The gateway to the next map: a standing ring, a glowing floor disc and a
/// beam into the sky.
///
/// The beam is the important part. The portal opens at the arena's centre and
/// the player is usually somewhere else, fighting; a ring at head height is
/// invisible past the first mound, but a column of light reads from anywhere on
/// the map and over the HUD's off-screen arrow says the same thing twice.
///
/// It is only a visual. Whether the player has walked in is decided by
/// <see cref="StageDirector"/> with a distance check, the same way contact
/// damage is: no Area3D, no collision layer to keep in sync, and nothing to go
/// wrong when the arena under it is rebuilt.
/// </summary>
public sealed partial class Portal : Node3D
{
    /// <summary>Horizontal distance from the centre that counts as "in".
    /// A little wider than the drawn disc so a player sliding past at speed
    /// still goes through, rather than overshooting by a frame.</summary>
    public const float EnterRadius = 3.0f;

    private const float RingRadius = 2.4f;
    private const float OpenSeconds = 1.1f;

    private static readonly Color Core = new(0.55f, 1.00f, 0.92f);
    private static readonly Color Rim = new(0.95f, 0.85f, 1.00f);

    private Node3D _ring = null!;
    private MeshInstance3D _disc = null!;
    private MeshInstance3D _beam = null!;
    private OmniLight3D _light = null!;
    private float _age;
    private Label3D _timer = null!;
    private int _shownSeconds = -1;

    /// <summary>Seconds until this portal pulls everyone through, shown over
    /// the ring; null (the rest room's CONTINUE portal) shows nothing.</summary>
    public float? Countdown { get; set; }

    /// <summary>True once the opening animation has finished. Entry is refused
    /// before that, so the player cannot be yanked away the instant the boss
    /// dies, before they have even seen the portal appear.</summary>
    public bool IsOpen => _age >= OpenSeconds;

    public override void _Ready()
    {
        Name = "Portal";

        _ring = new Node3D { Name = "Ring", Position = new Vector3(0f, RingRadius + 0.3f, 0f) };
        AddChild(_ring);
        // Torus lies flat by default; stand it up so it reads as a doorway.
        _ring.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = RingRadius - 0.22f, OuterRadius = RingRadius + 0.22f, RingSegments = 48, Rings = 12 },
            Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f),
            MaterialOverride = Glow(Rim, 3f, additive: false),
        });
        // The membrane inside the ring: translucent so the map behind shows
        // through, which keeps the portal from hiding an enemy standing behind it.
        _ring.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = RingRadius - 0.1f, BottomRadius = RingRadius - 0.1f, Height = 0.05f, RadialSegments = 40 },
            Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f),
            MaterialOverride = Glow(Core with { A = 0.35f }, 2f, additive: true),
        });

        _disc = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = EnterRadius, BottomRadius = EnterRadius, Height = 0.04f, RadialSegments = 40 },
            Position = new Vector3(0f, 0.05f, 0f),
            MaterialOverride = Glow(Core with { A = 0.5f }, 2.5f, additive: true),
        };
        AddChild(_disc);

        _beam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 1.1f, Height = 70f, RadialSegments = 16, CapTop = false, CapBottom = false },
            Position = new Vector3(0f, 35f, 0f),
            MaterialOverride = Glow(Core with { A = 0.22f }, 2f, additive: true),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_beam);

        _light = new OmniLight3D { LightColor = Core, LightEnergy = 3f, OmniRange = 12f, Position = new Vector3(0f, 2.5f, 0f) };
        AddChild(_light);

        _timer = new Label3D
        {
            Name = "Countdown",
            FontSize = 64,
            PixelSize = 0.024f,
            OutlineSize = 14,
            Modulate = Rim,
            OutlineModulate = new Color(0.08f, 0.10f, 0.22f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 3,
            Position = new Vector3(0f, RingRadius * 2f + 1.3f, 0f),
            Visible = false,
        };
        AddChild(_timer);

        Scale = Vector3.One * 0.01f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;

        // Opens with an overshoot: growing past full size and settling reads as
        // something arriving, where a linear scale-up reads as a loading bar.
        float t = Mathf.Clamp(_age / OpenSeconds, 0f, 1f);
        float s = t + Mathf.Sin(t * Mathf.Pi) * 0.4f;
        Scale = Vector3.One * Mathf.Max(0.01f, s);

        _ring.Rotation = new Vector3(0f, _age * 0.9f, 0f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_age * 4f);
        _light.LightEnergy = 2.5f + pulse * 1.5f;
        _disc.Scale = new Vector3(1f + pulse * 0.06f, 1f, 1f + pulse * 0.06f);

        _timer.Visible = Countdown.HasValue;
        if (Countdown is float left)
        {
            int secs = Mathf.CeilToInt(left);
            if (secs != _shownSeconds)
            {
                _shownSeconds = secs;
                _timer.Text = Meta.Loc.F("CLOSES IN {0}", secs);
            }
            // The last ten seconds turn warm and pulse.
            _timer.Modulate = left <= 10f ? new Color(1f, 0.55f, 0.45f).Lerp(Rim, pulse * 0.5f) : Rim;
        }
    }

    /// <summary>True when <paramref name="p"/> is standing in the portal.</summary>
    public bool Contains(Vector3 p)
    {
        Vector3 d = p - GlobalPosition;
        return IsOpen && d.Y > -2f && d.Y < 6f && new Vector2(d.X, d.Z).LengthSquared() <= EnterRadius * EnterRadius;
    }

    private static StandardMaterial3D Glow(Color color, float energy, bool additive)
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = energy,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        if (color.A < 1f) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        if (additive) m.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
        return m;
    }
}
