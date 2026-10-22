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

/// <summary>
/// Flag-gated proof that the catalogue is well formed and that every entry in it
/// is reachable in the running game. Nothing here runs unless asked for.
///
///     -- --content-log        log acquisitions, first spawns and thorns totals
///     -- --validate-content   run the full audit on the first combat tick
///
/// The audit is deliberately done inside the real engine rather than as a unit
/// test: "this weapon can fire" is only a meaningful claim if it is the real
/// <see cref="WeaponRuntime"/>, the real projectile pool and the real targeting
/// that fired it. Every line it prints starts with <c>CONTENT</c> so a log can be
/// grepped without touching the <c>SUMMARY</c> line other tools parse.
///
/// Running the audit perturbs the run's random stream (the offer simulation
/// rolls its own pool), so an audit run is a check, not a replay — use a
/// separate run for seeded evidence.
/// </summary>
public static class ContentAudit
{
    private static bool _parsed;
    private static bool _log;
    private static bool _validate;
    private static bool _attached;
    private static bool _showcase;

    private static readonly HashSet<string> SeenEnemies = new();
    private static readonly Dictionary<string, int> SpawnCounts = new();

    /// <summary>True when either flag is present.</summary>
    public static bool Logging { get { Parse(); return _log || _validate; } }

    /// <summary>True until the audit (or the showcase) has run, when requested.</summary>
    public static bool Pending { get { Parse(); return _validate || _showcase; } }

    private static void Parse()
    {
        if (_parsed) return;
        _parsed = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--content-log") _log = true;
            else if (arg == "--validate-content") _validate = true;
            else if (arg == "--content-showcase") _showcase = true;
        }
    }

    private static float RunSeconds => Main.Instance?.Run?.Seconds ?? 0f;

    private static void Line(string text) => GD.Print("CONTENT " + text);

    // ------------------------------------------------------------ run logging

    /// <summary>Subscribe the acquisition loggers. Called by the weapon system
    /// when it enters the tree; a no-op without a flag.</summary>
    public static void Attach()
    {
        if (!Logging || _attached) return;
        _attached = true;
        EventBus.Subscribe<WeaponAcquired>(static e =>
            Line($"acquire weapon {e.Id} L{e.NewLevel} t={RunSeconds:0}"));
        EventBus.Subscribe<ItemAcquired>(static e =>
            Line($"acquire item {e.Id} x{e.NewStackCount} t={RunSeconds:0}"));
    }

    /// <summary>Called by <see cref="Enemy.Spawn"/>. Logs the first appearance of
    /// each definition and counts the rest.</summary>
    public static void NoteSpawn(EnemyDef? def)
    {
        if (def is null || !Logging) return;
        SpawnCounts[def.Id] = SpawnCounts.TryGetValue(def.Id, out int n) ? n + 1 : 1;
        if (SeenEnemies.Add(def.Id))
            Line($"first-spawn {def.Id} t={RunSeconds:0}{(def.IsElite ? " elite" : "")}{(def.IsBoss ? " boss" : "")}");
    }

    /// <summary>End-of-run totals. Called when the weapon system leaves the tree.</summary>
    public static void Report()
    {
        if (!Logging) return;
        string spawns = string.Join(" ", SpawnCounts.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}"));
        Line($"spawns {spawns}");
        Line($"thorns hits={Enemy.ThornsHits} dealt={Enemy.ThornsDealt:0}");
    }

    // ------------------------------------------------------------------ audit

    private static int _checks;
    private static readonly List<string> Failures = new();

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (!ok)
        {
            Failures.Add(what);
            Line("FAIL " + what);
        }
    }

    /// <summary>
    /// Run every check once. Called by <see cref="WeaponSystem.Tick"/> after it
    /// has resolved the player and before any weapon fires, so the muzzle is
    /// real and the projectile pool is empty.
    /// </summary>
    internal static void Run(WeaponSystem system)
    {
        if (_showcase) Showcase(system);
        if (!_validate) return;
        _validate = false;
        _log = true;

        bool hitEvents = WeaponSystem.PublishHitEvents;
        bool numbers = WeaponSystem.PublishDamageNumbers;
        WeaponSystem.PublishHitEvents = false;
        WeaponSystem.PublishDamageNumbers = false;

        try
        {
            Line($"catalog weapons={Catalog.Weapons.Count} items={Catalog.Items.Count} enemies={Catalog.Enemies.Count}");
            AuditIds();
            AuditWeapons(system);
            AuditItems();
            AuditEnemies(system);
            AuditDirector();
            AuditOffers(system);
        }
        catch (Exception e)
        {
            Check(false, "audit threw: " + e);
        }
        finally
        {
            WeaponSystem.PublishHitEvents = hitEvents;
            WeaponSystem.PublishDamageNumbers = numbers;
            Targeting.Source = null;
            Targeting.Refresh();
        }

        Line(Failures.Count == 0
            ? $"AUDIT PASS checks={_checks}"
            : $"AUDIT FAIL {Failures.Count}/{_checks}");
    }

    // -------------------------------------------------------------- showcase

    /// <summary>
    /// --content-showcase: hand the player the second-wave weapons and stand one
    /// of each second-wave enemy in an arc in front of them, so a single capture
    /// shows the new silhouettes and effects. The statues are outside the
    /// director (never stepped, never targeted) and exist only for the picture.
    /// </summary>
    private static void Showcase(WeaponSystem system)
    {
        _showcase = false;
        _log = true;

        foreach (string id in new[] { "forgefall_maul", "rimeheart", "tempest_spindle", "blightwasp_swarm", "hailstone_cluster" })
            if (Catalog.WeaponById(id) is { } w) system.Add(w);

        string[] enemies = { "brine_leech", "kelp_shambler", "gloom_lurcher", "gale_revenant", "tidewall_colossus", "breaker_matriarch" };
        if (Main.Instance?.Player is not { } player) return;

        Vector3 origin = player.GlobalPosition;
        Vector3 fwd = PlayerCamera.Instance?.FlatForward ?? Vector3.Forward;
        float yaw0 = MathF.Atan2(fwd.X, -fwd.Z);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (Catalog.EnemyById(enemies[i]) is not { } def) continue;
            float angle = yaw0 + Mathf.DegToRad(-55f + 110f * i / (enemies.Length - 1));
            Vector3 at = origin + new Vector3(MathF.Sin(angle), 0f, -MathF.Cos(angle)) * 11f;
            var enemy = new Enemy { Name = $"Showcase_{def.Id}" };
            system.AddChild(enemy);
            enemy.Configure(def, 50f, 0f, 1f);
            enemy.Spawn(at);
            enemy.LookAt(new Vector3(origin.X, at.Y, origin.Z), Vector3.Up, useModelFront: true);
            Line($"showcase {def.Id} at {at}");
        }
    }

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
            Check(def.CooldownAtLevel(def.MaxLevel) >= 0.05f, $"weapon max-level cooldown {def.Id}");

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
                         : def.MoveSpeed >= 5f ? "Runner"
                         : def.Health >= 60f || def.KnockbackResist >= 0.5f ? "Brute" : "Grunt";
            Line($"enemy {def.Id,-18} {shape,-6} cost={def.Cost,-2} unlock={def.UnlockAtSeconds,4:0}s hp={def.Health} spd={def.MoveSpeed}");
        }

        AuditThorns(system);

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
                if (def.IsBoss || def.IsElite || sec < def.UnlockAtSeconds) continue;
                float ln = MathF.Log(MathF.Max(1f, def.Cost) / target);
                weights.Add((def.Id, MathF.Exp(-ln * ln * 1.4f)));
            }
            float total = weights.Sum(w => w.w);
            var shares = weights.Where(w => w.w / total >= 0.02f).OrderByDescending(w => w.w)
                                .Select(w => $"{w.id}={w.w / total * 100f:0}%");
            foreach (var w in weights) if (w.w / total >= 0.02f) reached.Add(w.id);
            Line($"director t={sec,3}s target={target:0.0} {string.Join(" ", shares)}");
        }

        foreach (EnemyDef def in Catalog.Enemies.Where(e => !e.IsBoss && !e.IsElite))
            Check(reached.Contains(def.Id), $"enemy {def.Id} never holds 2% of the director's picks");
        foreach (EnemyDef def in Catalog.Enemies.Where(e => e.IsBoss || e.IsElite))
            Check(def.UnlockAtSeconds < RunState.TargetSeconds - 240f, $"enemy {def.Id} unlocks too late to be met");
    }

    // ---------------------------------------------------------------- offers

    /// <summary>Thousands of level-up rolls against the run's starting state:
    /// every weapon and item must turn up as a card.</summary>
    private static void AuditOffers(WeaponSystem system)
    {
        var counts = new Dictionary<string, int>();
        var pool = new OfferPool(Catalog.Items, Catalog.Weapons, new Rng(0xC0FFEE));
        Inventory? inventory = Inventory.Current;

        const int rolls = 4000;
        for (int r = 0; r < rolls; r++)
        {
            foreach (Offer o in pool.Roll(3, system, inventory, system.Stats))
            {
                string? id = o.Weapon?.Id ?? o.Item?.Id;
                if (id is not null) counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
            }
        }

        // The offer pool installs its Rng on the drop table; give the run its own back.
        if (Main.Instance?.Run?.Rng is { } runRng) PickupField.Ensure(runRng);

        foreach (WeaponDef w in Catalog.Weapons.Skip(1))   // index 0 is already owned
        {
            counts.TryGetValue(w.Id, out int n);
            Check(n > 0, $"weapon {w.Id} never offered in {rolls} rolls");
        }
        foreach (ItemDef i in Catalog.Items)
        {
            counts.TryGetValue(i.Id, out int n);
            Check(n > 0, $"item {i.Id} never offered in {rolls} rolls");
        }

        Line("offers per 4000 rolls: " + string.Join(" ",
            counts.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}")));
    }
}
