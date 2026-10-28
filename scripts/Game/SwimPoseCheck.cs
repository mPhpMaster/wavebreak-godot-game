using Godot;
using Wavebreak.Actors;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public sealed partial class SwimPoseCheck : Node
{
    private readonly Player _player;
    private int _step, _jumps;
    private float _time, _baseline, _vertical;
    private bool _passed = true;
    public SwimPoseCheck(Player player) => _player = player;

    private void Check(bool value, string label)
    { _passed &= value; GD.Print($"SWIMPOSE {(value ? "OK" : "FAIL")} {label}"); }

    public override void _PhysicsProcess(double delta)
    {
        _time += (float)delta;
        if (_step == 0 && _time > 0.3f)
        {
            Main.Instance!.SetProcess(false);
            Main.Instance.Enemies.ClearField(999); Main.Instance.Enemies.Quiet = true;
            Vector3 up = SwimmingNavigation.Wish(new Vector2(0f, -1f), 0f, 0.8f);
            var reinforced = SwimmingNavigation.WithVertical(up, true, false);
            var downward = SwimmingNavigation.WithVertical(-up, false, true);
            Check(reinforced.Reinforced && downward.Reinforced && reinforced.SpeedScale == 1.12f
                && downward.SpeedScale == 1.12f && reinforced.Direction.Dot(up) > 0.999f,
                "up/down reinforcement is a fixed 12 percent boost without changing aim");
            Check(SwimmingNavigation.WithVertical(Vector3.Zero, true, false).SpeedScale == 1f
                && SwimmingNavigation.WithVertical(up, false, true).SpeedScale == 1f
                && SwimmingNavigation.WithVertical(up, true, true).SpeedScale == 1f
                && SwimmingNavigation.WithVertical(Vector3.Zero, false, false).Direction == Vector3.Zero,
                "vertical-only, opposing, both-held and idle inputs cannot stack a boost");
            Check(reinforced.Direction.Length() <= 1.001f && 6f * reinforced.SpeedScale < Player.BreachSpeed,
                "boost is bounded and alone remains below the default breach threshold");
            _jumps = _player.Jumps + _player.AirJumpsUsed;
            _player.Teleport(Vector3.Up * 8f);
            PlayerCamera.Instance?.SetPitchDegrees(0f);
            Input.ActionPress("move_forward"); _step++;
        }
        else if (_step == 1 && _time > 1.1f)
        {
            Check(Mathf.Abs(_player.SwimmingPitch + Mathf.Pi * 0.5f) < 0.2f, "level travel poses the entire body horizontally");
            PlayerCamera.Instance?.SetPitchDegrees(65f); _step++;
        }
        else if (_step == 2 && _time > 2.2f)
        {
            Check(_player.SwimmingPitch > -0.8f && _player.Velocity.Y > 3f, "ascending swimmer points head-first upward");
            _baseline = _player.Velocity.Length(); _vertical = _player.Velocity.Y;
            Input.ActionPress("jump"); _step++;
        }
        else if (_step == 3 && _time > 3f)
        {
            Check(_player.SwimmingBoostActive && _player.Velocity.Length() > _baseline + 0.3f
                && _player.Velocity.Length() < _baseline * 1.14f && _player.Velocity.Y > _vertical,
                "real forward-plus-rise input produces the modest upward speed increase");
            Input.ActionRelease("jump"); PlayerCamera.Instance?.SetPitchDegrees(-65f); _step++;
        }
        else if (_step == 4 && _time > 4.3f)
        {
            Check(_player.SwimmingPitch < -2.3f && _player.Velocity.Y < -3f, "diving swimmer points head-first downward");
            _baseline = _player.Velocity.Length(); _vertical = -_player.Velocity.Y;
            Input.ActionPress("slide"); _step++;
        }
        else if (_step == 5 && _time > 5.1f)
        {
            Check(_player.SwimmingBoostActive && _player.Velocity.Length() > _baseline + 0.3f
                && _player.Velocity.Length() < _baseline * 1.14f && -_player.Velocity.Y > _vertical,
                "real forward-plus-dive input produces the modest downward speed increase");
            Input.ActionRelease("slide"); Input.ActionRelease("move_forward");
            _player.Teleport(Vector3.Up * 8f); PlayerCamera.Instance?.SetPitchDegrees(65f); _step++;
        }
        else if (_step == 6 && _time > 5.8f)
        {
            Check(!_player.SwimmingBoostActive && Mathf.Abs(_player.SwimmingPitch + Mathf.Pi * 0.5f) < 0.3f,
                "idle settles smoothly into a neutral horizontal float");
            Check(_player.Rotation.X == 0f && _player.Rotation.Z == 0f
                && Mathf.Abs(PlayerCamera.Instance!.Pitch - Mathf.DegToRad(65f)) < 0.01f,
                "visual pitch never rotates the capsule or changes camera look");
            Check(_player.Jumps + _player.AirJumpsUsed == _jumps && _player.SwimmingAnimationVerified,
                "no normal jump, slide or running animation is introduced");
            Check(SwimmingPresentation.TargetPitch(Vector3.Up) > -0.01f
                && SwimmingPresentation.TargetPitch(Vector3.Down) < -3.13f,
                "the shared local/remote pose supports full vertical alignment");
            GD.Print(_passed ? "SWIMPOSE PASS" : "SWIMPOSE FAIL");
            GetTree().Quit(_passed ? 0 : 1); _step++;
        }
    }

    public override void _ExitTree()
    { Input.ActionRelease("jump"); Input.ActionRelease("slide"); Input.ActionRelease("move_forward"); }
}

