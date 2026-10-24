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
    float Reward,     // Pearls multiplier
    int Phases = 3,   // maps (phases) in a run: rest rooms between, the final boss on the last
    float PortalSeconds = 90f)   // how long a boss portal stays open before it pulls everyone through
{
    public string Summary => Reward <= 1f
        ? Loc.T("The tide as it was meant to be.")
        : Loc.F("enemies x{0:0.##} hp, x{1:0.##} dmg, x{2:0.##} spawns  ·  Pearls x{3:0.##}", Health, Damage, SpawnRate, Reward);
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
        // Harder tiers are also longer: more maps before the final boss (see
        // Meta/Routes for which maps, Data/Stages.ForPhase for how hard each is),
        // and their boss portals close sooner (StageDirector: at zero the
        // portal pulls everyone through).
        new("normal",    "Normal",    1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 3, 90f),
        new("hard",      "Hard",      1.45f, 1.30f, 1.25f, 1.15f, 1.50f, 3, 75f),
        new("nightmare", "Nightmare", 2.10f, 1.70f, 1.55f, 1.30f, 2.25f, 4, 60f),
        // Past Nightmare: each tier unlocks by winning the one before it, like the rest.
        new("tempest",   "Tempest",   3.00f, 2.20f, 1.80f, 1.45f, 3.00f, 4, 50f),
        new("abyss",     "Abyss",     4.30f, 2.80f, 2.10f, 1.60f, 4.00f, 5, 40f),
        new("leviathan", "Leviathan", 6.20f, 3.60f, 2.45f, 1.75f, 5.50f, 5, 35f),
        new("endless",   "Endless Tide", 9.00f, 4.80f, 2.80f, 1.90f, 7.50f, 5, 30f),
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
        // Co-op: the host's tier for everyone (enemies are the host's anyway;
        // this keeps every player's rewards and banner in step).
        if (Net.Coop.InRun && ById(Net.Coop.DifficultyId) is { } host) Active = host;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--difficulty=") && ById(arg["--difficulty=".Length..]) is { } forced)
                Active = forced;
        EndlessMode.BeginRun();   // replaces Active with loop 1's tier when on
    }

    /// <summary>Endless mode steps the tier up mid-run.</summary>
    internal static void SetActive(DifficultyTier tier) => Active = tier;

    /// <summary>The run's last boss is down: record a win on the active tier.
    /// Returns the tier this unlocked, if it unlocked one. Does not save.</summary>
    public static DifficultyTier? RecordWin()
    {
        if (EndlessMode.Active) return null;   // an endless run never "wins" a tier
        int i = IndexOf(Active);
        bool nextWasLocked = i + 1 < All.Count && !IsUnlocked(i + 1);
        Profile.SetMeta(WonKey(Active), Profile.GetMeta(WonKey(Active)) + 1);
        return nextWasLocked && IsUnlocked(i + 1) ? All[i + 1] : null;
    }
}
