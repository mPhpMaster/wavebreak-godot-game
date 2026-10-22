using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Progression;

/// <summary>
/// One button on the level-up screen.
///
/// An offer carries either a <see cref="WeaponDef"/>, an <see cref="ItemDef"/>, or
/// neither — the last being the consolation payout used when the pool has nothing
/// left to give. All three carry their own display text, built when the offer is
/// rolled, so the UI never has to know how a weapon upgrade differs from an item
/// stack in order to draw one.
/// </summary>
public sealed class Offer
{
    private Offer(string title, string detail, string description, string effect,
                  Rarity rarity, Color tint,
                  WeaponDef? weapon = null, ItemDef? item = null,
                  float healFraction = 0f, int gold = 0)
    {
        Title = title;
        Detail = detail;
        Description = description;
        Effect = effect;
        Rarity = rarity;
        Tint = tint;
        Weapon = weapon;
        Item = item;
        HealFraction = healFraction;
        Gold = gold;
    }

    /// <summary>The weapon this offer grants or levels, or null.</summary>
    public WeaponDef? Weapon { get; }

    /// <summary>The item this offer adds a stack of, or null.</summary>
    public ItemDef? Item { get; }

    /// <summary>Name of the thing on offer.</summary>
    public string Title { get; }

    /// <summary>Short status line: "New", "Level 3 → 4", "Stack 2/5".</summary>
    public string Detail { get; }

    /// <summary>Flavour text, falling back to the mechanical summary when the
    /// definition has none.</summary>
    public string Description { get; }

    /// <summary>What it actually does, generated from the definition: "+15%
    /// Damage", "18 damage every 0.62s".</summary>
    public string Effect { get; }

    public Rarity Rarity { get; }

    /// <summary>Colour for the card. Taken from the definition so an item looks
    /// the same on the level-up screen as it does in the world.</summary>
    public Color Tint { get; }

    /// <summary>Fraction of maximum health this offer restores. Non-zero only on
    /// consolation offers.</summary>
    public float HealFraction { get; }

    /// <summary>Gold this offer pays. Non-zero only on consolation offers.</summary>
    public int Gold { get; }

    public bool IsWeapon => Weapon is not null;
    public bool IsItem => Item is not null;

    /// <summary>True for the consolation offer: no weapon, no item, just a payout.</summary>
    public bool IsBonus => Weapon is null && Item is null;

    /// <summary>Alias of <see cref="Title"/>, so a caller that thinks in
    /// definitions rather than cards reads the same field.</summary>
    public string DisplayName => Title;

    /// <summary>
    /// Applies the parts of the offer that are not a weapon or an item — the heal
    /// and the gold. Harmless on a weapon or item offer, where both are zero, so
    /// a caller may always call it.
    /// </summary>
    public void Apply(Player? player, Inventory? inventory)
    {
        if (HealFraction > 0f && player is not null && GodotObject.IsInstanceValid(player)
            && player.Health is { } health)
        {
            health.HealFraction(HealFraction);
        }

        if (Gold > 0) (inventory ?? Inventory.Current)?.GrantGold(Gold);
    }

    public override string ToString() => $"{Title} ({Detail})";

    // ------------------------------------------------------------- factories

    internal static Offer ForWeapon(WeaponDef def, int currentLevel)
    {
        int next = currentLevel + 1;
        bool isNew = currentLevel <= 0;

        string detail = isNew ? "New weapon" : $"Level {currentLevel} → {next}";
        string effect = isNew
            ? $"{def.DamageAtLevel(1):0} damage every {def.CooldownAtLevel(1):0.00}s"
              + Plural(def.ProjectilesAtLevel(1))
            : WeaponDelta(def, currentLevel, next);

        string description = string.IsNullOrWhiteSpace(def.Description)
            ? $"{def.Shape} · {def.Element}"
            : def.Description;

        return new Offer(def.DisplayName, detail, description, effect, def.Rarity, def.Tint, weapon: def);
    }

    private static string Plural(int projectiles)
        => projectiles > 1 ? $", {projectiles} projectiles" : "";

    /// <summary>Spells out what the level actually buys. A card that says "Level
    /// 3 → 4" and nothing else asks the player to trust the game; showing the two
    /// numbers lets them choose.</summary>
    private static string WeaponDelta(WeaponDef def, int from, int to)
    {
        var sb = new StringBuilder(64);
        sb.Append($"{def.DamageAtLevel(from):0.#} → {def.DamageAtLevel(to):0.#} damage");

        float before = def.CooldownAtLevel(from);
        float after = def.CooldownAtLevel(to);
        if (MathF.Abs(before - after) > 0.001f)
            sb.Append($", {before:0.00}s → {after:0.00}s");

        int extra = def.ProjectilesAtLevel(to) - def.ProjectilesAtLevel(from);
        if (extra > 0) sb.Append($", +{extra} projectile{(extra > 1 ? "s" : "")}");

        return sb.ToString();
    }

    internal static Offer ForItem(ItemDef def, int held)
    {
        string detail = held <= 0
            ? "New"
            : def.MaxStacks > 0 ? $"Stack {held + 1}/{def.MaxStacks}" : $"Stack {held + 1}";

        string effect = OfferPool.DescribeEffects(def);
        string description = string.IsNullOrWhiteSpace(def.Description) ? effect : def.Description;

        return new Offer(def.DisplayName, detail, description, effect, def.Rarity, def.Tint, item: def);
    }

    internal static Offer Bonus(string title, string description, float healFraction, int gold, Color tint)
    {
        var sb = new StringBuilder(48);
        if (healFraction > 0f) sb.Append($"Restores {healFraction * 100f:0}% health");
        if (gold > 0)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append($"+{gold} gold");
        }

        return new Offer(title, "Supply", description, sb.ToString(), Rarity.Common, tint,
                         healFraction: healFraction, gold: gold);
    }
}

/// <summary>
/// Builds the choices a level-up presents.
///
/// Three rules decide what appears:
///
/// <b>Nothing dead is ever offered.</b> Items at <see cref="ItemDef.MaxStacks"/>
/// and weapons at <see cref="WeaponDef.MaxLevel"/> are filtered out before
/// weighting. An offer the player cannot benefit from is worse than no offer —
/// it turns a choice between three things into a choice between two.
///
/// <b>Rarity is a weight, not a gate.</b> Every rarity can appear at level 1;
/// legendaries are simply far less likely than commons. Gating by level instead
/// makes early choices samey and late ones a foregone conclusion.
///
/// <b>Luck compounds with rarity.</b> See <see cref="LuckShift"/>.
///
/// <b>And there is always something.</b> When the pool is exhausted — every item
/// capped, every weapon maxed — the roll falls back to a supply offer that heals
/// and pays gold, so a level-up can never present an empty screen.
/// </summary>
public sealed class OfferPool
{
    /// <summary>
    /// Rarity slope for <em>weapons only</em>. Items carry their own
    /// <see cref="ItemDef.Weight"/>, authored alongside the item table and already
    /// shaped by rarity; applying a second slope on top of it would quietly
    /// override the balance table with a number from a different file. Weapons
    /// have no such field, so the pool supplies the slope for them.
    /// </summary>
    private static readonly float[] WeaponRarityWeight = { 1f, 0.55f, 0.25f, 0.10f, 0.035f };

    /// <summary>
    /// How much one point of luck bends the rarity curve. See
    /// <see cref="LuckShift"/> for the shape.
    /// </summary>
    public const float LuckPerTier = 0.45f;

    /// <summary>Luck is clamped before it is used. The floor keeps a heavy curse
    /// from producing negative weights; the ceiling keeps a runaway luck stack
    /// from making commons unofferable, which would strand a build with nothing
    /// cheap to fill its gaps.</summary>
    public const float MinLuck = -0.95f;
    public const float MaxLuck = 8f;

    /// <summary>Relative pull of a weapon against an item of the same rarity. New
    /// weapons edge ahead of upgrades because they change how a run plays.</summary>
    private const float NewWeaponWeight = 1.25f;
    private const float WeaponUpgradeWeight = 1f;

    private static readonly Color SupplyTint = new(0.55f, 0.95f, 0.65f);

    private readonly List<ItemDef> _items = new();
    private readonly List<WeaponDef> _weapons = new();
    private readonly Rng _rng;

    // Reused across rolls: a level-up happens dozens of times a run and there is
    // no reason for any of them to allocate scratch space.
    private readonly List<Offer> _candidates = new(64);
    private readonly List<float> _weights = new(64);

    /// <param name="items">The item catalogue. Held by reference into a private
    /// copy, so a later edit of the catalogue cannot change a run in flight.</param>
    /// <param name="weapons">The weapon catalogue.</param>
    /// <param name="rng">The run's seeded random source, so the same seed offers
    /// the same choices.</param>
    public OfferPool(IReadOnlyList<ItemDef>? items, IReadOnlyList<WeaponDef>? weapons, Rng rng)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));

        if (items is not null)
            for (int i = 0; i < items.Count; i++)
                if (items[i] is { } item) _items.Add(item);

        if (weapons is not null)
            for (int i = 0; i < weapons.Count; i++)
                if (weapons[i] is { } weapon) _weapons.Add(weapon);

        // The offer pool is the one progression object built with the run's Rng,
        // so it is the natural place to hand that Rng to the drop table.
        PickupField.Ensure(_rng);
    }

    /// <summary>Items and weapons the pool can draw from, ignoring what is held.</summary>
    public int CatalogueSize => _items.Count + _weapons.Count;

    /// <summary>
    /// Produces <paramref name="count"/> distinct choices.
    ///
    /// Always returns exactly <paramref name="count"/> entries: whatever the
    /// catalogue cannot fill is filled with supply offers.
    /// </summary>
    /// <param name="count">How many cards to show.</param>
    /// <param name="weaponSystem">The live weapon system, asked directly whether
    /// each weapon is still worth offering. Null is legal and falls back to the
    /// inventory's ledger of <see cref="WeaponAcquired"/> events, which is what a
    /// headless test or a between-runs screen has.</param>
    /// <param name="inventory">What the player already holds. Null offers
    /// everything as new.</param>
    /// <param name="stats">Read for <see cref="Stat.Luck"/>.</param>
    public List<Offer> Roll(int count, WeaponSystem? weaponSystem, Inventory? inventory, StatSheet? stats)
    {
        var chosen = new List<Offer>(Math.Max(0, count));
        if (count <= 0) return chosen;

        float luck = Math.Clamp(stats?.Get(Stat.Luck) ?? 0f, MinLuck, MaxLuck);

        // Three cases, and they are not the same: no system was supplied (fall
        // back to the ledger), a live one was (ask it), or a dead one was — a
        // system that has left the tree cannot receive anything, so offering a
        // weapon would hand the player a card that does nothing.
        bool supplied = weaponSystem is not null;
        WeaponSystem? live = supplied
                             && GodotObject.IsInstanceValid(weaponSystem)
                             && !weaponSystem!.IsQueuedForDeletion()
            ? weaponSystem
            : null;

        BuildCandidates(inventory, luck, live, offerWeapons: !supplied || live is not null);

        for (int i = 0; i < count; i++)
        {
            int pick = _rng.PickWeighted(_weights);
            if (pick < 0)
            {
                // Pool exhausted. Index by how many have already been handed out
                // so three consolation offers are three different ones.
                chosen.Add(Supply(chosen.Count));
                continue;
            }

            chosen.Add(_candidates[pick]);

            // Removed rather than zero-weighted: one thing cannot appear twice on
            // the same screen, and the lists are short enough that the shift is
            // cheaper than re-scanning zeros on every subsequent pick.
            _candidates.RemoveAt(pick);
            _weights.RemoveAt(pick);
        }

        return chosen;
    }

    /// <summary>
    /// Fills the candidate and weight buffers with everything still worth offering.
    /// </summary>
    /// <param name="weapons">Authoritative source of weapon levels and free slots,
    /// or null to use the inventory's ledger instead.</param>
    /// <param name="offerWeapons">False when there is nothing alive to receive a
    /// weapon, which makes every weapon card a dead choice.</param>
    private void BuildCandidates(Inventory? inventory, float luck, WeaponSystem? weapons, bool offerWeapons)
    {
        _candidates.Clear();
        _weights.Clear();

        for (int i = 0; i < _items.Count; i++)
        {
            ItemDef def = _items[i];
            if (inventory?.IsMaxed(def) == true) continue;

            int held = inventory?.StacksOf(def) ?? 0;

            // ItemDef.Weight is the balance table's own slope, already shaped by
            // rarity; luck is the only thing applied on top. A non-positive weight
            // is how an item leaves the pool without being deleted.
            float weight = def.Weight <= 0f ? 0f : def.Weight * LuckShift(def.Rarity, luck);
            if (weight <= 0f) continue;

            _candidates.Add(Offer.ForItem(def, held));
            _weights.Add(weight);
        }

        if (!offerWeapons) return;

        int owned = inventory?.WeaponCount ?? 0;

        for (int i = 0; i < _weapons.Count; i++)
        {
            WeaponDef def = _weapons[i];
            int level;

            if (weapons is not null)
            {
                // The system decides: it owns the slot count and the level cap,
                // and CanAdd answers "would taking this change anything" in one
                // call — which is exactly what "worth offering" means.
                if (!weapons.CanAdd(def)) continue;
                level = weapons.LevelOf(def.Id);
            }
            else
            {
                level = inventory?.WeaponLevel(def) ?? 0;
                if (level > 0 && level >= def.MaxLevel) continue;           // maxed
                if (level <= 0 && owned >= WeaponSystem.MaxWeapons) continue; // no slot
            }

            float weight = (level <= 0 ? NewWeaponWeight : WeaponUpgradeWeight)
                           * WeaponRarityWeight[Math.Clamp((int)def.Rarity, 0, WeaponRarityWeight.Length - 1)]
                           * LuckShift(def.Rarity, luck);
            if (weight <= 0f) continue;

            _candidates.Add(Offer.ForWeapon(def, level));
            _weights.Add(weight);
        }
    }

    /// <summary>
    /// The luck curve, stated once so it can be argued with:
    ///
    /// <code>
    ///     shift(rarity) = (1 + LuckPerTier * luck) ^ tier
    /// </code>
    ///
    /// where tier is 0 for common through 4 for legendary. At zero luck this is
    /// exactly 1 for every rarity — luck adds a shift, it does not replace the
    /// slope the item table already has. Above zero it compounds once per rarity
    /// step: one point of luck roughly doubles the odds of a rare and quadruples
    /// those of a legendary, and around five points a legendary is as likely as a
    /// common. Negative luck runs the same curve backwards.
    ///
    /// A flat "+x% to rare odds" was the alternative and it is worse in both
    /// directions: small amounts of luck do nothing noticeable, and large amounts
    /// saturate. Compounding per tier means every point is felt, and felt most at
    /// the top of the table where the player is looking.
    /// </summary>
    public static float LuckShift(Rarity rarity, float luck)
    {
        int tier = Math.Clamp((int)rarity, 0, WeaponRarityWeight.Length - 1);
        if (tier == 0) return 1f;

        float step = MathF.Max(0.05f, 1f + LuckPerTier * Math.Clamp(luck, MinLuck, MaxLuck));
        return MathF.Pow(step, tier);
    }

    /// <summary>
    /// The consolation prize. Three variants so a fully exhausted pool still
    /// presents a choice rather than the same card three times.
    /// </summary>
    private static Offer Supply(int index) => (index % 3) switch
    {
        0 => Offer.Bonus("Field Dressing", "Patched up and back in it.", 0.35f, 0, SupplyTint),
        1 => Offer.Bonus("Loose Change", "Scavenged from the wreckage.", 0f, 60, SupplyTint),
        _ => Offer.Bonus("Supply Cache", "A little of everything.", 0.18f, 35, SupplyTint),
    };

    // -------------------------------------------------------------- item text

    /// <summary>
    /// Turns an item's effect list into one readable line: "+15% Damage, +5 Armor".
    ///
    /// Generated rather than authored because a hand-written description is a
    /// second copy of the numbers, and the two drift the first time anyone
    /// rebalances an item.
    /// </summary>
    public static string DescribeEffects(ItemDef def)
    {
        if (def.Effects is null || def.Effects.Count == 0) return "";

        var sb = new StringBuilder(64);
        foreach (ItemEffect effect in def.Effects)
        {
            if (effect is null) continue;
            if (sb.Length > 0) sb.Append(", ");

            switch (effect.Op)
            {
                case ModifierOp.Additive:
                    sb.Append(Signed(effect.PerStack * 100f, "0.#")).Append("% ");
                    break;
                case ModifierOp.Multiplicative:
                    sb.Append('×').Append(effect.PerStack.ToString("0.##")).Append(' ');
                    break;
                default:
                    sb.Append(Signed(effect.PerStack, "0.##")).Append(' ');
                    break;
            }

            sb.Append(PrettyName(effect.Stat));
        }

        return sb.ToString();
    }

    private static string Signed(float value, string format)
        => (value >= 0f ? "+" : "") + value.ToString(format);

    /// <summary>"MaxHealth" reads as "Max Health" on a card. Splitting the enum
    /// name means a new stat needs no display table to go with it.</summary>
    private static string PrettyName(Stat stat)
    {
        string raw = stat.ToString();
        var sb = new StringBuilder(raw.Length + 4);

        for (int i = 0; i < raw.Length; i++)
        {
            if (i > 0 && char.IsUpper(raw[i]) && !char.IsUpper(raw[i - 1])) sb.Append(' ');
            sb.Append(raw[i]);
        }

        return sb.ToString();
    }
}
