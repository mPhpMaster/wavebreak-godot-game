using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Combat;

/// <summary>How a spawned projectile behaves once it is in the air.</summary>
public enum ProjectileMode
{
    /// <summary>Flies in a straight line, pierces, expires by range.</summary>
    Straight,

    /// <summary>Steers toward the nearest live target at a bounded turn rate.</summary>
    Homing,

    /// <summary>Detonates on first contact (or at end of life) for area damage.</summary>
    Explosive,

    /// <summary>Pure decoration: no movement, no collision, fades and dies.
    /// Melee swings damage instantly and use one of these to show the arc.</summary>
    Cosmetic,
}

/// <summary>
/// Everything a weapon has to say about one shot.
///
/// Passed as a struct rather than a dozen positional arguments because the five
/// attack shapes fill in overlapping-but-different subsets of it, and a call site
/// with nine floats in a row is where a blast radius quietly ends up in the pierce
/// count.
/// </summary>
public struct ProjectileSpec
{
    public ProjectileMode Mode;
    public Vector3 Origin;
    public Vector3 Direction;

    /// <summary>Metres per second.</summary>
    public float Speed;

    /// <summary>Metres before the shot gives up. Converted to a lifetime.</summary>
    public float Range;

    /// <summary>Radius of the shot's own hitbox.</summary>
    public float HitRadius;

    /// <summary>Area damage radius for <see cref="ProjectileMode.Explosive"/>.</summary>
    public float BlastRadius;

    /// <summary>Extra targets the shot passes through. 0 dies on first hit.</summary>
    public int Pierce;

    public Color Tint;

    /// <summary>Explicit lifetime in seconds. 0 derives it from range and speed.</summary>
    public float Lifetime;
}

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
        p._cosmeticScale = new Vector3(width, 0.18f, reach);

        p.Launch(null, spec, null);
        p._mesh.Position = new Vector3(0f, 0f, -reach * 0.5f);   // -Z is forward
        Live.Add(p);
    }

    /// <summary>Advance every live shot and every explosion flash by one frame.</summary>
    public static void UpdateAll(float dt)
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            Projectile p = Live[i];

            if (!GodotObject.IsInstanceValid(p))
            {
                SwapRemove(i);
                continue;
            }

            if (!p._retired) p.Step(dt);

            if (p._retired)
            {
                SwapRemove(i);
                _pool?.Release(p);
            }
        }

        ExplosionFx.UpdateAll(dt);
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

        Vector3 dir = FlatDirection(spec.Direction, Vector3.Forward);
        float speed = MathF.Max(0f, spec.Speed);
        _velocity = dir * speed;

        _life = spec.Lifetime > 0f
            ? spec.Lifetime
            : Mathf.Clamp(spec.Range / MathF.Max(0.01f, speed), 0.05f, MaxLifetime);

        Place(spec.Origin, dir);
        Dress();
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
        Vector3 z = -FlatDirection(forward, Vector3.Forward);
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
        _mesh.Transparency = 0f;
        Visible = true;
    }

    // ------------------------------------------------------------------ step

    private void Step(float dt)
    {
        _age += dt;

        if (_mode == ProjectileMode.Cosmetic)
        {
            if (_age >= _life) { Retire(); return; }
            StepCosmetic();
            return;
        }

        if (_age >= _life)
        {
            // A grenade that fizzles out mid-air reads as a bug. Expiry detonates.
            if (_mode == ProjectileMode.Explosive) Detonate(GlobalPosition);
            else Retire();
            return;
        }

        Vector3 next = GlobalPosition + _velocity * dt;

        if (_mode == ProjectileMode.Homing)
        {
            Steer(dt);
            Place(next, _velocity);      // only a seeker's heading changes in flight
        }
        else
        {
            GlobalPosition = next;
        }

        Contact(next);
    }

    private void StepCosmetic()
    {
        float t = Mathf.Clamp(_age / _life, 0f, 1f);
        _mesh.Scale = _cosmeticScale * (0.7f + 0.3f * t);
        _mesh.Transparency = t * t;      // hangs bright, then leaves quickly
    }

    private void Contact(Vector3 at)
    {
        int count = Targeting.Overlap(at, _hitRadius, ContactBuffer);

        for (int i = 0; i < count; i++)
        {
            IDamageable target = ContactBuffer[i];
            if (_pierced.Contains(target)) continue;

            if (_mode == ProjectileMode.Explosive)
            {
                Detonate(at);
                return;
            }

            _pierced.Add(target);
            DamageInfo info = ApplyHit(target, at, 1f);

            // A corpse a pierce shot happened to fly through does not spend a
            // pierce charge. The target reports that by nullifying the hit.
            if (info.Nullified) continue;

            if (_pierceLeft-- <= 0)
            {
                Retire();
                return;
            }
        }
    }

    private void Steer(float dt)
    {
        if (_lock is null || !_lock.IsAlive)
            _lock = Targeting.Nearest(GlobalPosition, HomingSeekRange);

        if (_lock is null || !_lock.IsAlive) return;

        Vector3 desired = FlatDirection(_lock.Position - GlobalPosition, Vector3.Zero);
        if (desired.LengthSquared() < 1e-6f) return;

        float speed = _velocity.Length();
        if (speed < 0.01f) return;

        // Turning on the flat plane by angle rather than by Slerp: Slerp is
        // undefined for exactly opposing vectors, which is precisely the case a
        // seeker that overshot its target hits every time.
        float current = MathF.Atan2(_velocity.X, _velocity.Z);
        float target = MathF.Atan2(desired.X, desired.Z);
        float delta = Mathf.Wrap(target - current, -Mathf.Pi, Mathf.Pi);
        float turn = Mathf.Clamp(delta, -HomingTurnRate * dt, HomingTurnRate * dt);

        float heading = current + turn;
        _velocity = new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading)) * speed;
    }

    private void Detonate(Vector3 at)
    {
        float blast = _blastRadius > 0f ? _blastRadius : _hitRadius;
        int count = Targeting.Overlap(at, blast, BlastBuffer);

        for (int i = 0; i < count; i++)
        {
            IDamageable target = BlastBuffer[i];

            // Full damage at the centre, half at the rim. A flat blast makes
            // where you put it irrelevant; a steep one makes every explosion feel
            // like it missed.
            Vector3 offset = target.Position - at;
            offset.Y = 0f;
            float distance = offset.Length();
            float falloff = blast <= 0.001f ? 1f : Mathf.Lerp(1f, 0.5f, Mathf.Clamp(distance / blast, 0f, 1f));

            ApplyHit(target, at, falloff);
        }

        ExplosionFx.Play(at, blast, _tint);
        Retire();
    }

    private DamageInfo ApplyHit(IDamageable target, Vector3 from, float scale)
    {
        DamageInfo info = _template!.Clone();
        if (scale != 1f) info.Amount *= scale;

        Vector3 push = FlatDirection(target.Position - from, Vector3.Zero);
        info.Direction = push.LengthSquared() > 1e-6f
            ? push.Normalized()
            : FlatDirection(_velocity, Vector3.Forward);

        target.TakeDamage(info);
        _system?.ReportHit(target, info);
        return info;
    }

    private void Retire()
    {
        if (_retired) return;
        _retired = true;

        Visible = false;
        _system = null;
        _template = null;
        _lock = null;
        _pierced.Clear();
    }

    /// <summary>Horizontal component, normalised, with a fallback for a vector
    /// that has none. Everything about aiming and hit testing happens on XZ.</summary>
    internal static Vector3 FlatDirection(Vector3 v, Vector3 fallback)
    {
        v.Y = 0f;
        return v.LengthSquared() > 1e-6f ? v.Normalized() : fallback;
    }
}

/// <summary>
/// The flash an explosion leaves behind: an expanding shell and a brief light.
///
/// Separate from <see cref="Projectile"/> and hard-capped, because this is the
/// one visual in the game that carries an <see cref="OmniLight3D"/>. Lights are
/// the fastest way to melt a forward+ renderer, so the pool ceiling is the cap on
/// how many can exist — reaching it drops the flash rather than the frame rate.
/// </summary>
internal sealed partial class ExplosionFx : Node3D, IPooled
{
    private const float Duration = 0.32f;
    private const int MaxConcurrent = 16;

    private static Pool<ExplosionFx>? _pool;
    private static readonly List<ExplosionFx> Live = new(MaxConcurrent);

    private MeshInstance3D _mesh = null!;
    private OmniLight3D _light = null!;
    private float _age;
    private float _radius = 1f;
    private bool _retired = true;

    public bool IsPooled { get; set; } = true;

    public ExplosionFx()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
        Visible = false;

        _mesh = new MeshInstance3D
        {
            Name = "Shell",
            Mesh = CombatVisuals.Orb,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_mesh);

        _light = new OmniLight3D
        {
            Name = "Flash",
            ShadowEnabled = false,
            OmniAttenuation = 1.6f,
        };
        AddChild(_light);
    }

    public static void Initialize(Node parent)
        => _pool = new Pool<ExplosionFx>(parent, static () => new ExplosionFx(), MaxConcurrent, prewarm: 4);

    public static void Shutdown()
    {
        Live.Clear();
        _pool?.Clear();
        _pool = null;
    }

    public static void Play(Vector3 position, float radius, Color tint)
    {
        ExplosionFx? fx = _pool?.Get();
        if (fx is null) return;      // at the light budget: skip the flash, keep the frame

        fx._age = 0f;
        fx._radius = MathF.Max(0.3f, radius);
        fx._retired = false;

        fx.GlobalTransform = new Transform3D(Basis.Identity, position);
        fx._mesh.MaterialOverride = CombatVisuals.Glow(tint, additive: true);
        fx._mesh.Transparency = 0f;
        fx._mesh.Scale = Vector3.One * (fx._radius * 0.35f);

        fx._light.LightColor = tint;
        fx._light.OmniRange = fx._radius * 2.6f;
        fx._light.LightEnergy = 4f;
        fx.Visible = true;

        Live.Add(fx);
    }

    public static void UpdateAll(float dt)
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            ExplosionFx fx = Live[i];

            if (GodotObject.IsInstanceValid(fx) && !fx._retired)
            {
                fx._age += dt;
                if (fx._age < Duration)
                {
                    float t = fx._age / Duration;
                    // Fast out, slow stop: the shell covers most of its radius in
                    // the first third, which is what reads as a detonation rather
                    // than an inflating balloon.
                    float eased = 1f - (1f - t) * (1f - t);
                    fx._mesh.Scale = Vector3.One * (fx._radius * (0.35f + 0.65f * eased));
                    fx._mesh.Transparency = t;
                    fx._light.LightEnergy = 4f * (1f - t);
                    continue;
                }
                fx._retired = true;
                fx.Visible = false;
            }

            int last = Live.Count - 1;
            Live[i] = Live[last];
            Live.RemoveAt(last);

            if (GodotObject.IsInstanceValid(fx)) _pool?.Release(fx);
        }
    }
}

/// <summary>
/// Shared geometry and materials for everything combat draws.
///
/// Every bolt of a given weapon is the same shape in the same colour, so building
/// a mesh or material per instance would allocate hundreds of copies of identical
/// data and give the renderer nothing to batch. Meshes are unit-sized and scaled
/// by the instance; materials are cached by colour.
/// </summary>
internal static class CombatVisuals
{
    private static readonly Dictionary<(Color Color, bool Additive), StandardMaterial3D> Materials = new();

    /// <summary>Unit bolt: radius 1, length 3, long axis on +Y.</summary>
    public static readonly CapsuleMesh Bolt = new()
    {
        Radius = 1f,
        Height = 3f,
        RadialSegments = 6,
        Rings = 2,
    };

    /// <summary>Unit sphere, radius 1.</summary>
    public static readonly SphereMesh Orb = new()
    {
        Radius = 1f,
        Height = 2f,
        RadialSegments = 8,
        Rings = 4,
    };

    /// <summary>Unit cube. Scaled into slabs and wedges.</summary>
    public static readonly BoxMesh Slab = new() { Size = Vector3.One };

    /// <summary>Flat unit disc, radius 1. The aura footprint.</summary>
    public static readonly CylinderMesh Disc = new()
    {
        TopRadius = 1f,
        BottomRadius = 1f,
        Height = 0.06f,
        RadialSegments = 28,
        Rings = 1,
    };

    /// <summary>
    /// A self-lit material in the given colour.
    ///
    /// Unshaded on purpose: a projectile has to be readable against whatever it
    /// happens to be flying over, and the alternative — relying on scene lighting
    /// to make a bullet visible — fails exactly when the screen is busiest. The
    /// colour is pushed past 1 so it blooms rather than sitting flat.
    /// </summary>
    /// <param name="color">Base colour. Alpha below 1 gives a translucent field.</param>
    /// <param name="additive">Add rather than blend. Right for auras and flashes,
    /// where overlapping copies should build up instead of hiding each other.</param>
    public static StandardMaterial3D Glow(Color color, bool additive)
    {
        (Color color, bool additive) key = (color, additive);
        if (Materials.TryGetValue(key, out StandardMaterial3D? cached) && GodotObject.IsInstanceValid(cached))
            return cached;

        StandardMaterial3D material = new()
        {
            AlbedoColor = new Color(color.R * 1.35f, color.G * 1.35f, color.B * 1.35f, color.A),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            // Alpha even when opaque: GeometryInstance3D.Transparency, which is
            // how everything here fades, needs a blendable material to write to.
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        if (additive) material.BlendMode = BaseMaterial3D.BlendModeEnum.Add;

        Materials[key] = material;
        return material;
    }
}
