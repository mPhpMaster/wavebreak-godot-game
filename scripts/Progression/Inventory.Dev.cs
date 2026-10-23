using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Progression;

/// <summary>Developer-panel build and economy editing (UI/DevPanel). Gold and
/// experience go through the bus exactly like a pickup's, so the HUD and every
/// listener see them; the amounts are exact, not scaled by Gold/XP Gain.</summary>
public sealed partial class Inventory
{
    /// <summary>Remove one copy of an item and re-apply what is left.</summary>
    public bool DevRemove(ItemDef def)
    {
        string id = KeyOf(def);
        if (!_byId.TryGetValue(id, out ItemStack? stack) || stack.Count <= 0) return false;
        stack.Count--;
        TotalStacks--;
        ReapplyStack(stack);
        if (stack.Count == 0)
        {
            _byId.Remove(id);
            _stacks.Remove(stack);
        }
        return true;
    }

    /// <summary>Remove every item.</summary>
    public void DevClearItems()
    {
        foreach (ItemStack s in _stacks) _stats.RemoveAllFrom(s.Def);
        _stacks.Clear();
        _byId.Clear();
        TotalStacks = 0;
    }

    /// <summary>Forget a weapon in the level ledger (after it was removed).</summary>
    public void DevForgetWeapon(string id) => _weaponLevels.Remove(id);

    public void DevForgetAllWeapons() => _weaponLevels.Clear();

    /// <summary>Set gold to exactly <paramref name="gold"/>.</summary>
    public void DevSetGold(int gold)
    {
        int target = System.Math.Max(0, gold);
        int delta = target - Gold;
        Gold = target;
        if (delta != 0) Publish(new GoldGained(delta, Gold));
    }

    /// <summary>Exactly enough experience for <paramref name="levels"/> level-ups
    /// (each one queues a choice, like a real level-up).</summary>
    public void DevLevelUp(int levels)
    {
        if (levels <= 0) return;
        int target = System.Math.Min(XpCurve.MaxLevel, Level + levels);
        int need = XpCurve.TotalXpForLevel(target) - Xp;
        if (need > 0) Bank(need);
    }

    /// <summary>Raw experience, unscaled.</summary>
    public void DevAddXp(int amount)
    {
        if (amount > 0) Bank(amount);
    }
}
