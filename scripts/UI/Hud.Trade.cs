using Godot;
using Wavebreak.Core;
using Wavebreak.Progression;

namespace Wavebreak.UI;

/// <summary>A trade at the swap meet can take a tome away, which the tome
/// rows (driven only by TomeAcquired) cannot show: rebuild them from the book.
/// The weapon rows are untouched (weapons are not traded).</summary>
public sealed partial class Hud
{
    public void ResyncTomes(TomeBook? tomes)
    {
        if (_tomeList is null) return;
        foreach (Node n in _tomeList.GetChildren())
            if (n != _tomeHeader) n.QueueFree();
        _tomeRows.Clear();
        _tomeList.Visible = false;
        if (tomes is null) return;
        foreach (Data.TomeDef t in tomes.Held) OnTomeAcquired(new TomeAcquired(t.Id, tomes.LevelOf(t)));
        if (tomes.Count == 0) _tomeHeader.Text = Meta.Loc.T("TOMES");
    }
}
