using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>Audit of rolled weapon upgrades and of the element/status system.</summary>
public static partial class ContentAudit
{
    private static void AuditUpgrades(WeaponSystem system)
    {
        var rng = new Rng(0x0FF5E7);
        var byRarity = new int[5];
        var lucky = new int[5];

        foreach (WeaponDef def in Catalog.Weapons.Concat(Meta.MetaContent.Weapons))
        {
            HashSet<UpgradeStat> eligible = WeaponUpgrades.Eligible(def).ToHashSet();
            bool bad = false;
            for (int i = 0; i < 200; i++)
            {
                WeaponUpgrade up = WeaponUpgrades.Roll(def, rng, 0f);
                byRarity[(int)up.Rarity]++;
                int min = up.Rarity switch { Rarity.Common or Rarity.Uncommon => 1, Rarity.Rare or Rarity.Epic => 2, _ => 3 };
                int max = up.Rarity switch { Rarity.Common => 1, Rarity.Uncommon or Rarity.Rare => 2, _ => 3 };
                int n = up.Lines.Count;
                bool countOk = n >= Math.Min(min, eligible.Count) && n <= max;
                bool distinct = up.Lines.Select(l => l.Stat).Distinct().Count() == n;
                bool valid = up.Lines.All(l => l.Value > 0f && float.IsFinite(l.Value) && eligible.Contains(l.Stat));
                bool core = n > 0 && up.Lines[0].Stat is UpgradeStat.Damage or UpgradeStat.AttackSpeed or UpgradeStat.Projectiles;
                if (!(countOk && distinct && valid && core))
                {
                    if (!bad) Check(false, $"upgrade roll for {def.Id} malformed: {up}");
                    bad = true;
                }
                lucky[(int)WeaponUpgrades.Roll(def, rng, 4f).Rarity]++;
            }

            // A full random climb to max level must stay sane and must grow.
            var runtime = new WeaponRuntime(def);
            float before = runtime.BaseDamage * runtime.BaseProjectiles / runtime.BaseCooldown;
            while (runtime.CanLevel) runtime.LevelUp(WeaponUpgrades.Roll(def, rng, 2f, 1));
            float after = runtime.BaseDamage * runtime.BaseProjectiles / runtime.BaseCooldown;
            Check(runtime.Level == def.MaxLevel, $"upgrade climb {def.Id} stopped at L{runtime.Level}");
            Check(after > before && runtime.BaseCooldown >= 0.05f && runtime.BaseProjectiles <= def.Projectiles + 2 * def.MaxLevel,
                  $"upgrade climb {def.Id} power {before:0.0}->{after:0.0} cd={runtime.BaseCooldown:0.00} proj={runtime.BaseProjectiles}");
            runtime.Dispose();
        }

        Check(byRarity[0] > byRarity[1] && byRarity[1] > byRarity[2] && byRarity[2] > byRarity[3] && byRarity[3] > byRarity[4],
              "upgrade rarities not descending at luck 0: " + string.Join("/", byRarity));
        Check(lucky[4] > byRarity[4] && lucky[0] < byRarity[0], "luck does not shift upgrade rarity");
        Line($"upgrades rarity luck0={string.Join("/", byRarity)} luck4={string.Join("/", lucky)}");

        // Held weapons must be offered with a rolled upgrade and the card's
        // rarity must be the upgrade's.
        var pool = new OfferPool(Catalog.Items, Catalog.Weapons, new Rng(0xB0B));
        int upgrades = 0;
        for (int r = 0; r < 600; r++)
        {
            foreach (Offer o in pool.Roll(3, system, Inventory.Current, system.Stats))
            {
                if (o.Weapon is null || !system.Has(o.Weapon.Id)) continue;
                upgrades++;
                Check(o.Upgrade is not null && o.Rarity == o.Upgrade.Rarity && o.Effect.Length > 0,
                      $"held weapon {o.Weapon.Id} offered without a rolled upgrade");
            }
        }
        if (Main.Instance?.Run?.Rng is { } runRng) PickupField.Ensure(runRng);
        Check(system.Weapons.Count == 0 || upgrades > 0, "no weapon upgrade offered in 600 rolls");
        Line($"upgrade offers in 600 rolls: {upgrades}");
    }

    private static void AuditElements()
    {
        foreach (Element e in new[] { Element.Fire, Element.Ice, Element.Lightning, Element.Poison })
        {
            Check(StatusEffects.BaseChance(e) > 0f, $"element {e} has no base status chance");
            Check(Catalog.Weapons.Any(w => w.Element == e), $"no weapon carries {e}");
        }
        foreach (Stat s in new[] { Stat.ElementalDamage, Stat.StatusChance, Stat.StatusDuration, Stat.MegacritChance,
                                   Stat.IgniteChance, Stat.ChillChance, Stat.ShockChance, Stat.PoisonChance,
                                   Stat.FreezePower, Stat.UpgradeStats })
            Check(Catalog.StatName(s) != s.ToString(),$"stat {s} has no display name");

        Check(Catalog.Items.Any(i => i.Effects.Any(e => e.Stat == Stat.MegacritChance)), "no item grants megacrit");

        // Live statuses on a real enemy, far from the player.
        Vector3 feet = (Main.Instance?.Player?.GlobalPosition ?? Vector3.Zero) + new Vector3(0f, 0f, 45f);
        EnemyDef? grunt = Catalog.Enemies.FirstOrDefault(d => !d.IsBoss && !d.IsElite && d.SplitCount == 0);
        EnemyDef? boss = Catalog.Enemies.FirstOrDefault(d => d.IsBoss);
        if (grunt is null || Main.Instance?.Weapons is not { } system) return;

        Enemy e1 = AuditEnemy(system, grunt, feet, 1000f);
        float hp = e1.Health;
        e1.ApplyBurn(10f, 2f);
        e1.ApplyPoison(5f, 2f);
        e1.ApplyPoison(5f, 2f);
        StepFor(e1, feet, 1.1f);
        float dot = hp - e1.Health;
        Check(dot > 15f && dot < 30f, $"burn+poison dealt {dot:0.0} in 1.1s, expected ~20");
        bool froze = false;
        for (int i = 0; i < StatusEffects.FreezeStacks && !froze; i++) froze = e1.ApplyChill(2f, StatusEffects.FreezeStacks, 1f);
        Check(froze && e1.IsFrozen, "five chill stacks did not freeze");
        Vector3 at = e1.GlobalPosition;
        StepFor(e1, feet + new Vector3(0f, 0f, -40f), 0.5f);
        Vector3 moved = e1.GlobalPosition - at; moved.Y = 0f;
        Check(moved.Length() < 0.3f, "frozen enemy moved");
        StepFor(e1, feet, 2.5f);
        Check(!e1.IsFrozen && !e1.IsBurning && !e1.IsPoisoned, "statuses did not expire");
        Line($"status grunt {grunt.Id}: dot1s={dot:0.0} froze={froze}");
        Release(e1);

        if (boss is null) return;
        Enemy b = AuditEnemy(system, boss, feet + new Vector3(8f, 0f, 0f), 1000f);
        bool bossFroze = false;
        for (int i = 0; i < StatusEffects.FreezeStacks && !bossFroze; i++) bossFroze = b.ApplyChill(2f, StatusEffects.FreezeStacks, 1f);
        float bossFreeze = 0f;
        while (b.IsFrozen && bossFreeze < 3f) { StepFor(b, feet, 1f / 60f); bossFreeze += 1f / 60f; }
        Check(bossFroze && bossFreeze < 0.5f, $"boss freeze lasted {bossFreeze:0.00}s, expected ~0.3");
        b.ApplyStun(1f);
        Check(!b.IsStunned, "boss stunned straight out of a freeze (no immunity)");
        Line($"status boss {boss.Id}: freeze={bossFreeze:0.00}s");
        Release(b);
    }

    private static Enemy AuditEnemy(WeaponSystem system, EnemyDef def, Vector3 at, float health)
    {
        var enemy = new Enemy { Name = $"Audit_status_{def.Id}" };
        system.AddChild(enemy);
        enemy.Configure(def, health / MathF.Max(1f, def.Health), 0f, 1f);
        enemy.Spawn(at);
        return enemy;
    }

    private static void StepFor(Enemy enemy, Vector3 target, float seconds)
    {
        const float dt = 1f / 60f;
        for (float t = 0f; t < seconds && enemy.IsAlive; t += dt)
            enemy.Step(dt, target, null, Vector3.Zero);
    }

    private static void Release(Enemy enemy)
    {
        if (enemy.IsAlive) enemy.Despawn();
        enemy.QueueFree();
        EnemyManager.Instance?.DespawnBelow(-100f);
    }
}
