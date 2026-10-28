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
public static partial class ContentAudit
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
        ReportLevelUp();
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
            AuditTomes(system);
            AuditPerks(system);     // ContentAudit.Perks.cs
            Interactables.Audit(Check);
            AuditShrines();
            AuditUpgrades(system);
            AuditElements();
            AuditExpansion(system);
            AuditIcons();
            AuditCute(system);              // ContentAudit.CuteEnemies.cs
            AuditHeroes(system);            // ContentAudit.Heroes.cs
            AuditLocalisation();    // ContentAudit.Loc.cs
            AuditMapChallenges();   // map challenges, Toggler, slots, cute shrines (ContentAudit.MapChallenges.cs)
            AuditMusic();           // tracks per map, every file loads, Boombox catalogue (ContentAudit.Music.cs)
            AuditUnderwater();
            AuditExpeditions();
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

        string[] enemies = ShowcaseEnemyIds() ?? new[] { "brinespitter", "bloat_polyp", "reefhorn", "surge_leaper", "shoalmother", "deepcaller" };
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

        // Index 0 is already owned; signatures are never offered new (ContentAudit.Heroes.cs).
        foreach (WeaponDef w in Catalog.Weapons.Skip(1).Where(w => !Combat.Weapons.Signatures.Is(w.Id)))
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
