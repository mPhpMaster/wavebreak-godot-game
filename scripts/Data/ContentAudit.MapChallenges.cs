using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Meta;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary><c>--validate-content</c> checks for the map challenges, the
/// Toggler, the slot upgrades and the cute shrines.</summary>
public static partial class ContentAudit
{
    private static void AuditMapChallenges()
    {
        // ---- map challenges: shape, references, rewards.
        IReadOnlyList<MapChallengeDef> all = MapChallenges.All;
        Check(all.Count == 55, $"mapchal: expected 55 map challenges, have {all.Count}");
        var ids = new HashSet<string>();
        var names = new HashSet<string>();
        foreach (MapChallengeDef c in all)
        {
            Check(ids.Add(c.Id), $"mapchal: duplicate id {c.Id}");
            Check(names.Add(c.Name), $"mapchal: duplicate name {c.Name}");
            Check(c.Map is not null, $"mapchal: {c.Id} names unknown map {c.MapId}");
            Check(Routes.ById("start:" + c.MapId) is not null, $"mapchal: no start route for {c.MapId}");
            Check(c.Name.Length > 0 && c.Blurb.Length > 0, $"mapchal: {c.Id} has no text");
            Check(c.Pearls > 0, $"mapchal: {c.Id} pays nothing");
            Check(c.Tier is >= 0 and <= 3, $"mapchal: {c.Id} tier {c.Tier}");
            foreach (string m in c.Modifiers) Check(Challenges.ById(m) is not null, $"mapchal: {c.Id} unknown modifier {m}");
            Check(c.Modifiers.Distinct().Count() == c.Modifiers.Length, $"mapchal: {c.Id} repeats a modifier");
            switch (c.Cosmetic)
            {
                case CosmeticKind.Hat:
                    HatDef? hat = Hats.ById(c.CosmeticId);
                    Check(hat is not null && hat.Id != Hats.NoneId, $"mapchal: {c.Id} unknown hat {c.CosmeticId}");
                    Check(hat is null || hat.PearlCost > 0 || hat.UnlockedBy is not null, $"mapchal: {c.Id} hat {c.CosmeticId} is free already");
                    break;
                case CosmeticKind.Skin:
                    bool exists = Characters.All.Any(ch => ch.Skins.Any(s => s.Id == c.CosmeticId));
                    Check(exists, $"mapchal: {c.Id} unknown skin {c.CosmeticId}");
                    Check(Unlocks.SourceOf(c.CosmeticId) is not null, $"mapchal: {c.Id} skin {c.CosmeticId} has no quest to grant");
                    break;
            }
            Check(c.RulesText.Length > 0 && c.GoalText.Length > 0 && c.RewardText.Length > 0, $"mapchal: {c.Id} text");
        }
        foreach (string map in MapChallenges.MapOrder)
        {
            var mine = MapChallenges.ForMap(map).ToList();
            Check(Stages.ById(map) is not null, $"mapchal: map order names unknown map {map}");
            Check(mine.Count == 11, $"mapchal: {map} has {mine.Count} challenges (expected 11)");
            foreach (MapGoal g in new[] { MapGoal.Survive, MapGoal.Clear, MapGoal.Swarm })
                Check(mine.Any(c => c.Goal == g), $"mapchal: {map} has no {g} challenge");
            for (int tier = 1; tier <= 3; tier++)
                Check(mine.Count(c => c.Tier == tier) == 1, $"mapchal: {map} needs exactly one tier-{tier} challenge");
            Check(mine.Any(c => c.Twist != MapTwist.None), $"mapchal: {map} has no map twist");
        }
        Check(Stages.All.All(s => MapChallenges.MapOrder.Contains(s.Id)), "mapchal: a map has no challenges");
        int cosmetics = all.Count(c => c.Cosmetic != CosmeticKind.None);
        Check(cosmetics >= 10, $"mapchal: only {cosmetics} cosmetic rewards");
        Check(all.Where(c => c.Cosmetic != CosmeticKind.None).Select(c => c.CosmeticId).Distinct().Count() == cosmetics,
              "mapchal: a cosmetic is the reward of two challenges");

        // ---- twists change a copy, never the shared map table.
        foreach (MapTwist twist in System.Enum.GetValues<MapTwist>())
        {
            foreach (StageDef map in Stages.All)
            {
                int before = map.HazardPools;
                var copy = (StageDef)map.Duplicate();
                StageDef t = MapChallenges.ApplyTwist(copy, twist);
                Check(map.HazardPools == before, $"mapchal: twist {twist} edited {map.Id} itself");
                bool hazard = twist is MapTwist.Brambles or MapTwist.Frost or MapTwist.RestlessGraves or MapTwist.HighTide or MapTwist.LavaFloor;
                if (hazard) Check(t.HazardPools > 0 && t.HazardRadius > 0f && t.HazardName.Length > 0, $"mapchal: twist {twist} on {map.Id} places no pools");
            }
            if (twist != MapTwist.None) Check(MapChallenges.TwistName(twist).Length > 0, $"mapchal: twist {twist} has no name");
        }
        Line($"mapchal count={all.Count} maps={MapChallenges.MapOrder.Length} cosmetics={cosmetics} " +
             $"goals=survive:{all.Count(c => c.Goal == MapGoal.Survive)},clear:{all.Count(c => c.Goal == MapGoal.Clear)},swarm:{all.Count(c => c.Goal == MapGoal.Swarm)} " +
             $"pearls={all.Sum(c => c.Pearls)}");

        // ---- shop lines.
        foreach (string id in new[] { ShopExtras.TogglerId, ShopExtras.WeaponSlotId, ShopExtras.TomeSlotId })
        {
            ShopUpgradeDef? u = MetaShop.ById(id);
            Check(u is not null, $"extras: shop line {id} missing");
            if (u is null) continue;
            Check(u.Stat is null, $"extras: shop line {id} should not be a stat");
            for (int l = 1; l < u.MaxLevel; l++)
                Check(MetaShop.CostOf(u, l) > MetaShop.CostOf(u, l - 1), $"extras: {id} cost does not rise at level {l}");
            Check(u.Describe(1f).Length > 0, $"extras: {id} has no description");
        }

        // ---- Toggler: the minimum holds, and an unowned Toggler changes nothing.
        var weapons = MetaContent.OfferWeapons(Catalog.Weapons, toggled: false);
        var items = MetaContent.OfferItems(Catalog.Items, toggled: false);
        var allOffW = Toggler.Apply(weapons, w => w.Id, ToggleKind.Weapon, _ => true);
        var allOffI = Toggler.Apply(items, i => i.Id, ToggleKind.Item, _ => true);
        var allOffT = Toggler.Apply(Tomes.All.ToList(), t => t.Id, ToggleKind.Tome, _ => true);
        Check(allOffW.Count == Toggler.MinOf(ToggleKind.Weapon, weapons.Count) && allOffW.Count >= System.Math.Min(weapons.Count, WeaponSystem.MaxWeapons + 3),
              $"toggler: all weapons off keeps {allOffW.Count}");
        Check(allOffI.Count == Toggler.MinOf(ToggleKind.Item, items.Count), $"toggler: all items off keeps {allOffI.Count}");
        Check(allOffT.Count == Toggler.MinOf(ToggleKind.Tome, Tomes.All.Count) && allOffT.Count > Tomes.MaxSlots + 3 - 1,
              $"toggler: all tomes off keeps {allOffT.Count}");
        var oneOff = Toggler.Apply(weapons, w => w.Id, ToggleKind.Weapon, id => id == weapons[0].Id);
        Check(oneOff.Count == weapons.Count - 1 && !oneOff.Contains(weapons[0]), "toggler: switching one weapon off");
        Check(Toggler.Filter(weapons, w => w.Id, ToggleKind.Weapon).Count == weapons.Count || Toggler.Owned,
              "toggler: an unowned Toggler filtered the pool");
        // Signature weapons belong to a character and are never listed (Combat/Weapons/Signatures.cs).
        int listedWeapons = weapons.Count(w => !Combat.Weapons.Signatures.Is(w.Id));
        Check(Toggler.Entries().Count == listedWeapons + items.Count + Tomes.All.Count, "toggler: page entries");

        // Disabled tomes never come up, in level-ups.
        OfferPool? runPool = OfferPool.Current;
        try
        {
            var pool = new OfferPool(items, weapons, new Rng(0x7066E));
            pool.Tomes = new TomeBook(new StatSheet(), publish: false);
            foreach (TomeDef t in Tomes.All.Skip(1)) pool.DisabledTomes.Add(t.Id);
            int offered = 0, wrong = 0;
            for (int i = 0; i < 300; i++)
                foreach (Offer o in pool.Roll(3, null, null, null))
                {
                    if (o.Tome is null) continue;
                    offered++;
                    if (o.Tome != Tomes.All[0]) wrong++;
                }
            Check(wrong == 0 && offered > 0, $"toggler: disabled tomes offered {wrong}x (first tome {offered}x)");

            // Slots: a full system offers no new weapons; a free slot does.
            var system = new WeaponSystem { Slots = 0 };
            Check(system.IsFull && !system.CanAdd(weapons[0]), "slots: a 0-slot system still takes a weapon");
            int newOffers = 0;
            for (int i = 0; i < 100; i++)
                newOffers += pool.Roll(3, system, null, null).Count(o => o.Weapon is not null && o.Upgrade is null);
            Check(newOffers == 0, $"slots: {newOffers} new-weapon offers with no free slot");
            system.Slots = 1;
            Check(!system.IsFull && system.CanAdd(weapons[0]), "slots: a free slot refuses a weapon");
            system.Free();
        }
        finally
        {
            OfferPool.Current = runPool;
            if (Game.Main.Instance?.Run?.Rng is { } rr) Actors.PickupField.Ensure(rr);
        }

        // ---- cute shrines.
        Check(CuteShrines.BalanceStats.Select(b => b.Stat).Distinct().Count() == CuteShrines.BalanceStats.Count, "cute: duplicate balance stat");
        var sheet = new StatSheet();
        Check(CuteShrines.PlanBalance(sheet) is null, "cute: balance on a bare sheet should refuse");
        var lop = new object();
        sheet.Add(Stat.Damage, ModifierOp.Additive, 0.64f, lop);   // 8 units
        var plan = CuteShrines.PlanBalance(sheet);
        Check(plan is { } p0 && p0.Top.Stat == Stat.Damage && Mathf.IsEqualApprox(p0.Take, 4f, 0.01f), "cute: balance picks the heaviest stat");
        if (plan is { } p)
        {
            var before = CuteShrines.BalanceStats.ToDictionary(b => b.Stat, b => sheet.Get(b.Stat));
            var grant = new object();
            CuteShrines.ApplyBalance(sheet, p, grant);
            Check(sheet.Get(Stat.Damage) < before[Stat.Damage], "cute: balance did not lower the top stat");
            Check(CuteShrines.BalanceStats.Where(b => b.Stat != Stat.Damage).All(b => sheet.Get(b.Stat) > before[b.Stat]),
                  "cute: balance did not raise every other stat");
            float unitsAfter = CuteShrines.BalanceStats.Sum(b => CuteShrines.Units(sheet, b));
            Check(unitsAfter > 8f, $"cute: balance should add up to more than it took ({unitsAfter:0.00}u)");
            sheet.RemoveAllFrom(grant);
            Check(CuteShrines.BalanceStats.All(b => Mathf.IsEqualApprox(sheet.Get(b.Stat), before[b.Stat])), "cute: balance leaves residue");
        }
        for (int n = 1; n < 8; n++) Check(CuteShrines.WellPrice(n) > CuteShrines.WellPrice(n - 1), "cute: well price must rise");
        Check(CuteShrines.WellBonus(-1f) >= 0f && CuteShrines.WellBonus(100f) <= CuteShrines.WellBonusMax, "cute: well bonus chance out of range");
        foreach (StageDef s in Stages.All)
            Check(s.BalanceShrines is >= 0 and <= 3 && s.WishingWells is >= 0 and <= 3, $"cute: {s.Id} shrine counts");

        // ---- icons for the new things.
        foreach (string glyph in new[] { "scales", "well", "toggle", "flag", "slot" })
            Check(UI.Icons.Exists(glyph), $"icons: missing glyph {glyph}");
        foreach (BalanceStat b in CuteShrines.BalanceStats)
            Check(UI.Icons.Exists(b.Glyph), $"icons: balance glyph {b.Glyph}");
        Line($"extras toggler-min={Toggler.MinWeapons}w/{Toggler.MinItems}i/{Toggler.MinTomes}t weapons={weapons.Count} items={items.Count} tomes={Tomes.All.Count}");
    }
}
