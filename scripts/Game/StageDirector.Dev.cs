using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>Developer-panel controls over the phase machine (UI/DevPanel).
/// Each one goes through the same private steps the timer uses, so a jump
/// looks exactly like the real transition.</summary>
public sealed partial class StageDirector
{
    public int PhaseIndex => _index;
    public int PhaseCount => _stages.Count;
    public IReadOnlyList<StageDef> Route => _stages;

    /// <summary>Go to phase <paramref name="phase"/> (1-based) now: new map,
    /// field cleared, player on the centre pad.</summary>
    public void DevJumpToPhase(int phase)
    {
        if (_run.Over) return;
        Won = false;
        Swarm = null;
        Log($"[dev] jump to phase {phase}");
        EnterPhase(Mathf.Clamp(phase - 1, 0, _stages.Count - 1));
    }

    /// <summary>End the wave timer and send this phase's boss now. False when a
    /// boss is already up or the phase is past its boss.</summary>
    public bool DevBossNow()
    {
        if (_run.Over || State != PhaseState.Waves) return false;
        State = PhaseState.Boss;
        _bossAt = _run.PhaseSeconds;
        _nextBossReport = _bossAt + 30f;
        _retryAt = 0f;
        _enemies.ClearField(1f);
        SendBoss();
        Log("[dev] boss now");
        return true;
    }

    /// <summary>Count the phase boss as beaten: the portal opens (or, on the
    /// last map, the run is won and the Final Swarm begins).</summary>
    public bool DevOpenPortal()
    {
        if (_run.Over || State is not (PhaseState.Waves or PhaseState.Boss)) return false;
        _boss?.Despawn();
        OnBossKilled();
        Log("[dev] portal opened");
        return true;
    }

    /// <summary>Start the endless Final Swarm on the current map.</summary>
    public bool DevStartFinalSwarm()
    {
        if (_run.Over || State == PhaseState.FinalSwarm) return false;
        _boss?.Despawn();
        _boss = null;
        if (_portal is not null && GodotObject.IsInstanceValid(_portal)) _portal.QueueFree();
        _portal = null;
        Won = true;
        Swarm = new FinalSwarm(_run, _enemies, Stage);
        State = PhaseState.FinalSwarm;
        Swarm.Begin();
        Log("[dev] final swarm");
        return true;
    }

    /// <summary>Win the run on the next tick (the normal victory path).</summary>
    public void DevWin()
    {
        if (_run.Over) return;
        Won = true;
        State = PhaseState.Victory;
        _victoryIn = 0f;
        Log("[dev] win");
    }

    /// <summary>Swap the rest of the run onto <paramref name="route"/> and
    /// rebuild the current phase on its map.</summary>
    public void DevSetRoute(IReadOnlyList<StageDef> route)
    {
        if (_run.Over || route.Count == 0) return;
        _stages = route;
        DevJumpToPhase(Mathf.Clamp(_index + 1, 1, route.Count));
    }

    /// <summary>Replace only the current phase's map with <paramref name="map"/>
    /// (scaled for this phase) and rebuild it.</summary>
    public void DevSetMap(StageDef map)
    {
        if (_run.Over) return;
        var list = new List<StageDef>(_stages);
        int i = Mathf.Clamp(_index, 0, list.Count - 1);
        list[i] = Stages.ForPhase(map, i);
        _stages = list;
        DevJumpToPhase(i + 1);
    }

    /// <summary>The current map, for spawners that parent to it.</summary>
    public Node3D? DevArena => _arena is not null && GodotObject.IsInstanceValid(_arena) ? _arena : null;
}
