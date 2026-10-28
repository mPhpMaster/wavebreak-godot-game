using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Net;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>Real ENet check for shared cave discovery and host-owned enemy health.</summary>
public sealed partial class CoopCaveUiCheck : Node
{
    private float _time;
    private int _step;
    private Enemy? _target;
    private readonly NearbyEnemySelection _selection = new();
    private static readonly Vector3 Origin = new(-25, 20, -15);

    public override void _PhysicsProcess(double delta)
    {
        if (NetSession.Instance is not { Started: true } session || Player.Instance is not { } player
            || ReefCaveDiscovery.Instance is not { } cave || EnemyManager.Instance is not { } enemies) return;
        _time += (float)delta;
        if (_step == 0 && _time > 0.5f)
        {
            player.MoveOverride = static () => Vector3.Zero;
            player.Teleport(Coop.IsHost ? ReefCavePath.Entrance + Vector3.Left * 10 : Origin);
            if (Coop.IsHost) FogOfWar.Instance!.Reveal(new Vector2(ReefCavePath.Entrance.X, ReefCavePath.Entrance.Z), 30);
            _step++;
        }
        if (_step == 1 && _time > 2)
        {
            player.Teleport(Origin);
            PlayerCamera.Instance!.FaceDirection(Vector3.Forward); PlayerCamera.Instance.SetPitchDegrees(-15); PlayerCamera.Instance.SnapToTarget();
            FogOfWar.Instance!.Reveal(new Vector2(Origin.X, Origin.Z), 32);
            if (Coop.IsHost)
            {
                EnemyManager.DevFrozen = true;
                _target = enemies.SpawnAt(Catalog.EnemyById("carapace_bastion")!, Origin + new Vector3(-3, 0, -6));
                if (_target is not null) _target.TakeDamage(new DamageInfo { Amount = _target.Health * 0.5f, CanCrit = false, Knockback = 0 });
            }
            _step++;
        }
        if (_time < (Coop.IsHost ? 11 : 10)) return;
        _selection.Refresh(player, GetViewport().GetCamera3D(), enemies.AliveList);
        bool health = _selection.Visible.Any(e => e.Def?.Id == "carapace_bastion" && e.HealthFraction > 0 && e.HealthFraction < 0.8f);
        bool pass = cave.Discovered(CaveMapMarkerKind.Entrance) && !cave.Discovered(CaveMapMarkerKind.Exit)
            && health && (Coop.IsHost || session.CaveDiscoveryUpdates > 0);
        GD.Print($"COOP CAVEUI {(pass ? "PASS" : "FAIL")} role={Coop.Mode} entrance={cave.Discovered(CaveMapMarkerKind.Entrance)} "
            + $"exit={cave.Discovered(CaveMapMarkerKind.Exit)} health={health} discovery_packets={session.CaveDiscoveryUpdates}");
        SetPhysicsProcess(false); GetTree().Quit(pass ? 0 : 1);
    }
}
