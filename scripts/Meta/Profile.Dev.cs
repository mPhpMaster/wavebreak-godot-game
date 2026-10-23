namespace Wavebreak.Meta;

/// <summary>Developer-panel access to the profile (UI/DevPanel). Nothing in the
/// game itself ever takes an unlock back or wipes the file; only the panel does.</summary>
public static partial class Profile
{
    /// <summary>Take one unlock back. Returns true when it was held.</summary>
    public static bool Relock(string id) => UnlockSet.Remove(id);

    /// <summary>Forget a found secret kind.</summary>
    public static bool ForgetSecret(string kind) => SecretSet.Remove(kind);

    /// <summary>Back to a brand-new profile in memory: stats, unlocks, secrets,
    /// skins, Pearls, shop, difficulty and hats. Does not save.</summary>
    public static void ResetAll()
    {
        EnsureLoaded();
        Stats.Clear();
        UnlockSet.Clear();
        SecretSet.Clear();
        SkinChoice.Clear();
        MetaValues.Clear();
        SelectedCharacter = Characters.DefaultId;
    }
}
