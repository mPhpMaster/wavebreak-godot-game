using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>
/// Each character's unique rule (<see cref="CharacterDef.Passive"/>), as one
/// node per run.
///
/// The rules follow the codebase's two habits: they hear the run through the
/// bus (hits, kills, movement verbs) instead of being called from combat code,
/// and every stat they touch goes through the <see cref="StatSheet"/> with a
/// source tag that belongs to this node — a passive that grants "+30% damage
/// while fast" re-registers one tagged modifier when the value changes, so
/// removing it can never leave residue behind.
///
/// Automated runs that did not ask for a character (<c>--character</c>) get no
/// passive, so smoke tests and seeded captures keep playing the pre-passive
/// default exactly; <c>--character=drifter</c> turns it on for testing.
///
/// Passive effects use their own random stream, so a passive never shifts the
/// run's seeded sequence of spawns and offers.
/// </summary>
public sealed partial class CharacterPassives : Node
{
    // ---- tuning
    private const float MomentumMaxBonus = 0.30f;
    private const float MomentumFastScale = 1.15f;   // "fast" = above walk speed by this factor
    private const float MomentumBuildSeconds = 1.5f;
    private const float MomentumFadeSeconds = 2.5f;

    private const float BurnChance = 0.35f;          // per hit, times the weapon's proc coefficient
    private const float BurnFraction = 0.25f;        // of the hit, per second
    private const float BurnSeconds = 3f;
    private const float BurnTick = 0.5f;
    private const int MaxBurning = 96;

    private const float DashRefundPerKill = 0.3f;

    private const float GoldPerPercent = 10f;
    private const float GoldMaxBonus = 0.40f;

    private const float ResolveSeconds = 4f;
    private const float ResolveDamage = 0.35f;

    private const float AmbushSeconds = 5f;
    private const int AmbushMaxStacks = 5;
    private const float AmbushPerStack = 0.04f;

    private const int OverchargeEvery = 4;
    private const float OverchargeRadius = 7f;
    private const float OverchargeBase = 30f;
    private const float OverchargePerLevel = 3f;

    private static readonly Color BurnColor = new(1.00f, 0.50f, 0.15f);
    private static readonly Color StormColor = new(0.75f, 0.70f, 1.00f);

    private Main _main = null!;
    private PassiveKind _kind;
    private Rng _rng = null!;

    /// <summary>One source tag per stat, so each can be replaced independently.</summary>
    private readonly Dictionary<Stat, (object tag, float value)> _mods = new();

    private float _momentum;
    private float _resolve;
    private float _ambush;
    private int _ambushStacks;
    private int _charge;
    private float _burnTimer;

    private sealed class Burn { public float Dps; public float Left; }
    private readonly Dictionary<Enemy, Burn> _burning = new();
    private readonly List<Enemy> _scratch = new();

    /// <summary>Called from <see cref="RunMeta.Attach"/> after the character's
    /// stats are applied.</summary>
    public static void Attach(Main main, CharacterDef def)
    {
        if (def.Passive == PassiveKind.None) return;
        if (Launch.Automated && Launch.CharacterId is null) return;
        var node = new CharacterPassives { Name = "CharacterPassive", _main = main, _kind = def.Passive };
        node._rng = new Rng(main.Run.Seed ^ 0x9A55_1BE5UL);
        main.AddChild(node);
        GD.Print($"META passive {def.Id}: {def.PassiveName}");
    }

    public override void _Ready()
    {
        Player? p = _main.Player;
        if (_kind == PassiveKind.Tailwind && p is not null) p.ExtraAirJumps = 1;

        EventBus.Subscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Subscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Subscribe<PlayerMoved>(OnPlayerMoved);
        ReadyExpansion();
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Unsubscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Unsubscribe<PlayerMoved>(OnPlayerMoved);
        ExitExpansion();
    }

    private Player? LivePlayer
    {
        get
        {
            if (_main is null || !IsInstanceValid(_main)) return null;
            Player p = _main.Player;
            return p is not null && IsInstanceValid(p) ? p : null;
        }
    }

    // --------------------------------------------------------------- tick

    public override void _Process(double delta)
    {
        if (LivePlayer is not { } p || _main.Run is null || _main.Run.Over) return;
        float dt = (float)delta;
        bool alive = p.Health is null || p.Health.IsAlive;

        switch (_kind)
        {
            case PassiveKind.Momentum:
            {
                float walk = p.Stats.Get(Stat.MoveSpeed);
                bool fast = alive && p.PlanarVelocity.Length() > walk * MomentumFastScale;
                _momentum = fast ? Mathf.Min(1f, _momentum + dt / MomentumBuildSeconds)
                                 : Mathf.Max(0f, _momentum - dt / MomentumFadeSeconds);
                SetMod(p, Stat.Damage, ModifierOp.Additive, MomentumMaxBonus * _momentum);
                break;
            }
            case PassiveKind.GildedFortune:
            {
                int gold = _main.Inventory?.Gold ?? 0;
                float bonus = Mathf.Min(GoldMaxBonus, Mathf.Floor(gold / GoldPerPercent) * 0.01f);
                SetMod(p, Stat.Damage, ModifierOp.Additive, bonus);
                break;
            }
            case PassiveKind.Bulwark:
                if (_resolve > 0f)
                {
                    _resolve -= dt;
                    if (_resolve <= 0f)
                    {
                        SetMod(p, Stat.Damage, ModifierOp.Additive, 0f);
                        SetMod(p, Stat.Thorns, ModifierOp.Flat, 0f);
                    }
                }
                break;
            case PassiveKind.Ambush:
                if (_ambushStacks > 0)
                {
                    _ambush -= dt;
                    if (_ambush <= 0f) SetAmbush(p, 0);
                }
                break;
            case PassiveKind.Kindling:
                TickBurns(dt);
                break;
            default:
                TickExpansion(p, dt, alive);
                break;
        }
    }

    /// <summary>Replace this passive's modifier on one stat. Skips tiny changes
    /// so a slowly-moving bonus does not fire the sheet's Changed event every frame.</summary>
    private void SetMod(Player p, Stat stat, ModifierOp op, float value)
    {
        if (!_mods.TryGetValue(stat, out var cur)) cur = (new object(), 0f);
        if (Mathf.Abs(cur.value - value) < 0.005f && !(value == 0f && cur.value != 0f)) return;
        p.Stats.RemoveAllFrom(cur.tag);
        if (value != 0f) p.Stats.Add(stat, op, value, cur.tag);
        _mods[stat] = (cur.tag, value);
    }

    // ------------------------------------------------------------- events

    private void OnEnemyDamaged(EnemyDamaged e)
    {
        if (e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f || e.Target is null) return;
        bool killed = !e.Target.IsAlive;

        switch (_kind)
        {
            case PassiveKind.Kindling:
                if (killed || e.Target is not Enemy enemy) return;
                if (e.Info.Source is "burn" or "thorns" or "overcharge" || e.Info.ProcCoefficient <= 0f) return;
                if (!_rng.Chance(BurnChance * e.Info.ProcCoefficient)) return;
                Ignite(enemy, e.Info.Amount * BurnFraction);
                break;
            case PassiveKind.Tailwind:
                if (killed) LivePlayer?.RefundDash(DashRefundPerKill);
                break;
            case PassiveKind.Ambush:
                if (killed && e.Info.Crit && LivePlayer is { } p)
                {
                    SetAmbush(p, Mathf.Min(AmbushMaxStacks, _ambushStacks + 1));
                    _ambush = AmbushSeconds;
                }
                break;
            default:
                EnemyDamagedExpansion(e, killed);
                break;
        }
    }

    private void OnPlayerDamaged(PlayerDamaged e)
    {
        if (_kind != PassiveKind.Bulwark) return;
        if (e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f || LivePlayer is not { } p) return;
        bool fresh = _resolve <= 0f;
        _resolve = ResolveSeconds;
        SetMod(p, Stat.Damage, ModifierOp.Additive, ResolveDamage);
        SetMod(p, Stat.Thorns, ModifierOp.Flat, Mathf.Max(0f, p.Stats.Get(Stat.Armor)));
        if (fresh)
            EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.4f, "RETALIATE", new Color(0.95f, 0.45f, 0.35f), 1.1f));
    }

    private void OnPlayerMoved(PlayerMoved e)
    {
        MovedExpansion(e);
        if (_kind != PassiveKind.Overcharge || e.Verb is not (MoveVerb.Dash or MoveVerb.AirJump)) return;
        _charge++;
        if (_charge < OverchargeEvery) return;
        _charge = 0;
        Overcharge();
    }

    // ------------------------------------------------------------ effects

    private void SetAmbush(Player p, int stacks)
    {
        _ambushStacks = stacks;
        float v = AmbushPerStack * stacks;
        SetMod(p, Stat.MoveSpeed, ModifierOp.Additive, v);
        SetMod(p, Stat.AttackSpeed, ModifierOp.Additive, v);
    }

    private void Ignite(Enemy enemy, float dps)
    {
        if (dps <= 0f) return;
        if (_burning.TryGetValue(enemy, out Burn? b))
        {
            // Refresh, keeping the hotter flame rather than stacking: burns
            // that stacked per hit would turn a fast weapon into a furnace.
            b.Dps = Mathf.Max(b.Dps, dps);
            b.Left = BurnSeconds;
            return;
        }
        if (_burning.Count >= MaxBurning) return;
        _burning[enemy] = new Burn { Dps = dps, Left = BurnSeconds };
    }

    private void TickBurns(float dt)
    {
        if (_burning.Count == 0) return;
        _burnTimer += dt;
        if (_burnTimer < BurnTick) return;
        float step = _burnTimer;
        _burnTimer = 0f;

        _scratch.Clear();
        _scratch.AddRange(_burning.Keys);
        foreach (Enemy enemy in _scratch)
        {
            Burn b = _burning[enemy];
            b.Left -= step;
            if (!IsInstanceValid(enemy) || !enemy.IsAlive || b.Left <= -0.01f)
            {
                _burning.Remove(enemy);
                continue;
            }
            var info = new DamageInfo
            {
                Amount = b.Dps * step,
                Element = Element.Fire,
                CanCrit = false,
                ProcCoefficient = 0f,
                Source = "burn",
            };
            enemy.TakeDamage(info);
            if (info.Nullified || info.Amount <= 0f) { _burning.Remove(enemy); continue; }
            EventBus.Publish(new EnemyDamaged(enemy, info));
            if (info.Amount >= 1f)
                EventBus.Publish(new PopupRequested(enemy.AimPoint + Vector3.Up * 0.5f,
                    Mathf.RoundToInt(info.Amount).ToString(), BurnColor, 0.7f));
            if (!enemy.IsAlive || b.Left <= 0f) _burning.Remove(enemy);
        }
    }

    private void Overcharge()
    {
        if (LivePlayer is not { } p || EnemyManager.Instance is not { } em) return;
        int level = _main.Inventory?.Level ?? 1;
        float amount = (OverchargeBase + OverchargePerLevel * level) * p.Stats.GetMultiplier(Stat.Damage);
        float radius = OverchargeRadius * p.Stats.Get(Stat.AreaSize);
        Vector3 at = p.GlobalPosition;
        int hits = 0;

        foreach (Enemy e in em.AliveList.ToArray())
        {
            if (!IsInstanceValid(e) || !e.IsAlive) continue;
            Vector3 d = e.GlobalPosition - at;
            d.Y = 0f;
            float dist = d.Length();
            if (dist > radius) continue;
            var info = new DamageInfo
            {
                Amount = amount,
                Element = Element.Lightning,
                CanCrit = false,
                Knockback = 1.2f,
                Direction = dist > 0.01f ? d / dist : Vector3.Forward,
                ProcCoefficient = 0f,
                Source = "overcharge",
            };
            e.TakeDamage(info);
            if (info.Nullified || info.Amount <= 0f) continue;
            hits++;
            EventBus.Publish(new EnemyDamaged(e, info));
            EventBus.Publish(new PopupRequested(e.AimPoint + Vector3.Up * 0.35f,
                Mathf.RoundToInt(info.Amount).ToString(), StormColor, 1.0f));
        }

        EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.4f, "OVERCHARGE", StormColor, 1.3f));
        PlayerCamera.Instance?.Shake(0.12f + 0.01f * Mathf.Min(hits, 8), 0.2f);
    }
}
