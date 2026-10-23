using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Meta;

/// <summary>One difficulty tier. Every multiplier is 1 on Normal, so a Normal
/// run is the game exactly as it was before tiers existed.</summary>
public sealed record DifficultyTier(
    string Id,
    string Name,
    float Health,     // enemy health, bosses included
    float Damage,     // enemy damage
    float SpawnRate,  // director credit income and bank
    float Crowd,      // how fast the live cap grows with the clock
    float Reward)     // Pearls multiplier
{
    public string Summary => Reward <= 1f
        ? "The tide as it was meant to be."
        : $"enemies x{Health:0.##} hp, x{Damage:0.##} dmg, x{SpawnRate:0.##} spawns  ·  {Pearls.Name} x{Reward:0.##}";
}

/// <summary>
/// Difficulty tiers chosen before a run, on the character page.
///
/// A tier unlocks by winning a run (beating the last phase boss) on the tier
/// below it. The selection and which tiers have been won live in the profile.
///
/// <see cref="Active"/> is the tier of the run being played. It is resolved
/// once per run by <see cref="BeginRun"/> and read by the enemy director every
/// tick; it is Normal for any automated run without an explicit profile, so
/// smoke tests and captures are unchanged. <c>--difficulty=&lt;id&gt;</c> forces
/// a tier for a test run, locked or not.
/// </summary>
public static partial class DifficultyTiers
{
    public static readonly IReadOnlyList<DifficultyTier> All = new List<DifficultyTier>
    {
        new("normal",    "Normal",    1.00f, 1.00f, 1.00f, 1.00f, 1.00f),
        new("hard",      "Hard",      1.45f, 1.30f, 1.25f, 1.15f, 1.50f),
        new("nightmare", "Nightmare", 2.10f, 1.70f, 1.55f, 1.30f, 2.25f),
    };

    private const string SelectedKey = "difficulty";
    private static string WonKey(DifficultyTier t) => "won_" + t.Id;

    /// <summary>The tier of the run in progress.</summary>
    public static DifficultyTier Active { get; private set; } = All[0];

    public static int IndexOf(DifficultyTier tier)
    {
        for (int i = 0; i < All.Count; i++) if (All[i].Id == tier.Id) return i;
        return 0;
    }

    public static DifficultyTier? ById(string id)
    {
        foreach (DifficultyTier t in All) if (t.Id == id) return t;
        return null;
    }

    public static bool IsWon(DifficultyTier tier)
    {
        Profile.EnsureLoaded();
        return Profile.GetMeta(WonKey(tier)) > 0;
    }

    public static bool IsUnlocked(int index)
        => index <= 0 || (index < All.Count && IsWon(All[index - 1]));

    /// <summary>The player's choice on the character page, clamped to what is
    /// unlocked (a hand-edited profile cannot skip a tier).</summary>
    public static DifficultyTier Selected
    {
        get
        {
            Profile.EnsureLoaded();
            int i = (int)Math.Clamp(Profile.GetMeta(SelectedKey), 0L, All.Count - 1);
            while (i > 0 && !IsUnlocked(i)) i--;
            return All[i];
        }
    }

    /// <summary>Store a choice. Locked tiers are refused. Does not save.</summary>
    public static bool Select(int index)
    {
        if (index < 0 || index >= All.Count || !IsUnlocked(index)) return false;
        Profile.SetMeta(SelectedKey, index);
        return true;
    }

    /// <summary>The next unlocked tier after the selected one, wrapping.</summary>
    public static DifficultyTier CycleSelected()
    {
        int start = IndexOf(Selected);
        for (int step = 1; step <= All.Count; step++)
        {
            int i = (start + step) % All.Count;
            if (IsUnlocked(i)) { Select(i); break; }
        }
        return Selected;
    }

    /// <summary>Resolve <see cref="Active"/> for a new run.</summary>
    public static void BeginRun()
    {
        Profile.EnsureLoaded();
        Active = Selected;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--difficulty=") && ById(arg["--difficulty=".Length..]) is { } forced)
                Active = forced;
    }

    /// <summary>The run's last boss is down: record a win on the active tier.
    /// Returns the tier this unlocked, if it unlocked one. Does not save.</summary>
    public static DifficultyTier? RecordWin()
    {
        int i = IndexOf(Active);
        bool nextWasLocked = i + 1 < All.Count && !IsUnlocked(i + 1);
        Profile.SetMeta(WonKey(Active), Profile.GetMeta(WonKey(Active)) + 1);
        return nextWasLocked && IsUnlocked(i + 1) ? All[i + 1] : null;
    }
}
