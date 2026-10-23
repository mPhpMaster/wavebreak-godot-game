using Godot;
using Wavebreak.Core;

namespace Wavebreak.Actors;

/// <summary>
/// A chest in the world: a box, a lid, a tier colour and a floating marker.
/// The box is the Kenney mini-dungeon model when it is imported, primitives
/// otherwise (see <see cref="BuildModel"/>).
///
/// It is deliberately passive. It does not know what it costs, what it pays or
/// who is near it — <see cref="Game.ChestSpawner"/> decides all of that and only
/// tells the chest to <see cref="Open"/> or <see cref="Deny"/>. Keeping the
/// price and the roll out of here means a map can place chests without caring
/// how the economy works, and the economy can change without touching a mesh.
///
/// Three choices about how it looks, all for the third-person camera ~10 m back:
///
/// <b>The marker is the thing you see, not the box.</b> A 1 m crate is a dozen
/// pixels at the far side of the screen and the same muted brown as half the
/// scatter. The bobbing diamond above it is in the tier colour and emissive, so
/// "there is a chest over there" reads before the chest itself does.
///
/// <b>Colour is the tier.</b> Standard chests wear the rare blue, gilded ones the
/// legendary amber — the same colours the item cards use, so the promise is
/// legible: amber means "this rolls high". Free chests add a light column, because
/// "free" is the one fact that should pull the player across the arena.
///
/// <b>No collision.</b> A chest is found by distance like a pickup. A solid one
/// would snag the player mid-dash and give the swarm a pillar to bunch behind,
/// and it would have to be kept out of the crowd's way for no gameplay gain.
/// </summary>
public sealed partial class Chest : Node3D
{
    /// <summary>Footprint, for placement. Half-extents in metres.</summary>
    public static readonly Vector3 HalfExtents = new Vector3(0.58f, 0.6f, 0.42f) * VisualScale;

    /// <summary>Uniform scale of the whole model. The parts are authored at a
    /// believable 1.1 m; at the camera's distance that is small next to the
    /// player capsule, so the model is drawn a quarter larger.</summary>
    private const float VisualScale = 1.25f;

    private const float BodyHeight = 0.62f;

    private const string ModelPath = "res://assets/kenney/mini-dungeon/chest.glb";
    private const float ModelScale = 1.9f;
    private const float ModelBodyHeight = 0.2f;
    private const float MarkerHeight = 1.75f;

    private static readonly Color Wood = new(0.33f, 0.22f, 0.14f);
    private static readonly Color DeniedColor = new(1.0f, 0.22f, 0.22f);

    // Built once per tier and shared: chests are never recoloured individually,
    // and the denied flash animates the light, not the material.
    private static readonly StandardMaterial3D WoodMaterial = new()
    {
        AlbedoColor = Wood,
        Roughness = 0.85f,
    };

    public ChestTier Tier { get; private set; }

    /// <summary>Opens for nothing. Elite and boss drops.</summary>
    public bool IsFree { get; private set; }

    /// <summary>True from the moment <see cref="Open"/> is called; an open chest
    /// cannot be paid for twice even while its lid is still rising.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Run time it was placed, so the spawner can tell world chests from
    /// fresh drops and prefer culling the oldest.</summary>
    public float SpawnedAt { get; set; }

    /// <summary>Where prompts and reward text are anchored.</summary>
    public Vector3 Top => GlobalPosition + Vector3.Up * (_bodyHeight * VisualScale + 0.6f);

    public Color Tint => TierColor(Tier);

    private Node3D _lid = null!;
    private Node3D _marker = null!;
    private OmniLight3D _light = null!;
    private CpuParticles3D _burst = null!;
    private MeshInstance3D? _beam;
    private Label3D? _rewardLabel;

    private float _phase;
    private float _baseEnergy;
    private float _denyFlash;
    private float _lidClosed;
    private float _bodyHeight = BodyHeight;

    /// <summary>Godot builds script nodes through a parameterless constructor
    /// (hot reload, inspector). Use <see cref="Configure"/> after it.</summary>
    public Chest() { }

    public Chest(ChestTier tier, bool free) => Configure(tier, free);

    public void Configure(ChestTier tier, bool free)
    {
        Tier = tier;
        IsFree = free;
    }

    /// <summary>The colour a tier is drawn in, taken from the rarity palette so
    /// the chest promises what the item cards later deliver.</summary>
    public static Color TierColor(ChestTier tier) => tier == ChestTier.Gilded
        ? Data.Catalog.RarityColor(Data.Rarity.Legendary)
        : Data.Catalog.RarityColor(Data.Rarity.Rare);

    public override void _Ready()
    {
        Name = IsFree ? "FreeChest" : Tier == ChestTier.Gilded ? "GildedChest" : "Chest";
        Color tint = Tint;

        if (!BuildModel(tint)) BuildPrimitive(tint);

        // Marker: a diamond floating above, the long-range tell.
        _marker = new Node3D { Name = "Marker", Position = new Vector3(0f, MarkerHeight, 0f) };
        AddChild(_marker);
        float gem = IsFree ? 0.3f : Tier == ChestTier.Gilded ? 0.26f : 0.2f;
        AddMesh(new SphereMesh { Radius = gem, Height = gem * 2.6f, RadialSegments = 4, Rings = 2 },
                Vector3.Zero, Glow(tint, 2.4f), _marker);

        _baseEnergy = IsFree ? 1.6f : 0.9f;
        _light = new OmniLight3D
        {
            Position = new Vector3(0f, 1.0f, 0f),
            LightColor = tint,
            LightEnergy = _baseEnergy,
            OmniRange = 4.5f,
            ShadowEnabled = false,
        };
        AddChild(_light);

        if (IsFree)
        {
            // A soft column of light, visible from across the arena. Unshaded and
            // additive so it reads as light rather than as a solid pole.
            var beamMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(tint, 0.22f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            _beam = AddMesh(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.35f, Height = 9f, RadialSegments = 12 },
                            new Vector3(0f, 4.5f, 0f), beamMat);
            _beam.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        _burst = new CpuParticles3D
        {
            Name = "Burst",
            Position = new Vector3(0f, _bodyHeight, 0f),
            Emitting = false,
            OneShot = true,
            Amount = Tier == ChestTier.Gilded || IsFree ? 72 : 48,
            Lifetime = 1.1f,
            Explosiveness = 0.92f,
            Direction = Vector3.Up,
            Spread = 38f,
            InitialVelocityMin = 3.5f,
            InitialVelocityMax = 8.5f,
            Gravity = new Vector3(0f, -9f, 0f),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.2f,
            Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.09f, 0.09f) },
            MaterialOverride = Glow(tint, 3.5f),
        };
        AddChild(_burst);

        // Different chests should not bob in lockstep; a row of synchronised
        // markers looks like one object.
        Scale = Vector3.One * VisualScale;
        _phase = Mathf.Abs(GetInstanceId().GetHashCode() % 628) / 100f;
    }

    /// <summary>
    /// The Kenney mini-dungeon chest (CC0, see assets/CREDITS.md): it has its lid
    /// as a separate node already hinged at the back, which is the one thing the
    /// opening needs. Tier colour goes on as an additive overlay over its own
    /// texture rather than a recolour, so it still reads as wood and iron.
    /// Returns false when the asset is not imported, and the primitive chest is
    /// built instead — a missing file must not make chests disappear.
    /// </summary>
    private bool BuildModel(Color tint)
    {
        if (!ResourceLoader.Exists(ModelPath)) return false;
        if (ResourceLoader.Load<PackedScene>(ModelPath)?.Instantiate() is not Node3D model) return false;
        if (model.FindChild("lid", true, false) is not Node3D lid)
        {
            model.Free();
            return false;
        }

        // Authored 0.5 m wide; this scale puts it at ~1.2 m, a little wider than
        // the player, with the lid's hinge where the importer left it.
        model.Scale = Vector3.One * ModelScale;
        AddChild(model);
        _lid = lid;
        _lidClosed = lid.Rotation.X;
        _bodyHeight = ModelBodyHeight * ModelScale;

        var overlay = new StandardMaterial3D
        {
            AlbedoColor = new Color(tint, IsFree ? 0.3f : 0.14f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(model);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n is MeshInstance3D mi) mi.MaterialOverlay = overlay;
            foreach (Node c in n.GetChildren()) stack.Push(c);
        }

        // Glowing contents just above the body's top, under the closed lid.
        AddMesh(new BoxMesh { Size = new Vector3(0.44f, 0.01f, 0.44f) * ModelScale },
                new Vector3(0f, _bodyHeight + 0.01f, 0f), Glow(tint, 4f));
        return true;
    }

    /// <summary>The fallback: a box chest from primitives, with tinted bands.</summary>
    private void BuildPrimitive(Color tint)
    {
        var trim = Glow(tint, IsFree ? 1.6f : 0.9f);
        _bodyHeight = BodyHeight;

        // Base: a wooden box with two tinted bands and a lock plate facing +Z.
        AddMesh(new BoxMesh { Size = new Vector3(1.1f, BodyHeight, 0.78f) },
                new Vector3(0f, BodyHeight * 0.5f, 0f), WoodMaterial);
        foreach (float x in new[] { -0.36f, 0.36f })
            AddMesh(new BoxMesh { Size = new Vector3(0.1f, BodyHeight + 0.02f, 0.8f) },
                    new Vector3(x, BodyHeight * 0.5f, 0f), trim);
        AddMesh(new BoxMesh { Size = new Vector3(0.2f, 0.22f, 0.04f) },
                new Vector3(0f, BodyHeight - 0.14f, 0.4f), trim);

        // The glowing contents: a plate just under the rim, hidden by the lid
        // until it opens. This is what makes the opening read as "light pours out".
        AddMesh(new BoxMesh { Size = new Vector3(0.96f, 0.02f, 0.64f) },
                new Vector3(0f, BodyHeight - 0.03f, 0f), Glow(tint, 4f));

        // Lid on a hinge along the back edge, so rotating the pivot swings it
        // open rather than spinning it about its own middle.
        _lid = new Node3D { Name = "Lid", Position = new Vector3(0f, BodyHeight, -0.39f) };
        AddChild(_lid);
        _lidClosed = 0f;
        AddMesh(new BoxMesh { Size = new Vector3(1.14f, 0.24f, 0.82f) },
                new Vector3(0f, 0.12f, 0.41f), WoodMaterial, _lid);
        foreach (float x in new[] { -0.36f, 0.36f })
            AddMesh(new BoxMesh { Size = new Vector3(0.1f, 0.26f, 0.84f) },
                    new Vector3(x, 0.12f, 0.41f), trim, _lid);
    }

    /// <summary>
    /// Bob and pulse. Each chest runs its own <c>_Process</c>, unlike pickups:
    /// there are never more than a dozen, they touch nothing but their own
    /// transforms, and their order relative to combat is irrelevant — so the
    /// reason <see cref="Game.Main"/> centralises ticking does not apply.
    /// </summary>
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _phase += dt;

        if (!IsOpen)
        {
            _marker.Position = new Vector3(0f, MarkerHeight + Mathf.Sin(_phase * 2.2f) * 0.12f, 0f);
            _marker.RotateY(dt * 1.6f);
            float pulse = 0.8f + 0.2f * Mathf.Sin(_phase * 3f);
            _light.LightEnergy = _baseEnergy * pulse;
        }

        if (_denyFlash > 0f)
        {
            _denyFlash = Mathf.Max(0f, _denyFlash - dt * 3f);
            _light.LightColor = Tint.Lerp(DeniedColor, _denyFlash);
            _light.LightEnergy = Mathf.Max(_light.LightEnergy, _baseEnergy + 2.5f * _denyFlash);
        }
    }

    /// <summary>
    /// Plays the opening: lid swings, light floods out, a burst of sparks, and the
    /// reward's rarity hangs over the chest in its colour, under the item name the
    /// spawner pops through <see cref="PopupRequested"/> — the popup is brief, the
    /// rarity tag stays until the chest goes, so a glance back still tells you
    /// what that light was. Then the chest
    /// sinks away on its own. Gameplay is already settled by the time this runs —
    /// the reward is granted by the caller — so the animation can never be the
    /// reason something was or was not received.
    /// </summary>
    public void Open(string rarityText, Color rewardColor, UI.IconSpec? icon = null)
    {
        if (IsOpen) return;
        IsOpen = true;

        _burst.Emitting = true;
        _light.LightColor = Tint.Lerp(Colors.White, 0.35f);

        Tween t = CreateTween();
        t.SetParallel();
        t.TweenProperty(_lid, "rotation:x", _lidClosed - Mathf.DegToRad(112f), 0.38)
         .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.TweenProperty(_marker, "scale", Vector3.One * 0.01f, 0.2);
        t.TweenProperty(_light, "light_energy", 9f, 0.12);
        t.TweenProperty(_light, "omni_range", 7f, 0.12);
        t.Chain().TweenProperty(_light, "light_energy", 0.4f, 1.4);
        if (_beam is not null)
            t.TweenProperty(_beam, "scale", new Vector3(0.01f, 1f, 0.01f), 0.6);

        _rewardLabel = new Label3D
        {
            Text = rarityText,
            Modulate = rewardColor,
            OutlineModulate = new Color(0.03f, 0.03f, 0.05f),
            OutlineSize = 14,
            FontSize = 64,
            PixelSize = 0.005f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            RenderPriority = 3,
            OutlineRenderPriority = 2,
            Position = new Vector3(0f, _bodyHeight + 0.6f, 0f),
            Scale = Vector3.One * 0.3f,
        };
        AddChild(_rewardLabel);

        // The reward's picture rides above its rarity, so the same glyph the
        // level-up cards and the HUD use tells the player what came out.
        Sprite3D? picture = null;
        if (icon is { } spec && UI.Icons.Texture(spec.Glyph) is { } tex)
        {
            _rewardLabel.AddChild(picture = new Sprite3D
            {
                Texture = tex,
                Modulate = spec.Color,
                PixelSize = 0.0062f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                Shaded = false,
                RenderPriority = 3,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
                Position = new Vector3(0f, 0.68f, 0f),
            });
        }

        Tween label = CreateTween();
        label.TweenProperty(_rewardLabel, "scale", Vector3.One, 0.25)
             .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        label.Parallel().TweenProperty(_rewardLabel, "position:y", _bodyHeight + 1.2f, 0.6)
             .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        label.TweenInterval(1.6);
        label.TweenProperty(_rewardLabel, "modulate:a", 0f, 0.5);
        // 3D nodes do not inherit modulate, so the picture fades on its own.
        if (picture is not null) label.Parallel().TweenProperty(picture, "modulate:a", 0f, 0.5);

        // Linger long enough to be seen open, then sink into the floor and go.
        Tween exit = CreateTween();
        exit.TweenInterval(2.8);
        exit.TweenProperty(this, "scale", new Vector3(VisualScale, 0.02f, VisualScale), 0.45)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        exit.TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>Not enough gold: a short shake and a red flash of the light.
    /// Feedback lives on the chest because that is where the player is looking.</summary>
    public void Deny()
    {
        if (IsOpen) return;
        _denyFlash = 1f;

        // The lid rattles on its hinge: locked, not broken.
        Tween t = CreateTween();
        foreach (float z in new[] { 0.09f, -0.08f, 0.06f, -0.04f, 0f })
            t.TweenProperty(_lid, "rotation:z", z, 0.045);
    }

    private MeshInstance3D AddMesh(Mesh mesh, Vector3 at, Material mat, Node3D? parent = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = mat };
        (parent ?? this).AddChild(mi);
        return mi;
    }

    private static StandardMaterial3D Glow(Color color, float energy) => new()
    {
        AlbedoColor = color,
        Roughness = 0.4f,
        Metallic = 0.3f,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
    };
}
