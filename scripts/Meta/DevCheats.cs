using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// Profile-side cheats for the developer panel (UI/DevPanel): unlocks, Pearls,
/// the Pearl Shop, difficulty tiers and found shrines. Each one writes the
/// profile through <see cref="Profile.Save"/>, which is a no-op for automated
/// runs (no path), so a check run can call all of them without touching the
/// player's real file.
/// </summary>
public static class DevCheats
{
    /// <summary>Every unlock row, every hat, every shrine kind and every tier.
    /// Returns how many unlock rows were newly earned.</summary>
    public static int UnlockEverything()
    {
        Profile.EnsureLoaded();
        int n = 0;
        foreach (UnlockDef u in Unlocks.All) if (Profile.Unlock(u.Id)) n++;
        foreach (HatDef h in Hats.All)
            if (h.PearlCost > 0 && h.UnlockedBy is null) Profile.SetMeta(Hats.BoughtKey(h.Id), 1);
        MarkAllShrinesFound();
        UnlockAllTiers();
        Profile.Save();
        GD.Print($"DEV unlock all: +{n} rows ({Profile.UnlockedIds.Count}/{Unlocks.All.Count})");
        return n;
    }

    /// <summary>Earn or take back one unlock row. Saves.</summary>
    public static bool SetUnlocked(UnlockDef u, bool unlocked)
    {
        Profile.EnsureLoaded();
        bool changed = unlocked ? Profile.Unlock(u.Id) : Profile.Relock(u.Id);
        Profile.Save();
        return changed;
    }

    /// <summary>Record a win on every tier but the last, which is what opens
    /// the next one (<see cref="DifficultyTiers.IsUnlocked"/>). Does not save.</summary>
    public static void UnlockAllTiers()
    {
        for (int i = 0; i < DifficultyTiers.All.Count - 1; i++)
            DifficultyTiers.MarkWon(DifficultyTiers.All[i]);
    }

    /// <summary>Every hidden shrine kind counts as found. Does not save.</summary>
    public static void MarkAllShrinesFound()
    {
        foreach (SecretKindDef k in SecretSpawner.Kinds) Profile.FindSecret(k.Id);
    }

    public static void GivePearls(long amount)
    {
        Pearls.Earn(amount);
        Profile.Save();
    }

    /// <summary>Every Pearl Shop line at its top level, free. Saves.</summary>
    public static void MaxShop()
    {
        foreach (ShopUpgradeDef u in MetaShop.All) MetaShop.SetLevel(u, u.MaxLevel);
        Profile.Save();
    }

    /// <summary>Every Pearl Shop line back to zero, without a refund. Saves.</summary>
    public static void ResetShop()
    {
        foreach (ShopUpgradeDef u in MetaShop.All) MetaShop.SetLevel(u, 0);
        Profile.Save();
    }

    /// <summary>A brand-new profile. Saves (an empty file for a real profile).</summary>
    public static void ResetProfile()
    {
        Profile.ResetAll();
        Profile.Save();
        GD.Print("DEV profile reset");
    }
}
