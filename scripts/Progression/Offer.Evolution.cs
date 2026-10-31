using System.Collections.Generic;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.Progression;

/// <summary>
/// The gold EVOLUTION card. Not a weapon card: it never levels anything and is
/// not in the pool, so it has no <see cref="Source"/> and cannot be banished.
/// It sits beside a normal hand instead of taking a slot in it, and turning it
/// down costs nothing: it comes back on the next level-up or chest.
/// </summary>
public sealed partial class Offer
{
    /// <summary>The evolution this card applies, or null.</summary>
    public EvolutionDef? Evolution { get; private set; }

    public bool IsEvolution => Evolution is not null;

    /// <summary>The "not now" half of a chest's evolution hand.</summary>
    public bool IsDecline { get; private set; }

    internal static Offer ForEvolution(EvolutionDef e)
    {
        string recipe = Loc.F("{0} + {1}", Loc.T(Evolutions.WeaponName(e)), Loc.T(Evolutions.CatalystName(e)));
        return new Offer(e.Name, "Evolution", e.Description, recipe, Rarity.Legendary, e.Tint,
                         detailText: Loc.T("EVOLUTION")) { Evolution = e };
    }

    internal static Offer DeclineEvolution()
        => new("Not now", "Evolution", "The next level-up or chest offers the evolution again.", "",
               Rarity.Common, new Godot.Color(0.7f, 0.74f, 0.82f)) { IsDecline = true };
}

/// <summary>
/// The one rule, stated once: a weapon at max level whose catalyst is held is
/// ready, and the next level-up hand (a fourth card) or the next chest (after its
/// reward) offers its evolution. One card per hand; the panel, the chest, the
/// autopilot and the self-check all ask here.
/// </summary>
public static class EvolutionOffers
{
    /// <summary>How much a held weapon draws its catalyst. One card in a pool
    /// of about a hundred turns up every few dozen hands, so without this a pair
    /// is a lottery and a build cannot aim at one.</summary>
    public const float CatalystPull = 6f;

    /// <summary>How much a held catalyst draws its weapon's cards: seven upgrades
    /// on one weapon need to fit inside a run. Measured with the sim.</summary>
    public const float WeaponPull = 10f;

    /// <summary>Multiplier on a card's pool weight from the pairing. 1 for
    /// anything not half of an unfinished pair, so an unpaired build rolls
    /// exactly what it rolled before.</summary>
    public static float Pull(string id)
    {
        WeaponSystem? weapons = Game.Main.Instance?.Weapons;
        if (weapons is null) return 1f;
        if (Evolutions.ForWeapon(id) is { } byWeapon && weapons.Get(id)?.Evolution is null)
            return HasCatalyst(byWeapon, Inventory.Current, TomeBook.Current) ? WeaponPull : 1f;
        if (Evolutions.ForCatalyst(id) is { } e && weapons.Get(e.WeaponId) is { Evolution: null })
            return HasCatalyst(e, Inventory.Current, TomeBook.Current) ? 1f : CatalystPull;
        return 1f;
    }

    public static bool HasCatalyst(EvolutionDef e, Inventory? inventory, TomeBook? book)
        => (inventory?.StacksOf(e.CatalystId) ?? 0) > 0 || (book?.LevelOf(e.CatalystId) ?? 0) > 0;

    /// <summary>The first held weapon that can evolve now, in slot order.</summary>
    public static EvolutionDef? NextReady(WeaponSystem? weapons, Inventory? inventory, TomeBook? book)
    {
        if (weapons is null) return null;
        IReadOnlyList<WeaponRuntime> held = weapons.Weapons;
        for (int i = 0; i < held.Count; i++)
        {
            WeaponRuntime w = held[i];
            if (w.Evolution is not null || w.Level < w.Def.MaxLevel) continue;
            if (Evolutions.ForWeapon(w.Def.Id) is { } e && HasCatalyst(e, inventory, book)) return e;
        }
        return null;
    }

    /// <summary>What a chest shows when something is ready, or null.</summary>
    public static List<Offer>? ChestHand(WeaponSystem? weapons, Inventory? inventory, TomeBook? book)
        => NextReady(weapons, inventory, book) is { } e
            ? new List<Offer> { Offer.ForEvolution(e), Offer.DeclineEvolution() }
            : null;
}
