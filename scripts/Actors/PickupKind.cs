namespace Wavebreak.Actors;

public enum PickupKind
{
    /// <summary>Experience orb. Value is raw experience.</summary>
    Xp,

    /// <summary>Coin. Value is raw gold.</summary>
    Gold,

    /// <summary>Med-pack. Value is a fraction of maximum health, not points, so a
    /// pack found at minute one is still worth taking at minute fourteen.</summary>
    Health,
}

