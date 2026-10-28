using System;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Net;

namespace Wavebreak.Game;

public sealed partial class CoopPolishCheck : Node
{
    private float _clock;
    private int _step;
    private static void Require(bool value, string label)
    {
        GD.Print($"COOP POLISH {(value ? "OK" : "FAIL")} {label}");
        if (!value) throw new InvalidOperationException(label);
    }
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            if (NetSession.Instance is not { Started: true } session || Player.Instance is not { } player
                || session.Remotes.Count != 2 || session.Remotes.Values.Any(remote => !remote.HasState)) return;
            _clock += (float)delta;
            int slot = Coop.LocalPeer!.Slot;
            if (_step == 0)
            {
                player.MoveOverride = static () => Vector3.Zero;
                player.SetPhysicsProcess(false);
                Vector3[] positions = { new(-36, 35, 0), new(36, 50, 0), new(0, 42, 36), new(0, 42, -36) };
                player.Teleport(positions[Math.Clamp(slot, 0, positions.Length - 1)]);
                EnemyManager.DevFrozen = true;
                Main.Instance!.Enemies.ClearField(999);
                EventBus.Publish(new PlayerHealed(7 + slot));
                _step++;
            }
            if (Coop.IsHost && _clock is > 2 and < 4)
                foreach (var peer in Coop.Roster) RunScoreboard.NoteHit(peer.Id, 1 + peer.Slot);
            if (_step == 1 && _clock > 5)
            {
                if (Coop.IsHost) CheckTargets(session, player);
                Require(RunScoreboard.All.Count == 3 && RunScoreboard.All.All(line => line.DamageDealt > 0 && line.Dps > 0 && line.HealingRestored >= 7),
                    "three real peers exchange damage, DPS and restored-health events");
                _step++;
            }
            if (_step == 2 && _clock > 6)
            {
                if (slot == Coop.Roster.Max(peer => peer.Slot)) player.Health.Kill();
                _step++;
            }
            if (_step == 3 && _clock > 8)
            {
                if (Coop.IsHost)
                {
                    CheckTargets(session, player);
                    RemotePlayer down = session.Remotes.Values.Single(remote => remote.Down);
                    Require(!ReferenceEquals(Main.Instance!.Enemies.InspectNearestPlayer(down.GlobalPosition), down), "downed peer is excluded from nearest-player targeting");
                }
                Require(RunScoreboard.All.Any(line => line.Downs > 0), "down state reaches the replicated scoreboard");
                GD.Print("COOP POLISH PASS");
                _step++;
                GetTree().Quit();
            }
        }
        catch (Exception error) { GD.Print("COOP POLISH FAIL " + error); SetPhysicsProcess(false); GetTree().Quit(1); }
    }
    private static void CheckTargets(NetSession session, Player player)
    {
        var living = session.Remotes.Values.Where(remote => remote.IsAlive).Select(remote => ((Node3D)remote, (IDamageable)remote)).ToList();
        if (player.IsAlive) living.Add((player, player));
        Vector3 expected = living.Aggregate(Vector3.Zero, (sum, item) => sum + item.Item1.GlobalPosition) / living.Count;
        EnemyManager enemies = Main.Instance!.Enemies;
        Require(enemies.InspectTeamCenter(Vector3.One * 9999).DistanceTo(expected) < .01f, "spawn center is the mean of every living peer");
        foreach (var target in living)
            Require(ReferenceEquals(enemies.InspectNearestPlayer(target.Item1.GlobalPosition), target.Item2), "nearest target uses actual living player positions in 3D");
    }
}
