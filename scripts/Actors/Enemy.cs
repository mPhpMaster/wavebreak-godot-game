using System;
using System.Reflection;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

/// <summary>
/// One enemy: a pooled, manager-driven chaser that deals damage by touching the
/// player.
///
/// Three decisions shape this class.
///
/// It has no per-frame callbacks of its own. <see cref="EnemyManager"/> calls
/// <see cref="Step"/> instead, because three hundred nodes each crossing the
/// managed/native boundary for their own _PhysicsProcess costs more than the
/// steering maths does, and because a single driver can hoist the player lookup
/// and the neighbour grid out of the loop.
///
/// It steers rather than collides. Enemy bodies do not mask against each other:
/// two kinematic bodies pushing at one another just stall, and a hundred of them
/// converging on one point turns into a solid wall the player can stand behind.
/// A separation force spreads them out instead, which both looks better and
/// keeps the physics step cheap.
///
/// Its body is built from primitives at first configure and then reused for the
/// pool's lifetime, with the silhouette chosen from the stats that matter —
/// a sphere is fast, a box is tough, spikes mean elite. Reading the threat off
/// the shape is what makes a screen with two hundred enemies on it legible.
/// </summary>
public sealed partial class Enemy : CharacterBody3D, IDamageable, IPooled
{
    // ------------------------------------------------------------- tuning

    /// <summary>Physics layer enemies occupy. Weapons mask against this.</summary>
    public const uint EnemyLayer = 1u << 1;

    /// <summary>What an enemy collides with: the world only. See the class note
    /// on why enemies do not mask against each other.</summary>
    public const uint WorldLayer = 1u << 0;

    private const float Gravity = 24f;              // matches project.godot
    private const float Acceleration = 34f;         // m/s^2 toward the desired velocity
    private const float SeparationWeight = 1.35f;
    private const float KnockbackDamping = 26f;     // m/s^2 bleed-off
    private const float KnockbackScale = 2.4f;      // DamageInfo.Knockback -> m/s
    private const float MaxKnockbackSpeed = 22f;
    private const float FlashDuration = 0.11f;
    private const float TurnSpeed = 9f;             // radians/second
    private const float DashAcceleration = 220f;    // a charge is at speed within a few frames

    // ---------------------------------------------------------- identity

    private EnemyDef? _def;
    private Silhouette _builtSilhouette = Silhouette.None;
    private float _builtRadius = -1f, _builtHeight = -1f;

    // ------------------------------------------------------------- stats

    private float _maxHealth = 1f;
    private float _health = 1f;
    private float _contactDamage;
    private float _speed;
    private float _attackRange = 1.6f;
    private float _bodyRadius = 0.5f;

    /// <summary>
    /// Gap between the two bodies at which a melee swing lands, metres.
    ///
    /// Reach is measured from the enemy's edge to the player's edge, not centre to
    /// centre. The old centre-based test (AttackRange + 0.55) let a small enemy
    /// hit from well over a metre away — with real models that read as being
    /// damaged by something that had not reached you yet. A big enemy still hits
    /// from further off, because its own radius is part of the distance.
    /// </summary>
    private float ContactDistance => _bodyRadius + Player.BodyRadius + MeleeReach;

    /// <summary>Edge-to-edge melee reach: AttackRange beyond the old baseline of
    /// 1.3 lengthens it slightly (long-armed brutes), but never past 0.8 m.</summary>
    private float MeleeReach => Mathf.Clamp(0.3f + (_attackRange - 1.3f) * 0.25f, 0.2f, 0.8f);
    private float _attackCooldown = 1f;
    private float _knockbackResist;

    // ------------------------------------------------------------- state

    private float _attackTimer;
    private float _flashTimer;
    private float _groundY;
    private float _halfHeight = 0.8f;
    private Vector3 _knockback;
    private bool _dead = true;

    // ------------------------------------------------------------ visuals

    private MeshInstance3D? _bodyMesh;
    private MeshInstance3D? _accentMesh;
    private OmniLight3D? _light;
    private CollisionShape3D? _collision;
    private CapsuleShape3D? _capsule;
    private StandardMaterial3D? _bodyMaterial;
    private StandardMaterial3D? _accentMaterial;
    private Color _baseColor = Colors.White;
    private Color _accentColor = Colors.White;
    private Visuals.CharacterRig? _rig; // the model; see Visuals.EnemyVisuals

    // ------------------------------------------------- manager bookkeeping

    /// <summary>Which pool this instance came from. Owned by the manager.</summary>
    internal int PoolIndex = -1;

    /// <summary>Index into the manager's live list, for O(1) swap-removal.
    /// -1 when not live. Owned by the manager.</summary>
    internal int AliveIndex = -1;

    /// <summary>Raised when this enemy leaves the world, by death or despawn.
    /// The manager uses it to unregister and return the node to its pool.</summary>
    internal Action<Enemy>? Despawned;

    // --------------------------------------------------------- public API

    /// <summary>The definition this enemy was configured from.</summary>
    public EnemyDef? Def => _def;

    /// <inheritdoc/>
    public bool IsAlive => !_dead && _health > 0f;

    /// <summary>Pool bookkeeping flag. Written by <see cref="Pool{T}"/>.</summary>
    public bool IsPooled { get; set; } = true;

    public float Health => _health;
    public float MaxHealth => _maxHealth;
    public float HealthFraction => _maxHealth > 0f ? Mathf.Clamp(_health / _maxHealth, 0f, 1f) : 0f;

    public bool IsElite => _def is { IsElite: true };
    public bool IsBoss => _def is { IsBoss: true };

    /// <summary>
    /// Centre of mass, and what weapons should aim at.
    ///
    /// The node origin sits at the feet so floor detection and spawn placement
    /// work in ground coordinates; aiming at the feet would make every projectile
    /// look like it passed under the target.
    /// </summary>
    public Vector3 AimPoint => GlobalPosition + new Vector3(0f, _halfHeight, 0f);

    // Node3D already has a `Position` (local transform), which would silently
    // satisfy IDamageable with the wrong value. Explicit implementation makes the
    // interface read the world-space aim point instead.
    Vector3 IDamageable.Position => AimPoint;

    public Enemy()
    {
        // Manager-driven: see the class note.
        SetProcess(false);
        SetPhysicsProcess(false);
        CollisionLayer = 0;
        CollisionMask = 0;
        MotionMode = MotionModeEnum.Grounded;
        Visible = false;
    }

    // ----------------------------------------------------------- lifecycle

    /// <summary>
    /// Apply a definition and the director's difficulty multipliers. Cheap to
    /// call every spawn: the body is only rebuilt when the shape it needs
    /// actually changes, which in practice is once per pool.
    /// </summary>
    /// <param name="def">What this enemy is.</param>
    /// <param name="healthMultiplier">Director health scaling.</param>
    /// <param name="damageMultiplier">Director damage scaling.</param>
    /// <param name="speedMultiplier">Director speed scaling, including any
    /// per-instance jitter the caller wants baked in.</param>
    public void Configure(EnemyDef def, float healthMultiplier, float damageMultiplier, float speedMultiplier)
    {
        using var timing = Wavebreak.Game.Prof.Scope("Enemy.configure");
        ArgumentNullException.ThrowIfNull(def);
        _def = def;

        float radius = MathF.Max(0.12f, def.Radius);
        _bodyRadius = radius;
        float height = MathF.Max(radius * 2f + 0.02f, def.Height);
        _halfHeight = height * 0.5f;

        _maxHealth = MathF.Max(1f, def.Health * MathF.Max(0.01f, healthMultiplier));
        _health = _maxHealth;
        _contactDamage = MathF.Max(0f, def.Damage * MathF.Max(0f, damageMultiplier));
        _speed = MathF.Max(0.1f, def.MoveSpeed * MathF.Max(0.05f, speedMultiplier));
        // Nothing outwalks the player's base 6 m/s once the clock and modifiers
        // pile on; fast fodder that cannot be kited just feels unfair.
        _speed = MathF.Min(_speed, def.IsBoss || def.IsElite ? 5.6f : 5.2f);
        _attackRange = MathF.Max(0.2f, def.AttackRange);
        _attackCooldown = MathF.Max(0.15f, def.AttackCooldown);
        _knockbackResist = Mathf.Clamp(def.KnockbackResist, 0f, 1f);
        _abilityDamage = _contactDamage * MathF.Max(0f, def.AbilityDamage);

        BuildBody(def, radius, height);
        ApplyFlash(0f);
    }

    /// <summary>Place this enemy in the world and make it live. Must be called
    /// after <see cref="Configure"/>.</summary>
    public int SpawnGeneration { get; private set; }

    public void Spawn(Vector3 position)
    {
        SpawnGeneration++;
        _dead = false;
        _health = _maxHealth;
        _knockback = Vector3.Zero;
        _flashTimer = 0f;
        Velocity = Vector3.Zero;
        _groundY = position.Y;
        ClearStatuses();

        GlobalPosition = position;

        // Stagger the first swing so a wave that spawns together does not land a
        // synchronised volley the instant it reaches the player.
        _attackTimer = _attackCooldown * 0.5f;
        ResetBehaviour();
        ResetCityGiant();
        _woundLevel = -1; RefreshWounds();

        CollisionPriority = IsCityGiant ? 10 : 1;
        CollisionLayer = EnemyLayer | (IsCityGiant ? 1u << 6 : 0u);
        CollisionMask = WorldLayer | EnemyLayer | (IsCityGiant ? 1u << 6 : 0u);
        if (_collision is not null) _collision.Disabled = false;

        ApplyFlash(0f);
        Visible = true;
        SpawnExpansion();

        ContentAudit.NoteSpawn(_def);
    }

    /// <summary>Remove from the world without a kill — used when the director
    /// culls strays or a run ends. Does not publish <see cref="EnemyKilled"/>,
    /// so it cannot be farmed for XP.</summary>
    public void Despawn()
    {
        if (_dead) return;
        _dead = true;
        if (NetId > 0 && Net.Coop.IsHost) Net.NetSession.Instance?.OnEnemyGone(this, killed: false);
        Deactivate();
        Despawned?.Invoke(this);
    }

    /// <summary>
    /// Move, steer, and swing. Driven by <see cref="EnemyManager"/> from the
    /// physics step.
    /// </summary>
    /// <param name="dt">Physics delta.</param>
    /// <param name="playerPosition">World position to chase.</param>
    /// <param name="player">Damage target, or null when there is no player.</param>
    /// <param name="separation">Crowd-avoidance push for this frame, computed by
    /// the manager from its neighbour grid.</param>
    private int _slideTick;

    /// <summary>True on the ticks a distant, grounded, self-driven enemy skips
    /// its collision move: every other tick past 16 m, two in three past 30 m.
    /// Staggered by instance so the crowd's real slides spread evenly.</summary>
    private bool CoastThisTick(float distance)
    {
        int every = distance > 30f ? 3 : distance > 16f ? 2 : 1;
        if (every == 1 || IsBoss || _act != Act.None || !IsOnFloor() || _knockback.LengthSquared() > 0.01f || _detouring) return false;
        return (++_slideTick + (int)(GetInstanceId() & 7)) % every != 0;
    }

}



