using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;

namespace Wavebreak.Combat;

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
        // The shell is drawn capped and squashed into a low dome: a blast
        // scaled by area tomes used to swallow the player and the camera.
        fx._radius = Mathf.Clamp(radius, 0.3f, Weapons.FxGuard.MaxBlobRadius);
        fx._retired = false;

        fx.GlobalTransform = new Transform3D(Basis.Identity, position);
        fx._mesh.MaterialOverride = CombatVisuals.Glow(tint, additive: true);
        fx._mesh.Transparency = Wavebreak.Meta.GraphicsSettings.FxFade(0.25f);
        fx._mesh.Scale = Flat(fx._radius * 0.35f);

        fx._light.LightColor = tint;
        fx._light.OmniRange = fx._radius * 2.6f;
        fx._light.LightEnergy = 4f;
        fx.Visible = true;

        Live.Add(fx);
    }

    private static Vector3 Flat(float r) => new(r, r * 0.55f, r);

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
                    fx._mesh.Scale = Flat(fx._radius * (0.35f + 0.65f * eased));
                    fx._mesh.Transparency = Wavebreak.Meta.GraphicsSettings.FxFade(0.25f + 0.75f * t);
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

