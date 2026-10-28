using Godot;
using Wavebreak.Audio;
using Wavebreak.Data;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    private readonly MeshInstance3D?[] _hookTips = new MeshInstance3D?[2];
    private readonly HookLaunchAudio?[] _hookLaunchAudio = new HookLaunchAudio?[2];
    public bool LeftHookFiring => _hooks[0].Firing;
    public bool RightHookFiring => _hooks[1].Firing;
    internal Vector3 HookTip(HookSide side) => _hooks[(int)side].Firing ? _hooks[(int)side].Tip : _hooks[(int)side].Anchor;

    private void BeginHookFlight(HookSide side, Vector3 anchor)
    {
        if (!AttachHook(side, anchor)) return;
        int index = (int)side; HookAttachment hook = _hooks[index];
        hook.Attached = false; hook.Firing = true; hook.FlightTime = 0;
        hook.LaunchOrigin = hook.Tip = HookOrigin(side);
        hook.FlightDuration = Mathf.Clamp(hook.LaunchOrigin.DistanceTo(anchor) / 240f, .08f, .4f);
        if (_hookLaunchAudio[index] is null)
        { _hookLaunchAudio[index] = new HookLaunchAudio(); AddChild(_hookLaunchAudio[index]!); }
        _hookLaunchAudio[index]!.Play();
    }

    private void StepHookFlight(HookAttachment hook, float dt)
    {
        if (!hook.Firing) return;
        hook.FlightTime += dt;
        hook.Tip = hook.LaunchOrigin.Lerp(hook.Anchor, Mathf.Clamp(hook.FlightTime / hook.FlightDuration, 0, 1));
        if (hook.FlightTime < hook.FlightDuration) return;
        hook.Firing = false; hook.Attached = true;
        hook.Length = CenterPosition.DistanceTo(hook.Anchor);
    }

    private void DrawHookTip(int index)
    {
        HookAttachment hook = _hooks[index];
        if (_hookTips[index] is null && hook.Firing)
        {
            var tip = new MeshInstance3D { TopLevel = true,
                Mesh = new SphereMesh { Radius = .12f, Height = .24f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.8f, .9f, 1), Metallic = .8f },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(tip); _hookTips[index] = tip;
        }
        if (_hookTips[index] is not { } model) return;
        model.Visible = hook.Firing && IsAlive;
        if (model.Visible) model.GlobalPosition = hook.Tip;
    }
}
