using System;
using System.Collections.Generic;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Progression;

/// <summary>One item held one or more times. The count is the item's whole
/// mechanical state: every effect is <c>PerStack * Count</c>.</summary>
public sealed class ItemStack
{
    public ItemStack(ItemDef def) => Def = def;

    public ItemDef Def { get; }

    /// <summary>How many copies are held. Never above <see cref="ItemDef.MaxStacks"/>
    /// when that is set.</summary>
    public int Count { get; internal set; }

    public string Id => Def.Id;
    public string DisplayName => Def.DisplayName;
    public Rarity Rarity => Def.Rarity;

    public override string ToString() => Count > 1 ? $"{DisplayName} x{Count}" : DisplayName;
}

/// <summary>
/// Everything the player has accumulated this run: items, experience, level, gold.
///
/// Two decisions here are worth more than the rest of the file.
///
/// <b>Items re-apply their whole stack rather than adding a second modifier.</b>
/// Picking up a third copy removes every modifier tagged with the
/// <see cref="ItemDef"/> and adds them back at <c>PerStack * 3</c>. The obvious
/// alternative — add one more modifier per copy — drifts the moment a stack is
/// capped, refunded or recalculated, and leaves the stat sheet holding modifiers
/// nobody can account for. Re-applying makes the sheet a pure function of the
/// stack count, so it cannot disagree with the inventory.
///
/// <b>Experience arrives two ways and is counted once.</b> Pickups call
/// <see cref="GrantXp"/>, which scales by <see cref="Stat.XpGain"/> and publishes
/// the event. Anything that publishes <see cref="XpGained"/> directly — a future
/// quest reward, a debug command — is picked up by the subscription instead. A
/// re-entrancy flag stops the inventory's own publish from feeding back into its
/// own handler, which is the difference between "two paths" and "double credit".
/// </summary>
public sealed partial class Inventory
{
    /// <summary>The live inventory, or null between runs. Pickups resolve it
    /// through this rather than being handed a reference at spawn time.</summary>
    public static Inventory? Current { get; private set; }

    private readonly StatSheet _stats;
    private readonly List<ItemStack> _stacks = new();
    private readonly Dictionary<string, ItemStack> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _weaponLevels = new(StringComparer.Ordinal);

    private bool _publishing;
    private bool _subscribed;

    /// <param name="stats">The sheet item effects are written to. Held, not copied
    /// — the inventory is the only thing allowed to add modifiers sourced from an
    /// <see cref="ItemDef"/>.</param>
    public Inventory(StatSheet stats)
    {
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));

        EventBus.Subscribe<XpGained>(OnXpGained);
        EventBus.Subscribe<GoldGained>(OnGoldGained);
        EventBus.Subscribe<WeaponAcquired>(OnWeaponAcquired);
        _subscribed = true;

        Current = this;

        // The pickup field has no line in Main's construction order, so it is
        // installed by the progression objects the scene does build. Idempotent;
        // see PickupField.Ensure.
        PickupField.Ensure();
    }

    /// <summary>Drops the bus subscriptions. Not required when the run ends
    /// through <see cref="EventBus.Reset"/>, but a headless test that builds two
    /// inventories in one process needs it.</summary>
    public void Dispose()
    {
        if (_subscribed)
        {
            _subscribed = false;
            EventBus.Unsubscribe<XpGained>(OnXpGained);
            EventBus.Unsubscribe<GoldGained>(OnGoldGained);
            EventBus.Unsubscribe<WeaponAcquired>(OnWeaponAcquired);
        }

        if (ReferenceEquals(Current, this)) Current = null;
    }

    // ------------------------------------------------------------------ state

    /// <summary>Current level. Starts at 1; a run never levels down.</summary>
    public int Level { get; private set; } = 1;

    /// <summary>Lifetime experience this run, never spent. The level is derived
    /// from it, so the two can never disagree.</summary>
    public int Xp { get; private set; }

    public int Gold { get; private set; }

    /// <summary>Copies held across every item — the number a results screen means
    /// by "items".</summary>
    public int TotalStacks { get; private set; }

    /// <summary>Distinct items held.</summary>
    public int DistinctItems => _stacks.Count;

    /// <summary>Every held item with its count, in acquisition order.</summary>
    public IReadOnlyList<ItemStack> Items => _stacks;

    /// <summary>Experience earned since this level began, and what the level costs
    /// in total. The pair a bar needs.</summary>
    public int XpIntoLevel => XpCurve.XpIntoLevel(Xp);

    public int XpForNextLevel => XpCurve.XpForLevel(Level);

    /// <summary>Progress through the current level, 0..1.</summary>
    public float LevelProgress => XpCurve.ProgressToNext(Xp);

    // ------------------------------------------------------------------ items

    /// <summary>
    /// Adds one copy and re-applies the item's whole stack to the stat sheet.
    /// Returns false when the item is already at <see cref="ItemDef.MaxStacks"/>,
    /// which leaves both the inventory and the sheet untouched.
    /// </summary>
    public bool Add(ItemDef? def)
    {
        if (def is null) return false;

        string id = KeyOf(def);
        _byId.TryGetValue(id, out ItemStack? stack);

        int held = stack?.Count ?? 0;
        if (def.MaxStacks > 0 && held >= def.MaxStacks) return false;

        if (stack is null)
        {
            stack = new ItemStack(def);
            _byId[id] = stack;
            _stacks.Add(stack);
        }

        stack.Count = held + 1;
        TotalStacks++;
        ReapplyStack(stack);

        EventBus.Publish(new ItemAcquired(id, stack.Count));
        return true;
    }

    /// <summary>Copies of an item held, by id. Zero for anything never picked up.</summary>
    public int StacksOf(string? id)
        => id is not null && _byId.TryGetValue(id, out ItemStack? s) ? s.Count : 0;

    public int StacksOf(ItemDef? def) => def is null ? 0 : StacksOf(KeyOf(def));

    /// <summary>True when another copy would be refused. What the offer pool uses
    /// to keep dead choices out of a level-up.</summary>
    public bool IsMaxed(ItemDef? def)
        => def is not null && def.MaxStacks > 0 && StacksOf(def) >= def.MaxStacks;

    /// <summary>
    /// Rewrites the sheet's contribution from one item.
    ///
    /// Additive and flat effects scale linearly with the count. Multiplicative
    /// ones raise the factor to the power of the count, because that is what
    /// stacking a multiplier means — and because collapsing it into one modifier
    /// keeps the sheet's modifier list proportional to items held rather than to
    /// copies held.
    /// </summary>
    private void ReapplyStack(ItemStack stack)
    {
        ItemDef def = stack.Def;

        // Everything this item ever contributed is tagged with the def, so one
        // call clears it exactly — no bookkeeping of previous values.
        _stats.RemoveAllFrom(def);

        if (def.Effects is null || stack.Count <= 0) return;

        foreach (ItemEffect effect in def.Effects)
        {
            if (effect is null) continue;

            float value;
            if (effect.Op == ModifierOp.Multiplicative)
            {
                // A non-positive factor would zero or invert the stat for the rest
                // of the run; treat it as unset rather than as a trap.
                if (effect.PerStack <= 0f) continue;
                value = MathF.Pow(effect.PerStack, stack.Count);
            }
            else
            {
                value = effect.PerStack * stack.Count;
            }

            _stats.Add(effect.Stat, effect.Op, value, def);
        }
    }

    private static string KeyOf(ItemDef def)
        => string.IsNullOrEmpty(def.Id) ? def.DisplayName : def.Id;

    // ------------------------------------------------------------- experience

    /// <summary>
    /// Awards raw experience: scales it by <see cref="Stat.XpGain"/>, banks it,
    /// publishes <see cref="XpGained"/> and levels up as far as the new total
    /// reaches. This is the entry point for pickups and kills.
    /// </summary>
    public void GrantXp(int raw)
    {
        if (raw <= 0) return;

        // Rounded, but never down to nothing: a 1-xp orb has to stay worth
        // something or the smallest enemies quietly stop paying out.
        int amount = Math.Max(1, (int)MathF.Round(raw * _stats.GetMultiplier(Stat.XpGain)));
        Bank(amount);
    }

    /// <summary>Awards raw gold, scaled by <see cref="Stat.GoldGain"/>.</summary>
    public void GrantGold(int raw)
    {
        if (raw <= 0) return;

        int amount = Math.Max(1, (int)MathF.Round(raw * _stats.GetMultiplier(Stat.GoldGain)));
        Gold += amount;
        Publish(new GoldGained(amount, Gold));
    }

    /// <summary>Spends gold if there is enough. Returns false and changes nothing
    /// otherwise, so a shop can offer and refuse in one call.</summary>
    public bool SpendGold(int amount)
    {
        if (amount <= 0 || Gold < amount) return false;
        Gold -= amount;
        Publish(new GoldGained(-amount, Gold));
        return true;
    }

    private void Bank(int amount)
    {
        Xp += amount;
        Publish(new XpGained(amount, Xp));
        CheckLevelUps();
    }

    /// <summary>
    /// Levels up while the total allows it, one event per level. Multi-level jumps
    /// happen — a boss orb can be worth two — and the UI is entitled to see each
    /// one so it can queue that many choices.
    /// </summary>
    private void CheckLevelUps()
    {
        // Bounded: a corrupt curve must not be able to spin here forever, and 64
        // levels from a single orb is already far past anything legitimate.
        for (int guard = 0; guard < 64; guard++)
        {
            if (Level >= XpCurve.MaxLevel) return;
            if (Xp < XpCurve.TotalXpForLevel(Level + 1)) return;

            Level++;
            EventBus.Publish(new LevelGained(Level));
        }
    }

    /// <summary>Publishes while flagged, so the inventory's own subscription
    /// ignores the echo instead of counting the same award twice.</summary>
    private void Publish<T>(in T evt) where T : struct
    {
        bool outer = _publishing;
        _publishing = true;
        try { EventBus.Publish(evt); }
        finally { _publishing = outer; }
    }

    // ------------------------------------------------------------ bus wiring

    // Foreign publishers are trusted to have applied their own scaling — the
    // event carries a final amount, not a raw one.
    private void OnXpGained(XpGained e)
    {
        if (_publishing || e.Amount <= 0) return;
        Xp += e.Amount;
        CheckLevelUps();
    }

    private void OnGoldGained(GoldGained e)
    {
        if (_publishing || e.Amount == 0) return;
        Gold = Math.Max(0, Gold + e.Amount);
    }

    // ----------------------------------------------------------- weapon ledger

    /// <summary>
    /// Levels of every weapon held, mirrored from <see cref="WeaponAcquired"/>.
    ///
    /// The offer pool needs to know which weapons are maxed, and reading that from
    /// the weapon system would tie progression to combat's internals for one
    /// integer. The event already carries it, so the ledger costs a dictionary
    /// write per acquisition and nothing else.
    /// </summary>
    public IReadOnlyDictionary<string, int> WeaponLevels => _weaponLevels;

    /// <summary>Level of a held weapon, or 0 when it is not held.</summary>
    public int WeaponLevel(string? id)
        => id is not null && _weaponLevels.TryGetValue(id, out int level) ? level : 0;

    public int WeaponLevel(WeaponDef? def) => def is null ? 0 : WeaponLevel(def.Id);

    public bool HasWeapon(WeaponDef? def) => WeaponLevel(def) > 0;

    /// <summary>Distinct weapons held.</summary>
    public int WeaponCount => _weaponLevels.Count;

    private void OnWeaponAcquired(WeaponAcquired e)
    {
        if (string.IsNullOrEmpty(e.Id)) return;

        // Max, not assign: a system that re-publishes an old level (a refresh, a
        // reload) must not be able to walk a weapon backwards.
        int known = WeaponLevel(e.Id);
        _weaponLevels[e.Id] = Math.Max(known, Math.Max(1, e.NewLevel));
    }
}
