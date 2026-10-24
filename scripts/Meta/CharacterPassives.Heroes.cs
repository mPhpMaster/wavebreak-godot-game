using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>
/// Passives of the heroes trio (<see cref="Characters"/>, Heroes partial).
/// Same habits as the rest: the run is heard through the bus, stats go
/// through <see cref="SetMod"/> with this node's tags, nothing draws from the
/// run's seeded stream, and each one reads at a glance in the stat panel.
/// </summary>
public sealed partial class CharacterPassives
{
    // ---- Calm Lookout (Sunny)
    private const float CalmAfter = 3f;
    private const float CalmCrit = 0.15f;
    private const float CalmCritDamage = 0.30f;

    // ---- Old Salt (Biscuit)
    private const float SaltRadius = 5f;
    private const int SaltMax = 12;
    private const float SaltEvery = 0.25f;

    // ---- Sea Breeze (Flick)
    private const float BreezeMetres = 40f;
    private const int BreezeMax = 5;
    private const float BreezeAttack = 0.04f;
    private const float BreezeMove = 0.03f;
    private const float BreezeDecay = 2f;

    private static readonly Color CalmColor = new(1.00f, 0.90f, 0.50f);
    private static readonly Color BreezeColor = new(0.70f, 0.95f, 1.00f);

    private float _sinceHit;
    private bool _calm;
    private float _saltTimer;
    private float _breezeMetres, _breezeIdle;
    private int _breeze;
    private Vector3 _lastPos;
    private bool _havePos;

    private bool IsHeroPassive => _kind is PassiveKind.CalmLookout or PassiveKind.OldSalt or PassiveKind.SeaBreeze;

    private void ReadyHeroes()
    {
        if (!IsHeroPassive) return;
        EventBus.Subscribe<PlayerDamaged>(OnHeroDamaged);
    }

    private void ExitHeroes() => EventBus.Unsubscribe<PlayerDamaged>(OnHeroDamaged);

    private void TickHeroes(Player p, float dt, bool alive)
    {
        switch (_kind)
        {
            case PassiveKind.CalmLookout:
                _sinceHit += dt;
                if (!_calm && alive && _sinceHit >= CalmAfter) SetCalm(p, true);
                break;

            case PassiveKind.OldSalt:
            {
                _saltTimer -= dt;
                if (_saltTimer > 0f) break;
                _saltTimer = SaltEvery;
                int near = 0;
                if (alive && EnemyManager.Instance is { } em)
                {
                    Vector3 at = p.GlobalPosition;
                    var list = em.AliveList;
                    for (int i = 0; i < list.Count && near < SaltMax; i++)
                    {
                        Enemy e = list[i];
                        if (!e.IsAlive) continue;
                        Vector3 d = e.GlobalPosition - at;
                        d.Y = 0f;
                        if (d.LengthSquared() <= SaltRadius * SaltRadius) near++;
                    }
                }
                SetMod(p, Stat.Armor, ModifierOp.Flat, near);
                break;
            }

            case PassiveKind.SeaBreeze:
            {
                Vector3 pos = p.GlobalPosition;
                if (!_havePos) { _lastPos = pos; _havePos = true; }
                Vector3 step = pos - _lastPos;
                step.Y = 0f;
                _lastPos = pos;
                float moved = step.Length();
                if (moved > 5f) moved = 0f;   // a teleport or a respawn is not travel
                if (alive && moved > 0.01f)
                {
                    _breezeIdle = 0f;
                    _breezeMetres += moved;
                    if (_breezeMetres >= BreezeMetres)
                    {
                        _breezeMetres -= BreezeMetres;
                        if (_breeze < BreezeMax)
                        {
                            SetBreeze(p, _breeze + 1);
                            if (_breeze == BreezeMax)
                                EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.4f, "FULL SAIL", BreezeColor, 1.0f));
                        }
                    }
                }
                else if (_breeze > 0)
                {
                    _breezeIdle += dt;
                    if (_breezeIdle >= BreezeDecay) { _breezeIdle = 0f; SetBreeze(p, _breeze - 1); }
                }
                break;
            }
        }
    }

    private void SetCalm(Player p, bool on)
    {
        if (_calm == on) return;
        _calm = on;
        SetMod(p, Stat.CritChance, ModifierOp.Flat, on ? CalmCrit : 0f);
        SetMod(p, Stat.CritDamage, ModifierOp.Additive, on ? CalmCritDamage : 0f);
        if (on) EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.4f, "STEADY", CalmColor, 1.0f));
    }

    private void SetBreeze(Player p, int stacks)
    {
        _breeze = stacks;
        SetMod(p, Stat.AttackSpeed, ModifierOp.Additive, BreezeAttack * stacks);
        SetMod(p, Stat.MoveSpeed, ModifierOp.Additive, BreezeMove * stacks);
    }

    private void OnHeroDamaged(PlayerDamaged e)
    {
        if (_kind != PassiveKind.CalmLookout) return;
        if (e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f || LivePlayer is not { } p) return;
        _sinceHit = 0f;
        SetCalm(p, false);
    }
}
