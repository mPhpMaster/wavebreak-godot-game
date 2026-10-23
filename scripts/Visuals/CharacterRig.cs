using System;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>Whole-body poses a controller can ask for. Enemies stay in
/// <see cref="Locomotion"/>; the hero uses the rest to make each verb read.</summary>
public enum RigPose { Locomotion, Air, Dash, Slide, Slam }

/// <summary>
/// A model built from a <see cref="ModelSpec"/> plus the procedural animator that
/// poses it. One node per character; everything below it is shared geometry.
///
/// What drives it: by default it watches its own world position and derives the
/// velocity from the change, so it works under anything that moves — an enemy, a
/// showcase dummy, a corpse — without that thing knowing it exists. A controller
/// that knows better (the player) sets <see cref="ManualMotion"/> and feeds
/// <see cref="Velocity"/>, <see cref="Grounded"/> and <see cref="Pose"/>.
/// Gameplay events come in through four hooks: <see cref="PlayAttackWindup"/>,
/// <see cref="PlayAttack"/>, <see cref="PlayHit"/> and <see cref="PlayDeath"/>.
///
/// Why it is procedural rather than keyframed: a walk cycle scaled by real speed
/// never foot-slides, the same code animates every body type, and the cost is a
/// few sines — which matters at two hundred enemies.
///
/// Cost discipline (it runs for every enemy on screen):
///   * no allocations per frame — all state is fields, poses are plain floats;
///   * one transform write per moving part, and only when near the camera:
///     beyond <see cref="ModelSpec.LodDistance"/> the engine swaps in the merged
///     far mesh (visibility ranges, so the swap costs no script) and this node
///     stops posing limbs altogether;
///   * per-instance shader parameters (flash, dissolve, glow) are written only
///     when they change, so an idle crowd makes no material calls at all.
/// </summary>
public sealed partial class CharacterRig : Node3D
{
    public ModelSpec Spec { get; private set; } = null!;

    // -------------------------------------------------------------- inputs

    /// <summary>When true the rig does not derive motion from its position; the
    /// owner writes <see cref="Velocity"/> etc. every frame.</summary>
    public bool ManualMotion { get; set; }

    /// <summary>World-space velocity (only read when <see cref="ManualMotion"/>).</summary>
    public Vector3 Velocity { get; set; }
    public bool Grounded { get; set; } = true;
    public RigPose Pose { get; set; } = RigPose.Locomotion;

    /// <summary>Raised once when a death animation has finished and the rig has
    /// hidden itself. Pools listen to this to take the body back.</summary>
    public event Action<CharacterRig>? DeathFinished;

    public bool IsDying => _dying;

    /// <summary>
    /// When above zero, the rig anticipates the next swing on its own: after each
    /// <see cref="PlayAttack"/> it starts the windup shortly before the cooldown
    /// is up. Contact attackers swing on a fixed rhythm while in reach, so this
    /// reads correctly without the owner having to call the windup hook — and a
    /// target that stepped away just gets a swing at the air, which reads as a miss.
    /// </summary>
    public float AttackCooldown { get; set; }
    private float _sinceStrike = -1f;

    // -------------------------------------------------------------- nodes

    private struct Part
    {
        public PartDef Def;
        public MeshInstance3D Upper;
        public MeshInstance3D? Lower;
    }

    private BuiltModel _model = null!;
    private Node3D _hips = null!;
    private MeshInstance3D? _bodyMesh;
    private MeshInstance3D? _impostor;
    private MeshInstance3D? _aura;
    private Part[] _parts = Array.Empty<Part>();
    private GeometryInstance3D[] _geoms = Array.Empty<GeometryInstance3D>();

    // Imported-scene mode.
    private AnimationPlayer? _player;

    /// <summary>Playing clip, its position and whether it is still advancing —
    /// for verification (a frozen walk is invisible in a single screenshot).</summary>
    public string DebugClip => _player is null ? "-" :
        $"{_player.CurrentAnimation} t={_player.CurrentAnimationPosition:0.00}/{_player.CurrentAnimationLength:0.00} " +
        $"loop={(_player.HasAnimation(_player.CurrentAnimation) ? _player.GetAnimation(_player.CurrentAnimation).LoopMode : Animation.LoopModeEnum.None)} " +
        $"playing={_player.IsPlaying()} speed={_speed:0.0}";
    private string _clip = "";

    // -------------------------------------------------------------- state

    private float _time;
    private float _phase;
    private float _speed;            // smoothed planar speed
    private Vector3 _lastPos;
    private bool _havePos;
    private float _seed;

    private float _hit;              // 1 on hit, decays
    private float _flash;
    private float _windup, _windupDur; // seconds into windup / total; dur 0 = none
    private float _strike;           // 1 on release, decays
    private bool _dying;
    private float _death;
    private float _deathDir = 1f;
    private float _flip = -1f;       // seconds into a flip, -1 = none

    // Smoothed pose weights.
    private float _wAir, _wDash, _wSlide, _wSlam;

    // Last values written to the shader, to skip redundant calls.
    private float _sentFlash = -1f, _sentDissolve = -1f;
    private Color _glow = new(0, 0, 0, 0), _sentGlow = new(-1, 0, 0, 0);

    private bool _far;
    private int _lodTick;

    private static ulong _camFrame = ulong.MaxValue;
    private static Vector3 _camPos;
    private static bool _haveCam;

    // ------------------------------------------------------------ building

    /// <summary>
    /// Instance a model. <paramref name="faceZ"/>: the parent turns to face +Z
    /// (enemies do) rather than Godot's -Z, so the rig is turned around inside it.
    /// <paramref name="outline"/>: near parts get the ink outline pass.
    /// </summary>
    public static CharacterRig Create(ModelSpec spec, bool faceZ = false, bool outline = true)
    {
        var rig = new CharacterRig { Name = "Rig" };
        rig.Build(spec, faceZ, outline);
        return rig;
    }

    private void Build(ModelSpec spec, bool faceZ, bool outline)
    {
        Spec = spec;
        if (faceZ) Rotation = new Vector3(0f, Mathf.Pi, 0f);
        _seed = (GetInstanceId() % 1000) * 0.0137f;

        _hips = new Node3D { Name = "Hips" };
        AddChild(_hips);

        if (spec.IsImported)
        {
            BuildImported(spec);
            return;
        }

        _model = ModelBuilder.Get(spec);
        _hips.Position = _model.HipsOffset;
        Material mat = outline ? ToonMaterials.Character : ToonMaterials.CharacterNoOutline;
        float lod = spec.LodDistance;

        var geoms = new System.Collections.Generic.List<GeometryInstance3D>();
        MeshInstance3D Mesh(Node parent, Mesh mesh, string name)
        {
            var mi = new MeshInstance3D
            {
                Name = name,
                Mesh = mesh,
                MaterialOverride = mat,
                // Characters do not cast sun shadows: two hundred extra shadow
                // casters doubles the draw count for a detail nobody sees in a
                // crowd. The hero opts back in (see EnableShadow).
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            if (lod > 0f)
            {
                mi.VisibilityRangeEnd = lod;
                mi.VisibilityRangeEndMargin = 1.5f;
            }
            parent.AddChild(mi);
            geoms.Add(mi);
            return mi;
        }

        _bodyMesh = Mesh(_hips, _model.Body, "Body");
        _parts = new Part[_model.Parts.Count];
        for (int i = 0; i < _parts.Length; i++)
        {
            PartDef d = _model.Parts[i];
            MeshInstance3D up = Mesh(_hips, d.Upper, d.Role.ToString());
            up.Position = d.Pivot;
            if (d.Scale != 1f) up.Scale = Vector3.One * d.Scale;
            MeshInstance3D? low = null;
            if (d.Lower is not null)
            {
                low = Mesh(up, d.Lower, d.Role + "Lower");
                low.Position = d.LowerPivot;
            }
            _parts[i] = new Part { Def = d, Upper = up, Lower = low };
        }

        if (lod > 0f)
        {
            _impostor = new MeshInstance3D
            {
                Name = "Far",
                Mesh = _model.Impostor,
                MaterialOverride = ToonMaterials.CharacterNoOutline,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeBegin = lod,
                VisibilityRangeBeginMargin = 1.5f,
                Position = -_model.HipsOffset,
            };
            _hips.AddChild(_impostor);
            geoms.Add(_impostor);
        }

        if (_model.AuraRadius > 0f && spec.Aura)
        {
            _aura = new MeshInstance3D
            {
                Name = "Aura",
                Mesh = AuraMesh(_model.AuraRadius),
                MaterialOverride = ToonMaterials.Aura,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0f, 0.04f, 0f),
            };
            AddChild(_aura);
            _aura.SetInstanceShaderParameter("glow", spec.AuraColor);
        }

        _geoms = geoms.ToArray();
    }

    private static readonly System.Collections.Generic.Dictionary<int, Mesh> AuraMeshes = new();

    private static Mesh AuraMesh(float radius)
    {
        int key = Mathf.RoundToInt(radius * 10f);
        if (AuraMeshes.TryGetValue(key, out Mesh? m) && IsInstanceValid(m)) return m;
        m = new PlaneMesh { Size = new Vector2(radius * 2f, radius * 2f) };
        AuraMeshes[key] = m;
        return m;
    }

    /// <summary>Hero only: let the near parts cast sun shadows.</summary>
    public void EnableShadow()
    {
        foreach (GeometryInstance3D g in _geoms)
            if (g != _impostor) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
    }

    /// <summary>The merged rest-pose mesh: afterimages and ghosts use it.</summary>
    public Mesh? SilhouetteMesh => _model?.Impostor;

    /// <summary>Global transform of the posed body, for effects that should
    /// start where the body visibly is.</summary>
    public Transform3D BodyTransform => _hips.GlobalTransform;

    // ------------------------------------------------------------- imported

    private void BuildImported(ModelSpec spec)
    {
        _model = new BuiltModel { Height = spec.Height };
        Node3D inst = spec.Scene!.Instantiate<Node3D>();
        _hips.AddChild(inst);
        inst.Rotation = new Vector3(0f, spec.SceneYaw, 0f);

        float scale = spec.SceneScale;
        if (scale <= 0f)
        {
            Aabb box = MeasureScene(inst);
            scale = box.Size.Y > 0.001f ? spec.Height / box.Size.Y : 1f;
            spec.SceneScale = scale; // measured once per spec
        }
        inst.Scale = Vector3.One * scale;
        _player = FindPlayer(inst);
        if (_player is not null)
        {
            // Advanced by hand: lets far crowds animate at a fraction of the rate
            // (see AnimateImported), which is most of an imported crowd's cost.
            _player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
            LoopLocomotion(_player, spec);
            PlayClip(AnimSlot.Idle, 0f);
            // Desynchronise identical enemies so a wave does not march in step.
            _player.Advance((GetInstanceId() % 97) * 0.013f);
        }
        var geoms = new System.Collections.Generic.List<GeometryInstance3D>();
        CollectGeoms(inst, geoms);
        _geoms = geoms.ToArray();

        if (spec.Crown)
        {
            // Imported models have no head anchor we can trust, so the elite crown
            // floats above them, which reads better at distance anyway.
            _crown = new MeshInstance3D
            {
                Name = "Crown",
                Mesh = CrownMesh(),
                MaterialOverride = ToonMaterials.CharacterNoOutline,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0f, spec.Height * 1.08f, 0f),
                Scale = Vector3.One * Mathf.Clamp(spec.Height * 0.3f, 0.4f, 1.6f),
            };
            _hips.AddChild(_crown);
        }
        if (spec.Aura) AddAura(Mathf.Max(0.8f, spec.Height * 0.45f), spec.AuraColor);
        _rimMaterial = spec.Aura ? ToonMaterials.RimOverlay(spec.AuraColor) : null;
        ApplyOverlay(_rimMaterial);
    }

    private MeshInstance3D? _crown;
    private Material? _rimMaterial;
    private Material? _overlay;
    private float _animAccum;
    private int _animStep = 1, _animCounter;

    private static Mesh? _crownMesh;

    private static Mesh CrownMesh()
    {
        if (_crownMesh is not null && IsInstanceValid(_crownMesh)) return _crownMesh;
        var k = new MeshKit();
        Color gold = new(1f, 0.82f, 0.3f);
        k.Cylinder(Transform3D.Identity, 0.5f, 0.55f, 0.18f, gold, 0.9f, 10);
        for (int i = 0; i < 5; i++)
        {
            float a = i / 5f * Mathf.Tau;
            Vector3 b0 = new(Mathf.Cos(a) * 0.5f, 0.15f, Mathf.Sin(a) * 0.5f);
            k.Cone(b0, b0 + new Vector3(0f, 0.38f, 0f), 0.12f, gold, 1.2f, 4);
            k.Sphere(b0 + new Vector3(0f, 0.42f, 0f), 0.06f, new Color(1f, 0.4f, 0.35f), 1.6f, 5, 3);
        }
        _crownMesh = k.ToMesh();
        return _crownMesh;
    }

    private void AddAura(float radius, Color color)
    {
        _aura = new MeshInstance3D
        {
            Name = "Aura",
            Mesh = AuraMesh(radius),
            MaterialOverride = ToonMaterials.Aura,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, 0.04f, 0f),
        };
        AddChild(_aura);
        _aura.SetInstanceShaderParameter("glow", color);
    }

    /// <summary>Imported materials are the model's own, so the hit flash and the
    /// elite rim go on as an overlay pass, swapped in only while needed: an
    /// ordinary enemy that is not being hit draws exactly what the artist made.</summary>
    private void ApplyOverlay(Material? m)
    {
        if (m == _overlay) return;
        _overlay = m;
        foreach (GeometryInstance3D g in _geoms) g.MaterialOverlay = m;
    }

    private static Aabb MeasureScene(Node3D root)
    {
        Aabb box = new();
        bool any = false;
        void Walk(Node n, Transform3D xf)
        {
            Transform3D here = n is Node3D n3 ? xf * n3.Transform : xf;
            if (n is MeshInstance3D mi && mi.Mesh is not null)
            {
                Aabb b = here * mi.Mesh.GetAabb();
                box = any ? box.Merge(b) : b;
                any = true;
            }
            foreach (Node child in n.GetChildren()) Walk(child, here);
        }
        foreach (Node child in root.GetChildren()) Walk(child, Transform3D.Identity);
        return box;
    }

    /// <summary>
    /// Godot imports glTF clips as play-once unless the file says otherwise, and
    /// neither the Kenney nor the Quaternius exports do. A one-shot walk plays a
    /// single stride and then holds its last frame -- the body glides with its
    /// legs frozen. Locomotion clips must loop; attacks, hits and deaths must not.
    /// The Animation resources are shared by every instance of a model, so this
    /// is idempotent and effectively runs once per model.
    /// </summary>
    private static void LoopLocomotion(AnimationPlayer player, ModelSpec spec)
    {
        foreach (AnimSlot slot in new[] { AnimSlot.Idle, AnimSlot.Walk, AnimSlot.Run, AnimSlot.Dash,
                                          AnimSlot.Slide, AnimSlot.Fall })
        {
            if (!spec.Clips.TryGetValue(slot, out string? name) || !player.HasAnimation(name)) continue;
            Animation anim = player.GetAnimation(name);
            if (anim.LoopMode == Animation.LoopModeEnum.None) anim.LoopMode = Animation.LoopModeEnum.Linear;
        }
    }

    private static AnimationPlayer? FindPlayer(Node n)
    {
        if (n is AnimationPlayer p) return p;
        foreach (Node c in n.GetChildren())
            if (FindPlayer(c) is { } found) return found;
        return null;
    }

    private static void CollectGeoms(Node n, System.Collections.Generic.List<GeometryInstance3D> into)
    {
        if (n is GeometryInstance3D g)
        {
            g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            into.Add(g);
        }
        foreach (Node c in n.GetChildren()) CollectGeoms(c, into);
    }

    // ---------------------------------------------------------------- hooks

    /// <summary>Begin the anticipation pose for an attack landing in
    /// <paramref name="seconds"/>. Releases on its own at the end if nobody
    /// calls <see cref="PlayAttack"/>.</summary>
    public void PlayAttackWindup(float seconds)
    {
        if (_dying) return;
        _windupDur = Mathf.Max(0.05f, seconds);
        _windup = 0f;
    }

    /// <summary>The strike itself: snap from the windup into the lunge.</summary>
    public void PlayAttack()
    {
        if (_dying) return;
        Release();
        _sinceStrike = 0f;
    }

    private void Release()
    {
        _windupDur = 0f;
        _strike = 1f;
    }

    /// <summary>Flash white and recoil.</summary>
    public void PlayHit(float strength = 1f)
    {
        if (_dying) return;
        _hit = Mathf.Max(_hit, Mathf.Clamp(strength, 0.2f, 1f));
        _flash = Mathf.Max(_flash, Mathf.Clamp(strength, 0.3f, 1f));
    }

    /// <summary>Crumple and dissolve, then hide and raise <see cref="DeathFinished"/>.</summary>
    public void PlayDeath()
    {
        if (_dying) return;
        _dying = true;
        _death = 0f;
        _flash = 1f;
        _deathDir = (GetInstanceId() & 1) == 0 ? 1f : -1f;
        if (_player is not null) PlayClip(AnimSlot.Death, 0.1f);
    }

    /// <summary>Double-jump flip: one forward somersault.</summary>
    public void PlayFlip() => _flip = 0f;

    /// <summary>Tint emission (dash streak, slam heat). Alpha is strength.</summary>
    public void SetGlow(Color color, float amount) => _glow = new Color(color.R, color.G, color.B, amount);

    /// <summary>Directly set the white flash (0..1); the player drives its own.</summary>
    public void SetFlash(float amount) => _flash = Mathf.Max(_flash, amount);

    /// <summary>Back to a fresh, living pose. Pools call this on reuse.</summary>
    public void ResetState()
    {
        _dying = false;
        _death = 0f;
        _hit = _flash = _strike = 0f;
        _windupDur = 0f;
        _sinceStrike = -1f;
        _flip = -1f;
        _havePos = false;
        _speed = 0f;
        _glow = new Color(0, 0, 0, 0);
        Visible = true;
        PushShader(force: true);
        if (_player is not null)
        {
            _clip = "";
            PlayClip(AnimSlot.Idle, 0f);
            _hips.Transform = Transform3D.Identity;
        }
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f);
        _time += dt;
        TrackMotion(dt);

        _hit = Mathf.Max(0f, _hit - dt * 5f);
        _flash = Mathf.Max(0f, _flash - dt * 7f);
        _strike = Mathf.Max(0f, _strike - dt * 3.2f);
        if (_windupDur > 0f)
        {
            _windup += dt;
            if (_windup >= _windupDur) Release();
        }
        else if (_sinceStrike >= 0f && AttackCooldown > 0f)
        {
            _sinceStrike += dt;
            float lead = Mathf.Min(0.45f, AttackCooldown * 0.5f);
            if (_sinceStrike >= AttackCooldown - lead)
            {
                _sinceStrike = -1f; // one anticipation per real strike
                PlayAttackWindup(lead);
            }
        }
        if (_flip >= 0f)
        {
            _flip += dt;
            if (_flip > FlipDuration) _flip = -1f;
        }

        if (_dying)
        {
            _death += dt / (_player is not null ? ImportedDeathDuration : DeathDuration);
            if (_death >= 1f)
            {
                _death = 1f;
                Visible = false;
                PushShader();
                DeathFinished?.Invoke(this);
                return;
            }
        }

        PushShader();

        if (_player is not null)
        {
            AnimateImported(dt);
            return;
        }

        // LOD: every few frames, not every frame — the far mesh is swapped in by
        // the engine anyway, this only decides whether posing is worth doing.
        if (Spec.LodDistance > 0f && --_lodTick <= 0)
        {
            _lodTick = 6 + (int)(GetInstanceId() % 4);
            _far = CameraDistanceSq() > Spec.LodDistance * Spec.LodDistance;
        }
        if (_far && !_dying) return;

        switch (Spec.Body)
        {
            case BodyType.Quadruped: PoseQuadruped(dt); break;
            case BodyType.Blob: PoseBlob(dt); break;
            case BodyType.Flyer: PoseFlyer(dt); break;
            case BodyType.Serpent: PoseSerpent(dt); break;
            default: PoseBiped(dt); break;
        }
    }

    private const float DeathDuration = 0.7f;
    private const float ImportedDeathDuration = 1.15f;
    private const float FlipDuration = 0.42f;

    private void TrackMotion(float dt)
    {
        Vector3 v;
        if (ManualMotion) v = Velocity;
        else
        {
            Vector3 p = GlobalPosition;
            v = _havePos && dt > 0f ? (p - _lastPos) / dt : Vector3.Zero;
            _lastPos = p;
            _havePos = true;
            if (v.LengthSquared() > 900f) v = Vector3.Zero; // teleports and pool reuse
        }
        float planar = new Vector2(v.X, v.Z).Length();
        if (_dying) planar = 0f;
        _speed = Mathf.Lerp(_speed, planar, 1f - Mathf.Exp(-10f * dt));
    }

    private float CameraDistanceSq()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != _camFrame)
        {
            _camFrame = frame;
            Camera3D? cam = GetViewport()?.GetCamera3D();
            _haveCam = cam is not null;
            if (cam is not null) _camPos = cam.GlobalPosition;
        }
        return _haveCam ? GlobalPosition.DistanceSquaredTo(_camPos) : 0f;
    }

    private void PushShader(bool force = false)
    {
        if (_geoms.Length == 0 || _player is not null) return;
        float flash = _flash * _flash;
        float dissolve = _dying ? Mathf.SmoothStep(0.15f, 1f, _death) : 0f;
        if (force || Mathf.Abs(flash - _sentFlash) > 0.02f || (flash == 0f && _sentFlash != 0f))
        {
            _sentFlash = flash;
            foreach (GeometryInstance3D g in _geoms) g.SetInstanceShaderParameter(PFlash, flash);
        }
        if (force || Mathf.Abs(dissolve - _sentDissolve) > 0.01f || (dissolve == 0f && _sentDissolve != 0f))
        {
            _sentDissolve = dissolve;
            foreach (GeometryInstance3D g in _geoms) g.SetInstanceShaderParameter(PDissolve, dissolve);
        }
        if (force || !_glow.IsEqualApprox(_sentGlow))
        {
            _sentGlow = _glow;
            foreach (GeometryInstance3D g in _geoms) g.SetInstanceShaderParameter(PGlow, _glow);
        }
    }

    private static readonly StringName PFlash = "flash", PDissolve = "dissolve", PGlow = "glow";

    // ---------------------------------------------------------------- poses

    /// <summary>Shared whole-body terms: the hit recoil, the windup lean, the
    /// strike lunge and the death fall. Every body type adds its own on top.</summary>
    private void Common(float dt, out float pitch, out float lift, out float squash, out float lunge,
                        out float windup01)
    {
        windup01 = _windupDur > 0f ? Mathf.Clamp(_windup / _windupDur, 0f, 1f) : 0f;
        float w = Ease(windup01);
        float s = _strike * _strike;
        pitch = 0.28f * w - 0.45f * s + 0.3f * _hit;
        squash = 1f + 0.07f * w - 0.2f * _hit * (1f - _hit) * 4f * 0.5f;
        lunge = -0.22f * s * _model.Height;
        lift = 0f;
        if (_dying)
        {
            float d = Ease(_death);
            pitch = Mathf.Lerp(pitch, 1.35f * _deathDir, d);
            squash = Mathf.Lerp(squash, 0.55f, d);
            lift = -_model.HipsOffset.Y * 0.55f * d;
        }
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);

    private static float Approach(float current, float target, float rate, float dt)
        => Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

    private void SetHips(float x, float y, float z, float pitch, float roll, float yaw, float squash, float extraPitch = 0f)
    {
        float widen = 1f / Mathf.Sqrt(Mathf.Max(0.3f, squash));
        Basis b = Basis.FromEuler(new Vector3(pitch + extraPitch, yaw, roll)).Scaled(new Vector3(widen, squash, widen));
        _hips.Transform = new Transform3D(b, _model.HipsOffset + new Vector3(x, y, z));
    }

    private static void SetJoint(MeshInstance3D node, Vector3 pivot, float x, float z, float y = 0f)
        => node.Transform = new Transform3D(Basis.FromEuler(new Vector3(x, y, z)), pivot);

    private void PoseBiped(float dt)
    {
        bool brute = Spec.Body == BodyType.Brute;
        float H = _model.Height;
        float stride = Mathf.Max(0.2f, _model.StrideLength);

        // Weights for the hero's verbs ease in and out so poses blend.
        _wAir = Approach(_wAir, Pose == RigPose.Air || (!Grounded && Pose == RigPose.Locomotion) ? 1f : 0f, 12f, dt);
        _wDash = Approach(_wDash, Pose == RigPose.Dash ? 1f : 0f, 22f, dt);
        _wSlide = Approach(_wSlide, Pose == RigPose.Slide ? 1f : 0f, 16f, dt);
        _wSlam = Approach(_wSlam, Pose == RigPose.Slam ? 1f : 0f, 18f, dt);

        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out float wind);

        float run = Mathf.Clamp(_speed / 4.5f, 0f, 1.25f) * (1f - _wAir) * (1f - _wSlide);
        _phase += dt * _speed / stride * Mathf.Tau * (1f - _wSlide);
        float sn = Mathf.Sin(_phase), cs = Mathf.Cos(_phase);
        float swing = _model.LimbSwing * run;

        float legL = sn * swing, legR = -sn * swing;
        float kneeL = Mathf.Max(0f, cs) * 1.1f * run + 0.05f, kneeR = Mathf.Max(0f, -cs) * 1.1f * run + 0.05f;
        float armL = -legL * (brute ? 0.6f : 0.85f), armR = -legR * (brute ? 0.6f : 0.85f);
        float elbowL = 0.25f + 0.55f * run, elbowR = elbowL;
        float splay = (brute ? 0.22f : 0.1f) + 0.04f * Mathf.Sin(_time * 1.7f + _seed);
        float splayL = -splay, splayR = splay;
        float breathe = 1f + 0.022f * Mathf.Sin(_time * 2.3f + _seed) * (1f - run);
        float bob = Mathf.Abs(cs) * 0.035f * H * run;
        float lean = -0.1f * run - (brute ? 0.12f : 0f);
        float roll = sn * 0.05f * run;

        // Windup: arms rise overhead (both for brutes, the weapon arm otherwise);
        // the strike swings them through.
        float w = Ease(wind), s = _strike;
        armR = Mathf.Lerp(armR, 2.5f, w); elbowR = Mathf.Lerp(elbowR, 0.6f, w);
        if (brute) { armL = Mathf.Lerp(armL, 2.5f, w); elbowL = Mathf.Lerp(elbowL, 0.6f, w); }
        armR = Mathf.Lerp(armR, 0.55f, s); elbowR = Mathf.Lerp(elbowR, 0.1f, s);
        if (brute) armL = Mathf.Lerp(armL, 0.55f, s);
        legL = Mathf.Lerp(legL, 0.35f, s * 0.6f); legR = Mathf.Lerp(legR, -0.3f, s * 0.6f);

        // Air: one knee up, arms out for balance.
        float a = _wAir;
        float rising = Mathf.Clamp(Velocity.Y / 6f, -1f, 1f);
        legL = Mathf.Lerp(legL, 0.75f + 0.2f * rising, a); kneeL = Mathf.Lerp(kneeL, 1.3f, a);
        legR = Mathf.Lerp(legR, -0.25f, a); kneeR = Mathf.Lerp(kneeR, 0.45f, a);
        armL = Mathf.Lerp(armL, 0.4f, a); armR = Mathf.Lerp(armR, -0.3f, a);
        splayL = Mathf.Lerp(splayL, -0.75f, a); splayR = Mathf.Lerp(splayR, 0.75f, a);

        // Dash: everything streams back behind a forward-thrown chest.
        float d = _wDash;
        lean = Mathf.Lerp(lean, -0.55f, d);
        armL = Mathf.Lerp(armL, -1.35f, d); armR = Mathf.Lerp(armR, -1.35f, d);
        splayL = Mathf.Lerp(splayL, -0.35f, d); splayR = Mathf.Lerp(splayR, 0.35f, d);
        elbowL = Mathf.Lerp(elbowL, 0.15f, d); elbowR = Mathf.Lerp(elbowR, 0.15f, d);
        legL = Mathf.Lerp(legL, -0.75f, d); kneeL = Mathf.Lerp(kneeL, 0.9f, d);
        legR = Mathf.Lerp(legR, 0.25f, d); kneeR = Mathf.Lerp(kneeR, 0.5f, d);

        // Slide: low, leaning back, lead leg straight out, trail leg folded.
        float sl = _wSlide;
        float drop = -_model.HipsOffset.Y * 0.62f * sl;
        lean = Mathf.Lerp(lean, 0.5f, sl);
        legL = Mathf.Lerp(legL, 1.45f, sl); kneeL = Mathf.Lerp(kneeL, 0.1f, sl);
        legR = Mathf.Lerp(legR, 0.55f, sl); kneeR = Mathf.Lerp(kneeR, 2.0f, sl);
        armL = Mathf.Lerp(armL, 1.1f, sl); splayL = Mathf.Lerp(splayL, -0.6f, sl);
        armR = Mathf.Lerp(armR, -0.7f, sl); splayR = Mathf.Lerp(splayR, 0.25f, sl);

        // Slam: a cannonball — knees to chest, fists overhead, nose down.
        float sm = _wSlam;
        lean = Mathf.Lerp(lean, -0.35f, sm);
        legL = Mathf.Lerp(legL, 1.5f, sm); kneeL = Mathf.Lerp(kneeL, 2.3f, sm);
        legR = Mathf.Lerp(legR, 1.4f, sm); kneeR = Mathf.Lerp(kneeR, 2.3f, sm);
        armL = Mathf.Lerp(armL, 2.9f, sm); armR = Mathf.Lerp(armR, 2.9f, sm);
        splayL = Mathf.Lerp(splayL, -0.2f, sm); splayR = Mathf.Lerp(splayR, 0.2f, sm);
        elbowL = Mathf.Lerp(elbowL, 0.3f, sm); elbowR = Mathf.Lerp(elbowR, 0.3f, sm);

        // Flip: one somersault on the double jump, tucked.
        float flipAngle = 0f;
        if (_flip >= 0f)
        {
            float t = _flip / FlipDuration;
            flipAngle = -Mathf.Tau * (1f - (1f - t) * (1f - t));
            float tuck = Mathf.Sin(t * Mathf.Pi);
            legL = Mathf.Lerp(legL, 1.6f, tuck); legR = Mathf.Lerp(legR, 1.6f, tuck);
            kneeL = Mathf.Lerp(kneeL, 2.2f, tuck); kneeR = Mathf.Lerp(kneeR, 2.2f, tuck);
            armL = Mathf.Lerp(armL, 1.2f, tuck); armR = Mathf.Lerp(armR, 1.2f, tuck);
        }

        if (_dying)
        {
            float dd = Ease(_death);
            armL = Mathf.Lerp(armL, 1.2f * _deathDir, dd); armR = Mathf.Lerp(armR, 0.9f * _deathDir, dd);
            kneeL = Mathf.Lerp(kneeL, 1.2f, dd); kneeR = Mathf.Lerp(kneeR, 0.8f, dd);
        }

        // Flip spins about the body's middle, not the hips.
        float midY = _model.Height * 0.2f;
        Vector3 flipOffset = flipAngle != 0f
            ? new Vector3(0f, midY - midY * Mathf.Cos(flipAngle), midY * Mathf.Sin(flipAngle))
            : Vector3.Zero;
        SetHips(flipOffset.X, bob + lift + drop + flipOffset.Y, lunge + flipOffset.Z, pitch + lean, roll, 0f,
                squash * breathe, flipAngle);

        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part p = ref _parts[i];
            switch (p.Def.Role)
            {
                case PartRole.LegL: Limb(ref p, legL, 0f, -kneeL); break;
                case PartRole.LegR: Limb(ref p, legR, 0f, -kneeR); break;
                case PartRole.ArmL: Limb(ref p, armL, splayL, elbowL); break;
                case PartRole.ArmR: Limb(ref p, armR, splayR, elbowR); break;
                case PartRole.WingL: SetJoint(p.Upper, p.Def.Pivot, 0f, -0.25f + 0.15f * Mathf.Sin(_time * 3f), 0.5f); break;
                case PartRole.WingR: SetJoint(p.Upper, p.Def.Pivot, 0f, 0.25f - 0.15f * Mathf.Sin(_time * 3f), -0.5f); break;
            }
        }
    }

    /// <summary>A limb with an optional second joint. Unjointed limbs fold the
    /// bend into the upper angle so a knee-up still reads.</summary>
    private static void Limb(ref Part p, float x, float z, float bend)
    {
        if (p.Lower is null)
        {
            SetJoint(p.Upper, p.Def.Pivot, x + Mathf.Abs(bend) * 0.3f, z);
            return;
        }
        SetJoint(p.Upper, p.Def.Pivot, x, z);
        SetJoint(p.Lower, p.Def.LowerPivot, bend, 0f);
    }

    private void PoseQuadruped(float dt)
    {
        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out _);
        float stride = Mathf.Max(0.2f, _model.StrideLength);
        _phase += dt * _speed / stride * Mathf.Tau;
        float run = Mathf.Clamp(_speed / 4f, 0f, 1.3f);
        float sn = Mathf.Sin(_phase);
        float swing = _model.LimbSwing * run;
        float bob = Mathf.Abs(Mathf.Cos(_phase)) * 0.04f * _model.Height * run;
        float rock = Mathf.Sin(_phase * 2f) * 0.04f * run;
        float breathe = 1f + 0.02f * Mathf.Sin(_time * 2.6f + _seed) * (1f - Mathf.Min(1f, run));
        // Windup for a four-legged thing is a crouch, not a raised arm.
        float crouch = _windupDur > 0f ? Ease(Mathf.Clamp(_windup / _windupDur, 0f, 1f)) : 0f;
        SetHips(0f, bob + lift - crouch * 0.12f * _model.Height, lunge * 1.4f, pitch * 0.6f + rock - 0.1f * _strike,
                0f, 0f, squash * breathe);

        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part p = ref _parts[i];
            float a = p.Def.Role switch
            {
                PartRole.LegFL or PartRole.LegBR => sn * swing,
                PartRole.LegFR or PartRole.LegBL => -sn * swing,
                _ => 0f,
            };
            if (p.Def.Role is PartRole.WingL or PartRole.WingR)
            {
                SetJoint(p.Upper, p.Def.Pivot, 0f, p.Def.Side * (0.2f + 0.5f * Mathf.Sin(_time * 9f)));
                continue;
            }
            if (_dying) a = Mathf.Lerp(a, (p.Def.Role is PartRole.LegFL or PartRole.LegBL ? -0.6f : 0.6f), _death);
            SetJoint(p.Upper, p.Def.Pivot, a - crouch * 0.3f, 0f);
        }
    }

    private void PoseBlob(float dt)
    {
        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out float wind);
        float stride = Mathf.Max(0.2f, _model.StrideLength);
        float run = Mathf.Clamp(_speed / 3f, 0f, 1.2f);
        _phase += dt * (_speed / stride * Mathf.Tau + 0.5f);
        // Hop: up in the air on |sin|, squashed at each landing.
        float hop = Mathf.Abs(Mathf.Sin(_phase));
        float y = hop * 0.22f * _model.Height * run;
        float sq = 1f + (hop - 0.35f) * 0.28f * run;
        float idle = 1f + 0.05f * Mathf.Sin(_time * 3.1f + _seed) * (1f - Mathf.Min(1f, run));
        float w = Ease(wind);
        SetHips(0f, y + lift, lunge * 1.5f, pitch * 0.5f - 0.12f * run, 0f, 0f,
                squash * sq * idle * (1f - 0.18f * w) + 0.25f * _strike);
    }

    private void PoseFlyer(float dt)
    {
        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out float wind);
        float run = Mathf.Clamp(_speed / 4f, 0f, 1.2f);
        float bob = Mathf.Sin(_time * 2.4f + _seed * 7f) * 0.07f * _model.Height;
        float flapRate = 9f + 4f * run + 8f * _strike;
        float flap = Mathf.Sin(_time * flapRate + _seed * 3f);
        if (_dying) { bob = 0f; flap *= 1f - _death; lift = -_model.HipsOffset.Y * 0.8f * Ease(_death); }
        float w = Ease(wind);
        SetHips(0f, bob + lift + w * 0.1f * _model.Height, lunge * 1.6f,
                pitch - 0.3f * run + 0.25f * w, Mathf.Sin(_time * 1.3f + _seed) * 0.08f, 0f, squash);
        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part p = ref _parts[i];
            float side = p.Def.Side;
            SetJoint(p.Upper, p.Def.Pivot, -0.2f * w, side * (0.15f + flap * 0.7f) - side * 0.5f * w, side * -0.25f);
        }
    }

    private void PoseSerpent(float dt)
    {
        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out float wind);
        float stride = Mathf.Max(0.2f, _model.StrideLength);
        float run = Mathf.Clamp(_speed / 3f, 0.25f, 1.2f);
        _phase += dt * (_speed / stride * Mathf.Tau + 1.2f);
        float amp = _model.Height * 0.12f * run;
        float w = Ease(wind);
        float headSway = Mathf.Sin(_phase + 1.1f) * amp * 0.5f;
        // Windup coils the head back; the strike throws it forward.
        SetHips(headSway, lift, lunge * 1.8f + w * 0.1f * _model.Height, pitch * 0.5f,
                0f, Mathf.Cos(_phase + 1.1f) * 0.25f * run, squash);
        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part p = ref _parts[i];
            float ph = _phase - (p.Def.Index + 1) * 0.85f;
            float x = Mathf.Sin(ph) * amp * (1f + p.Def.Index * 0.12f) - headSway;
            float yaw = Mathf.Cos(ph) * 0.5f * run;
            float sc = p.Def.Scale * (_dying ? 1f - 0.3f * _death : 1f);
            p.Upper.Transform = new Transform3D(Basis.FromEuler(new Vector3(0f, yaw, 0f)).Scaled(Vector3.One * sc),
                                                p.Def.Pivot + new Vector3(x, 0f, 0f));
        }
    }

    // ------------------------------------------------------------- imported

    private void AnimateImported(float dt)
    {
        if (_player is null) return;

        ApplyOverlay(_flash > 0.05f ? ToonMaterials.FlashOverlay : _rimMaterial);

        AnimSlot slot = _dying ? AnimSlot.Death
                      : _strike > 0.45f ? AnimSlot.Attack
                      : _windupDur > 0f ? AnimSlot.Windup
                      : _hit > 0.55f && Spec.Clips.ContainsKey(AnimSlot.Hit) ? AnimSlot.Hit
                      : Pose == RigPose.Dash ? AnimSlot.Dash
                      : Pose == RigPose.Slide ? AnimSlot.Slide
                      : Pose == RigPose.Slam ? AnimSlot.Slam
                      : !Grounded ? (Velocity.Y > 0f ? AnimSlot.Jump : AnimSlot.Fall)
                      : _speed > Spec.RunThreshold ? AnimSlot.Run
                      : _speed > 0.35f ? AnimSlot.Walk
                      : AnimSlot.Idle;
        PlayClip(slot, 0.12f);
        float rate = slot switch
        {
            AnimSlot.Walk => Mathf.Clamp(_speed / 2.2f, 0.6f, 2.2f),
            AnimSlot.Run or AnimSlot.Dash => Mathf.Clamp(_speed / 6f, 0.8f, 1.8f),
            _ => 1f,
        };

        // Animation LOD: near models animate every frame, mid-range every other,
        // far every fourth (with the skipped time folded in, so they keep pace).
        if (Spec.LodDistance > 0f && --_lodTick <= 0)
        {
            _lodTick = 6 + (int)(GetInstanceId() % 4);
            float d2 = CameraDistanceSq();
            float lod = Spec.LodDistance;
            _animStep = d2 > lod * lod ? 4 : d2 > lod * lod * 0.4f ? 2 : 1;
        }
        _animAccum += dt * rate;
        if (++_animCounter >= _animStep || _dying)
        {
            _animCounter = 0;
            _player.Advance(_animAccum);
            _animAccum = 0f;
        }

        // Procedural layer on top of the clip: hit squash, windup lean, strike
        // lunge, the flip, and the death sink.
        Common(dt, out float pitch, out float lift, out float squash, out float lunge, out _);
        float flip = 0f;
        if (_flip >= 0f)
        {
            float t = _flip / FlipDuration;
            flip = -Mathf.Tau * (1f - (1f - t) * (1f - t));
        }
        float y = 0f, roll = 0f;
        if (_dying)
        {
            // The clip (if any) plays the fall; afterwards the body sinks away.
            float sink = Mathf.Clamp((_death - 0.55f) / 0.45f, 0f, 1f);
            y = -sink * _model.Height * 0.9f;
            squash = 1f;
            pitch = 0f;
            lunge = 0f;
            lift = 0f;
            if (!Spec.Clips.ContainsKey(AnimSlot.Death)) roll = Ease(Mathf.Min(1f, _death * 2.5f)) * 1.5f * _deathDir;
        }
        if (Pose == RigPose.Slam) pitch -= 0.5f;
        float mid = _model.Height * 0.45f;
        Vector3 flipOffset = flip != 0f ? new Vector3(0f, mid - mid * Mathf.Cos(flip), mid * Mathf.Sin(flip)) : Vector3.Zero;
        SetHips(0f, y + lift + flipOffset.Y, lunge + flipOffset.Z, pitch * 0.6f, roll, 0f, squash, flip);
    }


    private void PlayClip(AnimSlot slot, float blend)
    {
        if (_player is null) return;
        string? name = ResolveClip(slot);
        if (name is null || name == _clip) return;
        _clip = name;
        _player.Play(name, blend);
    }

    private string? ResolveClip(AnimSlot slot)
    {
        AnimSlot s = slot;
        for (int guard = 0; guard < 4; guard++)
        {
            if (Spec.Clips.TryGetValue(s, out string? n) && _player!.HasAnimation(n)) return n;
            s = s switch
            {
                AnimSlot.Run or AnimSlot.Dash or AnimSlot.Slide => AnimSlot.Walk,
                AnimSlot.Windup or AnimSlot.Slam => AnimSlot.Attack,
                AnimSlot.Fall => AnimSlot.Jump,
                AnimSlot.Walk or AnimSlot.Jump or AnimSlot.Hit or AnimSlot.Attack => AnimSlot.Idle,
                _ => AnimSlot.Idle,
            };
        }
        return null;
    }
}
