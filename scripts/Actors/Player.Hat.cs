namespace Wavebreak.Actors;

/// <summary>The cosmetic hat (Meta/Hats). Re-applied whenever the look changes,
/// because a new look is a new rig.</summary>
public sealed partial class Player
{
    /// <summary>Hat id being worn, or null.</summary>
    public string? HatId { get; private set; }

    public void WearHat(string? hatId)
    {
        HatId = hatId;
        ApplyHat();
    }

    private void ApplyHat()
    {
        if (_rig is not null && IsInstanceValid(_rig)) _rig.SetHat(HatId);
    }
}
