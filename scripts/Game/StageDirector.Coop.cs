using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// Co-op client side of the phase flow. The host runs the waves -> boss ->
/// portal machine (a client's <see cref="Tick"/> returns early) and sends each
/// step; these replay it here so the map, the HUD's boss bar and the portal
/// arrow match the host's.
/// </summary>
public sealed partial class StageDirector
{
    /// <summary>The host entered phase <paramref name="index"/>.</summary>
    public void NetPhase(int index)
    {
        if (index == _index || index < 0 || index >= _stages.Count) return;
        Log($"host moved to phase {index + 1}");
        EnterPhase(index);
    }

    /// <summary>The host's phase boss arrived.</summary>
    public void NetBoss(Enemy? boss, EnemyDef? def)
    {
        if (State != PhaseState.Waves) return;
        State = PhaseState.Boss;
        _bossAt = _run.PhaseSeconds;
        _boss = boss;
        _bossDef = def ?? boss?.Def;
        if (_bossDef is not null) _fought.Add(_bossDef.Id);
        EventBus.Publish(new PhaseBossArrived(_index + 1, _bossDef?.DisplayName ?? ""));
    }

    /// <summary>The host's portal countdown (twice a second).</summary>
    public void NetPortalTimer(float secondsLeft)
    {
        if (State == PhaseState.PortalOpen) _portalLeft = Mathf.Max(0f, secondsLeft);
    }

    /// <summary>The host's phase boss died: open the portal (or win) here too.</summary>
    public void NetBossDown()
    {
        if (State is not (PhaseState.Boss or PhaseState.Waves)) return;
        OnBossKilled();
    }
}
