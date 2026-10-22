using System;
using System.Reflection;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

/// <summary>
/// Finds the player without compiling against it.
///
/// The player is owned by another system and enemies need exactly three things
/// from it: where it is, an <see cref="IDamageable"/> to hit, and
/// <see cref="Stat.EnemyScaling"/> for the difficulty curve. Binding to the
/// concrete type for that would make the enemy layer un-buildable and
/// un-testable on its own, and would hard-couple two systems over three reads.
///
/// So the lookup is done once and cached: a static <c>Player.Instance</c> if the
/// player type provides one, else the "player" scene group, else a node named
/// "Player". Every read is a cached field afterwards; a failed lookup is retried
/// on a timer rather than every frame, because a full tree scan per enemy per
/// frame is exactly the kind of cost that hides until there are three hundred
/// enemies.
/// </summary>
internal static class PlayerRef
{
    private const double RetryMilliseconds = 250.0;

    private static Node3D? _node;
    private static IDamageable? _damageable;
    private static StatSheet? _sheet;
    private static ulong _nextAttemptMsec;

    private static Func<object?>? _instanceGetter;
    private static bool _instanceGetterSearched;

    /// <summary>The player node, or null when there is not one yet.</summary>
    public static Node3D? Node
    {
        get
        {
            if (_node is not null && GodotObject.IsInstanceValid(_node) && _node.IsInsideTree())
                return _node;
            Forget();
            return Resolve();
        }
    }

    /// <summary>Player node and damage interface together, so callers resolve once.</summary>
    public static bool TryGet(out Node3D node, out IDamageable? damageable)
    {
        Node3D? n = Node;
        node = n!;
        damageable = _damageable;
        return n is not null;
    }

    /// <summary>
    /// The player's <see cref="Stat.EnemyScaling"/>, or 1 when there is no
    /// player. Items that trade danger for reward move this, and the director
    /// multiplies its whole difficulty curve by it.
    /// </summary>
    public static float EnemyScaling
    {
        get
        {
            if (Node is null) return 1f;
            if (_sheet is null) return 1f;
            float v = _sheet.Get(Stat.EnemyScaling);
            return v > 0.01f ? v : 0.01f;
        }
    }

    /// <summary>Drop the cache. Call when a run ends so the next run does not
    /// inherit a stale handle.</summary>
    public static void Forget()
    {
        _node = null;
        _damageable = null;
        _sheet = null;
    }

    private static Node3D? Resolve()
    {
        ulong now = Time.GetTicksMsec();
        if (now < _nextAttemptMsec) return null;
        _nextAttemptMsec = now + (ulong)RetryMilliseconds;

        Node3D? found = FromStaticInstance() ?? FromTree();
        if (found is null || !GodotObject.IsInstanceValid(found)) return null;

        _node = found;
        _damageable = found as IDamageable;
        _sheet = FindStatSheet(found);
        return _node;
    }

    /// <summary>Reads <c>Player.Instance</c> if the player type exposes one. This
    /// is the intended path; the tree searches below are the safety net for a
    /// player that has not been written yet or is named something else.</summary>
    private static Node3D? FromStaticInstance()
    {
        if (!_instanceGetterSearched)
        {
            _instanceGetterSearched = true;
            _instanceGetter = FindInstanceGetter();
        }
        return _instanceGetter?.Invoke() as Node3D;
    }

    private static Func<object?>? FindInstanceGetter()
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Type[] types;
        try { types = typeof(PlayerRef).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = Array.Empty<Type>(); _ = e; }

        foreach (Type t in types)
        {
            if (t.Name != "Player" || !typeof(Node3D).IsAssignableFrom(t)) continue;

            PropertyInfo? prop = t.GetProperty("Instance", Flags);
            if (prop is not null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                return () => prop.GetValue(null);

            FieldInfo? field = t.GetField("Instance", Flags);
            if (field is not null) return () => field.GetValue(null);
        }
        return null;
    }

    private static Node3D? FromTree()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null) return null;

        if (tree.GetFirstNodeInGroup("player") is Node3D grouped) return grouped;
        if (tree.GetFirstNodeInGroup("Player") is Node3D groupedUpper) return groupedUpper;

        // owned: false — nodes added at runtime have no scene owner, and a
        // procedurally built level has no owned children at all.
        return tree.Root.FindChild("Player", recursive: true, owned: false) as Node3D;
    }

    /// <summary>Pulls the player's stat sheet off whatever member happens to hold
    /// it. Done once per player instance, so the reflection cost is irrelevant
    /// and the enemy layer does not need to know the player's field names.</summary>
    private static StatSheet? FindStatSheet(Node3D player)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type t = player.GetType();

        for (Type? cursor = t; cursor is not null && cursor != typeof(Node3D); cursor = cursor.BaseType)
        {
            foreach (PropertyInfo p in cursor.GetProperties(Flags | BindingFlags.DeclaredOnly))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                if (!typeof(StatSheet).IsAssignableFrom(p.PropertyType)) continue;
                try { if (p.GetValue(player) is StatSheet s) return s; }
                catch (TargetInvocationException) { /* property threw; keep looking */ }
            }
            foreach (FieldInfo f in cursor.GetFields(Flags | BindingFlags.DeclaredOnly))
            {
                if (!typeof(StatSheet).IsAssignableFrom(f.FieldType)) continue;
                if (f.GetValue(player) is StatSheet s) return s;
            }
        }
        return null;
    }
}

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
    private const float AssumedPlayerRadius = 0.55f;

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
        ArgumentNullException.ThrowIfNull(def);
        _def = def;

        float radius = MathF.Max(0.12f, def.Radius);
        float height = MathF.Max(radius * 2f + 0.02f, def.Height);
        _halfHeight = height * 0.5f;

        _maxHealth = MathF.Max(1f, def.Health * MathF.Max(0.01f, healthMultiplier));
        _health = _maxHealth;
        _contactDamage = MathF.Max(0f, def.Damage * MathF.Max(0f, damageMultiplier));
        _speed = MathF.Max(0.1f, def.MoveSpeed * MathF.Max(0.05f, speedMultiplier));
        _attackRange = MathF.Max(0.2f, def.AttackRange);
        _attackCooldown = MathF.Max(0.15f, def.AttackCooldown);
        _knockbackResist = Mathf.Clamp(def.KnockbackResist, 0f, 1f);

        BuildBody(def, radius, height);
        ApplyFlash(0f);
    }

    /// <summary>Place this enemy in the world and make it live. Must be called
    /// after <see cref="Configure"/>.</summary>
    public void Spawn(Vector3 position)
    {
        _dead = false;
        _health = _maxHealth;
        _knockback = Vector3.Zero;
        _flashTimer = 0f;
        Velocity = Vector3.Zero;
        _groundY = position.Y;

        GlobalPosition = position;

        // Stagger the first swing so a wave that spawns together does not land a
        // synchronised volley the instant it reaches the player.
        _attackTimer = _attackCooldown * 0.5f;

        CollisionLayer = EnemyLayer;
        CollisionMask = WorldLayer;
        if (_collision is not null) _collision.Disabled = false;

        ApplyFlash(0f);
        Visible = true;
    }

    /// <summary>Remove from the world without a kill — used when the director
    /// culls strays or a run ends. Does not publish <see cref="EnemyKilled"/>,
    /// so it cannot be farmed for XP.</summary>
    public void Despawn()
    {
        if (_dead) return;
        _dead = true;
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
    public void Step(float dt, Vector3 playerPosition, IDamageable? player, Vector3 separation)
    {
        if (_dead) return;

        UpdateFlash(dt);

        Vector3 position = GlobalPosition;
        Vector3 toPlayer = playerPosition - position;
        toPlayer.Y = 0f;

        float distance = toPlayer.Length();
        Vector3 heading = distance > 0.001f ? toPlayer / distance : Vector3.Zero;

        // Ease off the throttle inside striking distance. Enemies that keep
        // driving forward at full speed shove each other through the player and
        // produce a jittering pile instead of a ring.
        bool hasTarget = player is not null;
        float seek = !hasTarget ? 0f
                   : distance > _attackRange * 0.9f ? 1f
                   : 0.2f;

        Vector3 desired = heading * seek + separation * SeparationWeight;
        if (desired.LengthSquared() > 1f) desired = desired.Normalized();
        desired *= _speed;

        Vector3 velocity = Velocity;
        Vector3 horizontal = new(velocity.X, 0f, velocity.Z);
        horizontal -= _knockback;                       // steer the self-driven part only
        horizontal = horizontal.MoveToward(desired, Acceleration * dt);

        _knockback = _knockback.MoveToward(Vector3.Zero, KnockbackDamping * dt);

        float verticalSpeed = velocity.Y;
        if (IsOnFloor()) { if (verticalSpeed < 0f) verticalSpeed = 0f; }
        else verticalSpeed -= Gravity * dt;

        Velocity = new Vector3(horizontal.X + _knockback.X, verticalSpeed, horizontal.Z + _knockback.Z);
        MoveAndSlide();

        // A level without a floor collider would otherwise drop every enemy out
        // of the world. Spawn height is the floor of last resort.
        Vector3 after = GlobalPosition;
        if (after.Y < _groundY)
        {
            after.Y = _groundY;
            GlobalPosition = after;
            Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
        }

        if (distance > 0.05f)
        {
            float target = MathF.Atan2(heading.X, heading.Z);
            Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, target, MathF.Min(1f, TurnSpeed * dt)), 0f);
        }

        _attackTimer -= dt;
        if (player is not null
            && _attackTimer <= 0f
            && player.IsAlive
            && distance <= _attackRange + AssumedPlayerRadius)
        {
            Strike(player, heading);
        }
    }

    private void Strike(IDamageable player, Vector3 heading)
    {
        _attackTimer = _attackCooldown;

        // Fresh instance per swing rather than a reused buffer: PlayerDamaged
        // carries this reference, and anything that keeps a short damage log
        // would find its history rewritten underneath it. Contact attacks are
        // gated by a cooldown, so this is a handful of small objects a second —
        // not the per-frame allocation the pooling rule is aimed at.
        DamageInfo hit = new()
        {
            Amount = _contactDamage,
            Element = Element.Physical,
            CanCrit = false,
            Knockback = IsBoss ? 1.6f : 0.7f,
            Direction = heading,
            ProcCoefficient = 1f,
            Source = _def?.Id ?? "enemy",
        };
        player.TakeDamage(hit);

        // Recoil, so a hit reads as a lunge and the attacker briefly breaks
        // contact instead of grinding against the player at zero distance.
        AddKnockback(-heading, 0.55f);
    }

    // -------------------------------------------------------------- damage

    /// <inheritdoc/>
    public void TakeDamage(DamageInfo info)
    {
        if (info is null) return;

        if (_dead)
        {
            // Report nothing landed, so lifesteal and on-hit effects do not fire
            // off a corpse that a pierce shot happened to pass through.
            info.Nullified = true;
            info.Amount = 0f;
            return;
        }

        float amount = MathF.Max(0f, info.Amount);
        if (amount <= 0f)
        {
            info.Nullified = true;
            info.Amount = 0f;
            return;
        }

        // Overkill is not damage dealt. Clamping here is what makes lifesteal and
        // damage meters read the truth rather than the swing size.
        if (amount > _health) amount = _health;
        info.Amount = amount;

        _health -= amount;
        _flashTimer = FlashDuration;

        if (info.Knockback > 0f) AddKnockback(info.Direction, info.Knockback);

        if (_health <= 0f) Die();
    }

    private void AddKnockback(Vector3 direction, float strength)
    {
        Vector3 flat = new(direction.X, 0f, direction.Z);
        float lengthSquared = flat.LengthSquared();
        if (lengthSquared < 1e-6f) return;

        flat /= MathF.Sqrt(lengthSquared);
        _knockback += flat * strength * KnockbackScale * (1f - _knockbackResist);

        float speedSquared = _knockback.LengthSquared();
        if (speedSquared > MaxKnockbackSpeed * MaxKnockbackSpeed)
            _knockback = _knockback / MathF.Sqrt(speedSquared) * MaxKnockbackSpeed;
    }

    private void Die()
    {
        if (_dead) return;
        _dead = true;
        _health = 0f;

        int xp = _def?.XpValue ?? 0;
        int gold = _def?.GoldValue ?? 0;

        // Slightly above the feet: drops read better when they are not embedded
        // in the floor, and the pickup layer spawns from this point.
        EventBus.Publish(new EnemyKilled(GlobalPosition + new Vector3(0f, 0.35f, 0f), xp, gold, IsElite || IsBoss));

        Deactivate();
        Despawned?.Invoke(this);
    }

    /// <summary>Make the body inert immediately. Layer/mask zeroing rather than
    /// disabling shapes: it takes effect the moment it is set, so a corpse cannot
    /// be hit by a projectile later in the same physics step, and it avoids the
    /// deferred-call ordering trap that shape toggling has when a node dies and
    /// respawns inside one frame.</summary>
    private void Deactivate()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        Velocity = Vector3.Zero;
        _knockback = Vector3.Zero;
        AliveIndex = -1;
    }

    // ------------------------------------------------------------- visuals

    private void UpdateFlash(float dt)
    {
        if (_flashTimer <= 0f) return;
        _flashTimer -= dt;
        ApplyFlash(_flashTimer <= 0f ? 0f : _flashTimer / FlashDuration);
    }

    private void ApplyFlash(float amount)
    {
        if (_bodyMaterial is null) return;

        float t = Mathf.Clamp(amount, 0f, 1f);
        if (t <= 0f)
        {
            _bodyMaterial.AlbedoColor = _baseColor;
            _bodyMaterial.Emission = _baseColor;
            _bodyMaterial.EmissionEnergyMultiplier = EmissionFor(_builtSilhouette);
            if (_accentMaterial is not null)
            {
                _accentMaterial.AlbedoColor = _accentColor;
                _accentMaterial.Emission = _accentColor;
                _accentMaterial.EmissionEnergyMultiplier = EmissionFor(_builtSilhouette) * 1.8f;
            }
            return;
        }

        Color flashed = _baseColor.Lerp(Colors.White, t);
        _bodyMaterial.AlbedoColor = flashed;
        _bodyMaterial.Emission = flashed;
        _bodyMaterial.EmissionEnergyMultiplier = EmissionFor(_builtSilhouette) + t * 2.2f;
        if (_accentMaterial is not null)
        {
            _accentMaterial.AlbedoColor = _accentColor.Lerp(Colors.White, t);
            _accentMaterial.Emission = _accentMaterial.AlbedoColor;
            _accentMaterial.EmissionEnergyMultiplier = EmissionFor(_builtSilhouette) * 1.8f + t * 2.2f;
        }
    }

    private static float EmissionFor(Silhouette s) => s switch
    {
        Silhouette.Boss => 0.85f,
        Silhouette.Elite => 0.55f,
        _ => 0.22f,
    };

    /// <summary>
    /// Which primitive body an enemy wears.
    ///
    /// Chosen from stats rather than authored per definition, so silhouette and
    /// behaviour cannot drift apart: if a designer makes something fast, it
    /// becomes a low sphere and the player learns to read speed off the shape.
    /// </summary>
    private enum Silhouette { None, Grunt, Runner, Brute, Elite, Boss }

    private static Silhouette Classify(EnemyDef def)
    {
        if (def.IsBoss) return Silhouette.Boss;
        if (def.IsElite) return Silhouette.Elite;
        if (def.MoveSpeed >= 5f) return Silhouette.Runner;
        if (def.Health >= 60f || def.KnockbackResist >= 0.5f) return Silhouette.Brute;
        return Silhouette.Grunt;
    }

    private void BuildBody(EnemyDef def, float radius, float height)
    {
        Silhouette wanted = Classify(def);
        bool sameShape = wanted == _builtSilhouette
                      && Mathf.IsEqualApprox(radius, _builtRadius)
                      && Mathf.IsEqualApprox(height, _builtHeight);

        _baseColor = def.Tint;
        _accentColor = def.Tint.Lightened(0.45f);

        if (sameShape)
        {
            ApplyFlash(0f);
            return;
        }

        _builtSilhouette = wanted;
        _builtRadius = radius;
        _builtHeight = height;

        EnsureNodes();

        // Capsule collision for every silhouette: the shape the player fights is
        // the shape they see, and a single primitive keeps the physics step flat.
        _capsule!.Radius = radius;
        _capsule.Height = height;
        _collision!.Position = new Vector3(0f, height * 0.5f, 0f);

        _bodyMaterial ??= NewMaterial();
        _bodyMesh!.MaterialOverride = _bodyMaterial;
        _bodyMesh.Mesh = MeshLibrary.Body(wanted, radius, height);
        _bodyMesh.Position = new Vector3(0f, height * 0.5f, 0f);
        _bodyMesh.Scale = BodyScale(wanted);
        _bodyMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        Mesh? accent = MeshLibrary.Accent(wanted, radius, height);
        if (accent is null)
        {
            if (_accentMesh is not null) _accentMesh.Visible = false;
        }
        else
        {
            EnsureAccent();
            _accentMaterial ??= NewMaterial();
            _accentMesh!.Visible = true;
            _accentMesh.MaterialOverride = _accentMaterial;
            _accentMesh.Mesh = accent;
            _accentMesh.Position = new Vector3(0f, height + radius * 0.45f, 0f);
            _accentMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        // Only the things worth noticing get a light. Hundreds of OmniLight3Ds
        // is the fastest way to melt the forward+ renderer.
        bool wantsLight = wanted is Silhouette.Elite or Silhouette.Boss;
        if (wantsLight)
        {
            _light ??= NewLight();
            _light.Visible = true;
            _light.Position = new Vector3(0f, height * 0.6f, 0f);
            _light.OmniRange = wanted == Silhouette.Boss ? radius * 14f : radius * 8f;
            _light.LightEnergy = wanted == Silhouette.Boss ? 3.2f : 1.7f;
            _light.LightColor = _accentColor;
        }
        else if (_light is not null)
        {
            _light.Visible = false;
        }

        ApplyFlash(0f);
    }

    private static Vector3 BodyScale(Silhouette s) => s switch
    {
        // Squashed and stretched along the travel axis — reads as "moving fast"
        // even standing still.
        Silhouette.Runner => new Vector3(1f, 0.78f, 1.3f),
        Silhouette.Boss => new Vector3(1.05f, 1f, 1.05f),
        _ => Vector3.One,
    };

    private void EnsureNodes()
    {
        if (_collision is null)
        {
            _capsule = new CapsuleShape3D();
            _collision = new CollisionShape3D { Shape = _capsule, Name = "Body" };
            AddChild(_collision);
        }
        if (_bodyMesh is null)
        {
            _bodyMesh = new MeshInstance3D { Name = "Visual" };
            AddChild(_bodyMesh);
        }
    }

    private void EnsureAccent()
    {
        if (_accentMesh is not null) return;
        _accentMesh = new MeshInstance3D { Name = "Accent" };
        AddChild(_accentMesh);
    }

    private OmniLight3D NewLight()
    {
        OmniLight3D light = new()
        {
            Name = "Glow",
            ShadowEnabled = false,
            OmniShadowMode = OmniLight3D.ShadowMode.DualParaboloid,
        };
        AddChild(light);
        return light;
    }

    /// <summary>One material per instance, because the hit flash writes to it.
    /// Created once per pooled node and then reused for the whole run.</summary>
    private static StandardMaterial3D NewMaterial() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        EmissionEnabled = true,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
    };

    /// <summary>
    /// Shared meshes, keyed by silhouette and size.
    ///
    /// Every enemy of a given type has identical geometry, so building a mesh per
    /// instance would allocate hundreds of copies of the same vertex buffer and
    /// throw away the renderer's chance to batch them.
    /// </summary>
    private static class MeshLibrary
    {
        private static readonly System.Collections.Generic.Dictionary<string, Mesh> Cache = new();

        public static Mesh Body(Silhouette s, float radius, float height)
        {
            string key = $"b{(int)s}:{radius:0.###}:{height:0.###}";
            if (Cache.TryGetValue(key, out Mesh? cached) && GodotObject.IsInstanceValid(cached))
                return cached;

            Mesh mesh = s switch
            {
                Silhouette.Brute => new BoxMesh { Size = new Vector3(radius * 1.9f, height, radius * 1.9f) },
                Silhouette.Boss => new BoxMesh { Size = new Vector3(radius * 2.1f, height * 0.8f, radius * 2.1f) },
                Silhouette.Runner => new SphereMesh
                {
                    Radius = radius,
                    Height = height,
                    RadialSegments = 10,
                    Rings = 5,
                },
                _ => new CapsuleMesh
                {
                    Radius = radius,
                    Height = height,
                    RadialSegments = 10,
                    Rings = 3,
                },
            };

            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>The bit stuck on top: a crown of spikes for elites, a head for
        /// bosses, nothing for the rank and file.</summary>
        public static Mesh? Accent(Silhouette s, float radius, float height)
        {
            if (s is not (Silhouette.Elite or Silhouette.Boss)) return null;

            string key = $"a{(int)s}:{radius:0.###}:{height:0.###}";
            if (Cache.TryGetValue(key, out Mesh? cached) && GodotObject.IsInstanceValid(cached))
                return cached;

            Mesh mesh = s == Silhouette.Boss
                ? new SphereMesh
                {
                    Radius = radius * 0.85f,
                    Height = radius * 1.7f,
                    RadialSegments = 10,
                    Rings = 5,
                }
                : new PrismMesh
                {
                    Size = new Vector3(radius * 1.6f, radius * 1.6f, radius * 1.6f),
                    LeftToRight = 0.5f,
                };

            Cache[key] = mesh;
            return mesh;
        }
    }
}
