using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

/// <summary>
/// Host migration and late join for the phase flow
/// (Net/NetSession.Migration.cs, NetSession.Snapshot.cs).
///
/// A client already replays every step of the host's phase machine (Phase,
/// Boss, BossDown, RestEnter), so it holds the map, the boss, the portal and
/// the rest room. What it lacks are the host's private timers; the host sends
/// those in the run snapshot once a second (<see cref="WriteHostTimers"/>),
/// and a client that becomes the host takes them over
/// (<see cref="PromoteToHost"/>). A player joining mid-run is brought to the
/// host's phase and state with the same replay calls
/// (<see cref="CatchUpMap"/>, <see cref="CatchUpState"/>).
/// </summary>
public sealed partial class StageDirector
{
    /// <summary>Floats in <see cref="WriteHostTimers"/>.</summary>
    public const int HostTimersLength = 8;

    /// <summary>Host: phase index, state, boss arrival time, portal and rest
    /// countdowns, the swarm clock, the endless loop flag and the rest room's
    /// next map.</summary>
    public void WriteHostTimers(List<float> into)
    {
        into.Add(_index);
        into.Add((int)State);
        into.Add(_bossAt);
        into.Add(_portalLeft);
        into.Add(_restCountdown);
        into.Add(Swarm?.Seconds ?? 0f);
        into.Add(_loopRestart ? 1f : 0f);
        into.Add(_restNext);
    }

    /// <summary>New host: carry on the old host's phase machine from its last
    /// timers. The boss puppet (already promoted to a real enemy) stays the
    /// phase boss; if it is gone, the boss is sent again in a second.</summary>
    public void PromoteToHost(float[] t, int o)
    {
        bool have = o >= 0 && o + HostTimersLength <= t.Length && (int)t[o] == _index;
        if (have)
        {
            _bossAt = t[o + 2];
            if (State == PhaseState.RestRoom) _restCountdown = t[o + 4];
            if (Swarm is not null) Swarm.NetSetSeconds(t[o + 5]);
        }
        _portalSyncIn = 0f;
        _restStatusTimer = 0f;
        _nextBossReport = _run.PhaseSeconds + 30f;
        if (State == PhaseState.Boss && (_boss is null || !_boss.IsAlive))
        {
            _boss = _enemies.ActiveBoss;
            if (_boss is null)
            {
                Log("new host: the boss was not here; sending it again");
                _retryAt = _run.PhaseSeconds + 1f;
            }
        }
        Log($"took over the phase flow as the new host (phase {_index + 1}, {State})");
    }

    /// <summary>Late join / reconnect, first half: be on the host's map (or
    /// in its rest room). Run before the snapshot's enemies are placed, since
    /// a map change clears the field.</summary>
    public void CatchUpMap(int index, PhaseState state)
    {
        if (state == PhaseState.RestRoom)
        {
            if (State != PhaseState.RestRoom) NetRestRoom(index);
            return;
        }
        if ((index != _index || State == PhaseState.RestRoom) && index >= 0 && index < _stages.Count)
        {
            Log($"catching up with the host: phase {index + 1}");
            if (index == _index) _index = -2;   // force the rebuild out of the room
            EnterPhase(index);
        }
    }

    /// <summary>Late join / reconnect, second half: the boss, the open portal
    /// or the Final Swarm, as on the host. <paramref name="boss"/> is the
    /// boss's puppet, already placed from the snapshot.</summary>
    public void CatchUpState(PhaseState state, Enemy? boss, float portalLeft, float swarmSeconds)
    {
        switch (state)
        {
            case PhaseState.Boss:
                if (State == PhaseState.Waves) NetBoss(boss, boss?.Def);
                else if (State == PhaseState.Boss && boss is not null) _boss = boss;
                break;
            case PhaseState.PortalOpen:
                if (State is PhaseState.Waves or PhaseState.Boss) NetBossDown();
                NetPortalTimer(portalLeft);
                break;
            case PhaseState.FinalSwarm:
            case PhaseState.Victory:
                if (State is PhaseState.Waves or PhaseState.Boss) NetBossDown();
                if (Swarm is not null) Swarm.NetSetSeconds(swarmSeconds);
                break;
        }
    }

    /// <summary>The team's current anchor for a late joiner: the host's spot is
    /// sent, this keeps it on the floor of the current map or room.</summary>
    public Vector3 ClampToPlay(Vector3 at)
    {
        if (State == PhaseState.RestRoom) return RestRoom.SpawnPoint;
        return at.Y < -5f ? Arena.Centre + Vector3.Up * 0.3f : at + Vector3.Up * 0.3f;
    }
}
