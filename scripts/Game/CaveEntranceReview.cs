using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

public sealed partial class CaveEntranceReview : Node
{
    private readonly Player _player;
    private readonly string _prefix;
    public CaveEntranceReview(Player player, string prefix) { _player = player; _prefix = prefix; }
    public override void _Ready() => _ = Run();
    private async Task Wait(float s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
    private async Task Run()
    {
        await Wait(6.5f);
        _player.SetPhysicsProcess(false);
        EnemyManager.DevFrozen = true;
        EnemyManager.Instance!.ClearField(999f);
        int index = 0;
        foreach (Vector3 offset in new[] { new Vector3(-30, 3, 0), new Vector3(-8, 1, 0), new Vector3(-12, -12, 0) })
        {
            _player.Teleport(ReefCavePath.Entrance + offset);
            PlayerCamera.Instance!.FaceDirection(Vector3.Right);
            PlayerCamera.Instance.SetPitchDegrees(index == 2 ? 22f : -5f);
            PlayerCamera.Instance.SnapToTarget();
            await Wait(0.5f);
            await Screenshot.Capture(GetTree(), this, _prefix + "-" + index++ + ".png");
        }
        GD.Print("CAVE REVIEW COMPLETE"); GetTree().Quit();
    }
}
