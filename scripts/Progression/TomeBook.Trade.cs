using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Progression;

/// <summary>
/// Trading a whole tome, level and all (Game/Trade): the giver forgets it,
/// the receiver learns it straight at that level. Same rule as
/// <see cref="Add"/>: every modifier is tagged with the def, so removing is
/// one <see cref="StatSheet.RemoveAllFrom"/> and granting writes the total
/// for the level once.
/// </summary>
public sealed partial class TomeBook
{
    /// <summary>Forgets a held tome and drops all of its modifiers. Returns
    /// the level it had, or 0 when it was not held. The HUD's tome list has
    /// no "removed" event; the caller rebuilds it (Hud.ResyncTomes).</summary>
    public int TradeRemove(TomeDef? def)
    {
        if (def is null) return 0;
        int level = LevelOf(def);
        if (level <= 0) return 0;
        _stats.RemoveAllFrom(def);
        _order.Remove(def);
        _levels.Remove(def.Id);
        return level;
    }

    /// <summary>True when <paramref name="def"/> can be learned as a new tome:
    /// not held, and a slot free.</summary>
    public bool CanLearnTraded(TomeDef? def) => def is not null && LevelOf(def) == 0 && !IsFull;

    /// <summary>Learns a traded tome directly at <paramref name="level"/>
    /// (clamped to its cap). False, and nothing changed, when it is already
    /// held or every slot is taken.</summary>
    public bool TradeGrant(TomeDef? def, int level)
    {
        if (!CanLearnTraded(def) || level <= 0) return false;
        level = System.Math.Min(level, def!.MaxLevel);
        _order.Add(def);
        _levels[def.Id] = level;
        _stats.RemoveAllFrom(def);
        if (def.Op != ModifierOp.Multiplicative || def.PerLevel > 0f)
            _stats.Add(def.Stat, def.Op, def.ValueAtLevel(level), def);
        TomeExtras.Apply(def, _stats, level);
        if (_publish) EventBus.Publish(new TomeAcquired(def.Id, level));
        return true;
    }
}
