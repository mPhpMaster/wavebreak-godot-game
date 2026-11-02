using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Game;
using Wavebreak.Game.Puzzle;

namespace Wavebreak.Net;

/// <summary>
/// Co-op side of the puzzle maps (Game/Puzzle, Game/StageDirector.Puzzle.cs).
///
/// The host owns the puzzle state. It says which puzzle the team is on and
/// for how many players (<c>PuzzleEnter</c>; every machine builds the same
/// map from the seed), sends every changed slot of the state array as
/// (index, value) pairs (<c>PuzzleSet</c>, reliable, so a plate pressed and
/// let go is never seen in the wrong order), and streams the puzzle clock and
/// the exit count twice a second (<c>PuzzleClock</c>, unreliable). Clients ask
/// for actions (<c>PuzzleAsk</c>): push a crate, turn a mirror, pull a lever,
/// strike a bell, ask for a hint. Plates need no message at all: the host
/// already has every avatar's position.
///
/// A late joiner or a survivor reconnecting to a new host gets the whole array
/// and the clock after the world snapshot (<c>PuzzleFull</c>). A client that
/// becomes the host already holds the array, so it simply starts deciding.
/// </summary>
public sealed partial class NetSession
{
    /// <summary>Client: puzzle slots applied from the host.</summary>
    public int PuzzleSlotsApplied { get; private set; }
    /// <summary>Host: actions applied for clients.</summary>
    public int PuzzleAsksFromClients { get; private set; }
    /// <summary>Whole puzzle states applied here (late join, new host).</summary>
    public int PuzzleFullsApplied { get; private set; }

    /// <summary>This machine's lobby slot (0 for the host at the start).</summary>
    public int LocalSlot => Coop.LocalPeer?.Slot ?? 0;

    private bool PuzzleOpen(int index) => _main is not null && IsInstanceValid(_main)
        && _main.Stages is { InPuzzle: true } st && st.PhaseIndex == index && PuzzleMap.Current is not null;

    // ------------------------------------------------------------ host side

    public void SendPuzzleEnter(int index, int players)
    {
        if (!_go) return;
        FlushEvents();
        _chests.Clear();
        _claimedChests.Clear();
        Rpc(MethodName.PuzzleEnter, index, players);
    }

    public void SendPuzzleSet(int[] pairs)
    {
        if (!_go || _main?.Stages is not { } st) return;
        Rpc(MethodName.PuzzleSet, st.PhaseIndex, pairs);
    }

    public void SendPuzzleClock(float elapsed, int inside, int total, float countdown)
    {
        if (!_go || _main?.Stages is not { } st) return;
        Rpc(MethodName.PuzzleClock, st.PhaseIndex, elapsed, inside, total, countdown);
    }

    /// <summary>Host: the whole puzzle to one peer (after its world snapshot).</summary>
    private void SendPuzzle(long peer)
    {
        if (_main?.Stages is not { InPuzzle: true } st || PuzzleMap.Current is not { } map) return;
        map.Flush();
        RpcId(peer, MethodName.PuzzleFull, st.PhaseIndex, map.Plan.Players, map.Elapsed, (int[])map.State.V.Clone());
        GD.Print($"[coop] puzzle state to {Coop.NameOf(peer)}: puzzle {st.PhaseIndex + 1}, {map.State.SolvedCount}/{map.Plan.PuzzleRooms} rooms solved, hash {map.State.Hash()}");
    }

    /// <summary>Host: remote players' positions for the plates (alive ones),
    /// and how many remote players there are.</summary>
    public int PuzzlePositions(List<Vector3> into)
    {
        int n = 0;
        foreach (RemotePlayer r in _remotes.Values)
        {
            if (!IsInstanceValid(r)) continue;
            n++;
            if (r.HasState && !r.Down) into.Add(r.GlobalPosition);
        }
        return n;
    }

    /// <summary>Host: remote players standing where <paramref name="inside"/>
    /// says, and how many remote players there are.</summary>
    public (int Inside, int Total) CountRemotesInside(Func<Vector3, bool> inside)
    {
        int n = 0, total = 0;
        foreach (RemotePlayer r in _remotes.Values)
        {
            if (!IsInstanceValid(r)) continue;
            total++;
            if (r.HasState && !r.Down && inside(r.GlobalPosition)) n++;
        }
        return (n, total);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PuzzleAsk(int index, int action, int room, int a, int b)
    {
        if (!Coop.IsHost || !PuzzleOpen(index) || action is < 1 or > (int)PuzzleAction.Hint) return;
        PuzzleAsksFromClients++;
        PuzzleMap.Current!.Apply((PuzzleAction)action, room, a, b);
    }

    // ---------------------------------------------------------- client side

    /// <summary>Client: ask the host for an action on the current puzzle.</summary>
    public void SendPuzzleAsk(int action, int room, int a, int b)
    {
        if (!Coop.IsClient || _main?.Stages is not { InPuzzle: true } st) return;
        RpcId(1, MethodName.PuzzleAsk, st.PhaseIndex, action, room, a, b);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PuzzleEnter(int index, int players)
    {
        if (!Coop.IsClient || _main is null || !IsInstanceValid(_main)) return;
        _chests.Clear();
        _main.Stages.NetPuzzle(index, players);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PuzzleSet(int index, int[] pairs)
    {
        if (!Coop.IsClient || !PuzzleOpen(index)) return;
        PuzzleState state = PuzzleMap.Current!.State;
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            state.Apply(pairs[i], pairs[i + 1]);
            PuzzleSlotsApplied++;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 1)]
    private void PuzzleClock(int index, float elapsed, int inside, int total, float countdown)
    {
        if (Coop.IsClient && PuzzleOpen(index)) PuzzleMap.Current!.NetClock(elapsed, inside, total, countdown);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PuzzleFull(int index, int players, float elapsed, int[] values)
    {
        if (!Coop.IsClient || _main is null || !IsInstanceValid(_main)) return;
        _main.Stages.NetPuzzle(index, players);
        if (!PuzzleOpen(index)) return;
        PuzzleMap map = PuzzleMap.Current!;
        map.State.ApplyAll(values);
        map.SetElapsed(elapsed);
        map.Adopted();
        PuzzleFullsApplied++;
        GD.Print($"[coop] puzzle state from the host: puzzle {index + 1}, {map.State.SolvedCount}/{map.Plan.PuzzleRooms} rooms solved, hash {map.State.Hash()}");
    }
}
