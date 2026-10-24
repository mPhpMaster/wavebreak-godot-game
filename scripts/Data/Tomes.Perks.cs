using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using Wavebreak.Meta;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>
/// One number a perk grows by level ("hits needed": 12, 11, 10 ...). The
/// runtime (<c>Combat/Weapons/PerkProcs</c>) reads every value it uses from
/// here, so the card, the codex, the build box and the effect can never
/// disagree, and the audit can check the table directly.
/// </summary>
public sealed class PerkParam
{
    public PerkParam(string label, string unit, params float[] values)
    {
        Label = label;
        Unit = unit;
        Values = values;
    }

    /// <summary>Short English label for the "what this level adds" line (translated).</summary>
    public string Label { get; }

    /// <summary>"", "%", "s" or "m". Drawn in code, never through the table.</summary>
    public string Unit { get; }

    /// <summary>Index 0 is level 1.</summary>
    public float[] Values { get; }

    /// <summary>True when a smaller number is the upgrade (a shorter interval).</summary>
    public bool LowerIsBetter { get; init; }

    public float At(int level) => level <= 0 ? 0f : Values[Math.Clamp(level, 1, Values.Length) - 1];

    /// <summary>"12", "60%", "2.5s", "8 m" (Arabic: "2.5 ث", "8 م").</summary>
    public string Show(float v)
    {
        string n = v.ToString("0.##", CultureInfo.InvariantCulture);
        string text = Unit switch
        {
            "%" => n + "%",
            "s" => Loc.IsArabic ? n + " ث" : n + "s",
            "m" => Loc.IsArabic ? n + " م" : n + " m",
            _ => n,
        };
        return Loc.IsArabic ? Loc.IsolateNumbers(text) : text;
    }
}

/// <summary>
/// A perk: a tome whose levels buy a rule instead of a stat.
///
/// Why a tome and not a new kind of offer: everything the brief asks a perk to
/// respect already exists for tomes and is keyed by <see cref="TomeDef"/> —
/// the slot limit (<see cref="Progression.TomeBook.Slots"/>, widened by the
/// Bookshelf), banish (<see cref="Progression.OfferPool.Banished"/>), reroll
/// and skip, the Toggler (<see cref="Progression.OfferPool.DisabledTomes"/>),
/// "Prefer tomes" auto-pick, the HUD list, the build box, the codex, the dev
/// panel, mastery quests and <c>--give</c>. A sibling type would have to be
/// threaded through each of those by hand; a subclass gets all of them, and
/// costs only the three places that must say "perk" instead of "tome" (the
/// card text, the card's kind tag and the icon).
///
/// So perks share the tome slots: a build chooses between a clean stat and a
/// rule. The <see cref="TomeDef.Stat"/> of a perk is only its theme (icon
/// colour, auto-pick bias); <see cref="Progression.TomeBook.Add"/> writes no
/// modifier for it. Each level's numbers are in <see cref="Params"/>; the
/// behaviour lives in <c>Combat/Weapons/PerkProcs</c>.
/// </summary>
public partial class PerkDef : TomeDef
{
    /// <summary>English sentence with {n:fmt} holes, one per param. Also the Arabic key.</summary>
    public string Template { get; set; } = "";

    public PerkParam[] Params { get; set; } = Array.Empty<PerkParam>();

    /// <summary>Main glyph and badge (assets/icons).</summary>
    public string Glyph { get; set; } = "star";
    public string Badge { get; set; } = "spark";

    /// <summary>Its effect reaches teammates (synced in co-op, Net/NetSession.Perks.cs).</summary>
    public bool Coop { get; set; }

    /// <summary>Value of param <paramref name="index"/> at <paramref name="level"/> (0 when not held).</summary>
    public float V(int index, int level) => level <= 0 ? 0f : Params[index].At(level);

    /// <summary>The whole rule at a level: "Every 9 hits: lightning chains through 3 enemies for 90% of the hit."</summary>
    public string Summary(int level)
    {
        level = Math.Clamp(level, 1, MaxLevel);
        var args = new object[Params.Length];
        for (int i = 0; i < Params.Length; i++) args[i] = Params[i].At(level);
        return Loc.F(Template, args);
    }

    /// <summary>What taking <paramref name="level"/> changes: "hits needed 11 → 10 · chain targets 2 → 3".</summary>
    public string Adds(int level)
    {
        if (level <= 1) return Summary(1);
        string arrow = Loc.IsRtl ? " ← " : " → ";
        var sb = new StringBuilder(64);
        foreach (PerkParam p in Params)
        {
            float a = p.At(level - 1), b = p.At(level);
            if (MathF.Abs(a - b) < 1e-4f) continue;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(Loc.T(p.Label)).Append(' ').Append(p.Show(a)).Append(arrow).Append(p.Show(b));
        }
        return sb.ToString();
    }

    /// <summary>Card text: what this level adds, then the total after it.</summary>
    public string CardText(int currentLevel)
    {
        int next = currentLevel + 1;
        if (currentLevel <= 0)
            return Summary(1) + "\n" + Loc.F("Lv {0}/{1}  {2}", 1, MaxLevel, Loc.T("uses a tome slot"));
        return Loc.F("Lv {0}/{1}  {2}", next, MaxLevel, Adds(next)) + "\n" + Summary(next);
    }

    public override string DescribeStep() => Summary(1);

    public override string DescribeTotal(int level) => level <= 0 ? "" : Summary(level);
}

/// <summary>
/// The fifteen perks. Appended to <see cref="Tomes"/> after the expansion tomes
/// (one line in Tomes.Expansion.cs's static constructor), so every system that
/// walks <see cref="Tomes.All"/> sees them.
///
/// Balance record. Perk numbers are deliberately in the range of the matching
/// common item at two to three stacks; a perk at max level costs as many
/// level-ups as a tome and one of the same four slots, and none of the
/// conditional ones is ever on all the time.
/// </summary>
public static class Perks
{
    public const string Stormcount = "perk_stormcount";
    public const string BubbleNet = "perk_bubble_net";
    public const string BrineWake = "perk_brine_wake";
    public const string CrabStomp = "perk_crab_stomp";
    public const string RedTide = "perk_red_tide";
    public const string Barnacle = "perk_barnacle_hide";
    public const string Flotsam = "perk_flotsam_orbit";
    public const string Ballast = "perk_golden_ballast";
    public const string LastLight = "perk_last_light";
    public const string Lighthouse = "perk_lighthouse";
    public const string FairWinds = "perk_fair_winds";
    public const string Cornered = "perk_cornered_eel";
    public const string Beacon = "perk_crew_beacon";
    public const string Lifeline = "perk_lifeline";
    public const string TidalPulse = "perk_tidal_pulse";

    public static IReadOnlyList<PerkDef> All => List;

    public static PerkDef? ById(string? id)
    {
        if (id is null) return null;
        foreach (PerkDef p in List) if (p.Id == id) return p;
        return null;
    }

    private static PerkParam P(string label, string unit, params float[] v) => new(label, unit, v);
    private static PerkParam Lower(string label, string unit, params float[] v) => new(label, unit, v) { LowerIsBetter = true };

    private static readonly List<PerkDef> List = new()
    {
        // ---- procs
        Perk(Stormcount, "Stormcount", Rarity.Uncommon, Stat.Damage, 0.5f, "storm", "chain", new Color(0.95f, 0.92f, 0.45f),
             "The storm keeps a tally of your blows. When the count comes up, it pays out.",
             "Every {0:0} hits, lightning jumps to {1:0} more enemies for {2:0}%.",
             Lower("hits", "", 12, 11, 10, 9, 8, 7),
             P("jumps", "", 2, 2, 3, 3, 4, 5),
             P("damage", "%", 60, 70, 80, 90, 100, 120)),

        Perk(BubbleNet, "Bubble Net", Rarity.Uncommon, Stat.Shield, 0.5f, "bubble", "skull", new Color(0.62f, 0.92f, 1.00f),
             "Big things burst into bubbles when they die. The bubbles are worth catching.",
             "Elites drop a bubble: {0:0} shield for {1:0}s. Other kills: {2:0.#}%.",
             P("shield", "", 30, 40, 50, 62, 75),
             P("time", "s", 8, 9, 10, 11, 12),
             P("chance", "%", 0, 0, 1, 1.5f, 2)),

        Perk(BrineWake, "Cold Wake", Rarity.Common, Stat.MoveSpeed, 0.6f, "dash", "snowflake", new Color(0.55f, 0.85f, 0.95f),
             "Every dash leaves cold, heavy water behind you.",
             "Dashes leave a {2:0.#} m wake for {0:0.#}s: it chills and deals {1:0} a second.",
             P("time", "s", 2, 2.5f, 2.5f, 3, 3, 3.5f),
             P("damage", "", 6, 9, 12, 16, 20, 26),
             P("width", "m", 1.6f, 1.8f, 2f, 2.2f, 2.4f, 2.8f)),

        Perk(CrabStomp, "Crab Stomp", Rarity.Common, Stat.JumpHeight, 0.6f, "boot", "burst", new Color(0.95f, 0.55f, 0.40f),
             "Learn from the crabs: the best way down is onto someone.",
             "Land on an enemy: {0:0} damage in {1:0.#} m, {2:0.#}s stun, and bounce.",
             P("damage", "", 30, 45, 60, 80, 105),
             P("radius", "m", 2, 2.3f, 2.6f, 3, 3.4f),
             P("stun", "s", 0, 0.3f, 0.5f, 0.7f, 1f)),

        Perk(RedTide, "Crimson Tide", Rarity.Uncommon, Stat.Lifesteal, 0.5f, "fang", "target", new Color(0.90f, 0.30f, 0.36f),
             "A clean cut closes your own wounds.",
             "Crits heal {0:0.#} (up to {1:0} a second). Megacrits heal {2:0}% max health.",
             P("heal", "", 0.5f, 0.8f, 1.1f, 1.4f, 1.8f, 2.2f),
             P("per second", "", 5, 7, 9, 11, 13, 16),
             P("megacrit", "%", 0, 0, 1, 1, 2, 3)),

        Perk(Flotsam, "Flotsam Orbit", Rarity.Common, Stat.PickupRange, 0.6f, "orbit", "star", new Color(0.70f, 0.95f, 0.55f),
             "What the tide brings in, it carries around you for a while.",
             "Experience you collect orbits you for {2:0}s, hitting for {1:0}. Up to {0:0} motes.",
             P("motes", "", 2, 3, 4, 5, 6, 8),
             P("damage", "", 8, 10, 12, 15, 18, 22),
             P("time", "s", 5, 5, 6, 6, 7, 8)),

        Perk(Ballast, "Golden Ballast", Rarity.Uncommon, Stat.GoldGain, 0.5f, "coin", "bomb", new Color(1.00f, 0.82f, 0.30f),
             "Weight in the hold, weight in the swing.",
             "Gold: +{0:0.#}% damage a stack, up to {1:0}, {2:0}s each. Full: a coin burst for {3:0}.",
             P("per stack", "%", 0.6f, 0.7f, 0.8f, 0.9f, 1f, 1.2f),
             P("stacks", "", 10, 14, 18, 22, 26, 30),
             P("time", "s", 8, 8, 9, 9, 10, 10),
             P("burst", "", 0, 0, 0, 40, 60, 90)),

        Perk(LastLight, "Last Light", Rarity.Epic, Stat.MaxHealth, 0.25f, "lantern", "heart", new Color(1.00f, 0.90f, 0.60f),
             "One lamp kept lit for the worst moment.",
             "Refuse {0:0} lethal blows a map: back at {1:0}% health, 2s immune, a blast for {2:0}.",
             P("charges", "", 1, 1, 1, 2, 2),
             P("health", "%", 30, 40, 50, 50, 60),
             P("blast", "", 0, 40, 80, 120, 180)),

        Perk(Lighthouse, "Keeper's Beam", Rarity.Common, Stat.Luck, 0.55f, "tower", "beam", new Color(1.00f, 0.95f, 0.75f),
             "A keeper always knows where the wrecks wash up.",
             "Every {0:0}s a beam marks the nearest chest. Opening it: +{1:0} gold, {2:0}% heal.",
             Lower("every", "s", 60, 50, 40, 32, 25),
             P("gold", "", 10, 15, 20, 30, 40),
             P("heal", "%", 0, 0, 5, 8, 12)),

        Perk(TidalPulse, "Tidal Pulse", Rarity.Rare, Stat.AreaSize, 0.35f, "wave", "expand", new Color(0.40f, 0.75f, 1.00f),
             "The sea breathes out, and everything near you is pushed away.",
             "Every {0:0.#}s a pulse hits all within {2:0.#} m for {1:0} and pushes them back.",
             Lower("every", "s", 9, 8, 7, 6.5f, 6, 5),
             P("damage", "", 25, 35, 45, 60, 75, 95),
             P("radius", "m", 4, 4.5f, 5, 5.5f, 6, 7)),

        // ---- stats with a twist
        Perk(Barnacle, "Barnacle Hide", Rarity.Common, Stat.Armor, 0.6f, "shield", "hourglass", new Color(0.70f, 0.72f, 0.62f),
             "Stay put long enough and the sea starts building on you.",
             "Standing still: +{0:0.#} armour a second, up to {1:0}. Each point: +{2:0.##} thorns.",
             P("per second", "", 2, 2.5f, 3, 3.5f, 4, 5),
             P("cap", "", 10, 14, 18, 22, 26, 32),
             P("thorns", "", 0, 0, 0, 0.5f, 0.75f, 1)),

        Perk(FairWinds, "Following Sea", Rarity.Common, Stat.AttackSpeed, 0.6f, "feather", "fastforward", new Color(0.86f, 0.95f, 0.80f),
             "Move with the wind and your hands move faster too.",
             "Per m/s you move: +{0:0.#}% attack speed (up to +{1:0}%), +{2:0.#}% shot speed.",
             P("per m/s", "%", 1, 1.3f, 1.6f, 2, 2.4f, 3),
             P("cap", "%", 10, 14, 18, 22, 26, 32),
             P("shot speed", "%", 0, 0, 1, 1.5f, 2, 3)),

        Perk(Cornered, "Cornered Eel", Rarity.Uncommon, Stat.Damage, 0.5f, "heart_broken", "sword", new Color(0.85f, 0.45f, 0.75f),
             "Nothing fights harder than something with nowhere left to go.",
             "Below half health: up to +{0:0}% damage, {1:0}% lifesteal. Below a quarter: +{2:0} evasion.",
             P("damage", "%", 20, 28, 36, 45, 55, 70),
             P("lifesteal", "%", 0, 1, 1, 2, 2, 3),
             P("evasion", "", 0, 0, 5, 8, 12, 16)),

        // ---- co-op (work alone too)
        Perk(Beacon, "Crew Beacon", Rarity.Rare, Stat.AttackSpeed, 0.35f, "flag", "aura", new Color(1.00f, 0.70f, 0.40f),
             "Plant the crew's flag. Everyone near it fights better, you included.",
             "You and allies within {0:0} m: +{1:0}% attack speed, +{2:0} armour.",
             P("radius", "m", 7, 8, 9, 10, 12),
             P("attack speed", "%", 6, 9, 12, 15, 20),
             P("armour", "", 3, 4, 6, 8, 10)),

        Perk(Lifeline, "Lifeline", Rarity.Rare, Stat.HealthRegen, 0.35f, "chain", "heart", new Color(0.55f, 1.00f, 0.75f),
             "A rope for whoever falls, and a last pull for yourself.",
             "Allies you revive get up {0:0}% faster, from {1:0.#} m further. Below 35% health: heal {2:0}% (every 40s).",
             P("revive", "%", 30, 45, 60, 80, 100),
             P("reach", "m", 0.5f, 1, 1.5f, 2, 3),
             P("surge", "%", 8, 10, 12, 15, 18)),
    };

    private static PerkDef Perk(string id, string name, Rarity rarity, Stat theme, float weight, string glyph, string badge,
                                Color tint, string description, string template, params PerkParam[] ps)
    {
        int levels = ps.Length > 0 ? ps[0].Values.Length : 1;
        return new PerkDef
        {
            Id = id,
            DisplayName = name,
            Rarity = rarity,
            Stat = theme,
            Op = ModifierOp.Flat,
            PerLevel = 0f,
            MaxLevel = levels,
            Weight = weight,
            Tint = tint,
            Description = description,
            Template = template,
            Params = ps,
            Glyph = glyph,
            Badge = badge,
            Coop = id is Beacon or Lifeline,
        };
    }

    /// <summary>Every English string a perk can draw, for the audit and Arabic.</summary>
    public static IEnumerable<string> Strings()
    {
        foreach (PerkDef p in List)
        {
            yield return p.DisplayName;
            yield return p.Description;
            yield return p.Template;
            foreach (PerkParam q in p.Params) yield return q.Label;
        }
        foreach (string s in UiStrings) yield return s;
    }

    /// <summary>UI words the perk code draws besides the table above.</summary>
    public static readonly string[] UiStrings =
    {
        "· PERK", "New perk", "uses a tome slot", "PERK  ·  {0}", "Lv 1: {0}\nmax Lv {1}: {2}",
        "LAST LIGHT", "BUBBLE", "Chest marked", "SURGE",
    };
}
