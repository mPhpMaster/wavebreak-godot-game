using System;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Meta;
using Wavebreak.Progression;

namespace Wavebreak.UI;

internal static class BuildInspectionStamp
{
    internal static int Compute(WeaponSystem weapons, Inventory inventory, TomeBook? book)
    {
        var hash = new HashCode();
        hash.Add(Loc.Language); hash.Add(weapons.Slots); hash.Add(book?.Slots);
        foreach (WeaponRuntime weapon in weapons.Weapons) { hash.Add(weapon.Def.Id); hash.Add(weapon.Level); }
        if (book is not null)
            foreach (TomeDef tome in book.Held) { hash.Add(tome.Id); hash.Add(book.LevelOf(tome)); }
        foreach (ItemStack item in inventory.Items) { hash.Add(item.Id); hash.Add(item.Count); }
        if (LevelUpChoices.Current is { } choices)
        { hash.Add(choices.RerollsLeft); hash.Add(choices.BanishesLeft); hash.Add(choices.SkipsLeft); }
        return hash.ToHashCode();
    }
}
