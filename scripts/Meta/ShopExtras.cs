using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Game;

namespace Wavebreak.Meta;

/// <summary>
/// Pearl Shop lines that are not stats, and the run hook for everything this
/// layer adds (map challenges, the Toggler, slot upgrades, the cute shrines).
///
/// The lines are appended to <see cref="MetaShop.All"/>, so buying, pricing,
/// saving and refunding are the shop's own. They carry no stat: the run reads
/// their levels here.
///
///   Toggler      1 level   unlocks the Toggler page (Meta/Toggler, UI/TogglerPage)
///   Weapon Rack  3 levels  +1 weapon slot each (base 6, WeaponSystem.Slots)
///   Bookshelf    3 levels  +1 tome slot each (base 4, TomeBook.Slots)
/// </summary>
public static class ShopExtras
{
    public const string TogglerId = "toggler";
    public const string WeaponSlotId = "weapon_slot";
    public const string TomeSlotId = "tome_slot";

    public static readonly IReadOnlyList<ShopUpgradeDef> Upgrades = new List<ShopUpgradeDef>
    {
        new() { Id = TogglerId, Name = "The Toggler", Blurb = "Switch off weapons, items and tomes you never want offered.",
                PerLevel = 1f, MaxLevel = 1, BaseCost = 150,
                Describe = v => v > 0f ? "Owned: press to open the Toggler" : "Unlocks the Toggler page" },
        new() { Id = WeaponSlotId, Name = "Weapon Rack", Blurb = "Carry one more weapon at a time.",
                PerLevel = 1f, MaxLevel = 3, BaseCost = 120,
                Describe = v => $"+{v:0} weapon slot{(v == 1f ? "" : "s")} ({Combat.WeaponSystem.MaxWeapons + (int)v} total)" },
        new() { Id = TomeSlotId, Name = "Bookshelf", Blurb = "Study one more tome at a time.",
                PerLevel = 1f, MaxLevel = 3, BaseCost = 100,
                Describe = v => $"+{v:0} tome slot{(v == 1f ? "" : "s")} ({Data.Tomes.MaxSlots + (int)v} total)" },
    };

    private static int Level(string id) => MetaShop.ById(id) is { } u ? MetaShop.LevelOf(u) : 0;

    public static bool TogglerOwned => Level(TogglerId) > 0;
    public static int BonusWeaponSlots => Level(WeaponSlotId);
    public static int BonusTomeSlots => Level(TomeSlotId);

    /// <summary>Highest weapon slot count any profile can reach (HUD layout).</summary>
    public static int MaxWeaponSlots => Combat.WeaponSystem.MaxWeapons + 3;

    public static bool IsToggler(ShopUpgradeDef u) => u.Id == TogglerId;

    // ---------------------------------------------------------------- run

    /// <summary>One call from RunMeta.Attach, after the challenges.</summary>
    public static void Attach(Main main)
    {
        int weaponSlots = Combat.WeaponSystem.MaxWeapons + BonusWeaponSlots;
        main.Weapons.Slots = weaponSlots;
        if (Progression.TomeBook.Current is { } book) book.Slots = Data.Tomes.MaxSlots + BonusTomeSlots;
        if (Progression.OfferPool.Current is { } pool)
            foreach (string id in Toggler.DisabledTomes()) pool.DisabledTomes.Add(id);

        main.AddChild(new MapChallengeRuntime(main));
        CuteShrineSpawner.Attach(main, main.Run.Seed);
        GD.Print($"META extras weaponslots={weaponSlots} tomeslots={Progression.TomeBook.Current?.Slots ?? Data.Tomes.MaxSlots} " +
                 $"toggler={(TogglerOwned ? "owned" : "no")} mapchallenge={MapChallenges.Active?.Id ?? "none"}");
    }
}
