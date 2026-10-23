using System;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

/// <summary>
/// The player's survivability layer: health, shield, invulnerability frames,
/// regeneration and lifesteal.
///
/// Damage flows in one fixed order — evasion, armour, shield, health — and that
/// order is load-bearing. Evasion first means a dodge costs nothing and armour
/// never "wastes" itself on a hit that was never going to land; shield before
/// health means a shield stat is worth exactly its face value no matter how much
/// armour is stacked behind it.
///
/// The invulnerability window is the reason contact damage is survivable at all.
/// Without it, standing inside six enemies applies six hits per physics frame and
/// any amount of health evaporates in a frame or two — which reads to a player as
/// "I died for no reason" rather than "I was surrounded".
/// </summary>
public sealed partial class PlayerHealth : Node, IDamageable
{
    /// <summary>Seconds of immunity after a hit lands.</summary>
    public const float InvulnerabilityWindow = 0.4f;

    /// <summary>Quiet seconds required before the shield starts coming back.</summary>
    public const float ShieldRechargeDelay = 6f;

    /// <summary>Shield restored per second, as a fraction of its maximum.</summary>
    public const float ShieldRechargeRate = 0.25f;

    private static readonly Color DamageColor = new(1.00f, 0.34f, 0.30f);
    private static readonly Color ShieldColor = new(0.55f, 0.80f, 1.00f);
    private static readonly Color DodgeColor = new(0.78f, 0.92f, 1.00f);
    private static readonly Color HealColor = new(0.42f, 1.00f, 0.52f);

    private Player? _owner;
    private StatSheet _stats = new();
    private Rng _rng = new(Time.GetTicksUsec());

    private float _invulnerable;
    private float _shieldDelay;
    private float _pendingHealEvent;   // batched trickle healing, see ApplyTrickle
    private float _lastMax;
    private float _lastShieldMax;
    private bool _dead;
    private bool _subscribed;

    // Run summary, accumulated from the bus so that the RunEnded published on
    // death carries real numbers instead of zeroes. Cheap: two increments.
    private float _elapsed;
    private int _level = 1;
    private int _kills;

    // ------------------------------------------------------------------ state

    /// <summary>Current health. Never negative, never above <see cref="Max"/>.</summary>
    public float Current { get; private set; }

    /// <summary>Maximum health, straight from the stat sheet.</summary>
    public float Max => _stats.Get(Stat.MaxHealth);

    /// <summary>0..1, for health bars.</summary>
    public float Fraction => Max <= 0f ? 0f : Mathf.Clamp(Current / Max, 0f, 1f);

    /// <summary>Current shield points. Absorbed before health and regenerates on
    /// its own, which makes it the stat that rewards disengaging.</summary>
    public float Shield { get; private set; }

    public float ShieldMax => _stats.Get(Stat.Shield);

    public bool IsAlive => !_dead;

    /// <summary>Seconds of invulnerability left. Drives the hit shimmer.</summary>
    public float InvulnerableFor => _invulnerable;

    /// <summary>Seconds the player has been alive this run.</summary>
    public float RunSeconds => _elapsed;

    /// <summary>The stat sheet damage is resolved against.</summary>
    public StatSheet Stats => _stats;

    /// <summary>The random source evasion rolls against. Assign the run's seeded
    /// <see cref="Rng"/> so a replayed seed dodges the same hits.</summary>
    public Rng Rng
    {
        get => _rng;
        set => _rng = value;
    }

    /// <summary>Raised once, after <see cref="RunEnded"/> is published.</summary>
    public event Action? Died;

    // ------------------------------------------------------------- lifecycle

    /// <summary>
    /// Binds this health to a player. Call before adding the node to the tree:
    /// <see cref="_Ready"/> fills current health from the sheet, and a sheet
    /// attached afterwards would leave the player at the default 100 regardless
    /// of what their max actually is.
    /// </summary>
    public void Initialize(Player owner, Rng? rng = null)
    {
        _owner = owner;
        _stats = owner.Stats;
        if (rng is not null) _rng = rng;
    }

    public override void _Ready()
    {
        Current = _stats.Get(Stat.MaxHealth);
        Shield = _stats.Get(Stat.Shield);
        _lastMax = Current;
        _lastShieldMax = Shield;

        _stats.Changed += OnStatChanged;
        EventBus.Subscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Subscribe<EnemyKilled>(OnEnemyKilled);
        EventBus.Subscribe<LevelGained>(OnLevelGained);
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (!_subscribed) return;
        _subscribed = false;

        _stats.Changed -= OnStatChanged;
        EventBus.Unsubscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Unsubscribe<EnemyKilled>(OnEnemyKilled);
        EventBus.Unsubscribe<LevelGained>(OnLevelGained);
    }

    public override void _PhysicsProcess(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("PlayerHealth.phys");
        float dt = (float)delta;
        if (_dead) return;

        _elapsed += dt;

        if (_invulnerable > 0f)
            _invulnerable = Mathf.Max(0f, _invulnerable - dt);

        float regen = _stats.RegenPerSecond();
        if (regen > 0f) ApplyTrickle(regen * dt);

        RechargeShield(dt);

        // Trickle healing publishes in whole points. Publishing per frame would
        // fire PlayerHealed sixty times a second forever, and every listener
        // would have to defend against that individually.
        if (_pendingHealEvent >= 1f)
        {
            EventBus.Publish(new PlayerHealed(_pendingHealEvent));
            _pendingHealEvent = 0f;
        }
    }

    private void RechargeShield(float dt)
    {
        float max = ShieldMax;
        if (max <= 0f)
        {
            Shield = 0f;
            return;
        }

        if (_shieldDelay > 0f)
        {
            _shieldDelay = Mathf.Max(0f, _shieldDelay - dt);
            return;
        }

        if (Shield < max)
            Shield = Mathf.Min(max, Shield + max * ShieldRechargeRate * dt);
    }

    // ----------------------------------------------------------------- damage

    /// <summary>
    /// Applies a hit. <paramref name="info"/> is mutated to record what actually
    /// happened: <see cref="DamageInfo.Nullified"/> when the hit was ignored, and
    /// <see cref="DamageInfo.Amount"/> rewritten to the damage that truly landed
    /// so a caller reading it afterwards is reading reality.
    /// </summary>
    public void TakeDamage(DamageInfo info)
    {
        if (info is null) return;
        if (DamageLog) LogHit(info);

        if (Game.Screenshot.Invulnerable)
        {
            info.Nullified = true;
            info.Amount = 0f;
            return;
        }

        if (_dead || info.Nullified)
        {
            info.Nullified = true;
            info.Amount = 0f;
            return;
        }

        // 1. Invulnerability. Checked before evasion so that a crowd cannot burn
        //    through evasion rolls while the player is already immune.
        if (_invulnerable > 0f)
        {
            info.Nullified = true;
            info.Amount = 0f;
            return;
        }

        // 2. Evasion.
        if (_rng.Chance(_stats.EvasionChance()))
        {
            info.Nullified = true;
            info.Amount = 0f;
            EventBus.Publish(new PopupRequested(PopupAnchor, "dodge", DodgeColor, 0.85f));
            return;
        }

        // 3. Armour, as a fraction off the top.
        float incoming = Mathf.Max(0f, info.Amount) * (1f - _stats.ArmorReduction());

        // 4. Shield soaks what it can.
        float absorbed = 0f;
        if (Shield > 0f && incoming > 0f)
        {
            absorbed = Mathf.Min(Shield, incoming);
            Shield -= absorbed;
            incoming -= absorbed;
        }

        // 5. Whatever is left hits health.
        if (incoming > 0f)
            Current = Mathf.Max(0f, Current - incoming);

        float applied = absorbed + incoming;
        info.Amount = applied;

        _invulnerable = InvulnerabilityWindow;
        _shieldDelay = ShieldRechargeDelay;

        if (_owner is not null && IsInstanceValid(_owner))
        {
            _owner.FlashHit(Mathf.Clamp(applied / Mathf.Max(1f, Max * 0.25f), 0.35f, 1f));
            if (info.Knockback > 0f && info.Direction != Vector3.Zero)
                _owner.ApplyKnockback(info.Direction.Normalized() * Mathf.Min(info.Knockback, 10f) * 0.6f);
        }

        EventBus.Publish(new PlayerDamaged(info, Current));

        if (applied >= 0.5f)
        {
            // Shield-only hits read in the shield's colour: the player needs to
            // see that the hit was paid for by the thing that grows back.
            bool shieldOnly = incoming <= 0.0001f && absorbed > 0f;
            EventBus.Publish(new PopupRequested(
                PopupAnchor,
                Mathf.RoundToInt(Mathf.Max(1f, applied)).ToString(),
                shieldOnly ? ShieldColor : DamageColor,
                shieldOnly ? 0.9f : 1.1f));
        }

        if (Current <= 0f) Die();
    }

    /// <summary>Heals immediately, with a popup. For potions, pickups and item
    /// effects — anything the player should see land.</summary>
    public void Heal(float amount)
    {
        if (_dead || amount <= 0f) return;

        float max = Max;
        float before = Current;
        Current = Mathf.Min(max, Current + amount);

        float healed = Current - before;
        if (healed <= 0f) return;

        EventBus.Publish(new PlayerHealed(healed));

        if (healed >= 1f)
            EventBus.Publish(new PopupRequested(
                PopupAnchor, $"+{Mathf.RoundToInt(healed)}", HealColor, 0.85f));
    }

    /// <summary>Heals a fraction of maximum health. The form most items want,
    /// because a flat heal stops mattering once max health has tripled.</summary>
    public void HealFraction(float fractionOfMax) => Heal(Max * Mathf.Max(0f, fractionOfMax));

    /// <summary>
    /// Extends invulnerability to at least <paramref name="seconds"/>. Takes the
    /// maximum rather than adding, so a dash during post-hit i-frames neither
    /// shortens them nor stacks into a free minute of immunity.
    /// </summary>
    public void GrantInvulnerability(float seconds)
    {
        if (_dead || seconds <= 0f) return;
        _invulnerable = Mathf.Max(_invulnerable, seconds);
    }

    /// <summary>
    /// Healing that should not produce a popup or a per-frame event: regeneration
    /// and lifesteal. Applied to health right away, but the notification is
    /// batched until it is worth a whole point.
    /// </summary>
    private void ApplyTrickle(float amount)
    {
        if (_dead || amount <= 0f) return;

        float max = Max;
        if (Current >= max) return;

        float healed = Mathf.Min(amount, max - Current);
        Current += healed;
        _pendingHealEvent += healed;
    }

    /// <summary>Kills the player outright. Exists for tests and for scripted
    /// deaths; ordinary damage reaches <see cref="Die"/> on its own.</summary>
    public void Kill()
    {
        Current = 0f;
        Die();
    }

    private void Die()
    {
        if (_dead) return;

        _dead = true;
        Current = 0f;
        Shield = 0f;
        _invulnerable = 0f;
        _pendingHealEvent = 0f;

        // A death after the last boss fell (in the Final Swarm) still ends a won run.
        bool won = Game.Main.Instance is { } main && main.Stages is { Won: true };
        EventBus.Publish(new RunEnded(won, _elapsed, _level, _kills));
        Died?.Invoke();
    }

    // ------------------------------------------------------------ bus wiring

    /// <summary>
    /// Lifesteal. Reads the damage the player's weapons actually dealt rather
    /// than what they attempted, so an overkill hit on a one-health enemy does
    /// not heal for the full swing — the enemy's TakeDamage has already written
    /// the truth back into the info by the time this fires.
    /// </summary>
    private void OnEnemyDamaged(EnemyDamaged evt)
    {
        if (_dead) return;

        DamageInfo info = evt.Info;
        if (info is null || info.Nullified || info.Amount <= 0f) return;

        float fraction = _stats.Get(Stat.Lifesteal);
        if (fraction <= 0f) return;

        // Scaled by the proc coefficient: without it a machine-gun weapon would
        // out-heal a cannon by an order of magnitude on identical DPS.
        ApplyTrickle(info.Amount * fraction * Mathf.Max(0f, info.ProcCoefficient));
    }

    private void OnEnemyKilled(EnemyKilled evt) => _kills++;

    private void OnLevelGained(LevelGained evt) => _level = evt.Level;

    /// <summary>
    /// Keeps current health honest when the maximum moves. Rescaling
    /// proportionally is the only option that behaves for both directions: a
    /// half-health player who gains +50% max stays at half, and one who loses max
    /// health does not die from an item pickup.
    /// </summary>
    private void OnStatChanged(Stat stat)
    {
        if (stat == Stat.MaxHealth)
        {
            float max = _stats.Get(Stat.MaxHealth);
            float fraction = _lastMax > 0f ? Current / _lastMax : 1f;
            _lastMax = max;
            if (!_dead) Current = Mathf.Clamp(fraction * max, 0f, max);
        }
        else if (stat == Stat.Shield)
        {
            float max = _stats.Get(Stat.Shield);
            // Going from no shield at all to having one should hand it over
            // filled — otherwise the item that grants it appears to do nothing
            // for six seconds.
            Shield = _lastShieldMax > 0f
                ? Mathf.Clamp(Shield / _lastShieldMax * max, 0f, max)
                : max;
            _lastShieldMax = max;
        }
    }

    // ------------------------------------------------------------ IDamageable

    /// <summary>Where hits and numbers belong in the world: the player's torso.</summary>
    public Vector3 Position => _owner is not null && IsInstanceValid(_owner)
        ? _owner.CenterPosition
        : Vector3.Zero;

    private Vector3 PopupAnchor => Position + Vector3.Up * 0.55f;

    /// <summary>--damage-log: one line per incoming hit with its source and the
    /// distance to the nearest enemy, to track down damage that seems to come
    /// from nowhere.</summary>
    private static readonly bool DamageLog = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--damage-log") >= 0;

    private void LogHit(DamageInfo info)
    {
        Vector3 at = _owner is not null && IsInstanceValid(_owner) ? _owner.GlobalPosition : Vector3.Zero;
        Enemy? near = EnemyManager.Instance?.NearestTo(at, 500f);
        float d = near is null ? -1f : near.GlobalPosition.DistanceTo(at);
        GD.Print($"HIT src=\"{info.Source}\" amt={info.Amount:0.0} nearest={d:0.0}m by={near?.Def?.Id ?? "-"} t={Game.Main.Instance?.Run.Seconds ?? 0:0.0}");
    }
}
