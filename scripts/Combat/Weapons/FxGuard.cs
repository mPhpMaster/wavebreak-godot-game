using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// Keeps weapon effects from covering the player or the screen, whoever fired
/// them (the local player, or a teammate's effects drawn on this machine).
///
/// Two layers:
///
/// 1. Size caps, applied where each effect is built (the numbers below). The
///    hit area is untouched; only what is drawn is capped. Big effects lie flat
///    on the ground instead of standing up: a late build with area tomes used
///    to scale a vortex's core into a dome taller than the camera.
///
/// 2. A near-camera fade on every weapon material (<see cref="Track"/>):
///    <see cref="BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha"/> makes each
///    pixel closer to the camera than the player fade out, fully gone at about
///    half the camera's distance. Anything sitting between the camera and the
///    player is therefore see-through by construction, whatever its size. The
///    distances follow the camera's zoom (<see cref="Update"/>, once a frame).
///
/// On top of both, everything still fades through
/// <see cref="Meta.GraphicsSettings.FxFade"/> (the "Weapon effects opacity" option).
/// </summary>
internal static class FxGuard
{
    /// <summary>Visual radius cap for fields centred on a player (auras, pools under them).</summary>
    public const float MaxCentredRadius = 4.5f;

    /// <summary>Visual radius cap for one blob (vortex, explosion shell).</summary>
    public const float MaxBlobRadius = 2.8f;

    /// <summary>Visual reach and width cap for a swipe or breath sheet.</summary>
    public const float MaxSheet = 5f;

    /// <summary>Visual cap for a held blade (orbiters, boomerangs).</summary>
    public const float MaxBlade = 1.5f;

    /// <summary>Height above the feet a ground effect is drawn at.</summary>
    public const float GroundLift = 0.06f;

    /// <summary>Muzzle height over the feet (<see cref="WeaponBehaviors.Ground"/>).</summary>
    public const float MuzzleHeight = 0.85f;

    private static readonly List<BaseMaterial3D> Materials = new();

    // The line of sight this frame: camera to the player's chest.
    private static Vector3 _eye, _chest;
    private static bool _haveSight;
    private static float _distance = -1f;

    /// <summary>Give a weapon material the near-camera fade. Returns it.</summary>
    public static T Track<T>(T material) where T : BaseMaterial3D
    {
        material.DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha;
        Apply(material, _distance > 0f ? _distance : 10f);
        Materials.Add(material);
        return material;
    }

    /// <summary>Follow the camera's distance to the player. Cheap: the
    /// materials are touched only when it moved by more than a quarter metre.</summary>
    public static void Update()
    {
        if (Actors.PlayerCamera.Instance is not { } cam || !GodotObject.IsInstanceValid(cam)) return;
        if (Actors.Player.Instance is not { } player || !GodotObject.IsInstanceValid(player)) return;
        Camera3D? camera = cam.GetViewport()?.GetCamera3D();
        if (camera is null) return;
        _eye = camera.GlobalPosition;
        _chest = player.GlobalPosition + Vector3.Up * 0.9f;
        _haveSight = true;
        float d = _eye.DistanceTo(_chest);
        if (MathF.Abs(d - _distance) < 0.25f) return;
        if (_distance < 0f && Meta.Launch.Automated) GD.Print($"FX near-camera fade on: camera {d:0.0} m from the player, {Materials.Count} materials");
        _distance = d;
        for (int i = Materials.Count - 1; i >= 0; i--)
        {
            if (!GodotObject.IsInstanceValid(Materials[i])) { Materials.RemoveAt(i); continue; }
            Apply(Materials[i], d);
        }
    }

    private static void Apply(BaseMaterial3D m, float d)
    {
        // Invisible nearer than 60% of the way to the player, opaque only at
        // the player's own distance and beyond.
        m.DistanceFadeMinDistance = MathF.Max(0.5f, d * 0.60f);
        m.DistanceFadeMaxDistance = MathF.Max(1.0f, d * 1.0f);
    }

    /// <summary>How much an effect of visual radius <paramref name="radius"/>
    /// at <paramref name="at"/> blocks the camera's view of the local player:
    /// 0 when it is off the line of sight, up to 0.9 when it sits on it.
    /// Used as a floor on its transparency.</summary>
    public static float BlocksView(Vector3 at, float radius)
    {
        if (!_haveSight) return 0f;
        Vector3 line = _chest - _eye;
        float len = line.Length();
        if (len < 0.5f) return 0f;
        Vector3 dir = line / len;
        float along = (at - _eye).Dot(dir);
        if (along < 0f || along > len - 0.4f) return 0f;   // behind the camera or past the player
        float off = (at - (_eye + dir * along)).Length();
        float reach = radius + 0.9f;
        if (off >= reach) return 0f;
        return 0.9f * (1f - off / reach * 0.6f);
    }

    /// <summary>Forget the last frame's line of sight (scene change).</summary>
    public static void Reset() => _haveSight = false;

    /// <summary>Extra transparency for a big effect sitting on top of a
    /// player: 0 away from everyone, up to 0.6 when centred on one.</summary>
    public static float OverPlayer(Vector3 at, float radius)
    {
        if (radius < 1.2f || Actors.Player.Instance is not { } p || !GodotObject.IsInstanceValid(p)) return 0f;
        Vector3 d = p.GlobalPosition - at;
        d.Y = 0f;
        float r = MathF.Max(radius, 1.5f);
        return Mathf.Clamp(1f - d.Length() / (r * 1.6f), 0f, 1f) * 0.6f;
    }
}
