using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.UI;

/// <summary>
/// What to draw for one piece of content: a main glyph and up to three small
/// extras. Glyphs are names of white silhouettes in <c>assets/icons/</c>; the
/// colours tint them at draw time.
///
///   <see cref="Overlay"/> sits on top of the main glyph (a tome's stat on its cover);
///   <see cref="Badge"/> sits in a small disc at the bottom right (a weapon's element,
///   an item's second stat or its trigger);
///   <see cref="Corner"/> sits in a disc at the top right (the "upgrade" arrow).
/// </summary>
public readonly record struct IconSpec(
    string Glyph, Color Color,
    string? Badge = null, Color BadgeColor = default,
    string? Overlay = null, Color OverlayColor = default,
    string? Corner = null, Color CornerColor = default)
{
    public IconSpec WithBadge(string? glyph, Color color) => this with { Badge = glyph, BadgeColor = color };
    public IconSpec WithCorner(string? glyph, Color color) => this with { Corner = glyph, CornerColor = color };
}

/// <summary>
/// Maps every weapon, item, tome and supply to an <see cref="IconSpec"/>.
///
/// The rule is "similar things look similar, different things look different":
///
///   * Weapons: a glyph for how the weapon attacks (shape, or its own behaviour
///     for the expansion weapons), coloured by element, with the element's own
///     glyph as a badge. Two fire weapons share a colour; a fire aura and a fire
///     bomb still look nothing alike.
///   * Items: the glyph of the stat they mostly move (sword = damage, boot = move
///     speed, heart = health, shield = armour, clover = luck, magnet = pickup,
///     coin = gold ...), coloured by that stat, with the next stat as a badge.
///     Items whose real content is a rule rather than a number (procs, on-kill,
///     when-hit) are in <see cref="ItemOverrides"/>.
///   * Tomes: a book coloured like its stat with the stat's glyph on the cover,
///     so a Tome of Edges is visibly "the damage book".
///
/// Nothing resolves to blank: an unknown id falls back to its shape or primary
/// stat, and a missing texture falls back to <see cref="Fallback"/>.
/// <c>ContentAudit.Icons</c> checks every id resolves to a real texture.
/// </summary>
public static class Icons
{
    public const string Dir = "res://assets/icons/";
    public const string Fallback = "question";

    private static readonly Dictionary<string, Texture2D?> Cache = new();

    // --------------------------------------------------------------- textures

    public static string PathOf(string glyph) => Dir + glyph + ".png";

    /// <summary>True when the glyph has an imported texture.</summary>
    public static bool Exists(string? glyph)
        => !string.IsNullOrEmpty(glyph) && ResourceLoader.Exists(PathOf(glyph));

    /// <summary>The glyph's texture, or the fallback's. Never null unless the
    /// icon folder itself is missing.</summary>
    public static Texture2D? Texture(string? glyph)
    {
        glyph ??= Fallback;
        if (Cache.TryGetValue(glyph, out Texture2D? cached)) return cached;
        Texture2D? tex = Exists(glyph) ? GD.Load<Texture2D>(PathOf(glyph)) : null;
        if (tex is null && glyph != Fallback) tex = Texture(Fallback);
        Cache[glyph] = tex;
        return tex;
    }

    // ----------------------------------------------------------------- colours

    public static Color ElementColor(Element e) => e switch
    {
        Element.Fire      => new Color(1.00f, 0.56f, 0.26f),
        Element.Ice       => new Color(0.56f, 0.88f, 1.00f),
        Element.Lightning => new Color(1.00f, 0.92f, 0.36f),
        Element.Poison    => new Color(0.56f, 0.95f, 0.40f),
        _                 => new Color(0.86f, 0.88f, 0.94f),
    };

    public static string? ElementGlyph(Element e) => e switch
    {
        Element.Fire      => "flame",
        Element.Ice       => "snowflake",
        Element.Lightning => "bolt",
        Element.Poison    => "drop",
        _                 => null,
    };

    /// <summary>Glyph and colour for one stat. Every <see cref="Stat"/> member
    /// has a row; the default arm is only there for members added later.</summary>
    public static (string Glyph, Color Color) ForStat(Stat stat) => stat switch
    {
        Stat.MaxHealth       => ("heart", new Color(1.00f, 0.42f, 0.46f)),
        Stat.HealthRegen     => ("regen", new Color(0.52f, 0.95f, 0.58f)),
        Stat.Armor           => ("shield", new Color(0.66f, 0.78f, 0.96f)),
        Stat.Evasion         => ("feather", new Color(0.86f, 0.92f, 1.00f)),
        Stat.Shield          => ("bubble", new Color(0.44f, 0.86f, 1.00f)),
        Stat.Thorns          => ("spikes", new Color(0.90f, 0.56f, 0.96f)),
        Stat.Lifesteal       => ("fang", new Color(1.00f, 0.34f, 0.40f)),

        Stat.Damage          => ("sword", new Color(1.00f, 0.58f, 0.36f)),
        Stat.AttackSpeed     => ("fastforward", new Color(1.00f, 0.86f, 0.36f)),
        Stat.CritChance      => ("target", new Color(1.00f, 0.44f, 0.40f)),
        Stat.CritDamage      => ("burst", new Color(1.00f, 0.64f, 0.28f)),
        Stat.AreaSize        => ("expand", new Color(0.62f, 0.86f, 1.00f)),
        Stat.ProjectileCount => ("multi", new Color(0.98f, 0.82f, 0.52f)),
        Stat.ProjectileSpeed => ("comet", new Color(0.72f, 0.92f, 1.00f)),
        Stat.Knockback       => ("anchor", new Color(0.78f, 0.80f, 0.90f)),

        Stat.MoveSpeed       => ("boot", new Color(0.40f, 0.95f, 0.84f)),
        Stat.JumpHeight      => ("spring", new Color(0.62f, 0.94f, 1.00f)),
        Stat.DashCooldown    => ("dash", new Color(0.50f, 0.90f, 0.96f)),

        Stat.PickupRange     => ("magnet", new Color(1.00f, 0.48f, 0.42f)),
        Stat.XpGain          => ("star", new Color(0.52f, 0.86f, 1.00f)),
        Stat.GoldGain        => ("coin", new Color(1.00f, 0.82f, 0.30f)),
        Stat.Luck            => ("clover", new Color(0.46f, 0.92f, 0.42f)),

        Stat.Rerolls         => ("dice", new Color(0.92f, 0.92f, 0.98f)),
        Stat.Banishes        => ("lock", new Color(1.00f, 0.56f, 0.50f)),
        Stat.Skips           => ("next", new Color(0.86f, 0.90f, 0.98f)),
        Stat.EnemyScaling    => ("skull", new Color(0.86f, 0.44f, 0.94f)),

        Stat.ElementalDamage => ("gem", new Color(0.78f, 0.62f, 1.00f)),
        Stat.StatusChance    => ("gem", new Color(0.66f, 0.80f, 1.00f)),
        Stat.StatusDuration  => ("hourglass", new Color(0.78f, 0.62f, 1.00f)),
        Stat.MegacritChance  => ("spark", new Color(1.00f, 0.90f, 0.42f)),
        Stat.IgniteChance    => ("flame", ElementColor(Element.Fire)),
        Stat.ChillChance     => ("snowflake", ElementColor(Element.Ice)),
        Stat.ShockChance     => ("bolt", ElementColor(Element.Lightning)),
        Stat.PoisonChance    => ("drop", ElementColor(Element.Poison)),
        Stat.FreezePower     => ("snowflake", new Color(0.70f, 0.82f, 1.00f)),
        Stat.UpgradeStats    => ("wrench", new Color(0.88f, 0.88f, 0.94f)),
        Stat.Duration        => ("hourglass", new Color(0.94f, 0.82f, 0.60f)),
        _                    => (Fallback, Colors.White),
    };

    // ----------------------------------------------------------------- weapons

    /// <summary>Weapons whose behaviour is not their shape's default, or that
    /// deserve their own picture. Everything else uses <see cref="ShapeGlyph"/>.</summary>
    private static readonly Dictionary<string, string> WeaponOverrides = new()
    {
        ["splinter_rail"] = "dart",
        ["sparkfan"] = "multi",
        ["riptide_blades"] = "swords",
        ["bloomcap_spores"] = "mushroom",
        ["judgement_lance"] = "lance",
        ["glasswire_needles"] = "needles",
        ["forgefall_maul"] = "hammer",
        ["hailstone_cluster"] = "cluster",
        ["searing_longshot"] = "bow",
        ["tempest_spindle"] = "vortex",
        ["gullwing_chakram"] = "chakram",
        ["circling_shoal"] = "orbit",
        ["brine_wake"] = "drop",
        ["forked_squall"] = "bolt",
        ["lighthouse_beam"] = "beam",
        ["barnacle_mines"] = "mine",
        ["driftwood_sentry"] = "tower",
        ["maelstrom_seed"] = "vortex",
        ["skimstone_sling"] = "sling",
        ["sleetbreath_horn"] = "horn",
        ["thunderhead_censer"] = "storm",
        ["breaker_swell"] = "wave",
        ["undertow_chain"] = "chain",
        ["lantern_moths"] = "lantern",
        ["quarry_bell"] = "bell",
        // signature weapons (Data/Catalog.Heroes.cs)
        ["spyglass_glint"] = "spyglass",
        ["mooring_anchor"] = "mooring",
        ["skimmer_kite"] = "kite",
    };

    public static string ShapeGlyph(AttackShape shape) => shape switch
    {
        AttackShape.Projectile => "dart",
        AttackShape.Melee      => "sword",
        AttackShape.Aura       => "aura",
        AttackShape.Explosive  => "bomb",
        AttackShape.Homing     => "homing",
        _                      => Fallback,
    };

    public static string WeaponGlyph(WeaponDef def)
        => WeaponOverrides.TryGetValue(def.Id, out string? g) ? g : ShapeGlyph(def.Shape);

    public static IconSpec For(WeaponDef def)
    {
        // Element colour, pulled toward the weapon's own tint so two weapons of
        // one element still differ in hue.
        Color c = Readable(ElementColor(def.Element).Lerp(def.Tint, 0.4f));
        string glyph = WeaponGlyph(def);
        string? badge = ElementGlyph(def.Element);
        // A bolt-shaped lightning weapon does not need a bolt badge as well.
        if (badge == glyph) badge = null;
        return new IconSpec(glyph, c, badge, c);
    }

    // ------------------------------------------------------------------- items

    /// <summary>Items whose picture is not their primary stat: rules (procs,
    /// on-kill, when-hit, movement triggers) and unique legendaries. The badge
    /// says what triggers it: skull = on kill, broken heart = when hit, sword =
    /// on hit.</summary>
    private static readonly Dictionary<string, (string Glyph, string? Badge)> ItemOverrides = new()
    {
        // on hit
        ["sparking_limpet"] = ("bolt", "sword"),
        ["riptooth_charm"] = ("fang", "sword"),
        ["brine_bomb_locket"] = ("burst", "sword"),
        ["shatterglass_coral"] = ("burst", "target"),
        // on kill
        ["wraith_lantern"] = ("lantern", "skull"),
        ["scrimshaw_tally"] = ("sword", "skull"),
        ["powder_keg"] = ("bomb", "skull"),
        ["gilded_scale"] = ("coin", "skull"),
        ["pyre_totem"] = ("campfire", "skull"),
        // when hit
        ["urchin_shell"] = ("spikes", "heart_broken"),
        ["ink_sac"] = ("cloud", "heart_broken"),
        ["wrathful_conch"] = ("sword", "heart_broken"),
        ["lifebuoy"] = ("buoy", "heart"),
        // movement
        ["gale_sail"] = ("spring", "sword"),
        ["slickfin_greaves"] = ("boot", "sword"),
        ["wavecrest_fins"] = ("wave", "sword"),
        ["spindrift_cape"] = ("dash", "fastforward"),
        ["plunge_weights"] = ("anchor", "burst"),
        ["anchored_stance"] = ("anchor", "shield"),
        // interact / gold
        ["diving_bell"] = ("bell", "heart"),
        ["votive_candle"] = ("flame", "coin"),
        ["misers_anchor"] = ("coin", "sword"),
        ["coin_divers_glove"] = ("coin", "heart"),
        // unique pictures
        ["eye_of_the_maelstrom"] = ("storm", "skull"),
        ["mariners_last_wish"] = ("award", "heart"),
        ["tithe_of_the_deep"] = ("coin", "star"),
        ["crown_of_ruin"] = ("crown", "skull"),
        ["drowned_crown"] = ("crown", "clover"),
        ["heart_of_the_storm"] = ("storm", "multi"),
        ["abyssal_metronome"] = ("hourglass", "fastforward"),
        ["leviathan_hide"] = ("spikes", "skull"),
        ["eye_of_the_gyre"] = ("vortex", "gem"),
        ["drowned_moon"] = ("moon", "spark"),
        ["shipwrights_plane"] = ("wrench", "clover"),
        ["overclocked_heart"] = ("heart", "fastforward"),
        ["fathom_engine"] = ("gear", "multi"),
        ["blood_pact"] = ("heart_broken", "fang"),
        ["kraken_ink"] = ("cloud", "sword"),
        ["siege_battery"] = ("tower", "sword"),
        ["phase_cloak"] = ("feather", "bubble"),
        ["deep_lantern"] = ("lantern", "clover"),
        ["tidepool_lantern"] = ("lantern", "expand"),
        ["squall_lantern"] = ("lantern", "bolt"),
        ["wreckers_lantern"] = ("lantern", "multi"),
        ["scavengers_pouch"] = ("pouch", "magnet"),
        ["packrats_satchel"] = ("pouch", "star"),
        ["students_ledger"] = ("tome", "star"),
        ["bounty_ledger"] = ("tome", "coin"),
        ["field_rations"] = ("potion", "heart"),
        ["stillwater_flask"] = ("potion", "regen"),
        ["tidewater_flask"] = ("potion", "hourglass"),
        ["oiled_gears"] = ("gear", "fastforward"),
        ["tarnished_compass"] = ("target", "clover"),
        ["gamblers_coin"] = ("dice", "target"),
        // separated from a look-alike (ContentAudit fails on two equal pictures)
        ["whetstone"] = ("sword", "spark"),
        ["windborne_kite"] = ("cloud", "dash"),
        ["duelists_edge"] = ("swords", "target"),
        ["unbruised_feather"] = ("feather", "shield"),
        ["lamprey_ring"] = ("buoy", "fang"),
        ["twin_tide_quiver"] = ("bow", "multi"),
        ["barnacle_crust"] = ("mine", "shield"),
        ["coral_spur"] = ("spikes", "sword"),
        ["gullbone_whistle"] = ("horn", "spring"),
        ["headsmans_oath"] = ("skull", "burst"),
        ["driftglass_monocle"] = ("gem", "burst"),
        ["moonwell_drop"] = ("drop", "regen"),
        ["resonant_fork"] = ("bell", "fastforward"),
        ["salt_rusted_keyring"] = ("lock", "coin"),
        ["gilded_tooth"] = ("fang", "coin"),
    };

    /// <summary>The effect an item is "about": the largest-looking positive
    /// effect, so a drawback ("-5% move speed") never becomes the picture.</summary>
    private static ItemEffect? Primary(ItemDef def, ItemEffect? skip = null)
    {
        if (def.Effects is null) return null;
        foreach (ItemEffect e in def.Effects)
        {
            if (e is null || ReferenceEquals(e, skip)) continue;
            if (skip is not null && e.Stat == skip.Stat) continue;
            bool good = e.Op == ModifierOp.Multiplicative ? e.PerStack > 1f : e.PerStack > 0f;
            // Lower is better for a dash cooldown and for enemy scaling is "bad".
            if (e.Stat == Stat.DashCooldown) good = e.Op == ModifierOp.Multiplicative ? e.PerStack < 1f : e.PerStack < 0f;
            if (e.Stat == Stat.EnemyScaling) good = false;
            if (good) return e;
        }
        return null;
    }

    public static IconSpec For(ItemDef def)
    {
        ItemEffect? first = Primary(def);
        (string Glyph, Color Color) main = first is not null ? ForStat(first.Stat) : ("pouch", def.Tint);

        if (ItemOverrides.TryGetValue(def.Id, out var o))
        {
            Color c = Readable(def.Tint);
            Color bc = BadgeColorFor(o.Badge, c);
            return new IconSpec(o.Glyph, c, o.Badge, bc);
        }

        ItemEffect? second = first is not null ? Primary(def, first) : null;
        if (second is not null && ForStat(second.Stat).Glyph == main.Glyph) second = null;
        // The item's own tint, not the stat's: every item gets its own colour.
        Color own = Readable(def.Tint);
        return second is null
            ? new IconSpec(main.Glyph, own)
            : new IconSpec(main.Glyph, own, ForStat(second.Stat).Glyph, ForStat(second.Stat).Color);
    }

    /// <summary>The colour a badge glyph usually has, so a skull badge is
    /// always the same skull.</summary>
    private static Color BadgeColorFor(string? glyph, Color fallback) => glyph switch
    {
        "skull" => new Color(0.92f, 0.90f, 0.86f),
        "heart_broken" or "heart" => ForStat(Stat.MaxHealth).Color,
        "sword" => ForStat(Stat.Damage).Color,
        "fastforward" => ForStat(Stat.AttackSpeed).Color,
        "coin" => ForStat(Stat.GoldGain).Color,
        "clover" => ForStat(Stat.Luck).Color,
        "star" => ForStat(Stat.XpGain).Color,
        "magnet" => ForStat(Stat.PickupRange).Color,
        "target" => ForStat(Stat.CritChance).Color,
        "shield" => ForStat(Stat.Armor).Color,
        "bubble" => ForStat(Stat.Shield).Color,
        "multi" => ForStat(Stat.ProjectileCount).Color,
        "expand" => ForStat(Stat.AreaSize).Color,
        "burst" => ForStat(Stat.CritDamage).Color,
        "regen" => ForStat(Stat.HealthRegen).Color,
        "hourglass" => ForStat(Stat.Duration).Color,
        "bolt" => ElementColor(Element.Lightning),
        "gem" => ForStat(Stat.ElementalDamage).Color,
        "spark" => ForStat(Stat.MegacritChance).Color,
        "spikes" => ForStat(Stat.Thorns).Color,
        "fang" => ForStat(Stat.Lifesteal).Color,
        "dash" => ForStat(Stat.DashCooldown).Color,
        "spring" => ForStat(Stat.JumpHeight).Color,
        _ => fallback,
    };

    /// <summary>Lift dark definition tints (ink, iron) so the glyph still reads
    /// on a dark card.</summary>
    private static Color Readable(Color c)
    {
        float lum = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
        return lum < 0.55f ? c.Lerp(Colors.White, (0.55f - lum) / 0.55f * 0.8f + 0.15f) : c;
    }

    // ------------------------------------------------------------------- tomes

    public static readonly Color BookInk = new(0.08f, 0.09f, 0.13f);

    /// <summary>Tomes that are not "one stat, more of it".</summary>
    private static readonly Dictionary<string, string> TomeOverrides = new()
    {
        ["tome_balance"] = "gem",     // a little of every offensive and defensive stat
        ["tome_chaos"] = "vortex",    // random stats each level
    };

    public static IconSpec For(TomeDef def)
    {
        // A perk is a rule, not a book: its own glyph and trigger badge (Data/Tomes.Perks.cs).
        if (def is PerkDef perk) return new IconSpec(perk.Glyph, Readable(perk.Tint), perk.Badge, BadgeColorFor(perk.Badge, perk.Tint));
        (string glyph, Color color) = ForStat(def.Stat);
        if (TomeOverrides.TryGetValue(def.Id, out string? o)) glyph = o;
        return new IconSpec("tome", color, Overlay: glyph, OverlayColor: BookInk);
    }

    // ------------------------------------------------------------------ offers

    public static readonly Color UpgradeColor = new(0.46f, 1.00f, 0.56f);

    /// <summary>The picture for a level-up card. Upgrades to something already
    /// held carry a green arrow in the corner.</summary>
    public static IconSpec For(Offer offer)
    {
        if (offer.Weapon is { } w)
        {
            IconSpec s = For(w);
            return offer.Detail.StartsWith("New") ? s : s.WithCorner("up", UpgradeColor);
        }
        if (offer.Item is { } item)
        {
            IconSpec s = For(item);
            return offer.Detail.StartsWith("New") ? s : s.WithCorner("up", UpgradeColor);
        }
        if (offer.Tome is { } tome)
        {
            IconSpec s = For(tome);
            return offer.Detail.StartsWith("New") ? s : s.WithCorner("up", UpgradeColor);
        }
        return ForSupply(offer.HealFraction, offer.Gold);
    }

    public static IconSpec ForSupply(float heal, int gold)
    {
        if (heal > 0f && gold > 0) return new IconSpec("pouch", new Color(0.55f, 0.95f, 0.65f), "coin", ForStat(Stat.GoldGain).Color);
        if (gold > 0) return new IconSpec("coin", ForStat(Stat.GoldGain).Color);
        return new IconSpec("medkit", new Color(1.00f, 0.50f, 0.50f));
    }

    /// <summary>Ids named in the override tables, so the audit can catch an
    /// override left behind by a renamed weapon or item.</summary>
    public static IEnumerable<string> OverrideIds => WeaponOverrides.Keys.Concat(ItemOverrides.Keys);

    /// <summary>The picture's shape, ignoring colour: glyph, badge and overlay.
    /// Two pieces of content with the same signature would look alike.</summary>
    public static string Signature(IconSpec s) => $"{s.Glyph}|{s.Badge}|{s.Overlay}";

    /// <summary>Groups of ids whose icons share a signature (should be empty).</summary>
    public static List<string> Collisions()
    {
        var seen = new Dictionary<string, List<string>>();
        void Add(IconSpec s, string id)
        {
            string k = Signature(s);
            if (!seen.TryGetValue(k, out var l)) seen[k] = l = new List<string>();
            l.Add(id);
        }
        foreach (WeaponDef w in Catalog.Weapons.Concat(Meta.MetaContent.Weapons)) Add(For(w), w.Id);
        foreach (ItemDef i in Catalog.Items.Concat(Meta.MetaContent.Items)) Add(For(i), i.Id);
        foreach (TomeDef t in Tomes.All) Add(For(t), t.Id);
        return seen.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key + " : " + string.Join(", ", kv.Value)).ToList();
    }

    /// <summary>Every glyph a spec names, for the content audit.</summary>
    public static IEnumerable<string> GlyphsOf(IconSpec s)
    {
        yield return s.Glyph;
        if (s.Badge is not null) yield return s.Badge;
        if (s.Overlay is not null) yield return s.Overlay;
        if (s.Corner is not null) yield return s.Corner;
    }
}
