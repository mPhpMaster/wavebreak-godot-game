using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>The two friendly map encounters (<see cref="Game.CuteShrineSpawner"/>).
/// Separate from <see cref="MapShrineKind"/> so the map-shrine code, the
/// autopilot and the shrine HUD stay exactly as they were.</summary>
public enum CuteShrineKind { Balance, Well }

/// <summary>One stat the Shrine of Balance weighs, and the size of one "unit"
/// of it (about a common shrine blessing), so different stats compare.</summary>
public sealed record BalanceStat(Stat Stat, ModifierOp Op, float Unit, string Glyph, Color Color);

/// <summary>
/// The Shrine of Balance and the Wishing Well, as numbers. Placement counts are
/// per map on <see cref="StageDef.BalanceShrines"/> / <see cref="StageDef.WishingWells"/>.
///
/// <b>Shrine of Balance.</b> Your bonuses are weighed in units (one unit is
/// about one common shrine blessing): +8% damage, +7% attack speed, +15 max
/// health and so on. The shrine takes <see cref="BalanceTake"/> of your
/// heaviest bonus and spreads it, times <see cref="BalanceReturn"/>, evenly
/// over the other seven. A lopsided build gets rounder and a little bigger
/// overall. It needs a bonus of at least <see cref="BalanceMinUnits"/> units
/// to work with, and each shrine balances once.
///
/// <b>Wishing Well.</b> Throw gold in for a random blessing. The price climbs
/// with every coin thrown this run; each well takes <see cref="WellUsesPerShrine"/>.
/// Every throw also has a <see cref="WellBonusChance"/> (plus luck) chance of a
/// second, different blessing: "the well liked that one".
/// </summary>
public static class CuteShrines
{
    public const float InteractRadius = 2.8f;
    public const float InteractHeight = 2.2f;
    public const float MinSpacing = 14f;

    // ------------------------------------------------------------ balance

    public static readonly IReadOnlyList<BalanceStat> BalanceStats = new List<BalanceStat>
    {
        new(Stat.Damage, ModifierOp.Additive, 0.08f, "sword", new Color(1.00f, 0.55f, 0.45f)),
        new(Stat.AttackSpeed, ModifierOp.Additive, 0.07f, "hourglass", new Color(1.00f, 0.80f, 0.40f)),
        new(Stat.MaxHealth, ModifierOp.Flat, 15f, "heart", new Color(1.00f, 0.50f, 0.62f)),
        new(Stat.Armor, ModifierOp.Flat, 3f, "shield", new Color(0.70f, 0.74f, 0.86f)),
        new(Stat.MoveSpeed, ModifierOp.Additive, 0.05f, "boot", new Color(0.50f, 0.92f, 0.95f)),
        new(Stat.CritChance, ModifierOp.Flat, 0.03f, "target", new Color(1.00f, 0.62f, 0.85f)),
        new(Stat.AreaSize, ModifierOp.Additive, 0.07f, "expand", new Color(0.62f, 0.78f, 1.00f)),
        new(Stat.Luck, ModifierOp.Flat, 0.10f, "clover", new Color(0.55f, 0.95f, 0.55f)),
    };

    public const float BalanceTake = 0.5f;
    public const float BalanceReturn = 1.6f;
    public const float BalanceMinUnits = 2f;

    /// <summary>How many units of bonus a sheet carries in one stat.</summary>
    public static float Units(StatSheet sheet, BalanceStat b)
    {
        float v = sheet.Get(b.Stat);
        float baseValue = StatSheet.BaseValue(b.Stat);
        float bonus;
        if (StatSheet.IsBonusStat(b.Stat)) bonus = v;                        // damage: 0 is x1
        else if (b.Op == ModifierOp.Additive && baseValue > 0f) bonus = v / baseValue - 1f;
        else bonus = v - baseValue;
        return bonus / b.Unit;
    }

    /// <summary>The balance a sheet would get: the heaviest stat, how many
    /// units it gives up, and how many each other stat receives. Null when
    /// nothing is heavy enough.</summary>
    public static (BalanceStat Top, float Take, float Give)? PlanBalance(StatSheet sheet)
    {
        BalanceStat? top = null;
        float best = float.MinValue;
        foreach (BalanceStat b in BalanceStats)
        {
            float u = Units(sheet, b);
            if (u > best) { best = u; top = b; }
        }
        if (top is null || best < BalanceMinUnits) return null;
        float take = best * BalanceTake;
        float give = take * BalanceReturn / (BalanceStats.Count - 1);
        return (top, take, give);
    }

    /// <summary>Apply a plan to a sheet under <paramref name="source"/>, so it
    /// can be removed exactly (the panel's preview does just that).</summary>
    public static void ApplyBalance(StatSheet sheet, (BalanceStat Top, float Take, float Give) plan, object source)
    {
        foreach (BalanceStat b in BalanceStats)
        {
            float units = ReferenceEquals(b, plan.Top) ? -plan.Take : plan.Give;
            sheet.Add(b.Stat, b.Op, units * b.Unit, source);
        }
    }

    /// <summary>"+8% Damage" for a unit count of a balance stat.</summary>
    public static string DescribeUnits(BalanceStat b, float units)
    {
        float v = units * b.Unit;
        // Whole stats read to one decimal; fractions to a tenth of a percent.
        v = b.Op == ModifierOp.Flat && b.Unit >= 1f ? MathF.Round(v, 1) : MathF.Round(v, 3);
        return Catalog.DescribeEffect(new ItemEffect { Stat = b.Stat, Op = b.Op, PerStack = v });
    }

    /// <summary>A stat's value as shown on the panel ("x1.24", "142", "12%").</summary>
    public static string Show(StatSheet sheet, Stat stat)
    {
        float v = sheet.Get(stat);
        return stat switch
        {
            Stat.Damage => $"x{1f + v:0.00}",
            Stat.AttackSpeed or Stat.AreaSize => $"x{v:0.00}",
            Stat.CritChance => $"{v * 100f:0.#}%",
            Stat.MoveSpeed => $"{v:0.0} m/s",
            Stat.Luck => $"{v:0.00}",
            _ => $"{v:0.#}",
        };
    }

    // --------------------------------------------------------------- well

    public const int WellUsesPerShrine = 3;
    public const int WellBaseCost = 20;
    public const int WellCostStep = 15;
    public const int WellCostCurve = 3;
    public const float WellBonusChance = 0.18f;
    public const float WellBonusPerLuck = 0.04f;
    public const float WellBonusMax = 0.5f;

    /// <summary>20, 38, 62, 92, 128 ... gold for the n-th coin of the run.</summary>
    public static int WellPrice(int thrown) => WellBaseCost + WellCostStep * thrown + WellCostCurve * thrown * thrown;

    public static float WellBonus(float luck) => Math.Clamp(WellBonusChance + WellBonusPerLuck * luck, 0f, WellBonusMax);

    // --------------------------------------------------------------- look

    public static string Name(CuteShrineKind kind) => kind == CuteShrineKind.Balance ? "Shrine of Balance" : "Wishing Well";

    public static Color Tint(CuteShrineKind kind)
        => kind == CuteShrineKind.Balance ? new Color(0.80f, 0.70f, 1.00f) : new Color(0.50f, 0.88f, 1.00f);

    public static string Glyph(CuteShrineKind kind) => kind == CuteShrineKind.Balance ? "scales" : "well";
}
