using System.Collections.Generic;
using System.Linq;
using Wavebreak.Combat;
using Wavebreak.Meta;

namespace Wavebreak.Data;

/// <summary>
/// The evolution table (Data/Evolutions.cs): at least 18 rows covering the
/// original eighteen weapons, real and unique ids, a real behaviour change on
/// every row (a delivery or a rider, never numbers alone), Arabic for every
/// name and description, and a catalyst a fresh profile can actually be offered.
/// Each evolved form is fired for real.
/// </summary>
public static partial class ContentAudit
{
    private const int BaseWeaponCount = 18;

    private static void AuditEvolutions(WeaponSystem system)
    {
        IReadOnlyList<EvolutionDef> all = Evolutions.All;
        Check(all.Count >= 18, $"evolutions: only {all.Count} rows");
        foreach (WeaponDef w in Catalog.Weapons.Take(BaseWeaponCount))
            Check(Evolutions.ForWeapon(w.Id) is not null, $"evolutions: base weapon {w.Id} has no evolution");

        foreach (var dup in all.GroupBy(e => e.Id).Where(g => g.Count() > 1)) Check(false, $"evolutions: duplicate id {dup.Key}");
        foreach (var dup in all.GroupBy(e => e.WeaponId).Where(g => g.Count() > 1)) Check(false, $"evolutions: two rows for weapon {dup.Key}");
        foreach (var dup in all.GroupBy(e => e.CatalystId).Where(g => g.Count() > 1)) Check(false, $"evolutions: catalyst {dup.Key} used twice");
        foreach (var dup in all.GroupBy(e => e.Name).Where(g => g.Count() > 1)) Check(false, $"evolutions: name {dup.Key} used twice");

        foreach (EvolutionDef e in all)
        {
            WeaponDef? weapon = Catalog.WeaponById(e.WeaponId);
            Check(weapon is not null, $"evolution {e.Id}: no weapon {e.WeaponId}");
            Check(!Combat.Weapons.Signatures.Is(e.WeaponId), $"evolution {e.Id}: signature weapons do not evolve");
            Check(e.Delivery != EvolutionDelivery.Keep || e.Rider != EvolutionRider.None, $"evolution {e.Id}: numbers only, no behaviour change");
            Check(e.DamageScale > 0f && e.CooldownScale > 0f, $"evolution {e.Id}: bad scales");
            Check(e.Tint != (weapon?.Tint ?? e.Tint) || weapon is null, $"evolution {e.Id}: same tint as the weapon");
            Check(e.Trail.A > 0f, $"evolution {e.Id}: no trail");
            Check(!string.IsNullOrWhiteSpace(e.Name) && !string.IsNullOrWhiteSpace(e.Description), $"evolution {e.Id}: no text");
            Check(Loc.ArabicFor(e.Name) is not null, $"evolution {e.Id}: no Arabic name");
            Check(Loc.ArabicFor(e.Description) is not null, $"evolution {e.Id}: no Arabic description");

            // A tome or an item with a real pool weight and no unlock in front of
            // it: the pairing must be reachable on a brand-new profile.
            TomeDef? tome = Tomes.ById(e.CatalystId);
            ItemDef? item = Catalog.ItemById(e.CatalystId);
            Check(tome is not null || item is not null, $"evolution {e.Id}: no catalyst {e.CatalystId}");
            Check(tome is not PerkDef, $"evolution {e.Id}: a perk cannot be a catalyst");
            Check((tome?.Weight ?? 0f) > 0f || (item?.Weight ?? 0f) > 0f, $"evolution {e.Id}: catalyst {e.CatalystId} is never offered");
            Check(Unlocks.SourceOf(e.CatalystId) is null, $"evolution {e.Id}: catalyst {e.CatalystId} is locked behind a quest");
            Check(Catalog.Items.Contains(item) || tome is not null, $"evolution {e.Id}: catalyst {e.CatalystId} is not in the pool");

            if (weapon is null) continue;
            WeaponDef evolved = e.Evolve(weapon);
            Check(evolved.Id == weapon.Id, $"evolution {e.Id}: the evolved form changed the weapon id");
            Check(evolved != weapon && weapon.DisplayName != e.Name, $"evolution {e.Id}: the catalogue weapon was edited");
            (int hits, float damage) = EvolvedFireTest(system, weapon, e);
            Check(hits > 0, $"evolution {e.Id}: never hit");
            Line($"evolution {e.Id,-24} {weapon.Id,-18} + {e.CatalystId,-18} {e.Delivery,-9} {e.Rider,-7} hits={hits} dmg={damage:0}");
        }
        Line($"evolutions count={all.Count}");
    }

    private static (int hits, float damage) EvolvedFireTest(WeaponSystem system, WeaponDef def, EvolutionDef evolution)
    {
        _fireEvolution = evolution;
        (int hits, float damage) result = FireTest(system, def, def.MaxLevel);
        _fireEvolution = null;
        return result;
    }
}
