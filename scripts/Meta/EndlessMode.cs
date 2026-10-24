using System;
using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// Endless mode: off until every difficulty tier has been won, then a toggle on
/// the character page. An endless run starts on Normal and plays the three-map
/// loop again and again; each completed loop raises the tier one step, and past
/// the last tier it keeps compounding (<see cref="Loop"/> beyond the table adds
/// +60% health, +35% damage, +15% spawns per extra loop), so it never ends.
///
/// The director asks <see cref="Active"/> whether the last boss of a loop ends
/// the run; if it does not, it calls <see cref="NextLoop"/> and goes back to
/// phase one (through a rest room). <see cref="DifficultyTiers.Active"/> is
/// replaced with the loop's tier, which the enemy director already reads
/// every tick.
/// </summary>
public static class EndlessMode
{
    private const string EnabledKey = "endless_on";
    private const string BestKey = "endless_best_loop";

    /// <summary>Unlocked once the last tier has been won.</summary>
    public static bool Unlocked => DifficultyTiers.IsWon(DifficultyTiers.All[^1]);

    /// <summary>The player's toggle (only meaningful once unlocked).</summary>
    public static bool Selected
    {
        get { Profile.EnsureLoaded(); return Unlocked && Profile.GetMeta(EnabledKey) > 0; }
        set { Profile.SetMeta(EnabledKey, value && Unlocked ? 1 : 0); }
    }

    /// <summary>True for the run in progress.</summary>
    public static bool Active { get; private set; }

    /// <summary>Completed loops this run (0 on the first pass).</summary>
    public static int Loop { get; private set; }

    public static long BestLoop { get { Profile.EnsureLoaded(); return Profile.GetMeta(BestKey); } }

    /// <summary>Called by <see cref="DifficultyTiers.BeginRun"/>.</summary>
    public static void BeginRun()
    {
        Loop = 0;
        Active = Selected || Array.IndexOf(OS.GetCmdlineUserArgs(), "--endless") >= 0;
        if (Net.Coop.InRun) Active = Net.Coop.Endless;
        if (Active) DifficultyTiers.SetActive(TierFor(0));
    }

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
            Phases = 3,
        };
    }

    /// <summary>A loop's last boss is down: raise the stakes and go again.</summary>
    public static DifficultyTier NextLoop()
    {
        Loop++;
        if (Loop > BestLoop) Profile.SetMeta(BestKey, Loop);
        DifficultyTier next = TierFor(Loop);
        DifficultyTiers.SetActive(next);
        return next;
    }
}
