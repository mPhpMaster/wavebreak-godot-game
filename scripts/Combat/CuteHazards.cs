using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Combat;

/// <summary>
/// What the cute wave leaves in the world besides its bodies: trapping
/// bubbles, slime puddles, the pinch and bubble holds on the player, and the
/// lantern-fish's gloom over the screen.
///
/// Ticked from the end of <see cref="EnemyAttacks.Tick"/> (one line there), so
/// it runs on the same clock and against the same target as every other enemy
/// attack, and the content audit can point it at a dummy. Damage still leaves
/// through <see cref="EnemyAttacks.Hit"/>, so i-frames, armour and evasion
/// answer a bubble exactly as they answer a shot.
///
/// The holds go onto the player as one multiplicative MoveSpeed modifier tagged
/// with <see cref="SlowSource"/> and are recomputed every tick from timers, so
/// whatever happens (a hold expiring, two holds overlapping, the run ending)
/// removing them is one <see cref="StatSheet.RemoveAllFrom"/>. A dash pops a
/// bubble hold and dodges new bubbles: the answer is always the dash.
/// </summary>
public static class CuteHazards
{
    public static readonly Color BubbleColor = new(0.72f, 0.93f, 1.00f, 0.42f);
    public static readonly Color SlimeColor = new(0.70f, 1.00f, 0.62f, 0.55f);
    private static readonly Color TrapTextColor = new(0.60f, 0.92f, 1.00f);
    private static readonly Color PinchTextColor = new(1.00f, 0.62f, 0.55f);

    private const float PlayerRadius = 0.4f, PlayerLow = 0.4f, PlayerHigh = 1.4f;

    /// <summary>Move speed multiplier while held in a bubble: not zero, so the
    /// player still turns and the camera still reads input.</summary>
    public const float TrapSpeed = 0.1f;

    private const int MaxBubbles = 96;
    private const int MaxPuddles = 120;
    private const float MaxGloom = 0.8f;

    private sealed class Bubble
    {
        public Vector3 Pos, Vel;
        public float Radius, Life, Age, Damage, Hold, Wobble;
        public string Source = "";
        public Node3D? Node;
    }

    private sealed class Puddle
    {
        public Vector3 Centre;
        public float Radius, Life, Age, Slow;
        public GroundDecal? Decal;
    }

    private static readonly List<Bubble> Bubbles = new(32);
    private static readonly Stack<Bubble> SpareBubbles = new();
    private static readonly Stack<Node3D> SpareBubbleNodes = new();
    private static readonly List<Puddle> Puddles = new(64);
    private static readonly Stack<Puddle> SparePuddles = new();

    /// <summary>The tag on the player's MoveSpeed modifier.</summary>
    public static readonly object SlowSource = new();
    private static Player? _slowed;
    private static float _applied = 1f;
    private static float _trapLeft, _pinchLeft, _pinchSlow;
    private static Node3D? _trapNode;

    private static float _gloomSum, _gloom;
    private static readonly Vector3[] Lures = new Vector3[4];
    private static readonly float[] LureWeight = new float[4];
    private static CanvasLayer? _gloomLayer;
    private static ShaderMaterial? _gloomMaterial;

    private static SphereMesh? _sphere;
    private static StandardMaterial3D? _bubbleMat, _glintMat;

    // -------------------------------------------------------------- numbers

    public static int ActiveBubbles => Bubbles.Count;
    public static int ActivePuddles => Puddles.Count;
    public static bool PlayerTrapped => _trapLeft > 0f;
    /// <summary>The MoveSpeed multiplier the holds currently put on the player.</summary>
    public static float AppliedSlow => _applied;
    /// <summary>How dark the gloom is heading (0..<see cref="MaxGloom"/>).</summary>
    public static float GloomTarget { get; private set; }
    public static float Gloom => _gloom;
    public static int BubblesBlown { get; private set; }
    public static int TrapsLanded { get; private set; }
    public static int PinchesLanded { get; private set; }
    public static int PuddlesDropped { get; private set; }

    // ------------------------------------------------------------- spawning

    /// <summary>Blow one bubble along <paramref name="direction"/> (flattened).
    /// Returns false at the cap.</summary>
    public static bool Blow(Vector3 origin, Vector3 direction, float speed, float damage, float radius,
                            float range, float hold, string source)
    {
        if (Bubbles.Count >= MaxBubbles) return false;
        Vector3 dir = new(direction.X, 0f, direction.Z);
        dir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : Vector3.Forward;

        Bubble b = SpareBubbles.Count > 0 ? SpareBubbles.Pop() : new Bubble();
        b.Pos = origin;
        b.Vel = dir * MathF.Max(0.5f, speed);
        b.Radius = MathF.Max(0.15f, radius);
        b.Life = Mathf.Clamp(range / MathF.Max(0.5f, speed), 0.3f, 6f);
        b.Age = 0f;
        b.Damage = damage;
        b.Hold = hold;
        b.Wobble = (BubblesBlown % 17) * 0.37f;
        b.Source = source;
        b.Node = AcquireBubbleNode();
        if (b.Node is not null)
        {
            b.Node.GlobalPosition = origin;
            b.Node.Scale = Vector3.One * (b.Radius * 0.3f);
        }
        Bubbles.Add(b);
        BubblesBlown++;
        return true;
    }

    /// <summary>A slime puddle on the floor at <paramref name="centre"/>.
    /// Skipped (returns false) when no decal is free: a hazard nobody can see
    /// is worse than one fewer puddle.</summary>
    public static bool DropPuddle(Vector3 centre, float radius, float life, float slow)
    {
        if (Puddles.Count >= MaxPuddles) return false;
        GroundDecal? decal = EnemyAttacks.AcquireDecal();
        if (decal is null) return false;
        Puddle p = SparePuddles.Count > 0 ? SparePuddles.Pop() : new Puddle();
        p.Centre = centre;
        p.Radius = radius;
        p.Life = life;
        p.Age = 0f;
        p.Slow = Mathf.Clamp(slow, 0f, 0.8f);
        p.Decal = decal;
        decal.SetDisc(centre, radius * 0.4f, 1f, SlimeColor with { A = 0f });
        Puddles.Add(p);
        PuddlesDropped++;
        return true;
    }

    /// <summary>A landed pinch: the player walks slower for a moment. A dash
    /// (i-frames) shrugs it off, like the damage.</summary>
    public static void Pinch(IDamageable? target, float seconds, float slow)
    {
        if (target is not Player p || !p.IsAlive || p.IsDashing) return;
        bool fresh = _pinchLeft <= 0f;
        _pinchLeft = MathF.Max(_pinchLeft, seconds);
        _pinchSlow = MathF.Max(fresh ? 0f : _pinchSlow, Mathf.Clamp(slow, 0f, 0.8f));
        PinchesLanded++;
        if (fresh) Popup(p, Meta.Loc.T("PINCHED"), PinchTextColor);
    }

    /// <summary>Hold the player in a bubble for <paramref name="seconds"/>.</summary>
    public static void Trap(Player p, float seconds)
    {
        if (!p.IsAlive || p.IsDashing || seconds <= 0f) return;
        bool fresh = _trapLeft <= 0f;
        _trapLeft = MathF.Max(_trapLeft, MathF.Min(seconds, 1.5f));
        TrapsLanded++;
        if (fresh) Popup(p, Meta.Loc.T("BUBBLED!"), TrapTextColor);
    }

    /// <summary>A lantern-fish asks for the lights to dim. <paramref name="weight"/>
    /// is its closeness, 0..1; the screen darkens with the sum, capped.</summary>
    public static void NoteGloom(Vector3 lure, float weight)
    {
        if (weight <= 0f) return;
        _gloomSum += weight;
        // Keep the four strongest lures: they get holes in the dark.
        int weakest = 0;
        for (int i = 1; i < LureWeight.Length; i++) if (LureWeight[i] < LureWeight[weakest]) weakest = i;
        if (weight > LureWeight[weakest])
        {
            LureWeight[weakest] = weight;
            Lures[weakest] = lure;
        }
    }

    /// <summary>Slow a puddle puts on feet at this point (0 when none).</summary>
    public static float SlowAt(Vector3 feet)
    {
        float slow = 0f;
        for (int i = 0; i < Puddles.Count; i++)
        {
            Puddle p = Puddles[i];
            if (feet.Y > p.Centre.Y + 0.6f) continue;   // a jump clears the slime
            float dx = feet.X - p.Centre.X, dz = feet.Z - p.Centre.Z;
            float r = p.Radius + PlayerRadius * 0.5f;
            if (dx * dx + dz * dz <= r * r) slow = MathF.Max(slow, p.Slow);
        }
        return slow;
    }

    // --------------------------------------------------------------- update

    public static void Tick(float dt, Vector3 feet, IDamageable? target)
    {
        bool canHit = target is not null && target.IsAlive;
        Player? player = target as Player;

        for (int i = Bubbles.Count - 1; i >= 0; i--)
        {
            Bubble b = Bubbles[i];
            b.Age += dt;
            b.Pos += b.Vel * dt;
            // Bubbles drift and wobble: slow, soft, readable.
            float bob = MathF.Sin(b.Age * 5f + b.Wobble) * 0.12f;
            Vector3 at = b.Pos + new Vector3(0f, bob, 0f);
            if (b.Node is not null && GodotObject.IsInstanceValid(b.Node))
            {
                float grow = MathF.Min(1f, 0.3f + b.Age * 4f);
                float jiggle = 1f + MathF.Sin(b.Age * 9f + b.Wobble) * 0.06f;
                b.Node.GlobalPosition = at;
                b.Node.Scale = new Vector3(jiggle, 2f - jiggle, jiggle) * (b.Radius * grow);
            }

            bool done = b.Age >= b.Life;
            if (!done && canHit && Touches(at, b.Radius, feet))
            {
                done = true;
                if (player is not null && player.IsDashing)
                {
                    Popup(player, Meta.Loc.T("POP"), TrapTextColor, 0.7f);
                }
                else
                {
                    EnemyAttacks.Hit(target!, b.Damage, b.Vel, 0.2f, b.Source);
                    if (player is not null) Trap(player, b.Hold);
                }
            }
            if (!done) continue;
            EnemyAttacks.Flash(new Vector3(at.X, feet.Y, at.Z), b.Radius * 1.4f, BubbleColor);
            Bubbles[i] = Bubbles[^1];
            Bubbles.RemoveAt(Bubbles.Count - 1);
            RetireBubble(b);
        }

        for (int i = Puddles.Count - 1; i >= 0; i--)
        {
            Puddle p = Puddles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                Puddles[i] = Puddles[^1];
                Puddles.RemoveAt(Puddles.Count - 1);
                EnemyAttacks.ReleaseDecal(p.Decal);
                p.Decal = null;
                SparePuddles.Push(p);
                continue;
            }
            float fade = MathF.Min(1f, (p.Life - p.Age) / 0.8f);
            float spread = MathF.Min(1f, 0.4f + p.Age * 4f);
            p.Decal?.SetDisc(p.Centre, p.Radius * spread, 1f, SlimeColor with { A = SlimeColor.A * fade });
        }

        if (player is not null && canHit) UpdatePlayer(dt, player, SlowAt(feet));
        else if (player is not null) ClearPlayer();     // the player died: no holds on a corpse

        UpdateGloom(dt, player);
    }

    private static void UpdatePlayer(float dt, Player player, float puddleSlow)
    {
        if (_trapLeft > 0f)
        {
            if (player.IsDashing)
            {
                _trapLeft = 0f;
                Popup(player, Meta.Loc.T("POP!"), TrapTextColor, 0.9f);
            }
            else _trapLeft -= dt;
        }
        if (_pinchLeft > 0f) _pinchLeft -= dt;

        float mult = 1f;
        if (_trapLeft > 0f) mult = TrapSpeed;
        else
        {
            if (_pinchLeft > 0f) mult = MathF.Min(mult, 1f - _pinchSlow);
            if (puddleSlow > 0f) mult = MathF.Min(mult, 1f - puddleSlow);
        }
        SetSlow(player, mult);

        // The bubble around a held player: the hold is visible, and so is its end.
        if (_trapLeft > 0f)
        {
            Node3D? n = _trapNode;
            if (n is null || !GodotObject.IsInstanceValid(n)) n = _trapNode = AcquireBubbleNode();
            if (n is not null)
            {
                n.Visible = true;
                float t = (float)Time.GetTicksMsec() * 0.001f;
                float j = 1f + MathF.Sin(t * 8f) * 0.05f;
                n.GlobalPosition = player.GlobalPosition + new Vector3(0f, 0.95f, 0f);
                n.Scale = new Vector3(j, 2f - j, j) * 1.05f;
            }
        }
        else if (_trapNode is not null && GodotObject.IsInstanceValid(_trapNode))
        {
            _trapNode.Visible = false;
        }
    }

    private static void SetSlow(Player player, float mult)
    {
        if (!ReferenceEquals(_slowed, player))
        {
            ClearPlayer();
            _slowed = player;
        }
        if (MathF.Abs(mult - _applied) < 0.001f) return;
        player.Stats.RemoveAllFrom(SlowSource);
        if (mult < 0.999f) player.Stats.Add(Stat.MoveSpeed, ModifierOp.Multiplicative, mult, SlowSource);
        _applied = mult;
    }

    private static void ClearPlayer()
    {
        if (_slowed is not null && GodotObject.IsInstanceValid(_slowed)) _slowed.Stats.RemoveAllFrom(SlowSource);
        _slowed = null;
        _applied = 1f;
        _trapLeft = _pinchLeft = _pinchSlow = 0f;
        if (_trapNode is not null && GodotObject.IsInstanceValid(_trapNode)) _trapNode.Visible = false;
    }

    // ---------------------------------------------------------------- gloom

    private static void UpdateGloom(float dt, Player? player)
    {
        GloomTarget = MathF.Min(MaxGloom, _gloomSum * 0.75f);
        _gloomSum = 0f;
        // Dims in about a second, lifts a little faster once the light is out.
        float rate = GloomTarget > _gloom ? 0.8f : 1.2f;
        _gloom = Mathf.MoveToward(_gloom, player is null ? 0f : GloomTarget, rate * dt);

        if (_gloom <= 0.005f || player is null)
        {
            if (_gloomLayer is not null && GodotObject.IsInstanceValid(_gloomLayer)) _gloomLayer.Visible = false;
            Array.Clear(LureWeight);
            return;
        }

        EnsureGloomLayer(player);
        if (_gloomMaterial is null || _gloomLayer is null) return;
        _gloomLayer.Visible = true;
        _gloomMaterial.SetShaderParameter("dark", _gloom);

        Viewport vp = player.GetViewport();
        Camera3D? cam = vp.GetCamera3D();
        Vector2 size = vp.GetVisibleRect().Size;
        _gloomMaterial.SetShaderParameter("aspect", size.Y > 0f ? size.X / size.Y : 1.7f);
        if (cam is not null && size.X > 0f)
        {
            Vector2 c = cam.UnprojectPosition(player.GlobalPosition + new Vector3(0f, 0.9f, 0f)) / size;
            _gloomMaterial.SetShaderParameter("centre", c);
        }
        for (int i = 0; i < Lures.Length; i++)
        {
            Vector4 l = Vector4.Zero;
            if (LureWeight[i] > 0f && cam is not null && size.X > 0f && !cam.IsPositionBehind(Lures[i]))
            {
                Vector2 uv = cam.UnprojectPosition(Lures[i]) / size;
                float dist = cam.GlobalPosition.DistanceTo(Lures[i]);
                l = new Vector4(uv.X, uv.Y, Mathf.Clamp(1.6f / MathF.Max(1f, dist), 0.035f, 0.16f), 0.95f);
            }
            _gloomMaterial.SetShaderParameter("lure" + i, l);
        }
        Array.Clear(LureWeight);
    }

    private static void EnsureGloomLayer(Player player)
    {
        if (_gloomLayer is not null && GodotObject.IsInstanceValid(_gloomLayer) && _gloomLayer.IsInsideTree()) return;
        Node? host = player.GetTree()?.CurrentScene ?? player.GetParent();
        if (host is null) return;

        _gloomMaterial = new ShaderMaterial { Shader = new Shader { Code = GloomShader } };
        // Under the HUD (layer 1), over the world.
        _gloomLayer = new CanvasLayer { Name = "Gloom", Layer = 0 };
        var rect = new ColorRect
        {
            Name = "Dark",
            Material = _gloomMaterial,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = Colors.White,
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _gloomLayer.AddChild(rect);
        host.AddChild(_gloomLayer);
    }

    private const string GloomShader = @"
shader_type canvas_item;
uniform float dark = 0.0;
uniform float aspect = 1.7;
uniform vec2 centre = vec2(0.5, 0.55);
uniform vec4 lure0 = vec4(0.0);
uniform vec4 lure1 = vec4(0.0);
uniform vec4 lure2 = vec4(0.0);
uniform vec4 lure3 = vec4(0.0);

float hole(vec2 uv, vec4 l) {
    if (l.w <= 0.0) return 1.0;
    float d = length((uv - l.xy) * vec2(aspect, 1.0));
    return 1.0 - l.w * (1.0 - smoothstep(l.z * 0.4, l.z, d));
}

void fragment() {
    vec2 uv = UV;
    float d = length((uv - centre) * vec2(aspect, 1.0));
    // A pocket of light around the player; the dark closes in from the edges.
    float edge = smoothstep(0.12, 0.62, d);
    float a = dark * mix(0.18, 1.0, edge);
    a *= hole(uv, lure0) * hole(uv, lure1) * hole(uv, lure2) * hole(uv, lure3);
    // A warm rim around each lure so the light reads as light, not a hole.
    COLOR = vec4(0.015, 0.02, 0.07, clamp(a, 0.0, 0.95));
}
";

    // ---------------------------------------------------------- bookkeeping

    /// <summary>Retire everything: bubbles, puddles, holds, gloom. Called from
    /// <see cref="EnemyAttacks.Clear"/> (run reset) and by the audit.</summary>
    public static void Clear()
    {
        foreach (Bubble b in Bubbles) RetireBubble(b);
        Bubbles.Clear();
        foreach (Puddle p in Puddles)
        {
            EnemyAttacks.ReleaseDecal(p.Decal);
            p.Decal = null;
            SparePuddles.Push(p);
        }
        Puddles.Clear();
        ClearPlayer();
        _gloomSum = 0f;
        _gloom = 0f;
        GloomTarget = 0f;
        Array.Clear(LureWeight);
        if (_gloomLayer is not null && GodotObject.IsInstanceValid(_gloomLayer)) _gloomLayer.Visible = false;
    }

    private static bool Touches(Vector3 p, float radius, Vector3 feet)
    {
        float y = Mathf.Clamp(p.Y, feet.Y + PlayerLow, feet.Y + PlayerHigh);
        Vector3 d = p - new Vector3(feet.X, y, feet.Z);
        float reach = radius + PlayerRadius;
        return d.LengthSquared() <= reach * reach;
    }

    private static void Popup(Player p, string text, Color color, float scale = 1f)
        => EventBus.Publish(new PopupRequested(p.GlobalPosition + new Vector3(0f, 2.3f, 0f), text, color, scale));

    private static Node3D? AcquireBubbleNode()
    {
        while (SpareBubbleNodes.Count > 0)
        {
            Node3D n = SpareBubbleNodes.Pop();
            if (GodotObject.IsInstanceValid(n) && n.IsInsideTree())
            {
                n.Visible = true;
                return n;
            }
        }
        Node? parent = EnemyManager.Instance;
        if (parent is null || !GodotObject.IsInstanceValid(parent) || !parent.IsInsideTree()) return null;

        _sphere ??= new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 };
        _bubbleMat ??= new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = BubbleColor,
            EmissionEnabled = true,
            Emission = new Color(0.35f, 0.6f, 0.85f),
            EmissionEnergyMultiplier = 0.5f,
            RimEnabled = true,
            Rim = 1f,
            RimTint = 0.2f,
            Roughness = 0.1f,
            CullMode = BaseMaterial3D.CullModeEnum.Back,
        };
        _glintMat ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 1f, 1f, 0.9f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var node = new Node3D { Name = "Bubble" };
        node.AddChild(new MeshInstance3D
        {
            Mesh = _sphere,
            MaterialOverride = _bubbleMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        node.AddChild(new MeshInstance3D
        {
            Mesh = _sphere,
            MaterialOverride = _glintMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(-0.38f, 0.45f, 0.55f),
            Scale = new Vector3(0.2f, 0.14f, 0.1f),
        });
        parent.AddChild(node);
        return node;
    }

    private static void RetireBubble(Bubble b)
    {
        if (b.Node is not null && GodotObject.IsInstanceValid(b.Node))
        {
            b.Node.Visible = false;
            SpareBubbleNodes.Push(b.Node);
        }
        b.Node = null;
        SpareBubbles.Push(b);
    }
}
