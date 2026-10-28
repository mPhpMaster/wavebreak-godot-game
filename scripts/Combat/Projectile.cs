using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Combat;

/// <summary>
/// One shot in flight.
///
/// Three decisions shape this class.
///
/// It has no <c>_Process</c>. A survivors-like has hundreds of these alive at
/// once, and hundreds of nodes each crossing the managed/native boundary for
/// their own per-frame callback costs more than the vector maths does.
/// <see cref="WeaponSystem"/> drives every live projectile from one loop, which
/// also means hit registration happens at a defined point in the frame instead of
/// wherever the scene tree happens to put it.
///
/// It does not use physics. Collision is an XZ distance test against the enemy
/// set <see cref="Targeting"/> already gathered for aiming. A shape query per
/// projectile per frame would allocate a native array and dictionary per call;
/// this reuses one managed list and a spatial hash that was going to be built
/// anyway. Testing on XZ only is deliberate: it makes hits independent of how
/// tall a target is or where its origin sits.
///
/// Its geometry is shared and its instances are pooled. The only per-instance
/// Godot objects are the node and its one <see cref="MeshInstance3D"/>.
/// </summary>
public sealed partial class Projectile : Node3D, IPooled
{
    /// <summary>Ceiling on how long any shot can live, whatever its range says.
    /// A stray projectile that never expires is a slow leak of the frame budget.</summary>
    private const float MaxLifetime = 10f;

    /// <summary>Radians per second a homing shot may turn. Bounded on purpose:
    /// instant snapping makes a homing weapon unmissable and unreadable, and it
    /// never produces the arc-back that makes one look alive.</summary>
    private const float HomingTurnRate = 7.5f;

    /// <summary>How far a homing shot looks for a replacement when its target
    /// dies. Generous — a seeker that gives up mid-flight just looks broken.</summary>
    private const float HomingSeekRange = 26f;

    // ------------------------------------------------------------------ pool

    private static Pool<Projectile>? _pool;
    private static readonly List<Projectile> Live = new(256);

    // Two buffers, not one: Detonate runs from inside the contact loop, and a
    // shared buffer would have the explosion overwrite the list being iterated.
    private static readonly List<IDamageable> ContactBuffer = new(64);
    private static readonly List<IDamageable> BlastBuffer = new(64);

    // ----------------------------------------------------------------- state

    private readonly List<IDamageable> _pierced = new(8);
    private MeshInstance3D _mesh = null!;

    private WeaponSystem? _system;
    private DamageInfo? _template;
    private ProjectileMode _mode;

    private Vector3 _velocity;
    private Vector3 _cosmeticScale = Vector3.One;
    private Color _tint = Colors.White;

    private float _hitRadius = 0.25f;
    private float _blastRadius;
    private float _life = 1f;
    private float _age;
    private int _pierceLeft;
    private IDamageable? _lock;
    private bool _retired = true;

    /// <inheritdoc/>
    public bool IsPooled { get; set; } = true;

    /// <summary>Shots currently in the air. For debug overlays and tests.</summary>
    public static int ActiveCount => Live.Count;

    public Projectile()
    {
        // Manager-driven; see the class note.
        SetProcess(false);
        SetPhysicsProcess(false);
        Visible = false;

        _mesh = new MeshInstance3D
        {
            Name = "Visual",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_mesh);
    }

    // ------------------------------------------------------------- lifecycle

    /// <summary>
    /// Build the pools. Call once when the world is built; safe to call again,
    /// which rebuilds them against the new parent after a scene reload.
    /// </summary>
    /// <param name="parent">Node the pooled instances live under. Should not be a
    /// <see cref="Node3D"/> that moves — projectiles are positioned in world
    /// space and a moving parent would drag them along.</param>
    /// <param name="capacity">Hard ceiling on shots in flight.</param>
    public static void Initialize(Node parent, int capacity = 512)
    {
        Shutdown();
        _pool = new Pool<Projectile>(parent, static () => new Projectile(), capacity, prewarm: 64);
        ExplosionFx.Initialize(parent);
        Weapons.SignatureFx.Initialize(parent);
    }

    /// <summary>Drop every pooled instance. Call when a run ends.</summary>
    public static void Shutdown()
    {
        for (int i = 0; i < Live.Count; i++)
        {
            Projectile p = Live[i];
            if (GodotObject.IsInstanceValid(p)) p.Retire();
        }
        Live.Clear();
        _pool?.Clear();
        _pool = null;
        ExplosionFx.Shutdown();
        Weapons.SignatureFx.Shutdown();
    }

    /// <summary>
    /// Put a shot in the air. Returns null when the pool is at capacity, which
    /// callers should treat as "not this frame" rather than as an error.
    /// </summary>
    /// <param name="system">Who fired it, for hit reporting.</param>
    /// <param name="spec">Shape of the shot.</param>
    /// <param name="template">The hit this shot carries. Cloned per target, so
    /// one shot's crit roll applies to everything it touches.</param>
    public static Projectile? Spawn(WeaponSystem system, in ProjectileSpec spec, DamageInfo template)
    {
        Projectile? p = _pool?.Get();
        if (p is null) return null;

        p.Launch(system, spec, template);
        Live.Add(p);
        return p;
    }

    /// <summary>
    /// The visual half of a melee swing: a wedge that blooms and fades in front
    /// of the attacker. Damage has already been applied by the caller — this is
    /// only what the player sees.
    /// </summary>
    public static void SpawnSwipe(Vector3 origin, Vector3 direction, float reach, float halfAngleDegrees, Color tint)
    {
        Projectile? p = _pool?.Get();
        if (p is null) return;

        // Level, always: a sheet tilted up at a flying target swings through
        // the camera and whites out the screen.
        Vector3 flat = new(direction.X, 0f, direction.Z);
        if (flat.LengthSquared() > 1e-6f) direction = flat.Normalized();

        ProjectileSpec spec = new()
        {
            Mode = ProjectileMode.Cosmetic,
            Origin = origin,
            Direction = direction,
            Speed = 0f,
            Range = reach,
            Tint = tint,
            Lifetime = 0.16f,
        };

        // Chord of the arc: how wide the wedge has to be to look like it covers
        // the angle that actually got hit. Set before Launch, because Dress reads
        // it when it sizes the mesh.
        float width = MathF.Max(0.4f, 2f * reach * MathF.Sin(Mathf.DegToRad(Mathf.Clamp(halfAngleDegrees, 5f, 89f))));
        // Drawn capped and flat on the ground (Weapons/FxGuard): with area
        // tomes the hit reach grows past 10 m, and a sheet that size at knee
        // height read as a white floor laid over the whole screen.
        float shownReach = MathF.Min(reach, Weapons.FxGuard.MaxSheet);
        p._cosmeticScale = new Vector3(MathF.Min(width, Weapons.FxGuard.MaxSheet), 0.04f, shownReach);

        p.Launch(null, spec, null);
        // -Z is forward; on the ground under the muzzle.
        p._mesh.Position = new Vector3(0f, -(Weapons.FxGuard.MuzzleHeight - Weapons.FxGuard.GroundLift), -shownReach * 0.5f);
        Live.Add(p);
    }

    /// <summary>Advance every live shot and every explosion flash by one frame.</summary>
    public static void UpdateAll(float dt)
    {
        Weapons.FxGuard.Update();   // near-camera fade follows the zoom
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            Projectile p = Live[i];

            if (!GodotObject.IsInstanceValid(p))
            {
                SwapRemove(i);
                continue;
            }

            if (!p._retired)
            {
                p.Step(dt);
                if (!p._retired) p.KeepOutOfSight();
            }

            if (p._retired)
            {
                SwapRemove(i);
                _pool?.Release(p);
            }
        }

        ExplosionFx.UpdateAll(dt);
        Weapons.SignatureFx.UpdateAll(dt);   // signature anchors, kites, glints
    }

    // Order in the live list is meaningless, so the tail fills the hole. The
    // element moved into slot i was already visited this frame — the loop runs
    // backwards — so nothing is processed twice or skipped.
    private static void SwapRemove(int index)
    {
        int last = Live.Count - 1;
        Live[index] = Live[last];
        Live.RemoveAt(last);
    }

    // ---------------------------------------------------------------- launch

    private void Launch(WeaponSystem? system, in ProjectileSpec spec, DamageInfo? template)
    {
        _system = system;
        _template = template;
        _mode = spec.Mode;
        _tint = spec.Tint;

        _hitRadius = MathF.Max(0.15f, spec.HitRadius);
        _blastRadius = MathF.Max(0f, spec.BlastRadius);
        _pierceLeft = Math.Max(0, spec.Pierce);
        _pierced.Clear();
        _lock = null;
        _age = 0f;
        _retired = false;

        Vector3 dir = FlightDirection(spec.Direction, Vector3.Forward);
        float speed = MathF.Max(0f, spec.Speed);
        _velocity = dir * speed;

        _life = spec.Lifetime > 0f
            ? spec.Lifetime
            : Mathf.Clamp(spec.Range / MathF.Max(0.01f, speed), 0.05f, MaxLifetime);

        Place(spec.Origin, dir);
        Dress();
        ClearMotion();   // Combat/Weapons/Projectile.Motion.cs
    }

    /// <summary>
    /// Writes position and heading in one transform assignment.
    ///
    /// Built by hand rather than with <c>LookAt</c>: that needs the node to be
    /// inside the tree and throws when the target coincides with the origin,
    /// both of which are reachable states for a pooled projectile.
    /// </summary>
    private void Place(Vector3 position, Vector3 forward)
    {
        // Godot points -Z forward, so the basis' Z column is the reverse heading.
        // Normalised here rather than at the call sites: an un-normalised column
        // is a scale on the basis, which would silently resize the mesh by the
        // projectile's speed.
        Vector3 z = -FlightDirection(forward, Vector3.Forward);
        Vector3 x = Vector3.Up.Cross(z);

        Basis basis = x.LengthSquared() < 1e-6f
            ? Basis.Identity                       // heading was vertical; nothing to align
            : new Basis(x.Normalized(), z.Cross(x.Normalized()).Normalized(), z);

        GlobalTransform = new Transform3D(basis, position);
    }

    /// <summary>Picks the mesh, size and material for the mode. Every branch sets
    /// mesh, rotation and scale, because instances are recycled across modes and a
    /// value left over from the last life is a very confusing bug.</summary>
    private void Dress()
    {
        switch (_mode)
        {
            case ProjectileMode.Explosive:
                _mesh.Mesh = CombatVisuals.Orb;
                _mesh.RotationDegrees = Vector3.Zero;
                _mesh.Position = Vector3.Zero;
                _mesh.Scale = Vector3.One * Mathf.Clamp(_hitRadius, 0.22f, 0.7f);
                break;

            case ProjectileMode.Cosmetic:
                _mesh.Mesh = CombatVisuals.Slab;
                _mesh.RotationDegrees = Vector3.Zero;
                _mesh.Position = Vector3.Zero;
                _mesh.Scale = _cosmeticScale;
                break;

            default:
                _mesh.Mesh = CombatVisuals.Bolt;
                // The unit bolt's long axis is +Y; -90 about X lays it along -Z,
                // which is the direction Place() aimed the node at.
                _mesh.RotationDegrees = new Vector3(-90f, 0f, 0f);
                _mesh.Position = Vector3.Zero;
                // Deliberately smaller than the hitbox. Generous hitboxes feel
                // good; bolts the size of the hitbox look like beach balls.
                _mesh.Scale = Vector3.One * Mathf.Clamp(_hitRadius * 0.55f, 0.10f, 0.34f);
                break;
        }

        _mesh.MaterialOverride = CombatVisuals.Glow(_tint, additive: _mode == ProjectileMode.Cosmetic);
        _mesh.Transparency = Wavebreak.Meta.GraphicsSettings.FxFade(0f);
        Visible = true;
    }

    private void Retire()
    {
        if (_retired) return;
        _retired = true;

        Visible = false;
        _sightFaded = false;
        _system = null;
        _template = null;
        _lock = null;
        _pierced.Clear();
        ClearMotion();
    }

    /// <summary>Horizontal component, normalised, with a fallback for a vector
    /// that has none. Everything about aiming and hit testing happens on XZ.</summary>
    internal static Vector3 FlightDirection(Vector3 value, Vector3 fallback)
    {
        if (!Game.Arena.Current.Underwater && !Game.Arena.IsCity) value.Y = 0;
        return value.LengthSquared() > 1e-6f ? value.Normalized() : fallback;
    }

    internal static Vector3 FlatDirection(Vector3 v, Vector3 fallback)
    {
        v.Y = 0f;
        return v.LengthSquared() > 1e-6f ? v.Normalized() : fallback;
    }
}

