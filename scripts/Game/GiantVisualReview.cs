using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Game;

public sealed partial class GiantVisualReview : Node
{
    private readonly Player _player;
    private readonly string _prefix;
    public GiantVisualReview(Player player, string prefix) { _player = player; _prefix = prefix; }
    public override void _Ready() => _ = Run();
    private async Task Wait(float t) => await ToSignal(GetTree().CreateTimer(t), SceneTreeTimer.SignalName.Timeout);
    private async Task Run()
    {
        await Wait(2f); _player.SetPhysicsProcess(false);
        EnemyManager.DevFrozen = true; EnemyManager manager = EnemyManager.Instance!;
        manager.ClearField(999f);
        foreach (string id in new[] { "city_bellwarden", "city_gatewarden", "city_archon" })
        {
            manager.ClearField(999f);
            Enemy giant = manager.SpawnAt(Catalog.EnemyById(id)!, new Vector3(0, 0, -48))!;
            giant.Rotation = Vector3.Zero;
            _player.Teleport(new Vector3(0, giant.Def!.Height * .83f, -38));
            PlayerCamera.Instance!.FaceDirection(Vector3.Forward); PlayerCamera.Instance.SetPitchDegrees(0);
            PlayerCamera.Instance.SnapToTarget(); await Wait(.6f);
            await Screenshot.Capture(GetTree(), this, _prefix + "-" + id + "-close.png");
            _player.Teleport(new Vector3(0, 1, -8));
            PlayerCamera.Instance.SetPitchDegrees(12); PlayerCamera.Instance.SnapToTarget(); await Wait(.5f);
            await Screenshot.Capture(GetTree(), this, _prefix + "-" + id + "-scale.png");
        }
        GD.Print("GIANT REVIEW COMPLETE"); GetTree().Quit();
    }
}
