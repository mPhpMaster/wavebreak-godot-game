using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>What a hidden shrine looks like and what touching it does.</summary>
public enum ShrineGift { Restore, Offering, Insight }

/// <summary>One kind of in-run secret, as data.</summary>
public sealed class SecretKindDef
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string RewardText { get; init; }
    public required ShrineGift Gift { get; init; }
    public required Color Glow { get; init; }

    /// <summary>Stat boosts for the rest of the run, granted on activation on
    /// top of the gift. A secret that only heals is forgotten a minute later;
    /// one that makes you stronger is worth the detour.</summary>
    public IReadOnlyList<CharacterStat> RunBoons { get; init; } = System.Array.Empty<CharacterStat>();

    /// <summary>A smaller boost applied at the start of every later run once this
    /// kind has ever been found -- the lasting reason to go looking.</summary>
    public IReadOnlyList<CharacterStat> LastingBoons { get; init; } = System.Array.Empty<CharacterStat>();

    /// <summary>"+12% Damage, +5% Crit Chance" for popups and the codex.</summary>
    public static string Describe(IReadOnlyList<CharacterStat> boons)
    {
        var parts = new List<string>();
        foreach (CharacterStat b in boons)
            parts.Add(Catalog.DescribeEffect(new ItemEffect { Stat = b.Stat, Op = b.Op, PerStack = b.Value }));
        return string.Join(Loc.IsArabic ? "، " : ", ", parts);
    }
}

/// <summary>
/// Places the hidden shrines.
///
///     SecretSpawner.Populate(mapRoot, rng, arenaRadius);
///
/// Map-agnostic on purpose: a map hands over its root, its own generator and
/// its radius, and gets one of each shrine kind placed in the band just inside
/// the wall — the "odd spot" nobody walks to while kiting through the middle.
/// Call it once per map; shrines are children of the given parent, so they leave
/// with the map.
///
/// The caller should pass an Rng derived from the run seed but separate from
/// the run's own stream: placing shrines must not shift which enemies spawn or
/// which cards are offered, or every seeded capture changes when this lands.
/// </summary>
public static class SecretSpawner
{
    public static readonly IReadOnlyList<SecretKindDef> Kinds = new List<SecretKindDef>
    {
        new()
        {
            Id = "tidestone", DisplayName = "Tidestone", Gift = ShrineGift.Restore,
            RewardText = "Fully healed, +100 gold", Glow = new Color(0.45f, 0.80f, 1.00f),
            RunBoons = new[]
            {
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, 25f),
                new CharacterStat(Stat.HealthRegen, ModifierOp.Flat, 6f),
                new CharacterStat(Stat.Armor, ModifierOp.Flat, 4f),
            },
            LastingBoons = new[] { new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, 8f) },
        },
        new()
        {
            Id = "hollow_idol", DisplayName = "Hollow Idol", Gift = ShrineGift.Offering,
            RewardText = "A gift from the hollow", Glow = new Color(1.00f, 0.72f, 0.30f),
            RunBoons = new[]
            {
                new CharacterStat(Stat.Damage, ModifierOp.Additive, 0.15f),
                new CharacterStat(Stat.CritChance, ModifierOp.Flat, 0.05f),
            },
            LastingBoons = new[] { new CharacterStat(Stat.Damage, ModifierOp.Additive, 0.04f) },
        },
        new()
        {
            Id = "gloam_brazier", DisplayName = "Gloam Brazier", Gift = ShrineGift.Insight,
            RewardText = "Sudden insight", Glow = new Color(0.78f, 0.52f, 1.00f),
            RunBoons = new[]
            {
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, 0.10f),
                new CharacterStat(Stat.XpGain, ModifierOp.Additive, 0.20f),
                new CharacterStat(Stat.PickupRange, ModifierOp.Flat, 1.5f),
            },
            LastingBoons = new[] { new CharacterStat(Stat.XpGain, ModifierOp.Additive, 0.05f) },
        },
    };

    private static readonly List<Shrine> Live = new();

    /// <summary>Shrines currently in the world, for tests and debug tools.</summary>
    public static IReadOnlyList<Shrine> Placed
    {
        get
        {
            Live.RemoveAll(s => !GodotObject.IsInstanceValid(s) || !s.IsInsideTree());
            return Live;
        }
    }

    public static bool AllKindsFound()
    {
        foreach (SecretKindDef k in Kinds)
            if (!Profile.HasFoundSecret(k.Id)) return false;
        return true;
    }

    /// <summary>
    /// Place one shrine of every kind around <paramref name="parent"/>'s origin.
    ///
    /// Kinds are spread a third of a circle apart (with jitter) so two are never
    /// found together, at 80–90% of the radius: inside the wall's collision,
    /// outside the scatter band most maps fill.
    /// </summary>
    public static List<Shrine> Populate(Node3D parent, Rng rng, float arenaRadius)
    {
        var placed = new List<Shrine>(Kinds.Count);
        if (parent is null || !GodotObject.IsInstanceValid(parent)) return placed;

        float start = rng.Range(0f, Mathf.Tau);
        for (int i = 0; i < Kinds.Count; i++)
        {
            float angle = start + Mathf.Tau * i / Kinds.Count + rng.Range(-0.35f, 0.35f);
            float dist = arenaRadius * rng.Range(0.80f, 0.90f);
            var shrine = new Shrine(Kinds[i])
            {
                Position = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist),
                Rotation = new Vector3(0f, rng.Range(0f, Mathf.Tau), 0f),
            };
            parent.AddChild(shrine);
            placed.Add(shrine);
            Live.Add(shrine);
        }
        return placed;
    }
}

/// <summary>
/// A hidden shrine. Nearly invisible from across the arena, it brightens as the
/// player approaches and offers a prompt when close; interacting grants a run
/// reward once and records the discovery.
///
/// "Revealed on approach" is the whole trick: from the middle of a fight it
/// reads as a faint smudge at the edge — noticeable to someone looking, missable
/// by someone kiting. That is what makes finding one feel like finding it.
/// </summary>
public sealed partial class Shrine : Node3D
{
    private const float RevealRange = 16f;
    private const float InteractRange = 3.2f;
    private const float FaintAlpha = 0.10f;

    public SecretKindDef Kind { get; }
    public bool Spent { get; private set; }

    private readonly List<StandardMaterial3D> _materials = new();
    private OmniLight3D _light = null!;
    private Label3D _prompt = null!;
    private Node3D _float = null!;
    private float _phase;
    private bool _inRange;

    public Shrine(SecretKindDef kind)
    {
        Kind = kind;
        Name = $"Shrine_{kind.Id}";
    }

    // Godot needs a parameterless constructor to instance a script type.
    public Shrine() : this(SecretSpawner.Kinds[0]) { }

    public override void _Ready()
    {
        BuildBody();

        _light = new OmniLight3D { LightColor = Kind.Glow, LightEnergy = 0f, OmniRange = 6f, Position = new Vector3(0f, 2f, 0f) };
        AddChild(_light);

        _prompt = new Label3D
        {
            Text = $"[E]  {Kind.DisplayName}",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 48,
            PixelSize = 0.006f,
            OutlineSize = 10,
            Modulate = Kind.Glow.Lightened(0.4f),
            Position = new Vector3(0f, 3.7f, 0f),
            NoDepthTest = true,
            Visible = false,
        };
        AddChild(_prompt);
    }

    /// <summary>Three silhouettes from primitives, one per kind, so a player who
    /// has found one recognises the family but not the specific shrine.</summary>
    private void BuildBody()
    {
        _float = new Node3D { Position = new Vector3(0f, 2.6f, 0f) };
        AddChild(_float);

        switch (Kind.Gift)
        {
            case ShrineGift.Restore: // a leaning standing stone with a ring above
                Part(new BoxMesh { Size = new Vector3(0.7f, 2.6f, 0.45f) }, new Vector3(0f, 1.3f, 0f), new Vector3(0f, 0f, 0.08f));
                Part(new TorusMesh { InnerRadius = 0.32f, OuterRadius = 0.42f }, Vector3.Zero, new Vector3(Mathf.Pi / 2f, 0f, 0f), _float, 1.6f);
                break;
            case ShrineGift.Offering: // squat seated figure with an empty bowl
                Part(new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.75f, Height = 0.6f }, new Vector3(0f, 0.3f, 0f), Vector3.Zero);
                Part(new CapsuleMesh { Radius = 0.42f, Height = 1.5f }, new Vector3(0f, 1.3f, 0f), Vector3.Zero);
                Part(new SphereMesh { Radius = 0.32f, Height = 0.64f }, new Vector3(0f, 2.25f, 0f), Vector3.Zero);
                Part(new SphereMesh { Radius = 0.18f, Height = 0.36f }, Vector3.Zero, Vector3.Zero, _float, 1.6f);
                break;
            default: // a bowl on a stem with a hovering flame
                Part(new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.3f, Height = 1.5f }, new Vector3(0f, 0.75f, 0f), Vector3.Zero);
                Part(new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.25f, Height = 0.4f }, new Vector3(0f, 1.7f, 0f), Vector3.Zero);
                Part(new SphereMesh { Radius = 0.3f, Height = 0.75f }, new Vector3(0f, -0.35f, 0f), Vector3.Zero, _float, 2f);
                break;
        }
    }

    private void Part(Mesh mesh, Vector3 pos, Vector3 rot, Node3D? parent = null, float glow = 0.6f)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = Kind.Glow with { A = FaintAlpha },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = Kind.Glow,
            EmissionEnergyMultiplier = glow * 0.2f,
            Roughness = 0.6f,
        };
        mat.SetMeta("glow", glow);
        _materials.Add(mat);
        (parent ?? this).AddChild(new MeshInstance3D
        {
            Mesh = mesh, Position = pos, Rotation = rot, MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public override void _Process(double delta)
    {
        _phase += (float)delta;
        _float.Position = new Vector3(0f, 2.6f + Mathf.Sin(_phase * 1.7f) * 0.15f, 0f);
        _float.RotateY((float)delta * 0.8f);

        var player = Main.Instance?.Player;
        float dist = player is not null && IsInstanceValid(player)
            ? (player.GlobalPosition - GlobalPosition) with { Y = 0f } is var d ? d.Length() : 999f
            : 999f;

        // 0 far away, 1 at arm's length. Squared so the shrine stays faint until
        // the player is clearly heading for it, then comes up quickly.
        float t = Mathf.Clamp(1f - (dist - InteractRange) / (RevealRange - InteractRange), 0f, 1f);
        t *= t;
        float alpha = Spent ? 0.35f : Mathf.Lerp(FaintAlpha, 1f, t);
        float shimmer = 0.85f + 0.15f * Mathf.Sin(_phase * 3f);
        foreach (StandardMaterial3D m in _materials)
        {
            Color c = Spent ? new Color(0.4f, 0.4f, 0.45f) : Kind.Glow;
            m.AlbedoColor = c with { A = alpha };
            float glow = (float)m.GetMeta("glow").AsDouble();
            m.EmissionEnergyMultiplier = Spent ? 0.05f : glow * Mathf.Lerp(0.2f, 2.2f, t) * shimmer;
        }
        _light.LightEnergy = Spent ? 0f : t * 2.2f * shimmer;

        _inRange = !Spent && dist <= InteractRange;
        _prompt.Visible = _inRange;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_inRange || Spent) return;
        bool pressed = (InputMap.HasAction("interact") && @event.IsActionPressed("interact"))
                       || @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.E }
                       || @event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.X };
        if (!pressed) return;
        GetViewport().SetInputAsHandled();
        Activate();
    }

    /// <summary>Grant the reward and record the discovery. Public so a test can
    /// reach it, but the normal path is the interact key above.</summary>
    public void Activate()
    {
        if (Spent) return;
        Spent = true;

        Main? main = Main.Instance;
        if (main is not null && IsInstanceValid(main))
        {
            // Boons first, so a full heal heals to the raised maximum.
            StatSheet s = main.Player.Stats;
            string before = $"hp={s.Get(Stat.MaxHealth):0} dmg=x{s.GetMultiplier(Stat.Damage):0.00} speed={s.Get(Stat.MoveSpeed):0.00}";
            foreach (CharacterStat b in Kind.RunBoons) s.Add(b.Stat, b.Op, b.Value, Kind);
            GD.Print($"META boon {Kind.Id}: {SecretKindDef.Describe(Kind.RunBoons)} | {before} -> " +
                     $"hp={s.Get(Stat.MaxHealth):0} dmg=x{s.GetMultiplier(Stat.Damage):0.00} speed={s.Get(Stat.MoveSpeed):0.00}");
            switch (Kind.Gift)
            {
                case ShrineGift.Restore:
                    main.Player.Health.HealFraction(1f);
                    main.Inventory.GrantGold(100);
                    break;
                case ShrineGift.Offering:
                    GiveItem(main);
                    break;
                case ShrineGift.Insight:
                    // About two levels' worth, measured against where the player
                    // is now, so the gift is the same size at minute one and ten.
                    main.Inventory.GrantXp(main.Inventory.XpForNextLevel * 2);
                    break;
            }
        }

        EventBus.Publish(new PopupRequested(GlobalPosition + new Vector3(0f, 3.4f, 0f),
                                            Kind.RewardText, Kind.Glow.Lightened(0.3f), 1.4f));
        if (Kind.RunBoons.Count > 0)
            EventBus.Publish(new PopupRequested(GlobalPosition + new Vector3(0f, 2.6f, 0f),
                                                SecretKindDef.Describe(Kind.RunBoons), Kind.Glow, 1.1f));
        GD.Print($"META secret {Kind.Id} found t={main?.Run?.Seconds ?? 0f:0}");
        EventBus.Publish(new SecretDiscovered(Kind.Id, GlobalPosition));
    }

    /// <summary>A random uncommon-or-better item the player can still take,
    /// drawn from a generator of its own so the gift does not disturb the run's
    /// seeded stream.</summary>
    private void GiveItem(Main main)
    {
        var pool = new List<ItemDef>();
        foreach (ItemDef def in MetaContent.OfferItems(Catalog.Items))
            if (def.Rarity >= Rarity.Uncommon && !main.Inventory.IsMaxed(def)) pool.Add(def);
        if (pool.Count == 0) { main.Inventory.GrantGold(150); return; }

        var rng = new Rng(main.Run.Seed ^ (ulong)(main.Run.Seconds * 1000f) ^ 0x1D01UL);
        main.Inventory.Add(rng.Pick(pool));
    }
}
