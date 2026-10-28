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

public static partial class ContentAudit
{
    // ------------------------------------------------------------------- ids

    private static void AuditIds()
    {
        var all = new List<string>();
        all.AddRange(Catalog.Weapons.Select(w => w.Id));
        all.AddRange(Catalog.Items.Select(i => i.Id));
        all.AddRange(Catalog.Enemies.Select(e => e.Id));

        foreach (string id in all) Check(!string.IsNullOrWhiteSpace(id), "empty id");
        foreach (var dup in all.GroupBy(x => x).Where(g => g.Count() > 1))
            Check(false, $"duplicate id {dup.Key}");

        foreach (WeaponDef w in Catalog.Weapons)
            Check(Catalog.WeaponById(w.Id) == w && w.DisplayName.Length > 0 && w.Description.Length > 0, $"weapon lookup/name {w.Id}");
        foreach (ItemDef i in Catalog.Items)
            Check(Catalog.ItemById(i.Id) == i && i.DisplayName.Length > 0 && i.Description.Length > 0, $"item lookup/name {i.Id}");
        foreach (EnemyDef e in Catalog.Enemies)
            Check(Catalog.EnemyById(e.Id) == e && e.DisplayName.Length > 0, $"enemy lookup/name {e.Id}");
    }

    // --------------------------------------------------------------- weapons

    private sealed class FakeTarget : IDamageable
    {
        public FakeTarget(Vector3 p) => Position = p;
        public Vector3 Position { get; }
        public bool IsAlive => true;
        public int Hits;
        public float Damage;

        public void TakeDamage(DamageInfo info)
        {
            Hits++;
            Damage += MathF.Max(0f, info.Amount);
        }
    }

    private static void AuditWeapons(WeaponSystem system)
    {
        foreach (WeaponDef def in Catalog.Weapons)
        {
            float dps1 = def.DamageAtLevel(1) * def.ProjectilesAtLevel(1) / def.CooldownAtLevel(1);
            Check(def.Damage > 0f && def.Cooldown > 0f && def.Projectiles >= 1 && def.MaxLevel >= 1 && def.Range > 0f,
                  $"weapon numbers {def.Id}");
            // Levels are rolled upgrades now (Combat/WeaponUpgrades), so the
            // per-level fields only seed the damage/speed roll sizes.
            Check(WeaponUpgrades.EligibleLines(def) >= 3, $"weapon {def.Id} has fewer than 3 upgrade lines");

            (int hits1, float dmg1) = FireTest(system, def, 1);
            (int hitsN, float dmgN) = FireTest(system, def, def.MaxLevel);

            Check(hits1 > 0, $"weapon {def.Id} never hit at L1");
            Check(hitsN > 0, $"weapon {def.Id} never hit at L{def.MaxLevel}");
            Check(dmgN > dmg1, $"weapon {def.Id} does not grow with level");

            Line($"weapon {def.Id,-18} {def.Rarity,-9} {def.Shape,-9} {def.Element,-9} " +
                 $"paperDps={dps1:0.0} L1: hits={hits1} dmg={dmg1:0} | L{def.MaxLevel}: hits={hitsN} dmg={dmgN:0}");
        }
    }

    /// <summary>Four seconds of one weapon against a line of indestructible
    /// dummies, through the real runtime, projectile pool and targeting.</summary>
    private static (int hits, float damage) FireTest(WeaponSystem system, WeaponDef def, int level)
    {
        Vector3 m = system.MuzzlePosition;
        var dummies = new List<FakeTarget>
        {
            new(m + new Vector3(2.0f, 0f, 0f)),
            new(m + new Vector3(4.5f, 0f, 0.4f)),
            new(m + new Vector3(7.0f, 0f, -0.4f)),
            new(m + new Vector3(10.0f, 0f, 0f)),
        };
        Targeting.Source = into => { foreach (FakeTarget d in dummies) into.Add(d); return into.Count; };

        var runtime = new WeaponRuntime(def);
        while (runtime.Level < level && runtime.LevelUp()) { }

        const float dt = 1f / 60f;
        for (int frame = 0; frame < 240; frame++)
        {
            Targeting.Refresh();
            runtime.Tick(dt, system);
            Projectile.UpdateAll(dt);
        }

        // Let whatever is still in the air land or expire, so the next test
        // starts with an empty pool and is not credited with these hits.
        for (int frame = 0; frame < 600 && Projectile.ActiveCount > 0; frame++)
        {
            Targeting.Refresh();
            Projectile.UpdateAll(dt);
        }

        runtime.Dispose();
        Combat.Weapons.SignatureFx.Clear();   // lingering signature effects (anchors) end with their test
        Targeting.Source = null;

        return (dummies.Sum(d => d.Hits), dummies.Sum(d => d.Damage));
    }

    // ----------------------------------------------------------------- items

    private static readonly object Baseline = new();

    private static void AuditItems()
    {
        foreach (ItemDef def in Catalog.Items)
        {
            Check(def.Effects is { Count: > 0 }, $"item {def.Id} has no effects");
            Check(def.Weight > 0f, $"item {def.Id} weight");

            var sheet = new StatSheet();
            // Floors (armour 0, thorns 0) would hide a negative or multiplicative
            // effect, so every stat starts somewhere it can visibly move from.
            foreach (Stat s in Enum.GetValues<Stat>())
                sheet.Add(s, ModifierOp.Flat, Catalog.IsFractionStat(s) ? 0.2f : 20f, Baseline);

            var before = Enum.GetValues<Stat>().ToDictionary(s => s, s => sheet.Get(s));

            // Same arithmetic as Inventory.ReapplyStack, at one stack and at cap.
            int stacks = def.MaxStacks > 0 ? def.MaxStacks : 5;
            foreach (int n in new[] { 1, stacks })
            {
                sheet.RemoveAllFrom(def);
                foreach (ItemEffect e in def.Effects!)
                {
                    float v = e.Op == ModifierOp.Multiplicative ? MathF.Pow(e.PerStack, n) : e.PerStack * n;
                    sheet.Add(e.Stat, e.Op, v, def);
                }

                foreach (IGrouping<Stat, ItemEffect> group in def.Effects!.GroupBy(e => e.Stat))
                {
                    float expected = group.Sum(e => e.Op == ModifierOp.Multiplicative ? e.PerStack - 1f : e.PerStack);
                    float delta = sheet.Get(group.Key) - before[group.Key];
                    Check(float.IsFinite(delta) && MathF.Sign(delta) == MathF.Sign(expected),
                          $"item {def.Id} x{n}: {group.Key} moved {delta:0.###}, expected sign of {expected:0.###}");
                }
            }

            sheet.RemoveAllFrom(def);
            foreach (Stat s in Enum.GetValues<Stat>())
                Check(MathF.Abs(sheet.Get(s) - before[s]) < 1e-4f, $"item {def.Id} leaves residue on {s}");

            string effects = string.Join(", ", def.Effects!.Select(Catalog.DescribeEffect));
            Line($"item {def.Id,-20} {def.Rarity,-9} cap={(def.MaxStacks == 0 ? "-" : def.MaxStacks.ToString())} {effects}");
        }
    }

    // --------------------------------------------------------------- enemies

    private static void AuditEnemies(WeaponSystem system)
    {
        Check(Catalog.Enemies.Any(e => e.IsBoss), "no boss");
        Check(Catalog.Enemies.Any(e => e.IsElite), "no elite");

        Vector3 park = system.MuzzlePosition + new Vector3(0f, -200f, 0f);

        foreach (EnemyDef def in Catalog.Enemies)
        {
            Check(def.Health > 0f && def.MoveSpeed > 0f && def.Cost >= 1 && def.AttackCooldown > 0f && def.Radius > 0f,
                  $"enemy numbers {def.Id}");
            Check(!(def.IsBoss && def.IsElite), $"enemy {def.Id} is both elite and boss");
            AuditBehaviourData(def);

            // Build one for real: configure, spawn, hurt, despawn. This is the
            // whole path the director takes, minus the choice of when.
            var enemy = new Enemy { Name = $"Audit_{def.Id}" };
            system.AddChild(enemy);
            enemy.Configure(def, 1f, 1f, 1f);
            enemy.Spawn(park);
            bool alive = enemy.IsAlive && MathF.Abs(enemy.MaxHealth - def.Health) < 0.01f;
            enemy.TakeDamage(new DamageInfo { Amount = 1f, Source = "audit" });
            bool hurt = enemy.Health < enemy.MaxHealth;
            enemy.Despawn();
            enemy.QueueFree();
            Check(alive && hurt, $"enemy {def.Id} failed to spawn/take damage");

            string shape = def.IsBoss ? "Boss" : def.IsElite ? "Elite"
                         : def.Behavior == EnemyBehavior.Shooter ? "Caster"
                         : def.Behavior == EnemyBehavior.Exploder ? "Bloat"
                         : def.Behavior == EnemyBehavior.Charger ? "Horned"
                         : def.Behavior == EnemyBehavior.Leaper ? "Hopper"
                         : def.MoveSpeed >= 5f ? "Runner"
                         : def.Health >= 60f || def.KnockbackResist >= 0.5f ? "Brute" : "Grunt";
            Line($"enemy {def.Id,-18} {shape,-6} cost={def.Cost,-2} unlock={def.UnlockAtSeconds,4:0}s hp={def.Health} spd={def.MoveSpeed} " +
                 $"{def.Behavior}{(def.SplitCount > 0 ? $" splits={def.SplitInto}x{def.SplitCount}" : "")}" +
                 $"{(def.IsBoss ? $" tier={def.BossTier} moves={string.Join(",", def.BossMoves.Select(m => m.Move + (m.MinPhase > 1 ? "(P2)" : "")))}" : "")}" +
                 $"{(def.DirectorSpawns ? "" : " summon-only")}");
        }

        for (int tier = 1; tier <= 3; tier++)
            Check(Catalog.Enemies.Any(e => e.IsBoss && e.BossTier == tier), $"no boss for tier {tier}");

        AuditThorns(system);
        AuditBehaviours(system);

        // The audit spawned outside the director; forget the counts it made.
        SpawnCounts.Clear();
        SeenEnemies.Clear();
    }

    private static readonly object ThornsProbe = new();

    /// <summary>
    /// Thorns is the one stat whose effect lives in the enemy rather than in the
    /// stat sheet, so it gets an end-to-end test: give the player 10 thorns, let
    /// a harmless Mote swing at them through the real <see cref="Enemy.Step"/>,
    /// and check the Mote lost exactly the thorns value.
    /// </summary>
    private static void AuditThorns(WeaponSystem system)
    {
        if (!PlayerRef.TryGet(out Node3D playerNode, out IDamageable? player) || player is null
            || system.Stats is not { } stats)
        {
            Check(false, "thorns: no player to test against");
            return;
        }

        EnemyDef mote = Catalog.Enemies[0];
        stats.Add(Stat.Thorns, ModifierOp.Flat, 10f, ThornsProbe);
        float expected = 10f * stats.GetMultiplier(Stat.Damage);
        int hitsBefore = Enemy.ThornsHits;

        var enemy = new Enemy { Name = "Audit_thorns" };
        system.AddChild(enemy);
        // Zero damage multiplier: the swing is real, the hurt is not.
        enemy.Configure(mote, 10f, 0f, 1f);
        enemy.Spawn(playerNode.GlobalPosition + new Vector3(0.6f, 0f, 0f));

        for (int frame = 0; frame < 240 && Enemy.ThornsHits == hitsBefore && enemy.IsAlive; frame++)
            enemy.Step(1f / 60f, playerNode.GlobalPosition, player, Vector3.Zero);

        float lost = enemy.MaxHealth - enemy.Health;
        bool landed = Enemy.ThornsHits == hitsBefore + 1;
        enemy.Despawn();
        enemy.QueueFree();
        stats.RemoveAllFrom(ThornsProbe);

        Check(landed && MathF.Abs(lost - expected) < 0.01f,
              $"thorns: expected one retaliation of {expected:0.##}, got hits={Enemy.ThornsHits - hitsBefore} lost={lost:0.##}");
        Line($"thorns probe: swing returned {lost:0.##} (expected {expected:0.##})");
    }

    /// <summary>
    /// Replays the director's choice weights (EnemyManager.PickAffordable) minute
    /// by minute, so the log shows which enemy is the staple when — and that the
    /// opening minutes contain only the original roster.
    /// </summary>
    private static void AuditDirector()
    {
        var reached = new HashSet<string>();
        for (int sec = 0; sec <= 900; sec += 60)
        {
            float target = 1f + sec / 60f * 0.9f;
            var weights = new List<(string id, float w)>();
            foreach (EnemyDef def in Catalog.Enemies)
            {
                if (def.IsBoss || def.IsElite || !def.DirectorSpawns || sec < def.UnlockAtSeconds) continue;
                float ln = MathF.Log(MathF.Max(1f, def.Cost) / target);
                weights.Add((def.Id, MathF.Exp(-ln * ln * 1.4f)));
            }
            float total = weights.Sum(w => w.w);
            var shares = weights.Where(w => w.w / total >= 0.02f).OrderByDescending(w => w.w)
                                .Select(w => $"{w.id}={w.w / total * 100f:0}%");
            foreach (var w in weights) if (w.w / total >= 0.02f) reached.Add(w.id);
            Line($"director t={sec,3}s target={target:0.0} {string.Join(" ", shares)}");
        }

        foreach (EnemyDef def in Catalog.Enemies.Where(e => !e.IsBoss && !e.IsElite && e.DirectorSpawns))
            Check(reached.Contains(def.Id), $"enemy {def.Id} never holds 2% of the director's picks");
        foreach (EnemyDef def in Catalog.Enemies.Where(e => e.IsBoss || e.IsElite))
            Check(def.UnlockAtSeconds < RunState.TargetSeconds - 240f, $"enemy {def.Id} unlocks too late to be met");
    }

}
