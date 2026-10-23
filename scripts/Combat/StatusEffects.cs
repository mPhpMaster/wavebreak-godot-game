using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Combat;

/// <summary>
/// The element rules: which status a landed hit applies, and how strong it is.
/// The state itself lives on the enemy (<c>Actors/Enemy.Status.cs</c>); this
/// class only decides.
///
///   Fire      -> Burn:   25% of the hit per second for 3 s. Refreshes, keeps the hotter flame.
///   Ice       -> Chill:  -10% speed per stack (max -45%) for 2.5 s; at 5 stacks
///                        (minus Freeze Power, min 2) the target freezes for 1.2 s.
///   Lightning -> Shock:  arcs 40% of the hit to 2 more enemies within 6 m and stuns 0.4 s.
///   Poison    -> Poison: +1 stack (max 8), 8% of the hit per second per stack, 4 s.
///
/// Chance per hit = (element base + StatusChance stat + the hit's own bonus)
/// x proc coefficient, for the weapon's element; and Ignite/Chill/Shock/Poison
/// Chance x proc coefficient for every hit whatever its element (items).
/// Status ticks and arcs carry proc coefficient 0, so nothing chains forever.
/// Bosses take 30% of freeze/stun time and half the slow; elites 60%/75%.
/// </summary>
public static class StatusEffects
{
    // ---- megacrit
    /// <summary>A megacrit multiplies an already-critical hit again.</summary>
    public const float MegacritMultiplier = 3f;
    public static readonly Color MegacritColor = new(1.00f, 0.32f, 0.86f);

    // ---- tuning
    public const float BurnFraction = 0.25f;
    public const float BurnSeconds = 3f;
    public const float PoisonPerStack = 0.08f;
    public const float PoisonSeconds = 4f;
    public const float ChillSeconds = 2.5f;
    public const int FreezeStacks = 5;
    public const float FreezeSeconds = 1.2f;
    public const float StunSeconds = 0.4f;
    public const float ChainFraction = 0.4f;
    public const float ChainRadius = 6f;
    public const int ChainTargets = 2;

    /// <summary>Chance an elemental weapon applies its own status, before stats.</summary>
    public static float BaseChance(Element element) => element switch
    {
        Element.Fire => 0.30f,
        Element.Ice => 0.40f,
        Element.Lightning => 0.20f,
        Element.Poison => 0.45f,
        _ => 0f,
    };

    private static readonly (Element Element, Stat Stat)[] Procs =
    {
        (Element.Fire, Stat.IgniteChance),
        (Element.Ice, Stat.ChillChance),
        (Element.Lightning, Stat.ShockChance),
        (Element.Poison, Stat.PoisonChance),
    };

    private static readonly List<IDamageable> Buffer = new(32);
    private static Rng? _rng;
    private static ulong _seed;

    /// <summary>Own stream, derived from the run's seed, so status rolls never
    /// shift crits, spawns or offers.</summary>
    private static Rng RngFor(WeaponSystem system)
    {
        ulong seed = system.Rng.Seed;
        if (_rng is null || _seed != seed) { _rng = new Rng(seed ^ 0x57A7_05E1_C0DEUL); _seed = seed; }
        return _rng;
    }

    /// <summary>Called by <see cref="WeaponSystem.ReportHit"/> for every landed
    /// weapon hit.</summary>
    public static void OnHit(WeaponSystem system, IDamageable target, DamageInfo info)
    {
        if (target is not Enemy enemy || !enemy.IsAlive) return;
        if (info.Nullified || info.Amount <= 0f || info.ProcCoefficient <= 0f) return;

        StatSheet stats = system.Stats;
        Rng rng = RngFor(system);
        float proc = info.ProcCoefficient;
        float duration = stats.Get(Stat.StatusDuration) * MathF.Max(0.1f, info.StatusDuration);

        // Physical hits proc'd by items have not had Elemental Damage yet.
        float strength = info.Amount * (info.Element == Element.Physical ? stats.GetMultiplier(Stat.ElementalDamage) : 1f);

        Element applied = Element.Physical;
        if (info.Element != Element.Physical)
        {
            float chance = (BaseChance(info.Element) + stats.Get(Stat.StatusChance) + info.StatusChance) * proc;
            if (rng.Chance(chance))
            {
                Apply(system, enemy, info.Element, strength, duration, stats);
                applied = info.Element;
            }
        }

        foreach ((Element element, Stat stat) in Procs)
        {
            if (element == applied || !enemy.IsAlive) continue;
            float chance = stats.Get(stat);
            if (chance > 0f && rng.Chance(chance * proc))
                Apply(system, enemy, element, strength, duration, stats);
        }
    }

    /// <summary>
    /// Apply one element's status as if a hit of <paramref name="hit"/> damage
    /// had procced it. Public so other effects (passives, bosses' own gimmicks,
    /// new items with code) can use the same rules.
    /// </summary>
    public static void Apply(WeaponSystem? system, Enemy enemy, Element element, float hit, float duration, StatSheet? stats)
    {
        if (!enemy.IsAlive || hit <= 0f) return;
        duration = MathF.Max(0.1f, duration);

        switch (element)
        {
            case Element.Fire:
                enemy.ApplyBurn(hit * BurnFraction, BurnSeconds * duration);
                break;
            case Element.Poison:
                enemy.ApplyPoison(hit * PoisonPerStack, PoisonSeconds * duration);
                break;
            case Element.Ice:
            {
                float power = stats?.Get(Stat.FreezePower) ?? 0f;
                int freezeAt = Math.Max(2, FreezeStacks - (int)power);
                enemy.ApplyChill(ChillSeconds * duration, freezeAt, FreezeSeconds * duration * (1f + 0.2f * power));
                break;
            }
            case Element.Lightning:
                enemy.ApplyStun(StunSeconds * duration);
                Chain(system, enemy, hit * ChainFraction);
                break;
        }
    }

    private static void Chain(WeaponSystem? system, Enemy from, float amount)
    {
        if (amount <= 0f) return;
        Vector3 origin = from.AimPoint;
        int count = Targeting.Overlap(origin, ChainRadius, Buffer);

        // Nearest first, by a short selection: the list is small.
        int jumped = 0;
        while (jumped < ChainTargets)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                IDamageable t = Buffer[i];
                if (t is null || ReferenceEquals(t, from) || !t.IsAlive) continue;
                float sq = (t.Position - origin).LengthSquared();
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            if (best < 0) break;

            IDamageable target = Buffer[best];
            Buffer[best] = null!;
            jumped++;

            var info = new DamageInfo
            {
                Amount = amount,
                Element = Element.Lightning,
                CanCrit = false,
                ProcCoefficient = 0f,
                Source = "shock",
            };
            Vector3 to = target.Position;
            target.TakeDamage(info);
            StatusFx.Arc(system, origin, to);
            if (info.Nullified || info.Amount <= 0f) continue;

            if (WeaponSystem.PublishHitEvents) EventBus.Publish(new EnemyDamaged(target, info));
            if (WeaponSystem.PublishDamageNumbers && info.Amount >= 0.5f)
                EventBus.Publish(new PopupRequested(to + Vector3.Up * 0.35f,
                    Mathf.RoundToInt(Mathf.Max(1f, info.Amount)).ToString(),
                    WeaponSystem.ColorOf(Element.Lightning), 0.8f));
        }
        Buffer.Clear();
    }
}

/// <summary>
/// The arc a Shock draws to each enemy it jumps to: a stretched bolt that
/// fades over a few frames. Hard-capped pool, no lights, one shared material.
/// </summary>
internal sealed partial class StatusFx : Node
{
    private const int Capacity = 24;
    private const float Life = 0.16f;
    private static StatusFx? _instance;

    private readonly MeshInstance3D[] _arcs = new MeshInstance3D[Capacity];
    private readonly float[] _age = new float[Capacity];
    private int _next;

    public static void Arc(Node? host, Vector3 from, Vector3 to)
    {
        if (_instance is null || !IsInstanceValid(_instance))
        {
            if (host is null || !IsInstanceValid(host) || !host.IsInsideTree()) return;
            _instance = new StatusFx { Name = "StatusFx" };
            host.AddChild(_instance);
        }
        _instance.Spawn(from, to);
    }

    public override void _Ready()
    {
        var mat = CombatVisuals.Glow(new Color(0.95f, 0.92f, 0.45f), additive: true);
        for (int i = 0; i < Capacity; i++)
        {
            _arcs[i] = new MeshInstance3D
            {
                Mesh = CombatVisuals.Bolt,
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
                TopLevel = true,
            };
            _age[i] = Life;
            AddChild(_arcs[i]);
        }
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    private void Spawn(Vector3 from, Vector3 to)
    {
        if (!IsInsideTree() || _arcs[0] is null) return;
        Vector3 d = to - from;
        float len = d.Length();
        if (len < 0.05f) return;

        MeshInstance3D arc = _arcs[_next];
        _age[_next] = 0f;
        _next = (_next + 1) % Capacity;

        // Bolt is 3 long on +Y, radius 1.
        Vector3 y = d / len;
        Vector3 x = MathF.Abs(y.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : y.Cross(Vector3.Up).Normalized();
        Vector3 z = x.Cross(y);
        var basis = new Basis(x * 0.06f, y * (len / 3f), z * 0.06f);
        arc.GlobalTransform = new Transform3D(basis, from + d * 0.5f);
        arc.Transparency = 0f;
        arc.Visible = true;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = 0; i < Capacity; i++)
        {
            if (_age[i] >= Life) continue;
            _age[i] += dt;
            MeshInstance3D arc = _arcs[i];
            if (_age[i] >= Life) { arc.Visible = false; continue; }
            arc.Transparency = _age[i] / Life;
        }
    }
}
