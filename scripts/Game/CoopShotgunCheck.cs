using System;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Net;

namespace Wavebreak.Game;

public sealed partial class CoopShotgunCheck : Node
{
    private float _elapsed, _nextShot;
    private bool _initialized, _observed;
    public override void _PhysicsProcess(double delta)
    {
        if (NetSession.Instance is not { Started: true } session || Player.Instance is not { } player
            || session.Remotes.Count == 0 || session.Remotes.Values.Any(remote => !remote.HasState)) return;
        if (!_initialized)
        {
            Main.Instance!.SetProcess(false); Main.Instance.Enemies.ClearField(999); Main.Instance.Enemies.Quiet = true;
            player.MoveOverride = static () => Vector3.Zero;
            player.Teleport(new Vector3(Coop.IsHost ? -4 : 4, 74, 0));
            Input.ActionPress("slide"); _initialized = true;
        }
        _elapsed += (float)delta;
        if (_elapsed >= _nextShot) { player.ShotgunFeedback(Vector3.Down); _nextShot = _elapsed + .1f; }
        _observed |= session.Remotes.Values.All(remote => remote.ShotgunPosesReceived >= 8
            && remote.ShotgunGripError < .02f && remote.IsSwimming && remote.GlobalPosition.Y < 65);
        if (_elapsed < 4) return;
        bool ok = _observed && player.IsSwimming && !player.IsBreaching && player.CenterPosition.Y < 60
            && player.ShotgunRecoilSpeed <= .801f;
        GD.Print($"COOP SHOTGUN {(ok ? "PASS" : "FAIL")} role={Coop.Mode} remotePose={_observed} depth={WaterSurfaceHeight.At(player.GlobalPosition) - player.CenterPosition.Y:0.0}");
        Input.ActionRelease("slide"); SetPhysicsProcess(false);
        GetTree().CreateTimer(1).Timeout += () => GetTree().Quit(ok ? 0 : 1);
    }
    public override void _ExitTree() => Input.ActionRelease("slide");
}
