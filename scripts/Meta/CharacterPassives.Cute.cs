using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;
using Wavebreak.Visuals;

namespace Wavebreak.Meta;

/// <summary>
/// Passives of the cute crew (<see cref="Characters"/>, Cute partial). Same
/// habits as the others: the run is heard through the bus, stats go through
/// <see cref="SetMod"/>, and nothing here draws from the run's seeded stream.
/// The few visuals they need (turrets, bubbles, rings, a lantern glow) are
/// built here from primitives so a passive never depends on an asset.
/// </summary>
public sealed partial class CharacterPassives
{
    // ---- Pocket Turret (Pip)
    private const float TurretEvery = 10f;
    private const float TurretLife = 8f;
    private const float TurretFireEvery = 0.5f;
    private const float TurretRange = 12f;
    private const float TurretBase = 10f;
    private const float TurretPerLevel = 1.5f;
    private const int TurretMax = 3;

    // ---- Night Light (Lumi)
    private const float GlowRadius = 4f;
    private const float GlowEvery = 0.5f;
    private const float GlowBase = 3f;
    private const float GlowPerLevel = 0.6f;

    // ---- Bubble Charm (Tilly)
    private const int BubbleMax = 3;
    private const float BubbleRegrow = 9f;
    private const float BubbleSafety = 1.5f;
    private const float BubbleChillRadius = 5f;

    // ---- Belly Slide (Pebble)
    private const float SlideRadius = 4.5f;
    private const float SlideSpeed = 0.20f;
    private const float SlideSeconds = 2f;

    // ---- Coral Bloom (Rosie)
    private const float BloomEvery = 7f;
    private const float BloomHeal = 0.03f;
    private const float BloomRadius = 5f;
    private const float BloomPoisonBase = 4f;
    private const float BloomPoisonPerLevel = 0.8f;
    private const float BloomPoisonSeconds = 4f;

    // ---- Nine Lives (Whiskers)
    private const int Lives = 9;
    private const float LivesBelow = 0.30f;
    private const float LivesHeal = 0.20f;
    private const float LivesSafety = 2.5f;
    private const float LivesCooldown = 20f;

    private static readonly Color TurretColor = new(1.00f, 0.80f, 0.45f);
    private static readonly Color GlowColor = new(1.00f, 0.82f, 0.45f);
    private static readonly Color BubbleColor = new(0.80f, 0.72f, 1.00f);
    private static readonly Color FrostColor = new(0.65f, 0.88f, 1.00f);
    private static readonly Color BloomColor = new(1.00f, 0.55f, 0.70f);
    private static readonly Color MeowColor = new(1.00f, 0.75f, 0.40f);

    private sealed class Turret
    {
        public required Node3D Node;
        public float Life;
        public float Fire;
    }

    private readonly List<Turret> _turrets = new();
    private float _turretTimer = 2f;
    private float _glowTimer;
    private OmniLight3D? _glow;
    private int _bubbles = 1;
    private float _bubbleTimer;
    private readonly List<Node3D> _bubbleNodes = new();
    private float _orbitClock;
    private float _slideLeft;
    private float _bloomTimer = 3f;
    private int _livesLeft = Lives;
    private float _livesCooldown;

    private bool IsCute => _kind is PassiveKind.PocketTurret or PassiveKind.NightLight or PassiveKind.BubbleCharm
        or PassiveKind.BellySlide or PassiveKind.CoralBloom or PassiveKind.NineLives;

    private void ReadyCute()
    {
        if (!IsCute) return;
        EventBus.Subscribe<PlayerDamaged>(OnCuteDamaged);
        EventBus.Subscribe<PlayerMoved>(OnCuteMoved);
    }

    private void ExitCute()
    {
        EventBus.Unsubscribe<PlayerDamaged>(OnCuteDamaged);
        EventBus.Unsubscribe<PlayerMoved>(OnCuteMoved);
        foreach (Turret t in _turrets) if (IsInstanceValid(t.Node)) t.Node.QueueFree();
        _turrets.Clear();
        foreach (Node3D b in _bubbleNodes) if (IsInstanceValid(b)) b.QueueFree();
        _bubbleNodes.Clear();
        if (_glow is not null && IsInstanceValid(_glow)) _glow.QueueFree();
    }

    private int RunLevel => _main.Inventory?.Level ?? 1;

    private void TickCute(Player p, float dt, bool alive)
    {
        switch (_kind)
        {
            case PassiveKind.PocketTurret: TickTurrets(p, dt, alive); break;
            case PassiveKind.NightLight: TickGlow(p, dt, alive); break;
            case PassiveKind.BubbleCharm: TickBubbles(p, dt, alive); break;
            case PassiveKind.BellySlide:
                if (_slideLeft > 0f)
                {
                    _slideLeft -= dt;
                    if (_slideLeft <= 0f) SetMod(p, Stat.MoveSpeed, ModifierOp.Additive, 0f);
                }
                break;
            case PassiveKind.CoralBloom:
                if (!alive) break;
                _bloomTimer -= dt;
                if (_bloomTimer <= 0f) { _bloomTimer = BloomEvery; Bloom(p); }
                break;
            case PassiveKind.NineLives:
                if (_livesCooldown > 0f) _livesCooldown -= dt;
                break;
        }
    }

    // ------------------------------------------------------------- turrets

    private void TickTurrets(Player p, float dt, bool alive)
    {
        if (alive)
        {
            _turretTimer -= dt;
            if (_turretTimer <= 0f)
            {
                _turretTimer = TurretEvery;
                PlaceTurret(p);
            }
        }

        for (int i = _turrets.Count - 1; i >= 0; i--)
        {
            Turret t = _turrets[i];
            if (!IsInstanceValid(t.Node)) { _turrets.RemoveAt(i); continue; }
            t.Life -= dt;
            if (t.Life <= 0f)
            {
                Pop(t.Node.GlobalPosition + Vector3.Up * 0.5f, TurretColor, 0.8f);
                t.Node.QueueFree();
                _turrets.RemoveAt(i);
                continue;
            }
            // A little bob so it reads as alive.
            t.Node.Scale = Vector3.One * (1f + 0.04f * Mathf.Sin(t.Life * 9f));
            t.Fire -= dt;
            if (t.Fire > 0f) continue;
            t.Fire = TurretFireEvery;
            TurretShoot(p, t);
        }
    }

    private void PlaceTurret(Player p)
    {
        if (_turrets.Count >= TurretMax)
        {
            Turret oldest = _turrets[0];
            if (IsInstanceValid(oldest.Node)) oldest.Node.QueueFree();
            _turrets.RemoveAt(0);
        }
        var node = new Node3D { Name = "PocketTurret" };
        node.AddChild(new MeshInstance3D
        {
            Mesh = TurretMesh(),
            MaterialOverride = ToonMaterials.CharacterNoOutline,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _main.AddChild(node);
        Vector3 at = p.GlobalPosition;
        node.GlobalPosition = new Vector3(at.X, at.Y, at.Z);
        _turrets.Add(new Turret { Node = node, Life = TurretLife * Mathf.Max(0.5f, p.Stats.Get(Stat.Duration)), Fire = 0.3f });
        EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.2f, "TURRET!", TurretColor, 0.9f));
    }

    private void TurretShoot(Player p, Turret t)
    {
        if (EnemyManager.Instance is not { } em) return;
        Vector3 from = t.Node.GlobalPosition + Vector3.Up * 0.55f;
        Enemy? best = null;
        float bestD = TurretRange * TurretRange;
        foreach (Enemy e in em.AliveList)
        {
            if (!IsInstanceValid(e) || !e.IsAlive) continue;
            float d = (e.GlobalPosition - from).LengthSquared();
            if (d < bestD) { bestD = d; best = e; }
        }
        if (best is null) return;

        Vector3 aim = best.AimPoint;
        Vector3 flat = (aim - from) with { Y = 0f };
        if (flat.LengthSquared() > 0.01f) t.Node.Rotation = new Vector3(0f, Mathf.Atan2(-flat.X, -flat.Z), 0f);

        var info = new DamageInfo
        {
            Amount = (TurretBase + TurretPerLevel * RunLevel) * p.Stats.GetMultiplier(Stat.Damage),
            Element = Element.Physical,
            CanCrit = false,
            Knockback = 0.4f,
            Direction = flat.LengthSquared() > 0.01f ? flat.Normalized() : Vector3.Forward,
            ProcCoefficient = 0f,
            Source = "turret",
        };
        best.TakeDamage(info);
        Shot(from, aim);
        if (info.Nullified || info.Amount <= 0f) return;
        EventBus.Publish(new EnemyDamaged(best, info));
        EventBus.Publish(new PopupRequested(aim + Vector3.Up * 0.4f, Mathf.RoundToInt(info.Amount).ToString(), TurretColor, 0.7f));
    }

    private static Mesh? _turretMesh;

    /// <summary>A tiny tin turret: a driftwood stump, a round teal body with a
    /// face, and a stubby barrel pointing -Z. Built once.</summary>
    private static Mesh TurretMesh()
    {
        if (_turretMesh is not null && IsInstanceValid(_turretMesh)) return _turretMesh;
        var k = new MeshKit();
        Color wood = new(0.62f, 0.44f, 0.30f);
        Color teal = new(0.45f, 0.85f, 0.80f);
        Color cream = new(1.00f, 0.95f, 0.85f);
        k.Cylinder(new Transform3D(Basis.Identity, Vector3.Zero), 0.30f, 0.24f, 0.22f, wood, 0f, 10);
        k.Sphere(new Vector3(0f, 0.50f, 0f), 0.30f, teal, 0f, 12, 8);
        k.Tube(new Vector3(0f, 0.52f, -0.20f), new Vector3(0f, 0.55f, -0.50f), 0.09f, 0.08f, cream, 0f, 8);
        k.Sphere(new Vector3(0.11f, 0.60f, -0.25f), 0.045f, new Color(0.08f, 0.08f, 0.10f));
        k.Sphere(new Vector3(-0.11f, 0.60f, -0.25f), 0.045f, new Color(0.08f, 0.08f, 0.10f));
        k.Sphere(new Vector3(0.17f, 0.52f, -0.22f), 0.04f, new Color(1.00f, 0.60f, 0.65f));
        k.Sphere(new Vector3(-0.17f, 0.52f, -0.22f), 0.04f, new Color(1.00f, 0.60f, 0.65f));
        k.Tube(new Vector3(0f, 0.78f, 0f), new Vector3(0f, 0.95f, 0f), 0.02f, 0.02f, cream, 0f, 4);
        k.Sphere(new Vector3(0f, 0.98f, 0f), 0.06f, new Color(1.00f, 0.45f, 0.45f), 0.8f);
        return _turretMesh = k.ToMesh();
    }

    /// <summary>A pellet flying from the barrel to the target.</summary>
    private void Shot(Vector3 from, Vector3 to)
    {
        var pellet = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = Game.Arena.Flat(TurretColor, emission: 1.2f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _main.AddChild(pellet);
        pellet.GlobalPosition = from;
        Tween tw = pellet.CreateTween();
        tw.TweenProperty(pellet, "global_position", to, 0.12f);
        tw.TweenCallback(Callable.From(pellet.QueueFree));
    }

    // ---------------------------------------------------------- night light

    private void TickGlow(Player p, float dt, bool alive)
    {
        if (_glow is null || !IsInstanceValid(_glow))
        {
            _glow = new OmniLight3D { Name = "NightLight", LightColor = GlowColor, LightEnergy = 1.4f, OmniRange = 5.5f, ShadowEnabled = false };
            p.AddChild(_glow);
            _glow.Position = Vector3.Up * 1.4f;
        }
        _glow.LightEnergy = alive ? 1.3f + 0.25f * Mathf.Sin(_glowTimer * 6f) : 0f;
        if (!alive) return;

        _glowTimer -= dt;
        if (_glowTimer > 0f) return;
        _glowTimer = GlowEvery;
        float radius = GlowRadius * p.Stats.Get(Stat.AreaSize);
        float amount = (GlowBase + GlowPerLevel * RunLevel) * p.Stats.GetMultiplier(Stat.Damage);
        foreach (Enemy e in EnemiesWithin(p.GlobalPosition, radius))
        {
            if (!IsInstanceValid(e) || !e.IsAlive) continue;
            var info = new DamageInfo
            {
                Amount = amount,
                Element = Element.Fire,
                CanCrit = false,
                ProcCoefficient = 0f,
                Source = "nightlight",
            };
            e.TakeDamage(info);
            if (info.Nullified || info.Amount <= 0f) continue;
            EventBus.Publish(new EnemyDamaged(e, info));
            if (info.Amount >= 1f)
                EventBus.Publish(new PopupRequested(e.AimPoint + Vector3.Up * 0.3f, Mathf.RoundToInt(info.Amount).ToString(), GlowColor, 0.6f));
        }
    }

    // -------------------------------------------------------------- bubbles

    private void TickBubbles(Player p, float dt, bool alive)
    {
        if (alive && _bubbles < BubbleMax)
        {
            _bubbleTimer += dt;
            if (_bubbleTimer >= BubbleRegrow)
            {
                _bubbleTimer = 0f;
                _bubbles++;
            }
        }
        else _bubbleTimer = 0f;

        // One orbiting bubble per charge.
        while (_bubbleNodes.Count < _bubbles)
        {
            var b = new MeshInstance3D
            {
                Name = "Bubble",
                Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 14, Rings = 8 },
                MaterialOverride = BubbleMaterial(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _main.AddChild(b);
            _bubbleNodes.Add(b);
        }
        while (_bubbleNodes.Count > _bubbles)
        {
            Node3D last = _bubbleNodes[^1];
            _bubbleNodes.RemoveAt(_bubbleNodes.Count - 1);
            if (IsInstanceValid(last)) last.QueueFree();
        }

        _orbitClock += dt;
        Vector3 centre = p.GlobalPosition + Vector3.Up * 1.1f;
        for (int i = 0; i < _bubbleNodes.Count; i++)
        {
            float a = _orbitClock * 2.2f + i * Mathf.Tau / BubbleMax;
            Node3D b = _bubbleNodes[i];
            if (!IsInstanceValid(b)) continue;
            b.Visible = alive;
            b.GlobalPosition = centre + new Vector3(Mathf.Cos(a) * 1.0f, 0.25f * Mathf.Sin(_orbitClock * 3f + i), Mathf.Sin(a) * 1.0f);
        }
    }

    private static StandardMaterial3D? _bubbleMat;

    private static StandardMaterial3D BubbleMaterial() => _bubbleMat ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(BubbleColor, 0.45f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        EmissionEnabled = true,
        Emission = BubbleColor,
        EmissionEnergyMultiplier = 0.6f,
        Roughness = 0.1f,
        RimEnabled = true,
        Rim = 1f,
    };

    // ---------------------------------------------------------- bloom / slide

    private void Bloom(Player p)
    {
        p.Health?.HealFraction(BloomHeal);
        float radius = BloomRadius * p.Stats.Get(Stat.AreaSize);
        float dps = (BloomPoisonBase + BloomPoisonPerLevel * RunLevel) * p.Stats.GetMultiplier(Stat.Damage);
        foreach (Enemy e in EnemiesWithin(p.GlobalPosition, radius))
            e.ApplyPoison(dps, BloomPoisonSeconds * Mathf.Max(0.5f, p.Stats.Get(Stat.StatusDuration)));
        Ring(p.GlobalPosition, radius, BloomColor);
        EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.3f, "BLOOM", BloomColor, 0.9f));
    }

    private void OnCuteMoved(PlayerMoved e)
    {
        if (_kind != PassiveKind.BellySlide || e.Verb != MoveVerb.Dash || LivePlayer is not { } p) return;
        float radius = SlideRadius * p.Stats.Get(Stat.AreaSize);
        foreach (Enemy en in EnemiesWithin(p.GlobalPosition, radius))
            en.ApplyChill(2.5f, 4, 1.2f);
        Ring(p.GlobalPosition, radius, FrostColor);
        _slideLeft = SlideSeconds;
        SetMod(p, Stat.MoveSpeed, ModifierOp.Additive, SlideSpeed);
    }

    // ---------------------------------------------------- bubbles / nine lives

    private void OnCuteDamaged(PlayerDamaged e)
    {
        if (e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f || LivePlayer is not { } p || p.Health is not { } h) return;
        if (!h.IsAlive) return;

        if (_kind == PassiveKind.BubbleCharm && _bubbles > 0)
        {
            _bubbles--;
            h.GrantInvulnerability(BubbleSafety);
            float radius = BubbleChillRadius * p.Stats.Get(Stat.AreaSize);
            foreach (Enemy en in EnemiesWithin(p.GlobalPosition, radius))
                en.ApplyChill(3f, 3, 1.2f);
            Ring(p.GlobalPosition, radius, BubbleColor);
            EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.4f, "POP!", BubbleColor, 1.0f));
        }
        else if (_kind == PassiveKind.NineLives && _livesLeft > 0 && _livesCooldown <= 0f && h.Fraction < LivesBelow)
        {
            // Runs before the health check that ends the run (PlayerDamaged is
            // published first), so a life can catch a hit that would have killed.
            _livesLeft--;
            _livesCooldown = LivesCooldown;
            h.HealFraction(LivesHeal);
            h.GrantInvulnerability(LivesSafety);
            Ring(p.GlobalPosition, 3f, MeowColor);
            EventBus.Publish(new PopupRequested(p.CenterPosition + Vector3.Up * 1.5f,
                $"NINE LIVES  {_livesLeft} left", MeowColor, 1.1f));
        }
    }

    // ---------------------------------------------------------------- helpers

    private static List<Enemy> EnemiesWithin(Vector3 at, float radius)
    {
        var list = new List<Enemy>();
        if (EnemyManager.Instance is not { } em) return list;
        float r2 = radius * radius;
        foreach (Enemy e in em.AliveList)
        {
            if (!IsInstanceValid(e) || !e.IsAlive) continue;
            Vector3 d = e.GlobalPosition - at;
            d.Y = 0f;
            if (d.LengthSquared() <= r2) list.Add(e);
        }
        return list;
    }

    /// <summary>A soft ring that swells out to <paramref name="radius"/> and fades:
    /// the shared "something just happened around you" cue.</summary>
    private void Ring(Vector3 at, float radius, Color color)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(color, 0.7f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.88f, OuterRadius = 1f, Rings = 32, RingSegments = 6 },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _main.AddChild(ring);
        ring.GlobalPosition = at + Vector3.Up * 0.15f;
        ring.Scale = new Vector3(radius * 0.3f, 1f, radius * 0.3f);
        Tween tw = ring.CreateTween().SetParallel();
        tw.TweenProperty(ring, "scale", new Vector3(radius, 1f, radius), 0.35f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tw.TweenProperty(mat, "albedo_color:a", 0f, 0.4f);
        tw.Chain().TweenCallback(Callable.From(ring.QueueFree));
    }

    private void Pop(Vector3 at, Color color, float scale)
        => EventBus.Publish(new PopupRequested(at, "pop", color, scale));
}
