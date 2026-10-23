using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Meta;

/// <summary>Which shelf a toggled entry sits on.</summary>
public enum ToggleKind { Weapon, Item, Tome }

/// <summary>One entry on the Toggler page.</summary>
public sealed record ToggleEntry(ToggleKind Kind, string Id, string Name, Rarity Rarity, Resource Def);

/// <summary>
/// The Toggler: a Pearl Shop unlock that lets the player switch off weapons,
/// items and tomes they never want to see. A switched-off entry is left out of
/// the run's offer pool, so it never appears in level-up offers, chests, shrine
/// rewards or the merchant (all of which draw from the pool).
///
/// Stored as <c>[meta] off.&lt;id&gt; = 1</c>. Nothing applies until the
/// Toggler is bought (<see cref="Owned"/>); refunding it switches everything
/// back on in effect without forgetting the choices.
///
/// Each shelf keeps at least <see cref="MinOf"/> entries on, so offers never
/// run dry: the page refuses to go below it, and <see cref="Filter{T}"/>
/// re-enables the first switched-off entries if a profile somehow does.
/// Automated runs ignore it unless <c>--profile</c> is given (the in-memory
/// profile owns nothing).
/// </summary>
public static class Toggler
{
    public const int MinWeapons = 10;
    public const int MinItems = 20;
    public const int MinTomes = 8;

    private static string Key(string id) => "off." + id;

    public static bool Owned => ShopExtras.TogglerOwned;

    public static bool IsOff(string id)
    {
        Profile.EnsureLoaded();
        return Profile.GetMeta(Key(id)) > 0;
    }

    /// <summary>Flip one entry. Does not save.</summary>
    public static void Set(string id, bool off) => Profile.SetMeta(Key(id), off ? 1 : 0);

    /// <summary>The fewest entries a shelf may keep on (never more than it has).</summary>
    public static int MinOf(ToggleKind kind, int total) => Math.Min(total, kind switch
    {
        ToggleKind.Weapon => MinWeapons,
        ToggleKind.Item => MinItems,
        _ => MinTomes,
    });

    /// <summary>Everything the page lists: what this profile's runs can offer.</summary>
    public static List<ToggleEntry> Entries()
    {
        var list = new List<ToggleEntry>();
        foreach (WeaponDef w in MetaContent.OfferWeapons(Catalog.Weapons, toggled: false))
            list.Add(new ToggleEntry(ToggleKind.Weapon, w.Id, w.DisplayName, w.Rarity, w));
        foreach (ItemDef i in MetaContent.OfferItems(Catalog.Items, toggled: false))
            list.Add(new ToggleEntry(ToggleKind.Item, i.Id, i.DisplayName, i.Rarity, i));
        foreach (TomeDef t in Tomes.All)
            list.Add(new ToggleEntry(ToggleKind.Tome, t.Id, t.DisplayName, t.Rarity, t));
        return list;
    }

    /// <summary>True when switching <paramref name="e"/> off would leave its shelf
    /// below the minimum.</summary>
    public static bool AtMinimum(ToggleEntry e, IReadOnlyList<ToggleEntry> all)
    {
        int total = all.Count(x => x.Kind == e.Kind);
        int on = all.Count(x => x.Kind == e.Kind && !IsOff(x.Id));
        return on <= MinOf(e.Kind, total);
    }

    /// <summary>
    /// A run's list with the switched-off entries removed (a no-op until the
    /// Toggler is owned). Keeps at least the shelf minimum on, re-enabling in
    /// list order when needed.
    /// </summary>
    public static List<T> Filter<T>(List<T> list, Func<T, string> id, ToggleKind kind)
        => !Owned || list.Count == 0 ? list : Apply(list, id, kind, IsOff);

    /// <summary>The filter itself, with the off-test supplied (the audit
    /// drives it without a profile).</summary>
    public static List<T> Apply<T>(List<T> list, Func<T, string> id, ToggleKind kind, Func<string, bool> isOff)
    {
        int min = MinOf(kind, list.Count);
        var kept = list.Where(x => !isOff(id(x))).ToList();
        if (kept.Count < min)
            foreach (T x in list)
            {
                if (kept.Count >= min) break;
                if (!kept.Contains(x)) kept.Add(x);
            }
        if (kept.Count != list.Count)
            GD.Print($"META toggler {kind}: {list.Count - kept.Count} off, {kept.Count} on");
        return kept;
    }

    /// <summary>Tome ids switched off for this run, for the offer pool.</summary>
    public static HashSet<string> DisabledTomes()
    {
        var off = new HashSet<string>();
        if (!Owned) return off;
        List<TomeDef> kept = Filter(Tomes.All.ToList(), t => t.Id, ToggleKind.Tome);
        foreach (TomeDef t in Tomes.All) if (!kept.Contains(t)) off.Add(t.Id);
        return off;
    }

    /// <summary>Count switched off on one shelf (for the page header).</summary>
    public static int OffCount(IEnumerable<ToggleEntry> all, ToggleKind kind) => all.Count(e => e.Kind == kind && IsOff(e.Id));
}
