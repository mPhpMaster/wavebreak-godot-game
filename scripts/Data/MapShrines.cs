using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>The four kinds of map shrine. Not the hidden secrets
/// (<see cref="Meta.SecretSpawner"/>): these stand in the open like chests.</summary>
public enum MapShrineKind { Charge, Challenge, Statue, Greed }

/// <summary>One stat a shrine can bless, with its value at Common. Higher tiers
/// multiply it (<see cref="MapShrines.TierScale"/>).</summary>
public sealed class ShrineBoonDef
{
    public required string Id { get; init; }
    public required Stat Stat { get; init; }
    public required ModifierOp Op { get; init; }
    public required float Base { get; init; }
}

/// <summary>A rolled boon: which stat, at what tier, worth how much.</summary>
public readonly record struct ShrineBoon(ShrineBoonDef Def, Rarity Rarity, float Value)
{
    public Stat Stat => Def.Stat;
    public ModifierOp Op => Def.Op;
    public string Title => Catalog.StatName(Def.Stat);
    public string Effect => Catalog.DescribeEffect(new ItemEffect { Stat = Def.Stat, Op = Def.Op, PerStack = Value });
    public Color Color => Catalog.RarityColor(Rarity);
}

/// <summary>
/// Shrine tuning and content, as data. <see cref="Game.ShrineSpawner"/> owns the
/// behaviour; this file owns every number so balance is one table.
///
/// All stat changes go through the <see cref="StatSheet"/> pipeline with a
/// source tag per grant, so a boon or a curse stack can always be removed exactly.
/// </summary>
public static class MapShrines
{
    // ------------------------------------------------------------- reach

    /// <summary>Interact reach for the E-shrines (challenge, statue, greed).</summary>
    public const float InteractRadius = 2.8f;
    public const float InteractHeight = 2.2f;

    /// <summary>Closest two map shrines may stand.</summary>
    public const float MinSpacing = 16f;

    // ------------------------------------------------------------ charge

    /// <summary>Radius of the ring the player stands in.</summary>
    public const float ChargeRadius = 3.4f;

    /// <summary>Seconds of standing in the ring to fill it.</summary>
    public const float ChargeSeconds = 3.5f;

    /// <summary>Seconds a full ring takes to drain once the player leaves. A
    /// short step out to dodge loses a little, not everything.</summary>
    public const float ChargeDrainSeconds = 5f;

    /// <summary>Cards on the charge panel.</summary>
    public const int ChargeChoices = 3;

    // ---------------------------------------------------------- statue

    /// <summary>Price ladder shared by every statue in the run:
    /// Base + Step·n + Curve·n². 30, 56, 94, 144, 206…</summary>
    public const int StatueBaseCost = 30;
    public const int StatueCostStep = 20;
    public const int StatueCostCurve = 6;

    /// <summary>Blessings one statue gives before it crumbles.</summary>
    public const int StatueUsesPerShrine = 3;

    public static int StatuePrice(int bought)
        => StatueBaseCost + StatueCostStep * bought + StatueCostCurve * bought * bought;

    // ----------------------------------------------------------- greed

    /// <summary>Curses one greed altar accepts. Stacks add up across altars.</summary>
    public const int GreedUsesPerShrine = 2;

    /// <summary>One curse stack. <see cref="Stat.EnemyScaling"/> raises enemy
    /// health, damage and the spawn budget at once (more enemies), which is the
    /// whole curse in one stat; the rest is the payment.</summary>
    public static readonly IReadOnlyList<(Stat Stat, ModifierOp Op, float Value)> GreedStack = new[]
    {
        (Stat.EnemyScaling, ModifierOp.Additive, 0.12f),
        (Stat.GoldGain, ModifierOp.Additive, 0.20f),
        (Stat.XpGain, ModifierOp.Additive, 0.12f),
        (Stat.Luck, ModifierOp.Flat, 0.15f),
    };

    public static string DescribeGreed()
    {
        var parts = new List<string>();
        foreach (var (stat, op, value) in GreedStack)
            parts.Add(Catalog.DescribeEffect(new ItemEffect { Stat = stat, Op = op, PerStack = value }));
        return string.Join(", ", parts);
    }

    // ------------------------------------------------------- challenge

    /// <summary>"Kill N in 30 s": N = Base + PerMinute·minutes, capped.</summary>
    public const float SlaughterSeconds = 30f;
    public const int SlaughterBase = 18;
    public const float SlaughterPerMinute = 5f;
    public const int SlaughterMax = 110;

    /// <summary>"Slay the guardians": elites spawned around the shrine, all to
    /// die inside the window. Count = Base + phase - 1, capped.</summary>
    public const float GuardianSeconds = 40f;
    public const int GuardianBase = 2;
    public const int GuardianMax = 5;
    public const float GuardianRing = 9f;

    /// <summary>Success pays like a gilded chest (same luck bonus and rarity
    /// floor as <see cref="Game.ChestSpawner"/>) plus gold.</summary>
    public const float RewardLuckBonus = 2f;
    public const Rarity RewardFloor = Rarity.Rare;
    public const int RewardGoldBase = 40;
    public const int RewardGoldPerPhase = 20;

    public static int SlaughterTarget(float runSeconds)
        => Math.Min(SlaughterMax, SlaughterBase + (int)MathF.Round(SlaughterPerMinute * runSeconds / 60f));

    public static int GuardianCount(int phase) => Math.Clamp(GuardianBase + Math.Max(0, phase - 1), 1, GuardianMax);

    // ------------------------------------------------------------ look

    public static string Name(MapShrineKind kind) => kind switch
    {
        MapShrineKind.Charge => "Charge Shrine",
        MapShrineKind.Challenge => "Challenge Shrine",
        MapShrineKind.Statue => "Gilded Statue",
        _ => "Greed Altar",
    };

    public static Color Tint(MapShrineKind kind) => kind switch
    {
        MapShrineKind.Charge => new Color(0.35f, 0.85f, 1.00f),
        MapShrineKind.Challenge => new Color(1.00f, 0.38f, 0.22f),
        MapShrineKind.Statue => new Color(1.00f, 0.80f, 0.30f),
        _ => new Color(0.72f, 0.40f, 1.00f),
    };

    /// <summary>One letter for the screen marker.</summary>
    public static string Glyph(MapShrineKind kind) => kind switch
    {
        MapShrineKind.Charge => "C",
        MapShrineKind.Challenge => "!",
        MapShrineKind.Statue => "$",
        _ => "G",
    };

    // ------------------------------------------------------------ boons

    /// <summary>
    /// What a charge shrine or statue can bless. Common values are roughly a
    /// common item's single stack, so a shrine is worth the walk but is not a
    /// level-up. Projectile count, shield and thorns are left out: they are
    /// build-defining and belong to items.
    /// </summary>
    public static readonly IReadOnlyList<ShrineBoonDef> Boons = new List<ShrineBoonDef>
    {
        new() { Id = "damage", Stat = Stat.Damage, Op = ModifierOp.Additive, Base = 0.08f },
        new() { Id = "attack_speed", Stat = Stat.AttackSpeed, Op = ModifierOp.Additive, Base = 0.07f },
        new() { Id = "max_health", Stat = Stat.MaxHealth, Op = ModifierOp.Flat, Base = 15f },
        new() { Id = "regen", Stat = Stat.HealthRegen, Op = ModifierOp.Flat, Base = 8f },
        new() { Id = "armor", Stat = Stat.Armor, Op = ModifierOp.Flat, Base = 3f },
        new() { Id = "evasion", Stat = Stat.Evasion, Op = ModifierOp.Flat, Base = 3f },
        new() { Id = "crit_chance", Stat = Stat.CritChance, Op = ModifierOp.Flat, Base = 0.03f },
        new() { Id = "crit_damage", Stat = Stat.CritDamage, Op = ModifierOp.Flat, Base = 0.12f },
        new() { Id = "area", Stat = Stat.AreaSize, Op = ModifierOp.Additive, Base = 0.07f },
        new() { Id = "projectile_speed", Stat = Stat.ProjectileSpeed, Op = ModifierOp.Additive, Base = 0.10f },
        new() { Id = "knockback", Stat = Stat.Knockback, Op = ModifierOp.Additive, Base = 0.12f },
        new() { Id = "move_speed", Stat = Stat.MoveSpeed, Op = ModifierOp.Additive, Base = 0.05f },
        new() { Id = "pickup", Stat = Stat.PickupRange, Op = ModifierOp.Flat, Base = 0.8f },
        new() { Id = "xp", Stat = Stat.XpGain, Op = ModifierOp.Additive, Base = 0.08f },
        new() { Id = "gold", Stat = Stat.GoldGain, Op = ModifierOp.Additive, Base = 0.10f },
        new() { Id = "luck", Stat = Stat.Luck, Op = ModifierOp.Flat, Base = 0.10f },
    };

    /// <summary>Value multiplier per tier.</summary>
    public static float TierScale(Rarity r) => r switch
    {
        Rarity.Common => 1f,
        Rarity.Uncommon => 1.5f,
        Rarity.Rare => 2f,
        Rarity.Epic => 2.75f,
        _ => 3.5f,
    };

    private static readonly float[] TierWeight = { 62f, 26f, 9f, 2.6f, 0.4f };

    /// <summary>Rarity roll, bent upward by luck the same way the offer pool
    /// is (each tier up is weighted by (1 + luck·0.45)^tier).</summary>
    public static Rarity RollTier(Rng rng, float luck, Rarity floor = Rarity.Common)
    {
        float shift = 1f + Math.Clamp(luck, -0.95f, 8f) * 0.45f;
        var w = new float[TierWeight.Length];
        for (int i = 0; i < w.Length; i++)
            w[i] = i < (int)floor ? 0f : TierWeight[i] * MathF.Pow(MathF.Max(0.05f, shift), i);
        int pick = rng.PickWeighted(w);
        return (Rarity)Math.Clamp(pick, (int)floor, w.Length - 1);
    }

    /// <summary><paramref name="count"/> boons on distinct stats.</summary>
    public static List<ShrineBoon> RollBoons(Rng rng, int count, float luck, Rarity floor = Rarity.Common)
    {
        var pool = new List<ShrineBoonDef>(Boons);
        var result = new List<ShrineBoon>(count);
        while (result.Count < count && pool.Count > 0)
        {
            int i = rng.RangeInt(0, pool.Count);
            ShrineBoonDef def = pool[i];
            pool.RemoveAt(i);
            Rarity tier = RollTier(rng, luck, floor);
            result.Add(new ShrineBoon(def, tier, Round(def, def.Base * TierScale(tier))));
        }
        return result;
    }

    /// <summary>Flat whole-number stats stay whole; fractions keep two places.</summary>
    private static float Round(ShrineBoonDef def, float v)
        => def.Op == ModifierOp.Flat && def.Base >= 1f ? MathF.Round(v, MidpointRounding.AwayFromZero)
           : MathF.Round(v * 100f, MidpointRounding.AwayFromZero) / 100f;
}
