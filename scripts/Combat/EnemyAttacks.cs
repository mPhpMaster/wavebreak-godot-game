using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Combat;

/// <summary>
/// A flat marking on the floor: the telegraph language every enemy attack is
/// written in.
///
/// Three shapes cover the whole roster — a disc that fills from the centre
/// (where a blast will land), a band (a shockwave rolling outward) and a lane
/// that fills from its start (where a charge will run). One shared shader
/// draws all three; size, colour and fill are per-instance shader parameters,
/// so a hundred decals on screen are one material and zero per-frame
/// allocations, where a material per decal would be a hundred.
///
/// The fill is the important part. A telegraph that only appears and then
/// detonates tells the player *where*; one that fills tells them *when*, and
/// "when" is what makes a dodge a skill instead of a guess.
/// </summary>
public sealed partial class GroundDecal : MeshInstance3D, IPooled
{
    public enum Shape { Disc = 0, Band = 1, Lane = 2 }

    /// <summary>Lift off the floor, enough to beat depth fighting with the
    /// arena's own ring markings.</summary>
    private const float Lift = 0.05f;

    private static readonly StringName TintParam = "tint";
    private static readonly StringName FillParam = "fill";
    private static readonly StringName InnerParam = "inner";
    private static readonly StringName ShapeParam = "shape";

    private static PlaneMesh? _mesh;
    private static ShaderMaterial? _material;

    public bool IsPooled { get; set; } = true;

    public GroundDecal()
    {
        Name = "Decal";
        CastShadow = ShadowCastingSetting.Off;
        Mesh = _mesh ??= new PlaneMesh { Size = new Vector2(2f, 2f) };
        MaterialOverride = _material ??= new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        Visible = false;
    }

    /// <summary>A disc of <paramref name="radius"/> filled to
    /// <paramref name="fill"/> (0..1) of its radius.</summary>
    public void SetDisc(Vector3 centre, float radius, float fill, Color tint)
    {
        float r = MathF.Max(0.05f, radius);
        GlobalTransform = new Transform3D(Basis.Identity.Scaled(new Vector3(r, 1f, r)), centre + new Vector3(0f, Lift, 0f));
        SetParams(Shape.Disc, fill, 0f, tint);
    }

    /// <summary>A ring of outer radius <paramref name="radius"/> and the given
    /// band width — a shockwave's hit zone, drawn exactly.</summary>
    public void SetBand(Vector3 centre, float radius, float width, Color tint)
    {
        float r = MathF.Max(0.05f, radius);
        float inner = Mathf.Clamp((r - width) / r, 0f, 0.999f);
        GlobalTransform = new Transform3D(Basis.Identity.Scaled(new Vector3(r, 1f, r)), centre + new Vector3(0f, Lift + 0.01f, 0f));
        SetParams(Shape.Band, 1f, inner, tint);
    }

    /// <summary>A lane from <paramref name="start"/> along
    /// <paramref name="direction"/>, filled from the start.</summary>
    public void SetLane(Vector3 start, Vector3 direction, float length, float halfWidth, float fill, Color tint)
    {
        Vector3 dir = new(direction.X, 0f, direction.Z);
        dir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : Vector3.Forward;
        Vector3 right = Vector3.Up.Cross(dir).Normalized();
        float half = MathF.Max(0.1f, length * 0.5f);

        // Columns right/up/along: the plane's local +Z (UV.y = 1) is the far
        // end, so the shader can fill along UV.y.
        Basis basis = new(right * MathF.Max(0.1f, halfWidth), Vector3.Up, dir * half);
        GlobalTransform = new Transform3D(basis, start + dir * half + new Vector3(0f, Lift, 0f));
        SetParams(Shape.Lane, fill, 0f, tint);
    }

    private void SetParams(Shape shape, float fill, float inner, Color tint)
    {
        SetInstanceShaderParameter(TintParam, tint);
        SetInstanceShaderParameter(FillParam, Mathf.Clamp(fill, 0f, 1f));
        SetInstanceShaderParameter(InnerParam, inner);
        SetInstanceShaderParameter(ShapeParam, (float)(int)shape);
    }

    private const string ShaderCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, blend_mix;

instance uniform vec4 tint : source_color = vec4(1.0, 0.3, 0.2, 1.0);
instance uniform float fill = 0.0;
instance uniform float inner = 0.0;
instance uniform float shape = 0.0;

void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float a = 0.0;
    if (shape < 0.5) {
        float r = length(p);
        if (r > 1.0) discard;
        float rim = smoothstep(0.86, 0.96, r);
        float core = step(r, fill);
        a = max(rim * 0.95, 0.14 + core * 0.40);
        // A bright closing edge: the eye tracks the moving front, not the area.
        a += (1.0 - smoothstep(0.0, 0.05, abs(r - fill))) * 0.4 * step(0.01, fill);
    } else if (shape < 1.5) {
        float r = length(p);
        if (r > 1.0 || r < inner) discard;
        float t = (r - inner) / max(1.0 - inner, 0.001);
        a = 0.55 + 0.45 * smoothstep(0.2, 1.0, t);
    } else {
        vec2 q = abs(p);
        float rim = max(smoothstep(0.80, 0.94, q.x), smoothstep(0.95, 0.99, q.y));
        float core = step(UV.y, fill);
        a = max(rim * 0.95, 0.12 + core * 0.40);
    }
    ALBEDO = tint.rgb;
    ALPHA = clamp(a, 0.0, 1.0) * tint.a;
}
";
}

/// <summary>
/// One hostile shot. Deliberately slow, large, and the same colour whoever
/// fired it: an enemy projectile exists to be dodged, and the player should
/// never have to work out whether the glowing ball coming at them is theirs.
/// </summary>
public sealed partial class EnemyShot : Node3D, IPooled
{
    private static SphereMesh? _mesh;
    private static StandardMaterial3D? _material;

    internal Vector3 Velocity;
    internal float Radius;
    internal float Life;
    internal float Damage;
    internal string Source = "";

    public bool IsPooled { get; set; } = true;

    public EnemyShot()
    {
        Name = "EnemyShot";
        SetProcess(false);
        SetPhysicsProcess(false);
        Visible = false;

        _mesh ??= new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 6 };
        _material ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = EnemyAttacks.ShotColor,
        };
        AddChild(new MeshInstance3D
        {
            Name = "Visual",
            Mesh = _mesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }
}

/// <summary>
/// Everything an enemy throws at the player that outlives the swing: aimed
/// shots, delayed blasts and rolling shockwaves.
///
/// Built the same way as the player's <see cref="Projectile"/>: pooled nodes,
/// one update loop, distance tests instead of physics queries. The enemy
/// manager ticks it right after stepping the enemies, so a shot fired this
/// frame moves this frame and nothing depends on scene-tree order.
///
/// Every hit goes through <see cref="IDamageable.TakeDamage"/> on the player
/// with a fresh <see cref="DamageInfo"/>, exactly like a contact swing. That
/// is the whole reason this does not keep its own rules about damage: dash
/// i-frames, the post-hit window, evasion, armour and shield all apply to a
/// boss slam because they live in PlayerHealth and nowhere else.
///
/// The target is passed in each tick rather than looked up, so the content
/// audit can point every attack at a dummy and prove it connects.
/// </summary>
public static class EnemyAttacks
{
    /// <summary>Hostile shot colour. Hot pink: nothing the player owns uses it.</summary>
    public static readonly Color ShotColor = new(1.00f, 0.22f, 0.55f);

    /// <summary>Default telegraph colour for blasts.</summary>
    public static readonly Color DangerColor = new(1.00f, 0.32f, 0.18f, 0.85f);

    /// <summary>Shockwave colour — a different hue from blasts on purpose,
    /// because the answer is different: you jump a wave, you leave a disc.</summary>
    public static readonly Color WaveColor = new(1.00f, 0.80f, 0.25f, 0.9f);

    /// <summary>Player body as the attacks see it: a vertical segment with a
    /// radius. Matches Player.BodyRadius/BodyHeight without compiling against it.</summary>
    private const float PlayerRadius = 0.4f;
    private const float PlayerLow = 0.4f, PlayerHigh = 1.4f;

    /// <summary>Feet this far above a shockwave's floor clear it. A first jump
    /// peaks well above this, so the timing window is generous; standing,
    /// sliding or walking never clears it.</summary>
    public const float WaveClearance = 0.55f;

    private const float FlashTime = 0.18f;

    private enum Kind { Blast, Wave, Flash }

    private sealed class Hazard
    {
        public Kind Kind;
        public Vector3 Centre;
        public float Radius, Width, Speed, Delay, Age, Damage;
        public bool Resolved;
        public Color Tint;
        public string Source = "";
        public GroundDecal? Decal;
    }

    private static Pool<EnemyShot>? _shots;
    private static Pool<GroundDecal>? _decals;
    private static readonly List<EnemyShot> LiveShots = new(128);
    private static readonly List<Hazard> LiveHazards = new(32);
    private static readonly Stack<Hazard> SpareHazards = new();

    /// <summary>Shots fired this process, for logs.</summary>
    public static int ShotsFired { get; private set; }

    /// <summary>Attacks that reached the target this process (before i-frames
    /// decided whether they hurt), for logs and the audit.</summary>
    public static int Connects { get; private set; }

    public static int ActiveShots => LiveShots.Count;
    public static int ActiveHazards => LiveHazards.Count;

    /// <summary>Build the pools under <paramref name="parent"/>, which must not
    /// move: everything here is placed in world space.</summary>
    public static void Initialize(Node parent)
    {
        Clear();
        _shots = new Pool<EnemyShot>(parent, static () => new EnemyShot(), capacity: 320, prewarm: 24);
        _decals = new Pool<GroundDecal>(parent, static () => new GroundDecal(), capacity: 160, prewarm: 16);
    }

    /// <summary>Retire everything in flight. Pools are kept.</summary>
    public static void Clear()
    {
        for (int i = 0; i < LiveShots.Count; i++)
            if (GodotObject.IsInstanceValid(LiveShots[i])) _shots?.Release(LiveShots[i]);
        LiveShots.Clear();

        for (int i = 0; i < LiveHazards.Count; i++) RetireHazard(LiveHazards[i]);
        LiveHazards.Clear();
        CuteHazards.Clear();
    }

    // ------------------------------------------------------------ spawning

    /// <summary>Fire one shot along <paramref name="direction"/> (flattened).
    /// Returns false at pool capacity — a volley that is one shot short is a far
    /// better failure than a frame-time spike.</summary>
    public static bool Shoot(Vector3 origin, Vector3 direction, float speed, float damage, float radius, float range, string source)
    {
        EnemyShot? s = _shots?.Get();
        if (s is null) return false;

        Vector3 dir = new(direction.X, 0f, direction.Z);
        dir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : Vector3.Forward;

        s.Velocity = dir * MathF.Max(0.5f, speed);
        s.Radius = MathF.Max(0.1f, radius);
        s.Life = Mathf.Clamp(range / MathF.Max(0.5f, speed), 0.2f, 8f);
        s.Damage = damage;
        s.Source = source;
        s.GlobalPosition = origin;
        s.Scale = Vector3.One * s.Radius;
        LiveShots.Add(s);
        ShotsFired++;
        return true;
    }

    /// <summary>A disc that fills for <paramref name="delay"/> seconds and then
    /// hits whatever is inside it.</summary>
    public static void Blast(Vector3 centre, float radius, float delay, float damage, Color tint, string source)
    {
        Hazard h = NewHazard(Kind.Blast, centre, source, tint);
        h.Radius = radius;
        h.Delay = MathF.Max(0f, delay);
        h.Damage = damage;
        LiveHazards.Add(h);   // a zero delay resolves on the next tick
    }

    /// <summary>A ring rolling outward along the floor from
    /// <paramref name="centre"/> to <paramref name="reach"/>, after
    /// <paramref name="delay"/>. Only a player in the air clears it.</summary>
    public static void Shockwave(Vector3 centre, float reach, float speed, float width, float delay, float damage, string source)
    {
        Hazard h = NewHazard(Kind.Wave, centre, source, WaveColor);
        h.Radius = reach;
        h.Speed = MathF.Max(1f, speed);
        h.Width = MathF.Max(0.3f, width);
        h.Age = -MathF.Max(0f, delay);
        h.Damage = damage;
        LiveHazards.Add(h);
    }

    /// <summary>Purely visual pop at a detonation, so a hit and a miss both
    /// show the area that was live.</summary>
    public static void Flash(Vector3 centre, float radius, Color tint)
    {
        Hazard h = NewHazard(Kind.Flash, centre, "", tint);
        h.Radius = radius;
        LiveHazards.Add(h);
    }

    /// <summary>A decal for an enemy to drive itself (wind-up lanes, leap
    /// markers). Must be handed back with <see cref="ReleaseDecal"/>.</summary>
    public static GroundDecal? AcquireDecal() => _decals?.Get();

    public static void ReleaseDecal(GroundDecal? decal)
    {
        if (decal is not null && GodotObject.IsInstanceValid(decal)) _decals?.Release(decal);
    }

    // -------------------------------------------------------------- update

    /// <summary>Advance everything one frame against a target whose feet are at
    /// <paramref name="targetFeet"/>. A null target still moves and expires
    /// everything; it just cannot be hit.</summary>
    public static void Tick(float dt, Vector3 targetFeet, IDamageable? target)
    {
        bool canHit = target is not null && target.IsAlive;

        for (int i = LiveShots.Count - 1; i >= 0; i--)
        {
            EnemyShot s = LiveShots[i];
            if (!GodotObject.IsInstanceValid(s)) { SwapRemove(LiveShots, i); continue; }

            s.Life -= dt;
            Vector3 p = s.GlobalPosition + s.Velocity * dt;
            s.GlobalPosition = p;

            bool done = s.Life <= 0f;
            if (!done && canHit && TouchesBody(p, s.Radius, targetFeet))
            {
                Hit(target!, s.Damage, s.Velocity, 0.6f, s.Source);
                done = true;
            }

            if (done)
            {
                SwapRemove(LiveShots, i);
                _shots?.Release(s);
            }
        }

        for (int i = LiveHazards.Count - 1; i >= 0; i--)
        {
            Hazard h = LiveHazards[i];
            h.Age += dt;
            if (StepHazard(h, targetFeet, canHit ? target : null)) continue;
            SwapRemove(LiveHazards, i);
            RetireHazard(h);
        }

        CuteHazards.Tick(dt, targetFeet, target);
    }

    /// <summary>Returns false when the hazard is finished.</summary>
    private static bool StepHazard(Hazard h, Vector3 feet, IDamageable? target)
    {
        switch (h.Kind)
        {
            case Kind.Blast:
                if (!h.Resolved)
                {
                    float t = h.Delay > 0f ? h.Age / h.Delay : 1f;
                    h.Decal?.SetDisc(h.Centre, h.Radius, t, h.Tint);
                    if (t >= 1f) Detonate(h, feet, target);
                    return true;
                }
                return FadeFlash(h);

            case Kind.Flash:
                return FadeFlash(h);

            case Kind.Wave:
            {
                if (h.Age < 0f)
                {
                    // Pending second ring: show where it will start, faintly.
                    h.Decal?.SetBand(h.Centre, 0.6f, 0.6f, h.Tint with { A = 0.35f });
                    return true;
                }
                float r = h.Age * h.Speed;
                if (r > h.Radius) return false;
                float fade = 1f - 0.6f * (r / h.Radius);
                h.Decal?.SetBand(h.Centre, MathF.Max(h.Width, r), h.Width, h.Tint with { A = h.Tint.A * fade });

                if (!h.Resolved && target is not null)
                {
                    float d = Flat(feet - h.Centre).Length();
                    bool inBand = d <= r + PlayerRadius && d >= r - h.Width - PlayerRadius;
                    bool grounded = feet.Y < h.Centre.Y + WaveClearance;
                    if (inBand && grounded)
                    {
                        h.Resolved = true;   // one hit per ring, however long you stand in it
                        Hit(target, h.Damage, Flat(feet - h.Centre), 1.4f, h.Source);
                    }
                }
                return true;
            }
        }
        return false;
    }

    private static void Detonate(Hazard h, Vector3 feet, IDamageable? target)
    {
        h.Resolved = true;
        h.Age = 0f;
        if (target is not null)
        {
            Vector3 d = Flat(feet - h.Centre);
            // A blast is a column, not a disc: a jump does not clear it, which
            // is what separates "leave the circle" from "jump the wave".
            if (d.Length() <= h.Radius + PlayerRadius && feet.Y < h.Centre.Y + 3f)
                Hit(target, h.Damage, d, 1.2f, h.Source);
        }
    }

    private static bool FadeFlash(Hazard h)
    {
        float t = h.Age / FlashTime;
        if (t >= 1f) return false;
        h.Decal?.SetDisc(h.Centre, h.Radius * (1f + 0.12f * t), 1f, new Color(1f, 0.95f, 0.8f, 0.9f * (1f - t)));
        return true;
    }

    /// <summary>Land one enemy attack on <paramref name="target"/> through its own
    /// damage path. Public for attacks resolved by the enemy itself (a charge
    /// body-check), so they are counted and built the same way.</summary>
    public static void Hit(IDamageable target, float damage, Vector3 direction, float knockback, string source)
    {
        Connects++;
        ContentAudit.NoteConnect(source);

        // Fresh per hit for the same reason as Enemy.Strike: PlayerDamaged keeps
        // the reference. Hits are capped by i-frames, so this is a trickle.
        DamageInfo info = new()
        {
            Amount = MathF.Max(0f, damage),
            Element = Element.Physical,
            CanCrit = false,
            Knockback = knockback,
            Direction = direction,
            ProcCoefficient = 1f,
            Source = source,
        };
        target.TakeDamage(info);
    }

    /// <summary>Sphere against the player's vertical segment.</summary>
    private static bool TouchesBody(Vector3 p, float radius, Vector3 feet)
    {
        float y = Mathf.Clamp(p.Y, feet.Y + PlayerLow, feet.Y + PlayerHigh);
        Vector3 d = p - new Vector3(feet.X, y, feet.Z);
        float reach = radius + PlayerRadius;
        return d.LengthSquared() <= reach * reach;
    }

    // ---------------------------------------------------------- bookkeeping

    private static Hazard NewHazard(Kind kind, Vector3 centre, string source, Color tint)
    {
        Hazard h = SpareHazards.Count > 0 ? SpareHazards.Pop() : new Hazard();
        h.Kind = kind;
        h.Centre = centre;
        h.Source = source;
        h.Tint = tint;
        h.Age = 0f;
        h.Delay = 0f;
        h.Speed = 0f;
        h.Width = 0f;
        h.Damage = 0f;
        h.Radius = 1f;
        h.Resolved = false;
        h.Decal = _decals?.Get();
        return h;
    }

    private static void RetireHazard(Hazard h)
    {
        ReleaseDecal(h.Decal);
        h.Decal = null;
        SpareHazards.Push(h);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    private static void SwapRemove<T>(List<T> list, int index)
    {
        int last = list.Count - 1;
        list[index] = list[last];
        list.RemoveAt(last);
    }
}
