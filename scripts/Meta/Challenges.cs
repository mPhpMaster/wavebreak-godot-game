using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>
/// One run modifier. Everything it does is in <see cref="Challenges.BeginRun"/>
/// (multipliers) and <see cref="ChallengeRuntime"/> (behaviour), keyed by
/// <see cref="Id"/>; the record is just what the menu and the codex show.
/// </summary>
public sealed record ChallengeDef(string Id, string Name, string Blurb, float Reward)
{
    /// <summary>"+25% Pearls".</summary>
    public string RewardText => $"+{Reward * 100f:0}% {Pearls.Name}";
}

/// <summary>
/// Challenges: run modifiers picked on the play page, any number at once.
///
/// Each adds its <see cref="ChallengeDef.Reward"/> to the Pearls multiplier
/// (summed, like additive stats: three +25% challenges are x1.75). Clearing a
/// map with challenges on records each of them against that map in the
/// profile, and winning records a win; the codex shows both.
///
/// Selection is a bitmask in the profile's [meta] section. The run's set is
/// resolved once by <see cref="BeginRun"/>: the selection for a player's run,
/// none for any automated run unless <c>--challenges=a,b|all</c> is given, so
/// smoke tests are never changed by what a developer last picked in the menu.
///
/// The enemy director, the stage director and the weapon system read the
/// static multipliers below every tick. Every one of them is exactly 1 (or
/// false) with no challenge active, so the hooks are no-ops by default.
/// </summary>
public static partial class Challenges
{
    public static readonly IReadOnlyList<ChallengeDef> All = new List<ChallengeDef>
    {
        new("hyper",     "Riptide",        "Everything faster: enemies +30% speed and spawns +15%; you +30% move and +25% attack speed; waves 15% shorter.", 0.25f),
        new("glass",     "Glass Hull",     "Half max health, x1.5 damage.", 0.30f),
        new("swarm",     "Shoal",          "Twice the crowd and twice the spawn rate. Each enemy has 20% less health.", 0.40f),
        new("molten",    "Scalding Sand",  "Standing still burns: after a moment without moving you lose 8% max health per second.", 0.25f),
        new("speed",     "Ebb Clock",      "Waves 25% shorter, and every phase boss must fall within 75 seconds or the tide takes you.", 0.35f),
        new("chonk",     "Leviathans",     "Enemies are huge: x2.2 health, x1.3 damage, 25% slower, 40% fewer of them.", 0.25f),
        new("noitems",   "Castaway",       "No items: level-ups offer weapons and tomes only, chests pay supplies, merchants are empty.", 0.40f),
        new("pacifist",  "Becalmed",       "Your weapons hold fire for the first 45 seconds on every map. You move 15% faster while they do.", 0.20f),
        new("elites",    "Wardens",        "Elite waves every 20 seconds instead of 55, and they last longer.", 0.35f),
        new("fog",       "Sea Fret",       "A thick fog rolls in on every map. You will hear them first.", 0.15f),
        new("lowgrav",   "Buoyant",        "Half gravity, +50% jump height. Floaty, and so is everything you were dodging.", 0.10f),
        new("surge",     "Spring Tide",    "Every 45 seconds the tide surges: for 8 seconds spawns triple and enemies move 25% faster.", 0.30f),
    };

    private const string SelectedKey = "challenges";
    private static string ClearKey(ChallengeDef c, string mapId) => $"chal_{c.Id}@{mapId}";
    private static string WinKey(ChallengeDef c) => $"chal_{c.Id}_wins";

    // ---------------------------------------------------------- run state

    private static readonly HashSet<string> ActiveIds = new();

    /// <summary>The challenges of the run in progress, in table order.</summary>
    public static IReadOnlyList<ChallengeDef> Active { get; private set; } = Array.Empty<ChallengeDef>();

    public static bool IsActive(string id) => ActiveIds.Contains(id);

    // Read by EnemyManager every tick (the surge terms change during a run).
    public static float EnemyHealth { get; private set; } = 1f;
    public static float EnemyDamage { get; private set; } = 1f;
    public static float EnemySpeed => _enemySpeed * (SurgeActive ? 1.25f : 1f);
    public static float SpawnRate => _spawnRate * (SurgeActive ? 3f : 1f);
    public static float Crowd { get; private set; } = 1f;
    /// <summary>Visual size of every enemy model (Leviathans).</summary>
    public static float EnemyVisualScale { get; private set; } = 1f;

    /// <summary>Read by StageDirector: multiplier on each phase's wave length.
    /// Ignored when --phase-seconds is given.</summary>
    public static float PhaseTimeScale { get; private set; } = 1f;

    /// <summary>Pearls multiplier from the active set: 1 + Σ rewards.</summary>
    public static float RewardMultiplier { get; private set; } = 1f;

    /// <summary>Set by <see cref="ChallengeRuntime"/>: weapons hold fire (Becalmed).</summary>
    public static bool WeaponsSilenced { get; internal set; }

    /// <summary>Set by <see cref="ChallengeRuntime"/>: a Spring Tide surge is running.</summary>
    public static bool SurgeActive { get; internal set; }

    private static float _enemySpeed = 1f, _spawnRate = 1f;

    // ---------------------------------------------------------- selection

    public static int IndexOf(ChallengeDef c)
    {
        for (int i = 0; i < All.Count; i++) if (All[i].Id == c.Id) return i;
        return -1;
    }

    public static ChallengeDef? ById(string id) => All.FirstOrDefault(c => c.Id == id);

    private static long Mask
    {
        get { Profile.EnsureLoaded(); return Profile.GetMeta(SelectedKey); }
        set => Profile.SetMeta(SelectedKey, value);
    }

    public static bool IsSelected(ChallengeDef c) => (Mask & (1L << IndexOf(c))) != 0;

    /// <summary>Flip one challenge in the saved selection. Does not save.</summary>
    public static void Toggle(ChallengeDef c) => Mask ^= 1L << IndexOf(c);

    public static void ClearSelection() => Mask = 0;

    public static IReadOnlyList<ChallengeDef> Selected => All.Where(IsSelected).ToList();

    /// <summary>Pearls multiplier a set would pay.</summary>
    public static float RewardFor(IEnumerable<ChallengeDef> set) => 1f + set.Sum(c => c.Reward);

    // ------------------------------------------------------------- records

    /// <summary>Maps (of <see cref="Data.Stages.All"/>) this challenge has been
    /// cleared on.</summary>
    public static List<string> ClearedMaps(ChallengeDef c)
    {
        Profile.EnsureLoaded();
        var maps = new List<string>();
        foreach (Data.StageDef s in Data.Stages.All)
            if (Profile.GetMeta(ClearKey(c, s.Id)) > 0) maps.Add(s.DisplayName);
        return maps;
    }

    public static long Wins(ChallengeDef c)
    {
        Profile.EnsureLoaded();
        return Profile.GetMeta(WinKey(c));
    }

    /// <summary>Record a map cleared (its phase boss killed) with the active set.</summary>
    internal static void RecordClear(string mapId)
    {
        if (Active.Count == 0 || mapId.Length == 0) return;
        foreach (ChallengeDef c in Active) Profile.SetMeta(ClearKey(c, mapId), Profile.GetMeta(ClearKey(c, mapId)) + 1);
        Profile.Save();
        GD.Print($"META challenge clear map={mapId} set={string.Join(",", Active.Select(c => c.Id))}");
    }

    internal static void RecordWin()
    {
        if (Active.Count == 0) return;
        foreach (ChallengeDef c in Active) Profile.SetMeta(WinKey(c), Profile.GetMeta(WinKey(c)) + 1);
        Profile.Save();
        GD.Print($"META challenge win set={string.Join(",", Active.Select(c => c.Id))}");
    }

    // ---------------------------------------------------------------- run

    /// <summary>Resolve the run's set and its multipliers. Call once per run,
    /// before anything reads them (RunMeta.Attach does).</summary>
    public static void BeginRun()
    {
        Profile.EnsureLoaded();
        Launch.Parse();
        IEnumerable<ChallengeDef> set = Launch.SkipsMenu ? Array.Empty<ChallengeDef>() : Selected;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--challenges=")) continue;
            string list = arg["--challenges=".Length..];
            set = list == "all" ? All
                : list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Select(ById).Where(c => c is not null).Cast<ChallengeDef>().Distinct().ToList();
        }
        set = MapChallenges.Merge(set);   // the selected map challenge's modifiers (Meta/MapChallenges)
        Apply(set.OrderBy(IndexOf).ToList());
    }

    /// <summary>Back to "no challenges". Called when the run scene leaves.</summary>
    public static void Reset() => Apply(Array.Empty<ChallengeDef>());

    private static void Apply(IReadOnlyList<ChallengeDef> set)
    {
        Active = set;
        ActiveIds.Clear();
        foreach (ChallengeDef c in set) ActiveIds.Add(c.Id);

        float hp = 1f, dmg = 1f, speed = 1f, spawn = 1f, crowd = 1f, size = 1f, phase = 1f;
        if (IsActive("hyper")) { speed *= 1.3f; spawn *= 1.15f; phase *= 0.85f; }
        if (IsActive("swarm")) { crowd *= 2f; spawn *= 2f; hp *= 0.8f; }
        if (IsActive("speed")) phase *= 0.75f;
        if (IsActive("chonk")) { hp *= 2.2f; dmg *= 1.3f; speed *= 0.75f; crowd *= 0.6f; size = 1.45f; }

        hp *= MapChallenges.HealthScale; dmg *= MapChallenges.DamageScale;   // map challenge tier
        EnemyHealth = hp; EnemyDamage = dmg; _enemySpeed = speed; _spawnRate = spawn;
        Crowd = crowd; EnemyVisualScale = size; PhaseTimeScale = phase;
        RewardMultiplier = RewardFor(set);
        WeaponsSilenced = false;
        SurgeActive = false;
    }

    /// <summary>
    /// The player-side effects and the runtime node, for the live run. Called
    /// from RunMeta.Attach after <see cref="BeginRun"/>; the offer pool and the
    /// enemy director already exist by then.
    /// </summary>
    public static void Attach(Main main)
    {
        StatSheet stats = main.Player.Stats;
        if (IsActive("hyper"))
        {
            stats.Add(Stat.MoveSpeed, ModifierOp.Additive, 0.30f, Source);
            stats.Add(Stat.AttackSpeed, ModifierOp.Additive, 0.25f, Source);
            stats.Add(Stat.ProjectileSpeed, ModifierOp.Additive, 0.25f, Source);
        }
        if (IsActive("glass"))
        {
            stats.Add(Stat.MaxHealth, ModifierOp.Multiplicative, 0.5f, Source);
            stats.Add(Stat.Damage, ModifierOp.Multiplicative, 1.5f, Source);
        }
        if (IsActive("lowgrav"))
        {
            main.Player.SetGravityScale(0.5f);
            stats.Add(Stat.JumpHeight, ModifierOp.Additive, 0.5f, Source);
        }
        if (IsActive("noitems")) Progression.OfferPool.Current?.ClearItems();
        if (IsActive("elites"))
        {
            main.Enemies.EliteWaveInterval = 20f;
            main.Enemies.EliteWaveDuration = 9f;
        }

        main.AddChild(new ChallengeRuntime(main));
        GD.Print($"META challenges set=[{string.Join(",", Active.Select(c => c.Id))}] reward=x{RewardMultiplier:0.##}");
    }

    /// <summary>Source tag for every stat modifier a challenge adds.</summary>
    public static readonly object Source = new();

    /// <summary>Called by EnemyManager for every spawn: Leviathans' size.</summary>
    public static void DressEnemy(Node3D enemy)
    {
        if (EnemyVisualScale == 1f) return;
        // The model only: the collider stays as tuned, and a scaled physics
        // body is something Godot does not support well.
        foreach (Node child in enemy.GetChildren())
            if (child is Visuals.CharacterRig rig) rig.Scale = Vector3.One * EnemyVisualScale;
    }

    /// <summary>"Riptide, Glass Hull" or "none".</summary>
    public static string Describe(IEnumerable<ChallengeDef> set)
    {
        string s = string.Join(", ", set.Select(c => c.Name));
        return s.Length == 0 ? "none" : s;
    }
}
