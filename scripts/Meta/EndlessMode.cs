using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Meta;

/// <summary>One loop's twist: what the loop changes, on top of its tier. Every
/// multiplier is 1 unless named, and <see cref="EliteInterval"/> below 1 brings
/// elite waves closer together.</summary>
public sealed record LoopMutation(string Id, string Name, string Blurb)
{
    public float Health { get; init; } = 1f;
    public float Damage { get; init; } = 1f;
    public float Speed { get; init; } = 1f;
    public float SpawnRate { get; init; } = 1f;
    public float Crowd { get; init; } = 1f;
    public float EliteInterval { get; init; } = 1f;
    public bool Fog { get; init; }
}

/// <summary>
/// Endless mode: off until every difficulty tier has been won, then a toggle on
/// the character page. An endless run starts on Normal and plays the run's maps;
/// each completed loop draws the next set of maps (<see cref="Routes.ForLoop"/>),
/// raises the tier one step and rolls a <see cref="LoopMutation"/>. Past the last
/// tier it keeps compounding (<see cref="Loop"/> beyond the table adds +60%
/// health, +35% damage, +15% spawns per extra loop), so it never ends.
///
/// The loop is what gets harder, not the clock. The run clock's exponential
/// health term stops at <see cref="ClockCapMinutes"/>, about one loop in: left
/// running it reached x76 by minute 60 on top of the loop multipliers, a wall
/// no build could meet whichever map or mutation came up.
///
/// The director asks <see cref="Active"/> whether the last boss of a loop ends
/// the run; if it does not, it calls <see cref="NextLoop"/> and goes back to
/// phase one (through a rest room). <see cref="DifficultyTiers.Active"/> is
/// replaced with the loop's tier, which the enemy director already reads
/// every tick.
///
/// Co-op: maps and mutations are pure functions of the host's seed and the loop
/// count, so every machine holds the same ones and only the count travels: each
/// peer replays <see cref="NextLoop"/> with the boss kill, and a late joiner gets
/// the count from the world snapshot (<see cref="NetSetLoop"/>).
/// </summary>
public static class EndlessMode
{
    private const string EnabledKey = "endless_on";
    private const string BestKey = "endless_best_loop";

    /// <summary>Minutes of run clock the exponential health term counts.</summary>
    public const float ClockCapMinutes = 20f;

    /// <summary>The twists a loop can roll. Host-side numbers except
    /// <see cref="LoopMutation.Fog"/>, which each machine draws for itself.</summary>
    public static readonly IReadOnlyList<LoopMutation> Mutations = new List<LoopMutation>
    {
        new("riptide", "Riptide", "enemies 15% faster") { Speed = 1.15f },
        new("wardens", "Wardens", "elite waves come twice as often") { EliteInterval = 0.5f },
        new("sea_fret", "Sea Fret", "thick fog on every map") { Fog = true },
        new("shoal", "Shoal", "30% more enemies, 15% less health each") { SpawnRate = 1.3f, Crowd = 1.3f, Health = 0.85f },
        new("heavy_water", "Heavy Water", "enemies 30% tougher, 10% slower") { Health = 1.3f, Speed = 0.9f },
        new("razor_reef", "Razor Reef", "enemies hit 25% harder") { Damage = 1.25f },
    };

    /// <summary>Unlocked once the last tier has been won.</summary>
    public static bool Unlocked => DifficultyTiers.IsWon(DifficultyTiers.All[^1]);

    /// <summary>The player's toggle (only meaningful once unlocked).</summary>
    public static bool Selected
    {
        get { Profile.EnsureLoaded(); return Unlocked && Profile.GetMeta(EnabledKey) > 0; }
        set { Profile.SetMeta(EnabledKey, value && Unlocked ? 1 : 0); }
    }

    /// <summary>The toggle, or <c>--endless</c> for an automated run.</summary>
    public static bool Requested => Selected || Array.IndexOf(OS.GetCmdlineUserArgs(), "--endless") >= 0;

    /// <summary>True for the run in progress.</summary>
    public static bool Active { get; private set; }

    /// <summary>Completed loops this run (0 on the first pass).</summary>
    public static int Loop { get; private set; }

    /// <summary>This loop's twist. Null on the first pass and outside Endless.</summary>
    public static LoopMutation? Mutation { get; private set; }

    public static long BestLoop { get { Profile.EnsureLoaded(); return Profile.GetMeta(BestKey); } }

    // Read by the enemy director; all exactly 1 outside a mutated loop.
    public static float Health => Mutation?.Health ?? 1f;
    public static float Damage => Mutation?.Damage ?? 1f;
    public static float Speed => Mutation?.Speed ?? 1f;
    public static float SpawnRate => Mutation?.SpawnRate ?? 1f;
    public static float Crowd => Mutation?.Crowd ?? 1f;
    public static float EliteInterval => Mutation?.EliteInterval ?? 1f;
    public static bool Fog => Mutation?.Fog ?? false;

    /// <summary>Minutes for the exponential health term.</summary>
    public static float ClockMinutes(float minutes) => Active ? MathF.Min(minutes, ClockCapMinutes) : minutes;

    /// <summary>"LOOP 2  ·  Riptide: enemies 15% faster", or "" without a twist.</summary>
    public static string MutationBanner => Mutation is { } m
        ? Loc.F("LOOP {0}  ·  {1}: {2}", Loop + 1, Loc.T(m.Name), Loc.T(m.Blurb)) : "";

    private static ulong _seed;

    /// <summary>Called by <see cref="DifficultyTiers.BeginRun"/>.</summary>
    public static void BeginRun()
    {
        Loop = 0;
        Mutation = null;
        Active = Requested;
        if (Net.Coop.InRun) Active = Net.Coop.Endless;
        if (Active) DifficultyTiers.SetActive(TierFor(0));
    }

    /// <summary>The run's seed, which picks every loop's twist. From
    /// <see cref="Routes.ForRun"/>, the first thing that knows it.</summary>
    public static void BindSeed(ulong seed) => _seed = seed;

    /// <summary>The tier for loop <paramref name="loop"/>: the table in order,
    /// then the last tier with an extra multiplier for every loop past it.</summary>
    public static DifficultyTier TierFor(int loop)
    {
        var all = DifficultyTiers.All;
        if (loop < all.Count) return all[loop];
        DifficultyTier top = all[^1];
        int extra = loop - (all.Count - 1);
        float hp = MathF.Pow(1.60f, extra), dmg = MathF.Pow(1.35f, extra), spawn = MathF.Pow(1.15f, extra);
        return top with
        {
            Id = $"endless_{loop + 1}",
            Name = Loc.F("Endless {0}", loop + 1),
            Health = top.Health * hp,
            Damage = top.Damage * dmg,
            SpawnRate = top.SpawnRate * spawn,
            Crowd = MathF.Min(top.Crowd * MathF.Pow(1.05f, extra), 3f),
            Reward = top.Reward * MathF.Pow(1.2f, extra),
        };
    }

    /// <summary>The twist of loop <paramref name="loop"/> of the run of
    /// <paramref name="seed"/>: none on the first pass, then one per loop, never
    /// the same twice running. The same on every machine.</summary>
    public static LoopMutation? MutationFor(ulong seed, int loop)
    {
        int last = -1;
        for (int l = 1; l <= loop; l++)
        {
            var rng = new Rng(seed ^ 0x1007_5EEDUL ^ ((ulong)l * 0x9E3779B97F4A7C15UL));
            int i = rng.RangeInt(0, last < 0 ? Mutations.Count : Mutations.Count - 1);
            if (last >= 0 && i >= last) i++;
            last = i;
        }
        return last < 0 ? null : Mutations[last];
    }

    /// <summary>Co-op late join: the host's loop count and its tier.</summary>
    public static void NetSetLoop(int loop)
    {
        if (!Active || loop < 0 || loop == Loop) return;
        SetLoop(loop);
    }

    /// <summary>A loop's last boss is down: raise the stakes and go again.</summary>
    public static DifficultyTier NextLoop()
    {
        SetLoop(Loop + 1);
        if (Loop > BestLoop) Profile.SetMeta(BestKey, Loop);
        return DifficultyTiers.Active;
    }

    private static void SetLoop(int loop)
    {
        Loop = loop;
        Mutation = MutationFor(_seed, loop);
        DifficultyTiers.SetActive(TierFor(loop));
        GD.Print($"[endless] loop {Loop} tier {DifficultyTiers.Active.Id} mutation {Mutation?.Id ?? "none"}");
    }
}
