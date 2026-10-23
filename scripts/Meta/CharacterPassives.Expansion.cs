using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>
/// Passives of the expansion crew (<see cref="Characters"/>, Expansion
/// partial). Same habits as the originals: they hear the run through the bus,
/// and every stat goes through <see cref="SetMod"/> with this node's tags.
/// </summary>
public sealed partial class CharacterPassives
{
    private const float HarpoonBonus = 0.30f;
    private const float BeaconHeal = 0.20f;
    private const int SurgeEvery = 30;
    private const float SurgeSeconds = 4f;
    private const float SurgeAttackSpeed = 0.40f;
    private const int BarnacleMax = 15;
    private const float SalvagePerChest = 0.03f;
    private const float SalvageMax = 0.45f;
    private const float GullCrit = 0.20f;
    private const float LowTideBelow = 0.40f;
    private const float LowTideDamage = 0.30f;
    private const float LowTideSpeed = 0.15f;
    private const float SquallSeconds = 2.5f;
    private const float SquallDamage = 0.25f;

    private static readonly Color HarpoonColor = new(0.70f, 0.90f, 1.00f);
    private static readonly Color BeaconColor = new(1.00f, 0.90f, 0.50f);
    private static readonly Color SurgeColor = new(0.45f, 0.95f, 0.95f);

    private int _surgeKills;
    private float _surgeLeft;
    private int _chestsOpened;
    private float _squallLeft;
    private bool _lowTide;

    private void ReadyExpansion()
    {
        if (_kind == PassiveKind.Beacon) EventBus.Subscribe<LevelGained>(OnBeaconLevel);
        if (_kind == PassiveKind.Salvage) EventBus.Subscribe<ChestOpened>(OnSalvageChest);
        ReadyCute();   // Meta/CharacterPassives.Cute.cs
    }

    private void ExitExpansion()
    {
        EventBus.Unsubscribe<LevelGained>(OnBeaconLevel);
        EventBus.Unsubscribe<ChestOpened>(OnSalvageChest);
        ExitCute();
    }

    private void TickExpansion(Player p, float dt, bool alive)
    {
        switch (_kind)
        {
            case PassiveKind.Surge:
                if (_surgeLeft > 0f)
                {
                    _surgeLeft -= dt;
                    if (_surgeLeft <= 0f) SetMod(p, Stat.AttackSpeed, ModifierOp.Additive, 0f);
                }
                break;

            case PassiveKind.BarnacleSkin:
            {
                float minutes = (_main.Run?.Seconds ?? 0f) / 60f;
                SetMod(p, Stat.Armor, ModifierOp.Flat, Mathf.Min(BarnacleMax, Mathf.Floor(minutes)));
                break;
            }

            case PassiveKind.GullsEye:
                SetMod(p, Stat.CritChance, ModifierOp.Flat, alive && !p.IsOnFloor() ? GullCrit : 0f);
                break;

            case PassiveKind.LowTide:
            {
                bool low = alive && p.Health is not null && p.Health.Fraction < LowTideBelow;
                if (low == _lowTide) break;
                _lowTide = low;
                SetMod(p, Stat.Damage, ModifierOp.Additive, low ? LowTideDamage : 0f);
                SetMod(p, Stat.MoveSpeed, ModifierOp.Additive, low ? LowTideSpeed : 0f);
                if (low)
                    EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.4f, "LOW TIDE", new Color(0.45f, 0.6f, 1f), 1.1f));
                break;
            }

            case PassiveKind.SquallStep:
                if (_squallLeft > 0f)
                {
                    _squallLeft -= dt;
                    if (_squallLeft <= 0f) SetMod(p, Stat.Damage, ModifierOp.Additive, 0f);
                }
                break;

            default:
                TickCute(p, dt, alive);   // Meta/CharacterPassives.Cute.cs
                break;
        }
    }

    private void EnemyDamagedExpansion(EnemyDamaged e, bool killed)
    {
        switch (_kind)
        {
            case PassiveKind.Harpoon:
            {
                if (e.Target is not Enemy enemy || !(enemy.IsElite || enemy.IsBoss)) return;
                if (killed || e.Info.ProcCoefficient <= 0f) return;
                if (e.Info.Source is "harpoon" or "burn" or "thorns" or "overcharge") return;
                var info = new DamageInfo
                {
                    Amount = e.Info.Amount * HarpoonBonus,
                    Element = Element.Physical,
                    CanCrit = false,
                    ProcCoefficient = 0f,
                    Source = "harpoon",
                };
                enemy.TakeDamage(info);
                if (info.Nullified || info.Amount <= 0f) return;
                EventBus.Publish(new EnemyDamaged(enemy, info));
                if (info.Amount >= 1f)
                    EventBus.Publish(new PopupRequested(enemy.AimPoint + Vector3.Up * 0.6f,
                        Mathf.RoundToInt(info.Amount).ToString(), HarpoonColor, 0.75f));
                break;
            }

            case PassiveKind.Surge:
                if (!killed || LivePlayer is not { } p) return;
                if (++_surgeKills < SurgeEvery) return;
                _surgeKills = 0;
                bool fresh = _surgeLeft <= 0f;
                _surgeLeft = SurgeSeconds;
                SetMod(p, Stat.AttackSpeed, ModifierOp.Additive, SurgeAttackSpeed);
                if (fresh)
                    EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.4f, "CREST", SurgeColor, 1.1f));
                break;
        }
    }

    private void MovedExpansion(PlayerMoved e)
    {
        if (_kind != PassiveKind.SquallStep || e.Verb != MoveVerb.Dash || LivePlayer is not { } p) return;
        _squallLeft = SquallSeconds;
        SetMod(p, Stat.Damage, ModifierOp.Additive, SquallDamage);
    }

    private void OnBeaconLevel(LevelGained e)
    {
        if (LivePlayer is not { } p || p.Health is null || !p.Health.IsAlive) return;
        p.Health.HealFraction(BeaconHeal);
        EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.6f, "BEACON", BeaconColor, 1.0f));
    }

    private void OnSalvageChest(ChestOpened e)
    {
        if (LivePlayer is not { } p) return;
        _chestsOpened++;
        SetMod(p, Stat.Damage, ModifierOp.Additive, Mathf.Min(SalvageMax, SalvagePerChest * _chestsOpened));
    }
}
