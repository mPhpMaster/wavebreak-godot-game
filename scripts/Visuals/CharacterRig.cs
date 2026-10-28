using System;
using Godot;

namespace Wavebreak.Visuals;

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

}
