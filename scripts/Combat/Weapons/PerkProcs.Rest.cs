namespace Wavebreak.Combat.Weapons;

public sealed partial class PerkProcs
{
    private bool InRest => _main?.Stages?.InRestRoom == true;
    private void SuspendCombatEffects()
    {
        foreach (var effect in _fx) Give(effect.Mesh);
        _fx.Clear();
        foreach (var puddle in _wake) Give(puddle.Mesh);
        _wake.Clear(); _wakeHit.Clear(); _wakeFor = 0;
        foreach (var mote in _motes) Give(mote.Mesh);
        _motes.Clear();
        foreach (var bubble in _bubbles) Give(bubble.Mesh);
        _bubbles.Clear();
    }
}
