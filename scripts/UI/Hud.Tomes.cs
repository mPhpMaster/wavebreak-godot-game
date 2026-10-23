using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;

namespace Wavebreak.UI;

/// <summary>
/// The tome list: under the weapon list, same row style, tinted per tome, with
/// a small "TOMES n/4" header so the slot limit is visible before it bites.
/// Driven by <see cref="TomeAcquired"/> like the weapon rows are by
/// WeaponAcquired, so the HUD still holds no reference to the run's systems.
/// </summary>
public sealed partial class Hud
{
    private readonly Dictionary<string, Label> _tomeRows = new();
    private VBoxContainer _tomeList = null!;
    private Label _tomeHeader = null!;
    private bool _tomesSubscribed;

    private static readonly Color TomeHeaderTint = new(0.80f, 0.74f, 0.55f);

    private void BuildTomeList()
    {
        // Below the six weapon rows the weapon list can ever hold.
        float top = XpBarHeight + 12f + 30f + HealthBarHeight + ShieldBarHeight + 14f
                    + RowHeight * Combat.WeaponSystem.MaxWeapons + 10f;

        _tomeList = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _tomeList.AddThemeConstantOverride("separation", 0);
        Place(_tomeList, Margin, top, HealthBarWidth, RowHeight * (Tomes.MaxSlots + 2));
        AddChild(_tomeList);

        _tomeHeader = Text(Loc.T("TOMES"), 13, TomeHeaderTint);
        _tomeList.AddChild(_tomeHeader);

        if (!_tomesSubscribed)
        {
            _tomesSubscribed = true;
            EventBus.Subscribe<TomeAcquired>(OnTomeAcquired);
            TreeExiting += () =>
            {
                if (!_tomesSubscribed) return;
                _tomesSubscribed = false;
                EventBus.Unsubscribe<TomeAcquired>(OnTomeAcquired);
            };
        }
    }

    private void OnTomeAcquired(TomeAcquired e)
    {
        if (string.IsNullOrEmpty(e.Id) || _tomeList is null) return;

        TomeDef? def = Tomes.ById(e.Id);
        string name = Loc.T(def?.DisplayName ?? e.Id);

        if (!_tomeRows.TryGetValue(e.Id, out Label? row))
        {
            row = Text(name, 15, Ink);
            row.CustomMinimumSize = new Vector2(0f, RowHeight - 2f);
            if (def is not null) row.AddThemeColorOverride("font_color", def.Tint);
            _tomeList.AddChild(def is not null ? IconView.Row(Icons.For(def), row, RowHeight - 2f) : row);
            _tomeRows[e.Id] = row;
        }

        bool maxed = def is not null && e.NewLevel >= def.MaxLevel;
        row.Text = maxed ? Loc.F("{0}   MAX", name) : Loc.F("{0}   Lv {1}", name, e.NewLevel);

        int slots = TomeBook.Current?.Slots ?? Tomes.MaxSlots;
        _tomeHeader.Text = Loc.F("TOMES  {0}/{1}", _tomeRows.Count, slots);
        _tomeList.Visible = true;
    }
}
