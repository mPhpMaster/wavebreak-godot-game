using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// The perks' visuals, all small and brief. Three shapes only: a flat ground
/// ring (stomp, pulse, coin burst, last light), a thin vertical beam
/// (lighthouse), and small orbs (motes, bubbles, wake puddles are flat discs).
///
/// Rules, from <see cref="FxGuard"/>: nothing stands between the camera and the
/// player (rings lie on the ground, at most <see cref="FxGuard.MaxCentredRadius"/>
/// wide when centred on the player), everything fades through
/// <see cref="GraphicsSettings.FxFade"/> and the materials carry the near-camera
/// fade (<see cref="CombatVisuals.Glow"/>), and the number of live effects is
/// capped, so a busy late build cannot fill the screen.
/// </summary>
public sealed partial class PerkProcs
{
    private const int MaxFx = 24;

    private sealed class Fx
    {
        public MeshInstance3D Mesh = null!;
        public float Age, Life, From, To, Alpha;
        public bool Flat;
    }

    private readonly List<Fx> _fx = new(MaxFx);
    private readonly Stack<MeshInstance3D> _spare = new();

    private static readonly CylinderMesh BeamMesh = new()
    {
        TopRadius = 1f,
        BottomRadius = 1f,
        Height = 1f,
        RadialSegments = 10,
        Rings = 1,
    };

    private MeshInstance3D Take(Mesh mesh, Color tint)
    {
        MeshInstance3D m = _spare.Count > 0 ? _spare.Pop() : NewMesh();
        m.Mesh = mesh;
        m.MaterialOverride = CombatVisuals.Glow(tint, additive: true);
        m.Visible = true;
        return m;
    }

    private MeshInstance3D NewMesh()
    {
        var m = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(m);
        return m;
    }

    private void Give(MeshInstance3D m)
    {
        if (!IsInstanceValid(m)) return;
        m.Visible = false;
        _spare.Push(m);
    }

    /// <summary>A flat ring on the ground (<paramref name="at"/> is a foot position) that grows from a small disc to
    /// <paramref name="radius"/> and fades out.</summary>
    private void Ring(Vector3 at, float radius, Color tint, float life = 0.35f, bool centredOnPlayer = false)
    {
        if (_fx.Count >= MaxFx) return;
        float r = Mathf.Min(radius, centredOnPlayer ? FxGuard.MaxCentredRadius : FxGuard.MaxBlobRadius * 1.6f);
        MeshInstance3D m = Take(CombatVisuals.Disc, tint);
        m.GlobalTransform = new Transform3D(Basis.Identity, at + Vector3.Up * FxGuard.GroundLift);
        _fx.Add(new Fx { Mesh = m, Life = life, From = r * 0.3f, To = r, Alpha = 0.55f, Flat = true });
        Step(_fx[^1], 0f);
    }

    /// <summary>A thin pillar of light over <paramref name="at"/> (Lighthouse Keeper).</summary>
    private void Beam(Vector3 at, Color tint, float life)
    {
        if (_fx.Count >= MaxFx) return;
        MeshInstance3D m = Take(BeamMesh, tint);
        const float height = 26f;
        m.GlobalTransform = new Transform3D(Basis.Identity.Scaled(new Vector3(0.22f, height, 0.22f)),
                                            at + Vector3.Up * (height * 0.5f));
        _fx.Add(new Fx { Mesh = m, Life = life, From = 0.22f, To = 0.22f, Alpha = 0.45f, Flat = false });
        Step(_fx[^1], 0f);
    }

    private static void Step(Fx f, float dt)
    {
        f.Age += dt;
        float t = Mathf.Clamp(f.Age / f.Life, 0f, 1f);
        if (f.Flat)
        {
            float eased = 1f - (1f - t) * (1f - t);
            float r = Mathf.Lerp(f.From, f.To, eased);
            f.Mesh.Scale = new Vector3(r, 1f, r);
        }
        // Fade in for the first tenth, out for the rest.
        float own = t < 0.1f ? 1f - (1f - f.Alpha) * (t / 0.1f) : Mathf.Lerp(1f - f.Alpha, 1f, (t - 0.1f) / 0.9f);
        f.Mesh.Transparency = GraphicsSettings.FxFade(own);
    }

    private void TickFx(float dt)
    {
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            Fx f = _fx[i];
            if (!IsInstanceValid(f.Mesh)) { _fx.RemoveAt(i); continue; }
            Step(f, dt);
            if (f.Age < f.Life) continue;
            Give(f.Mesh);
            _fx.RemoveAt(i);
        }
    }

    /// <summary>Live effects right now (the self-check reads it to prove the cap).</summary>
    public int LiveFx => _fx.Count + _motes.Count + _bubbles.Count + _wake.Count;
}
