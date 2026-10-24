using System;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Progression;

/// <summary>
/// Trading between co-op players at the rest room's swap meet
/// (Game/Trade). The only two things the game never did before: take item
/// copies away, and move gold without it counting as earned or spent.
/// </summary>
public sealed partial class Inventory
{
    /// <summary>Removes up to <paramref name="count"/> copies of an item and
    /// re-applies what is left to the stat sheet (all of its modifiers go when
    /// the last copy does). Returns how many were removed.</summary>
    public int TradeRemove(ItemDef? def, int count)
    {
        if (def is null || count <= 0) return 0;
        string id = KeyOf(def);
        if (!_byId.TryGetValue(id, out ItemStack? stack) || stack.Count <= 0) return 0;

        int taken = Math.Min(count, stack.Count);
        stack.Count -= taken;
        TotalStacks -= taken;
        ReapplyStack(stack);
        if (stack.Count <= 0)
        {
            _byId.Remove(id);
            _stacks.Remove(stack);
            _stats.RemoveAllFrom(def);
        }
        // The results screen keeps its own stack table from this event; a
        // count of 0 drops the item from it.
        EventBus.Publish(new ItemAcquired(id, stack.Count));
        return taken;
    }

    /// <summary>
    /// Moves gold in or out for a trade. Not <see cref="GrantGold"/> (no gold
    /// gain scaling) and not <see cref="SpendGold"/>: traded gold was neither
    /// earned nor spent, so quests, the scoreboard, pickup sounds and gold
    /// procs must not see it. The HUD still hears the new total, through a
    /// zero-amount <see cref="GoldGained"/> that every other listener ignores.
    /// False, and nothing changed, when it would go below zero.
    /// </summary>
    public bool TradeGold(int delta)
    {
        if (delta == 0) return true;
        if (Gold + delta < 0) return false;
        Gold += delta;
        Publish(new GoldGained(0, Gold));
        return true;
    }
}
