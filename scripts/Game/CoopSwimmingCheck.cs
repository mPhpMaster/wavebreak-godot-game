using Godot;
using Wavebreak.Actors;
using Wavebreak.Net;

namespace Wavebreak.Game;

/// <summary>Optional real ENet peer-state check layered onto the co-op harness.</summary>
public sealed partial class CoopSwimmingCheck : Node
{
    private float _elapsed;
    private bool _remoteSwam;
    private bool _remoteRose;
    private bool _enemyRose;

    public override void _PhysicsProcess(double delta)
    {
        if (NetSession.Instance is not { Started: true } session || Player.Instance is not { } player) return;
        _elapsed += (float)delta;
        if (_elapsed < 2f) Input.ActionPress("jump");
        else Input.ActionRelease("jump");
        if (_elapsed >= 2f && _elapsed < 4f) Input.ActionPress("slide");
        else Input.ActionRelease("slide");
        foreach (RemotePlayer remote in session.Remotes.Values)
        {
            _remoteSwam |= remote.IsSwimming;
            _remoteRose |= remote.IsSwimming && remote.GlobalPosition.Y > 2f;
        }
        if (EnemyManager.Instance is { } enemies)
            foreach (Enemy enemy in enemies.Alive)
                _enemyRose |= enemy.GlobalPosition.Y > 1.5f;
        if (_elapsed < 7f) return;
        bool ok = player.IsSwimming && Coop.RouteId == "reef-dive" && Arena.Current.Id == "lumenreef"
            && _remoteSwam && _remoteRose && player.Jumps == 0 && player.AirJumpsUsed == 0
            && (!Coop.IsHost || _enemyRose);
        GD.Print($"COOP SWIM {(ok ? "PASS" : "FAIL")} role={Coop.Mode} route={Coop.RouteId} "
            + $"remote_pose={_remoteSwam} remote_vertical={_remoteRose} enemy_vertical={_enemyRose} jumps={player.Jumps}");
        SetPhysicsProcess(false);
    }

    public override void _ExitTree()
    {
        Input.ActionRelease("jump");
        Input.ActionRelease("slide");
    }
}
