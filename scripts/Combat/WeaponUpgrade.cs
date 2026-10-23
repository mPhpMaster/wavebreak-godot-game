using System;
using System.Collections.Generic;
using System.Text;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Combat;

/// <summary>One line a weapon upgrade can roll.</summary>
public enum UpgradeStat
{
    Damage,
    AttackSpeed,
    Projectiles,
    Area,
    ProjectileSpeed,
    Range,
    Knockback,
    CritChance,
    Pierce,
    StatusChance,
    StatusDuration,
}

/// <summary>
/// One rolled weapon level-up: a rarity and one to three stat lines scaled by
/// it. Rolled when a held weapon is offered, so the card can show exactly what
/// taking it buys; applied to <see cref="WeaponRuntime.Bonus"/> when taken.
/// </summary>
public sealed class WeaponUpgrade
{
    public WeaponUpgrade(Rarity rarity, IReadOnlyList<(UpgradeStat Stat, float Value)> lines)
    {
        Rarity = rarity;
        Lines = lines;
    }

    public Rarity Rarity { get; }
    public IReadOnlyList<(UpgradeStat Stat, float Value)> Lines { get; }

    public bool Has(UpgradeStat stat)
    {
        foreach (var l in Lines) if (l.Stat == stat) return true;
        return false;
    }

    /// <summary>One line per stat, "+31% Damage", for the card.</summary>
    public string Describe()
    {
        var sb = new StringBuilder(64);
        foreach (var (stat, value) in Lines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(WeaponUpgrades.DescribeLine(stat, value));
        }
        return sb.ToString();
    }

    public override string ToString() => $"{Rarity}: {Describe().Replace('\n', ',')}";
}

/// <summary>
/// The sum of every upgrade a weapon has taken. Percentages add (the codebase's
/// rule: ten +10% is +100%), counts add.
/// </summary>
public sealed class WeaponBonus
{
    public float Damage, AttackSpeed, Area, ProjectileSpeed, Range, Knockback, CritChance, StatusChance, StatusDuration;
    public int Projectiles, Pierce;

    public void Add(WeaponUpgrade? up)
    {
        if (up is null) return;
        foreach (var (stat, v) in up.Lines)
        {
            switch (stat)
            {
                case UpgradeStat.Damage: Damage += v; break;
                case UpgradeStat.AttackSpeed: AttackSpeed += v; break;
                case UpgradeStat.Projectiles: Projectiles += (int)MathF.Round(v); break;
                case UpgradeStat.Area: Area += v; break;
                case UpgradeStat.ProjectileSpeed: ProjectileSpeed += v; break;
                case UpgradeStat.Range: Range += v; break;
                case UpgradeStat.Knockback: Knockback += v; break;
                case UpgradeStat.CritChance: CritChance += v; break;
                case UpgradeStat.Pierce: Pierce += (int)MathF.Round(v); break;
                case UpgradeStat.StatusChance: StatusChance += v; break;
                case UpgradeStat.StatusDuration: StatusDuration += v; break;
            }
        }
    }
}

/// <summary>
/// Rolls weapon upgrades. The rules, stated once:
///
/// <b>Rarity</b> is drawn from <see cref="RarityWeight"/> bent by luck through
/// the same <see cref="Progression.OfferPool.LuckShift"/> curve items use.
///
/// <b>Line count</b>: Common 1, Uncommon 1-2, Rare 2, Epic 2-3, Legendary 3,
/// plus <c>Stat.UpgradeStats</c>, never more than the weapon has eligible lines.
///
/// <b>The first line is a core line</b> (damage, attack speed, or a projectile
/// where the weapon allows one) so an upgrade can never be pure trivia; the
/// rest come from everything the weapon's shape and element make meaningful.
///
/// <b>Values</b> scale by <see cref="RarityScale"/>. Damage and attack speed
/// start from the weapon's own authored <c>DamagePerLevel</c> and
/// <c>CooldownPerLevel</c>, so each weapon keeps its growth character; a weapon
/// authored with <c>ProjectilesEveryNLevels = 0</c> never rolls projectiles.
/// </summary>
public static class WeaponUpgrades
{
    public static readonly float[] RarityWeight = { 1f, 0.55f, 0.25f, 0.10f, 0.035f };
    public static readonly float[] RarityScale = { 1f, 1.3f, 1.7f, 2.2f, 3f };

    private static readonly List<(UpgradeStat, float)> Pool = new(16);
    private static readonly List<float> Weights = new(16);
    private static readonly List<float> RarityBuffer = new(5);

    public static WeaponUpgrade Roll(WeaponDef def, Rng rng, float luck, int extraLines = 0)
    {
        RarityBuffer.Clear();
        for (int i = 0; i < RarityWeight.Length; i++)
            RarityBuffer.Add(RarityWeight[i] * Progression.OfferPool.LuckShift((Rarity)i, luck));
        int r = Math.Max(0, rng.PickWeighted(RarityBuffer));
        return RollAt(def, (Rarity)r, rng, extraLines);
    }

    public static WeaponUpgrade RollAt(WeaponDef def, Rarity rarity, Rng rng, int extraLines = 0)
    {
        int count = rarity switch
        {
            Rarity.Common => 1,
            Rarity.Uncommon => rng.Chance(0.5f) ? 2 : 1,
            Rarity.Rare => 2,
            Rarity.Epic => rng.Chance(0.5f) ? 3 : 2,
            _ => 3,
        } + Math.Clamp(extraLines, 0, 3);

        var lines = new List<(UpgradeStat, float)>(count);

        // Core line first.
        BuildPool(def, coreOnly: true);
        int pick = rng.PickWeighted(Weights);
        if (pick >= 0) lines.Add((Pool[pick].Item1, ValueOf(def, Pool[pick].Item1, rarity)));

        BuildPool(def, coreOnly: false);
        while (lines.Count < count)
        {
            for (int i = Pool.Count - 1; i >= 0; i--)
                foreach (var l in lines)
                    if (l.Item1 == Pool[i].Item1) { Pool.RemoveAt(i); Weights.RemoveAt(i); break; }
            pick = rng.PickWeighted(Weights);
            if (pick < 0) break;
            lines.Add((Pool[pick].Item1, ValueOf(def, Pool[pick].Item1, rarity)));
        }

        return new WeaponUpgrade(rarity, lines);
    }

    /// <summary>A deterministic Common damage line: what the content audit
    /// levels weapons with, and what <see cref="WeaponSystem.Add(WeaponDef)"/>
    /// falls back to when there is nothing to roll from.</summary>
    public static WeaponUpgrade Reference(WeaponDef def)
        => new(Rarity.Common, new[] { (UpgradeStat.Damage, ValueOf(def, UpgradeStat.Damage, Rarity.Common)) });

    /// <summary>Lines this weapon can roll at all.</summary>
    public static int EligibleLines(WeaponDef def)
    {
        BuildPool(def, coreOnly: false);
        return Pool.Count;
    }

    public static IEnumerable<UpgradeStat> Eligible(WeaponDef def)
    {
        BuildPool(def, coreOnly: false);
        var copy = new List<UpgradeStat>(Pool.Count);
        foreach (var p in Pool) copy.Add(p.Item1);
        return copy;
    }

    private static void BuildPool(WeaponDef def, bool coreOnly)
    {
        Pool.Clear();
        Weights.Clear();
        bool shoots = def.Shape is AttackShape.Projectile or AttackShape.Homing or AttackShape.Explosive;
        bool areaShape = def.Shape is AttackShape.Aura or AttackShape.Explosive or AttackShape.Melee;
        bool elemental = def.Element != Element.Physical;

        float projWeight = def.Shape == AttackShape.Aura || def.ProjectilesEveryNLevels <= 0 ? 0f
            : def.ProjectilesEveryNLevels >= 4 ? 0.5f : 1f;

        Add(UpgradeStat.Damage, 3f);
        Add(UpgradeStat.AttackSpeed, 2f);
        Add(UpgradeStat.Projectiles, projWeight);
        if (coreOnly) return;

        Add(UpgradeStat.Area, areaShape ? 1.2f : 0.5f);
        Add(UpgradeStat.ProjectileSpeed, shoots ? 0.8f : 0f);
        Add(UpgradeStat.Range, def.Shape == AttackShape.Aura ? 0f : 0.7f);
        Add(UpgradeStat.Knockback, def.Knockback > 0f ? 0.5f : 0f);
        Add(UpgradeStat.CritChance, 1f);
        Add(UpgradeStat.Pierce, def.Shape is AttackShape.Projectile or AttackShape.Homing ? 0.6f : 0f);
        Add(UpgradeStat.StatusChance, elemental ? 1f : 0f);
        Add(UpgradeStat.StatusDuration, elemental ? 0.6f : 0f);

        static void Add(UpgradeStat s, float w)
        {
            if (w <= 0f) return;
            Pool.Add((s, w));
            Weights.Add(w);
        }
    }

    public static float ValueOf(WeaponDef def, UpgradeStat stat, Rarity rarity)
    {
        float k = RarityScale[Math.Clamp((int)rarity, 0, RarityScale.Length - 1)];
        bool big = rarity >= Rarity.Legendary;
        return stat switch
        {
            UpgradeStat.Damage => MathF.Max(0.15f, def.DamagePerLevel) * 1.2f * k,
            UpgradeStat.AttackSpeed => MathF.Max(0.05f, -def.CooldownPerLevel) * 1.6f * k,
            UpgradeStat.Projectiles => big ? 2f : 1f,
            UpgradeStat.Area => 0.12f * k,
            UpgradeStat.ProjectileSpeed => 0.15f * k,
            UpgradeStat.Range => 0.12f * k,
            UpgradeStat.Knockback => 0.25f * k,
            UpgradeStat.CritChance => 0.03f * k,
            UpgradeStat.Pierce => big ? 2f : 1f,
            UpgradeStat.StatusChance => 0.06f * k,
            UpgradeStat.StatusDuration => 0.15f * k,
            _ => 0f,
        };
    }

    public static string DescribeLine(UpgradeStat stat, float v) => stat switch
    {
        UpgradeStat.Projectiles => v > 1.5f ? Meta.Loc.F("+{0:0} Projectiles", v) : Meta.Loc.F("+{0:0} Projectile", v),
        UpgradeStat.Pierce => Meta.Loc.F("+{0:0} Pierce", v),
        _ => Meta.Loc.F("+{0:0.#}% {1}", v * 100f, Name(stat)),
    };

    public static string Name(UpgradeStat stat) => stat switch
    {
        UpgradeStat.AttackSpeed => "Attack Speed",
        UpgradeStat.ProjectileSpeed => "Projectile Speed",
        UpgradeStat.CritChance => "Crit Chance",
        UpgradeStat.StatusChance => "Status Chance",
        UpgradeStat.StatusDuration => "Status Duration",
        _ => stat.ToString(),
    };
}
