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
///     -- --content-showcase   stage the behaviour monsters and a boss mid-move
///        [--showcase-boss=id] [--showcase-move=index into its BossMoves]
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
            else if (arg.StartsWith("--showcase-boss=")) _showcaseBoss = arg["--showcase-boss=".Length..];
            else if (arg.StartsWith("--showcase-move=")) _showcaseMove = arg["--showcase-move=".Length..].ToInt();
        }
    }

    private static string _showcaseBoss = "thresher_prime";
    private static int _showcaseMove;

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

    private static readonly Dictionary<string, int> KillCounts = new();

    /// <summary>Called by <see cref="Enemy"/> on death. A spawn count proves an
    /// enemy appears; a kill count proves it can also be fought to the end.</summary>
    public static void NoteKill(EnemyDef? def)
    {
        if (def is null || !Logging) return;
        bool first = !KillCounts.ContainsKey(def.Id);
        KillCounts[def.Id] = KillCounts.TryGetValue(def.Id, out int n) ? n + 1 : 1;
        if (first && (def.IsBoss || def.IsElite || def.UnlockAtSeconds > 0f))
            Line($"first-kill {def.Id} t={RunSeconds:0}");
    }

    private static readonly Dictionary<(string, string), int> AbilityCounts = new();
    private static readonly Dictionary<string, int> ConnectCounts = new();

    /// <summary>An enemy started a behaviour or boss move. Counted per
    /// (enemy, move), so the log proves each move in a moveset is used rather
    /// than one move on repeat.</summary>
    public static void NoteAbility(EnemyDef? def, string move)
    {
        if (def is null || !Logging) return;
        var key = (def.Id, move);
        AbilityCounts[key] = AbilityCounts.TryGetValue(key, out int n) ? n + 1 : 1;
        if (n == 0) Line($"first-ability {def.Id} {move} t={RunSeconds:0}");
    }

    /// <summary>An enemy attack reached the player (before i-frames decided
    /// whether it hurt). Keyed by the DamageInfo source tag.</summary>
    public static void NoteConnect(string source)
    {
        if (!Logging) return;
        ConnectCounts[source] = ConnectCounts.TryGetValue(source, out int n) ? n + 1 : 1;
    }

    public static void NoteBossPhase(EnemyDef? def, float aliveSeconds)
    {
        if (def is null || !Logging) return;
        Line($"boss-phase2 {def.Id} t={RunSeconds:0} after={aliveSeconds:0}s");
    }

    /// <summary>Fight length is the number that says whether a boss is tuned:
    /// seconds from spawn to death.</summary>
    public static void NoteBossDeath(EnemyDef? def, float aliveSeconds)
    {
        if (def is null || !Logging) return;
        Line($"boss-killed {def.Id} t={RunSeconds:0} fight={aliveSeconds:0}s");
    }

    /// <summary>End-of-run totals. Called when the weapon system leaves the tree.</summary>
    public static void Report()
    {
        if (!Logging) return;
        Line("abilities " + string.Join(" ", AbilityCounts.OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2)
            .Select(p => $"{p.Key.Item1}:{p.Key.Item2}={p.Value}")));
        Line("connects " + string.Join(" ", ConnectCounts.OrderByDescending(p => p.Value)
            .Select(p => $"{p.Key}={p.Value}")));
        Line($"enemy-shots fired={EnemyAttacks.ShotsFired} connects={EnemyAttacks.Connects}");
        string spawns = string.Join(" ", SpawnCounts.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}"));
        Line($"spawns {spawns}");
        string kills = string.Join(" ", KillCounts.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}"));
        Line($"kills {kills}");
        if (Actors.EnemyManager.Instance is { } em)
            foreach (Actors.Enemy e in em.AliveList)
                if (GodotObject.IsInstanceValid(e) && e.IsAlive && e.IsBoss)
                    Line($"boss-alive {e.Def?.Id} hp={e.HealthFraction * 100f:0}%");
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
    /// --content-showcase: stand one of each behaviour monster in an arc in
    /// front of the player and a boss straight ahead, all of them *live*, so a
    /// capture a second or two in shows wind-ups, lanes, blast discs and shots
    /// rather than statues. They come from the real manager (pooled, stepped,
    /// targetable) with fifty times their health, so the starting weapon does
    /// not clear the stage before the picture is taken. The boss is held for
    /// one second and then forced into one move (default: its first), so the
    /// telegraph lands inside a predictable window: about 1-2s for a slam.
    /// </summary>
    private static void Showcase(WeaponSystem system)
    {
        _showcase = false;
        _log = true;

        string[] enemies = { "brinespitter", "bloat_polyp", "reefhorn", "surge_leaper", "shoalmother", "deepcaller" };
        if (Main.Instance?.Player is not { } player || EnemyManager.Instance is not { } em) return;

        Vector3 origin = player.GlobalPosition;
        Vector3 fwd = PlayerCamera.Instance?.FlatForward ?? Vector3.Forward;
        float yaw0 = MathF.Atan2(fwd.X, -fwd.Z);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (Catalog.EnemyById(enemies[i]) is not { } def) continue;
            float angle = yaw0 + Mathf.DegToRad(-70f + 140f * i / (enemies.Length - 1));
            Vector3 at = origin + new Vector3(MathF.Sin(angle), 0f, -MathF.Cos(angle)) * 9.5f;
            if (em.SpawnAt(def, at) is not { } enemy) continue;
            enemy.Configure(def, 50f, 1f, 1f);
            enemy.Spawn(at);
            // Ready almost at once, staggered, so every tell happens inside the
            // first two seconds instead of after the walkers reach contact.
            enemy.ForceMove(-1, delay: 0.3f + 0.15f * i);
            Line($"showcase {def.Id} {def.Behavior} at {at}");
        }

        if (Catalog.EnemyById(_showcaseBoss) is { IsBoss: true } bossDef)
        {
            Vector3 at = origin + new Vector3(MathF.Sin(yaw0), 0f, -MathF.Cos(yaw0)) * 7f;
            if (em.SpawnAt(bossDef, at) is { } boss)
            {
                boss.Configure(bossDef, 20f, 1f, 1f);
                boss.Spawn(at);
                int move = Math.Clamp(_showcaseMove, 0, Math.Max(0, bossDef.BossMoves.Count - 1));
                boss.ForceMove(move, delay: 1f);
                Line($"showcase boss {bossDef.Id} move={bossDef.BossMoves[move].Move} at {at}");
            }
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

    // ------------------------------------------------------------ behaviours

    /// <summary>Numbers a behaviour needs to be fair: a tell long enough to
    /// read, shots slow enough to dodge, ranges that make the move reachable.</summary>
    private static void AuditBehaviourData(EnemyDef def)
    {
        string id = def.Id;
        switch (def.Behavior)
        {
            case EnemyBehavior.Shooter:
                Check(def.AbilityRange >= 4f && def.AbilityCount >= 1 && def.WindUp >= 0.3f, $"shooter {id} range/count/wind-up");
                // The player walks at 6 m/s; anything much faster cannot be
                // side-stepped from range and stops being a dodge.
                Check(def.AbilitySpeed > 2f && def.AbilitySpeed <= 12f, $"shooter {id} shot speed {def.AbilitySpeed} not dodgeable");
                break;
            case EnemyBehavior.Charger:
                Check(def.AbilityRange >= 4f && def.WindUp >= 0.4f && def.AbilitySpeed > def.MoveSpeed * 2f, $"charger {id} range/wind-up/speed");
                break;
            case EnemyBehavior.Exploder:
                Check(def.AbilityRadius > 0.5f && def.WindUp >= 0.5f && def.AbilityRange > 0.5f, $"exploder {id} radius/fuse");
                break;
            case EnemyBehavior.Leaper:
                Check(def.AbilityRange >= 4f && def.AbilityRadius > 0.5f && def.WindUp >= 0.3f, $"leaper {id} range/radius/wind-up");
                break;
        }

        if (def.SplitCount > 0)
            Check(Catalog.EnemyById(def.SplitInto) is { IsBoss: false } && def.SplitCount <= 8, $"splitter {id} child {def.SplitInto}");

        if (!def.DirectorSpawns)
            Check(Catalog.Enemies.Any(e => e.SplitInto == id || e.BossMoves.Any(m => m.SummonId == id)),
                  $"summon-only {id} is never summoned or split into");

        if (!def.IsBoss)
        {
            Check(def.BossTier == 0 && def.BossMoves.Count == 0, $"non-boss {id} has boss data");
            return;
        }

        Check(def.BossTier is >= 1 and <= 3, $"boss {id} tier {def.BossTier}");
        Check(def.BossMoves.Count >= 3, $"boss {id} has {def.BossMoves.Count} moves (want 3+)");
        Check(def.BossMoves.Any(m => m.MinPhase >= 2), $"boss {id} has no phase-two move");
        Check(def.BossMoves.Any(m => m.MinPhase <= 1), $"boss {id} has no phase-one move");
        foreach (BossAttack m in def.BossMoves)
        {
            Check(m.WindUp >= 0.4f && m.Weight > 0f, $"boss {id} {m.Move}: wind-up {m.WindUp} too short to read");
            if (m.Move == BossMove.Summon)
                Check(Catalog.EnemyById(m.SummonId) is { IsBoss: false }, $"boss {id} summons unknown '{m.SummonId}'");
            if (m.Move is BossMove.RadialBurst or BossMove.Barrage)
                Check(m.Speed > 2f && m.Speed <= 12f, $"boss {id} {m.Move} shot speed {m.Speed} not dodgeable");
        }
    }

    /// <summary>
    /// Every behaviour and every boss move, fought for real: the enemy is
    /// configured and stepped through <see cref="Enemy.Step"/>, and its attacks
    /// travel through <see cref="EnemyAttacks"/> at a dummy, far below the
    /// arena. A move passes when it *connects* (counted by EnemyAttacks, so a
    /// contact swing cannot pass for it) — or, for a summon, when the adds
    /// exist. This is the claim "the boss can slam" made by the code that slams.
    /// </summary>
    private static void AuditBehaviours(WeaponSystem system)
    {
        Vector3 feet = system.MuzzlePosition + new Vector3(0f, -200f, 0f);
        EnemyAttacks.Clear();

        foreach (EnemyDef def in Catalog.Enemies)
        {
            if (def.IsBoss)
            {
                for (int i = 0; i < def.BossMoves.Count; i++) ProbeMove(system, def, i, feet);
                ProbePhase(system, def, feet);
            }
            else if (def.Behavior != EnemyBehavior.Chaser)
            {
                ProbeMove(system, def, -1, feet);
            }
            if (def.SplitCount > 0) ProbeSplit(system, def, feet);
        }

        EnemyAttacks.Clear();
        EnemyManager.Instance?.DespawnBelow(-100f);
        AbilityCounts.Clear();
        ConnectCounts.Clear();
    }

    private static void ProbeMove(WeaponSystem system, EnemyDef def, int moveIndex, Vector3 feet)
    {
        BossAttack? move = moveIndex >= 0 ? def.BossMoves[moveIndex] : null;
        float distance = move?.Move switch
        {
            BossMove.Slam => MathF.Min(2.5f, move.Radius * 0.6f),
            BossMove.Charge => Mathf.Clamp(move.MinRange + 2f, 5f, move.Radius - 2f),
            BossMove.RadialBurst => 4f,
            null => def.Behavior == EnemyBehavior.Shooter ? def.AbilityRange : def.AbilityRange * 0.6f,
            _ => 7f,
        };

        var dummy = new FakeTarget(feet + new Vector3(0f, 0.9f, 0f));
        var enemy = new Enemy { Name = $"Audit_{def.Id}_{moveIndex}" };
        system.AddChild(enemy);
        enemy.Configure(def, 1f, 1f, 1f);
        enemy.Spawn(feet + new Vector3(distance, 0f, 0f));
        // Ability ready at once: a charger that walks into contact range
        // before its cooldown expires never charges, and that is a property
        // of the stage, not of the behaviour under test.
        enemy.ForceMove(moveIndex, delay: 0f);

        int connects = EnemyAttacks.Connects;
        int alive = EnemyManager.Instance?.AliveCount ?? 0;
        bool summon = move?.Move == BossMove.Summon;
        float seconds = 0f;
        bool ok = false;
        const float dt = 1f / 60f;
        for (int frame = 0; frame < 900 && !ok; frame++)
        {
            if (enemy.IsAlive) enemy.Step(dt, feet, dummy, Vector3.Zero);
            EnemyAttacks.Tick(dt, feet, dummy);
            seconds += dt;
            ok = summon ? (EnemyManager.Instance?.AliveCount ?? 0) > alive : EnemyAttacks.Connects > connects;
        }

        string what = move is null ? def.Behavior.ToString() : move.Move.ToString();
        if (enemy.IsAlive) enemy.Despawn();
        enemy.QueueFree();
        EnemyAttacks.Clear();
        EnemyManager.Instance?.DespawnBelow(-100f);

        Check(ok, $"{def.Id} {what} never {(summon ? "summoned" : "connected")} (from {distance:0.#}m)");
        Line($"behaviour {def.Id,-18} {what,-12} {(ok ? "ok" : "FAILED")} after {seconds:0.00}s from {distance:0.#}m dummyDamage={dummy.Damage:0}");
    }

    private static void ProbePhase(WeaponSystem system, EnemyDef def, Vector3 feet)
    {
        var enemy = new Enemy { Name = $"Audit_{def.Id}_phase" };
        system.AddChild(enemy);
        enemy.Configure(def, 1f, 0f, 1f);
        enemy.Spawn(feet + new Vector3(30f, 0f, 0f));
        int before = enemy.Phase;
        enemy.TakeDamage(new DamageInfo { Amount = enemy.MaxHealth * 0.55f, Source = "audit" });
        int after = enemy.Phase;
        enemy.Despawn();
        enemy.QueueFree();
        Check(before == 1 && after == 2, $"boss {def.Id} phase {before}->{after} at 45% health");
    }

    private static void ProbeSplit(WeaponSystem system, EnemyDef def, Vector3 feet)
    {
        if (EnemyManager.Instance is not { } em) { Check(false, "split: no manager"); return; }
        var enemy = new Enemy { Name = $"Audit_{def.Id}_split" };
        system.AddChild(enemy);
        enemy.Configure(def, 1f, 0f, 1f);
        enemy.Spawn(feet + new Vector3(-30f, 0f, 0f));
        int before = em.AliveCount;
        enemy.TakeDamage(new DamageInfo { Amount = enemy.MaxHealth * 2f, Source = "audit" });
        int born = em.AliveCount - before;
        enemy.QueueFree();
        em.DespawnBelow(-100f);
        Check(born == def.SplitCount, $"splitter {def.Id} produced {born}, expected {def.SplitCount}");
        Line($"behaviour {def.Id,-18} Split        {(born == def.SplitCount ? "ok" : "FAILED")} {born}x {def.SplitInto}");
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
