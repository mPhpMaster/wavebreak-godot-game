using Godot;
using Wavebreak.Core;
using Wavebreak.Progression;

namespace Wavebreak.UI;

/// <summary>Developer panel: rebuild the weapon and tome rows after the build
/// was edited in ways the game never does (a weapon or tome taken away).</summary>
public sealed partial class Hud
{
    public void DevResync(Combat.WeaponSystem weapons, TomeBook? tomes)
    {
        foreach (Node n in _weaponList.GetChildren()) n.QueueFree();
        _weaponRows.Clear();
        _weaponOrder.Clear();
        foreach (Combat.WeaponRuntime w in weapons.Weapons)
            OnWeaponAcquired(new WeaponAcquired(w.Def.Id, w.Level));

        if (_tomeList is null) return;
        foreach (Node n in _tomeList.GetChildren())
            if (n != _tomeHeader) n.QueueFree();
        _tomeRows.Clear();
        _tomeList.Visible = false;
        if (tomes is null) return;
        foreach (Data.TomeDef t in tomes.Held) OnTomeAcquired(new TomeAcquired(t.Id, tomes.LevelOf(t)));
    }
}
