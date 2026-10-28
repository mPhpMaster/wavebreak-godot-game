using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

public sealed partial class BreachCheck : Node
{
    private readonly Player _player;
    private float _time, _startY, _peak;
    private int _step, _breaches, _entries, _jumps;
    private bool _passed = true, _rose, _fell, _airPose;

    public BreachCheck(Player player) => _player = player;

    private void Check(bool value, string label)
    { _passed &= value; GD.Print($"BREACH {(value ? "OK" : "FAIL")} {label}"); }

    public override void _PhysicsProcess(double delta)
    {
        _time += (float)delta;
        if (_step == 0 && _time > 0.4f)
        {
            Check(WaterSurfaceHeight.BaseHeight - UnderwaterTerrain.Height(Vector3.Zero) >= 60f,
                "at least 60m of usable water at the central seabed");
            Check(SwimmingNavigation.Wish(new Vector2(0f, -1f), 0f, 0.8f).Y > 0.6f
                && SwimmingNavigation.Wish(new Vector2(0f, -1f), 0f, -0.8f).Y < -0.6f,
                "forward follows upward and downward camera look");
            Check(SwimmingNavigation.Wish(Vector2.Zero, 0f, 1f) == Vector3.Zero
                && Mathf.Abs(SwimmingNavigation.Wish(Vector2.Right, 0f, 1f).Y) < 0.001f,
                "looking alone does not move; strafe stays lateral");
            _player.Teleport(Vector3.Up * 3f); _startY = 3f;
            PlayerCamera.Instance?.SetPitchDegrees(55f);
            Input.ActionPress("move_forward"); _step++;
        }
        else if (_step == 1 && _time > 1f)
        {
            Check(_player.GlobalPosition.Y > _startY + 0.8f, "real forward input ascends without rise input");
            _startY = _player.GlobalPosition.Y;
            PlayerCamera.Instance?.SetPitchDegrees(-55f); _step++;
        }
        else if (_step == 2 && _time > 1.6f)
        {
            Check(_player.Velocity.Y < -1f, "real forward input descends when looking down");
            Input.ActionRelease("move_forward");
            _player.Teleport(Vector3.Up * 8f); PlayerCamera.Instance?.SetPitchDegrees(65f);
            _step++;
        }
        else if (_step == 3 && _time > 2.1f)
        {
            Check(Mathf.Abs(_player.GlobalPosition.Y - 8f) < 0.1f, "idle look preserves depth");
            _breaches = _player.Breaches; _entries = _player.WaterEntries;
            _jumps = _player.Jumps + _player.AirJumpsUsed;
            _player.Teleport(Vector3.Up * (WaterSurfaceHeight.At(Vector3.Zero) - Player.BodyHeight * 0.5f - 1f));
            Input.ActionPress("jump"); _step++;
        }
        else if (_step == 4 && _time > 3.5f)
        {
            Check(!_player.IsBreaching && _player.Breaches == _breaches, "slow ascent treads at the moving surface without jumping");
            _player.Teleport(Vector3.Up * (WaterSurfaceHeight.At(Vector3.Zero) - Player.BodyHeight * 0.5f - 1.5f));
            _player.RequestDash(); _step++;
        }
        if (_step == 5)
        {
            if (_player.IsBreaching)
            {
                _rose |= _player.Velocity.Y > Player.BreachSpeed;
                _fell |= _player.Velocity.Y < -2f;
                _airPose |= (_player.NetPoseFlags & 64) != 0;
                _peak = Mathf.Max(_peak, _player.CenterPosition.Y - WaterSurfaceHeight.At(_player.GlobalPosition));
                Input.ActionRelease("jump");
            }
            if (_time > 7f)
            {
                Check(_rose && _player.Breaches > _breaches, "fast upward dash preserves momentum through the surface");
                Check(_peak > 3f && _fell && _airPose, "airborne apex, gravity-driven fall and replicated breach pose");
                Check(_player.WaterEntries > _entries && !_player.IsBreaching, "natural re-entry returns to buoyant swimming");
                Check(_player.Jumps + _player.AirJumpsUsed == _jumps, "no ordinary or air jump was used");
                Check(_player.SwimmingAnimationVerified, "swimming stroke resumes after re-entry");
                GD.Print(_passed ? "BREACH PASS" : "BREACH FAIL");
                GetTree().Quit(_passed ? 0 : 1); _step++;
            }
        }
    }

    public override void _ExitTree()
    { Input.ActionRelease("move_forward"); Input.ActionRelease("jump"); }
}
