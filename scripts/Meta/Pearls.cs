namespace Wavebreak.Meta;

/// <summary>
/// The permanent currency. Earned at the end of every run
/// (<see cref="RunRewards"/>), spent in the shop (<see cref="MetaShop"/>),
/// stored in the profile's <c>[meta]</c> section.
///
/// Gold is the run's money and is gone when the run ends; Pearls are what the
/// tide leaves on the shore. Two names so the two never get confused on screen.
/// </summary>
public static class Pearls
{
    public const string Name = "Pearls";

    private const string BalanceKey = "pearls";
    private const string LifetimeKey = "pearls_lifetime";

    public static long Balance
    {
        get { Profile.EnsureLoaded(); return Profile.GetMeta(BalanceKey); }
    }

    /// <summary>Everything ever earned, spent or not.</summary>
    public static long Lifetime
    {
        get { Profile.EnsureLoaded(); return Profile.GetMeta(LifetimeKey); }
    }

    /// <summary>Credit an amount. Does not save; the caller decides when.</summary>
    public static void Earn(long amount)
    {
        if (amount <= 0) return;
        Profile.EnsureLoaded();
        Profile.SetMeta(BalanceKey, Profile.GetMeta(BalanceKey) + amount);
        Profile.SetMeta(LifetimeKey, Profile.GetMeta(LifetimeKey) + amount);
    }

    /// <summary>Returned by a refund: back to the balance, not counted as earned.</summary>
    public static void Return(long amount)
    {
        if (amount <= 0) return;
        Profile.SetMeta(BalanceKey, Profile.GetMeta(BalanceKey) + amount);
    }

    /// <summary>Take an amount if there is enough. Does not save.</summary>
    public static bool TrySpend(long amount)
    {
        if (amount < 0) return false;
        long have = Balance;
        if (have < amount) return false;
        Profile.SetMeta(BalanceKey, have - amount);
        return true;
    }
}
