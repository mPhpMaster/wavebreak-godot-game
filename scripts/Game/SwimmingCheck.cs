using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>Real controller checks, enabled only by --check-swim on the reef.</summary>
public sealed partial class SwimmingCheck : Node
{
    private readonly Player _player;
    private StageDef _map;
    private float _elapsed;
    private int _step;
    private int _jumps;
    private float _high;
    private int _animationFrames;
    private bool _passed = true;

    public SwimmingCheck(Player player)
    {
        _player = player;
        _map = Arena.Current;
    }

    private void Check(bool condition, string label)
    {
        _passed &= condition;
        GD.Print($"SWIM {(condition ? "OK" : "FAIL")} {label}");
    }

    public override void _PhysicsProcess(double delta)
    {
        _elapsed += (float)delta;
        if (_step == 0 && _elapsed > 0.3f)
        {
            _map = Arena.Current;
            // Measure idle depth control without autopilot movement or a random shotgun kick.
            Main.Instance!.Enemies.ClearField(999);
            Main.Instance.Enemies.Quiet = true;
            _player.MoveOverride = static () => Vector3.Zero;
            Check(_player.IsSwimming && _map.Underwater, "map enables swimming");
            ReefGeometryCheck.Run(_player, Check);
            _player.Teleport(new Vector3(0f, 2f, 0f));
            _jumps = _player.Jumps + _player.AirJumpsUsed;
            _animationFrames = _player.SwimmingAnimationFrames;
            Input.ActionPress("jump");
            _step++;
        }
        else if (_step == 1 && _elapsed > 1.3f)
        {
            _high = _player.GlobalPosition.Y;
            Check(_player.SwimmingAnimationVerified && _player.SwimmingAnimationFrames > _animationFrames,
                "swim joints animate and walk/sprint clips are stopped");
            Check(_high > 4f && (_player.NetPoseFlags & 32) != 0, "held rise moves vertically and replicates swimming");
            Input.ActionRelease("jump");
            _step++;
        }
        else if (_step == 2 && _elapsed > 2.3f)
        {
            Check(Mathf.Abs(_player.Velocity.Y) < 0.1f && _player.GlobalPosition.Y >= _high,
                "release holds depth without gravity");
            Input.ActionPress("slide");
            _step++;
        }
        else if (_step == 3 && _elapsed > 3.3f)
        {
            Check(_player.GlobalPosition.Y < _high - 1f && !_player.IsSlamming && !_player.IsSliding,
                "held dive descends without slam or slide");
            Check(_player.Jumps + _player.AirJumpsUsed == _jumps, "no normal or air jump in water");
            Input.ActionRelease("slide");
            _player.Teleport(Vector3.Up * (WaterSurfaceHeight.At(Vector3.Zero) + 6f));
            _step++;
        }
        else if (_step == 4 && _elapsed > 3.5f)
        {
            Check(_player.IsBreaching && _player.Velocity.Y < 0f, "above-water recovery uses airborne gravity without a ceiling clamp");
            _player.Teleport(Vector3.Down * 10f);
            _step++;
        }
        else if (_step == 5 && _elapsed > 3.7f)
        {
            Check(_player.GlobalPosition.Y >= Player.SwimFloor, "bounded water floor");
            var valley = new Vector3(20f, 0f, -64f);
            valley.Y = UnderwaterTerrain.Height(valley) + 0.1f;
            _player.Teleport(valley);
            _step++;
        }
        else if (_step == 6 && _elapsed > 3.9f)
        {
            Check(_player.GlobalPosition.Y < -8f
                && _player.GlobalPosition.Y >= UnderwaterTerrain.Height(_player.GlobalPosition) + Player.SwimFloor - 0.1f,
                "swimming reaches deep valleys and follows the actual seabed");
            Arena.UseBounds(Stages.Run[0]);
            _player.Teleport(Vector3.Up * 0.1f);
            _step++;
        }
        else if (_step == 7 && _elapsed > 4.3f)
        {
            Check(!_player.IsSwimming && (_player.NetPoseFlags & 32) == 0
                && _player.MotionMode == CharacterBody3D.MotionModeEnum.Grounded, "exit restores ground controller and network pose");
            _player.RequestJump();
            _step++;
        }
        else if (_step == 8 && _elapsed > 4.8f)
        {
            Check(_player.Jumps + _player.AirJumpsUsed > _jumps, "ordinary map still jumps");
            Arena.UseBounds(_map);
            _player.Teleport(Vector3.Up * 2f);
            _step++;
        }
        else if (_step == 9 && _elapsed > 5.1f)
        {
            Check(_player.IsSwimming && !_player.IsSlamming && !_player.IsSliding, "re-entry clears ground movement state");
            GD.Print(_passed ? "SWIM PASS" : "SWIM FAIL");
            GetTree().Quit(_passed ? 0 : 1);
            _step++;
        }
    }

    public override void _ExitTree()
    {
        Input.ActionRelease("jump");
        Input.ActionRelease("slide");
        Arena.UseBounds(_map);
    }
}
