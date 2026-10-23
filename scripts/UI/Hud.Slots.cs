using Godot;
using Wavebreak.Meta;
using Wavebreak.Combat;

namespace Wavebreak.UI;

/// <summary>
/// "WEAPONS n/max" over the weapon rows, like the tome list's header, so the
/// weapon cap (6, raised by the Pearl Shop's Weapon Rack) is visible before it
/// bites. The slot count is set after the HUD is built (Meta/ShopExtras), so a
/// half-second timer keeps the header and the tome list's position current;
/// the tome list sits under however many weapon rows this run can hold.
/// </summary>
public sealed partial class Hud
{
    private Label _weaponHeader = null!;
    private int _shownWeaponSlots = -1;
    private int _shownWeaponCount = -1;

    private static readonly Color WeaponHeaderTint = new(0.62f, 0.82f, 1.00f);

    private void BuildWeaponHeader()
    {
        _weaponHeader = Text(Loc.T("WEAPONS"), 13, WeaponHeaderTint);
        _weaponList.AddChild(_weaponHeader);
        _weaponList.MoveChild(_weaponHeader, 0);

        var timer = new Timer { Name = "SlotWatch", WaitTime = 0.5, Autostart = true };
        timer.Timeout += RefreshWeaponSlots;
        AddChild(timer);
        CallDeferred(MethodName.RefreshWeaponSlots);
    }

    private void RefreshWeaponSlots()
    {
        if (_weaponHeader is null || !IsInstanceValid(_weaponHeader)) return;
        WeaponSystem? system = Game.Main.Instance?.Weapons;
        int slots = system?.Slots ?? WeaponSystem.MaxWeapons;
        int count = system?.Weapons.Count ?? _weaponRows.Count;
        if (slots == _shownWeaponSlots && count == _shownWeaponCount) return;
        _shownWeaponSlots = slots;
        _shownWeaponCount = count;
        _weaponHeader.Text = Loc.F("WEAPONS  {0}/{1}", count, slots);

        // Header plus one row per slot (rows are RowHeight plus the list's 2 px gap).
        float top = _weaponList.OffsetTop + (RowHeight - 6f) + slots * (RowHeight + 2f) + 10f;
        if (_tomeList is not null)
        {
            float h = _tomeList.OffsetBottom - _tomeList.OffsetTop;
            _tomeList.OffsetTop = top;
            _tomeList.OffsetBottom = top + h;
        }
    }
}
