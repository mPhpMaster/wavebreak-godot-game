namespace Wavebreak.Game;

public sealed partial class Main
{
    /// <summary>Automated co-op test: open a real level-up hand through the
    /// panel (not the autopilot's instant pick), so the test can walk while it
    /// is open and pick from it with a key.</summary>
    internal bool OpenLevelUpForTest()
    {
        var hand = _offers.Roll(3, _weapons, _inventory, _player.Stats);
        if (hand.Count == 0) return false;
        _levelUp.Show(hand, Choose);
        return _levelUp.IsOpen;
    }
}
