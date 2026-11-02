using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Game.Puzzle;

namespace Wavebreak.Net;

/// <summary>
/// The co-op puzzle check (<c>--coop-puzzle-test</c>, tests/coop.ps1
/// scenario <c>coop-puzzle</c>): a host and a guest on the puzzle route,
/// whose first puzzle opens on a twin-plate room (<c>--puzzle-rooms=twin,levers</c>),
/// then a third player who joins in the middle of the puzzle.
///
///   * the guest stands on plate B; the host must see it pressed (its own
///     state, from the guest's avatar) before it steps on plate A, and the
///     guest must see the press come back from the host;
///   * the twin room must be solved by the two of them together: assist off,
///     two players connected;
///   * the guest asks for a hint over the network; everyone must see it;
///   * the host writes the mark file then and both wait; the late joiner must
///     arrive on the same puzzle with the whole state (twin room solved, its
///     door open, the host's player count), and the team carries on;
///   * everyone claims a prize and leaves through the exit together
///     (<c>all</c>) into the rest room.
///
/// The run clock is held on puzzle maps, so this check runs on wall time.
/// </summary>
public sealed partial class NetSession
{
    private bool _ptOn, _ptParsed;
    private bool _ptPlateSeen, _ptPlateEcho, _ptTwinTogether, _ptTwinSolved, _ptHintSent, _ptHintEcho;
    private bool _ptLateState, _ptDoorOpen, _ptPlayersMatch, _ptLeft;
    private double _ptWaitUntil = -1, _ptLeftAt = -1, _ptBSeenAt = -1, _ptFullAt = -1;
    private string _ptExit = "";
    private RewardTier _ptTier;

    private void ParsePuzzleTest()
    {
        if (_ptParsed) return;
        _ptParsed = true;
        _ptOn = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--coop-puzzle-test") >= 0;
        if (_ptOn) Autopilot.PuzzleSteer = PuzzleTestSteer;
    }

    /// <summary>Per frame in a test run (from ProcessTest). True when the
    /// puzzle test has finished this run.</summary>
    private bool StepPuzzleTest()
    {
        ParsePuzzleTest();
        if (!_ptOn || _main is null || !IsInstanceValid(_main)) return false;
        StageDirector st = _main.Stages;
        double now = Now;

        if (PuzzleMap.Current is { } map && st.InPuzzle)
        {
            PuzzleState s = map.State;
            int twin = map.Plan.Rooms.FindIndex(r => r.Kind == RoomKind.Twin);
            if (twin > 0)
            {
                RoomPlan room = map.Plan.Rooms[twin];
                bool bOn = (s.Get(room, PuzzleState.RPressed) & 2) != 0;
                if (Coop.IsHost && bOn && !_ptPlateSeen && _remotes.Count > 0)
                {
                    // The host's own player is not on B: the press is the guest's.
                    Player? me = Player.Instance;
                    if (me is null || room.CellAt(me.GlobalPosition) != room.PlateB)
                    {
                        _ptPlateSeen = true;
                        _ptBSeenAt = now;
                        GD.Print("[coop-test] host sees the guest's plate B pressed");
                    }
                }
                if (Coop.IsClient && bOn) _ptPlateEcho = true;
                if (s.Solved(twin) && !_ptTwinSolved)
                {
                    _ptTwinSolved = true;
                    _ptTwinTogether = s.Get(room, PuzzleState.RAssist) == 0 && map.Plan.Players >= 2;
                    GD.Print($"[coop-test] twin room solved (assist={s.Get(room, PuzzleState.RAssist)} players={map.Plan.Players})");
                    if (Coop.IsHost && _testMarkFile is not null)
                    {
                        using (var f = FileAccess.Open(_testMarkFile, FileAccess.ModeFlags.Write)) f?.StoreString("twin");
                        GD.Print("[coop-test] mark written: twin room solved");
                        _testMarkFile = null;
                    }
                    // Hold the team until the late joiner is in (or a minute has passed).
                    _ptWaitUntil = now + 75;
                }
            }
            if (Coop.IsClient && !_testLateJoinClient && _ptTwinSolved && !_ptHintSent && s.Frontier < map.Plan.Rooms.Count - 1)
            {
                _ptHintSent = true;
                map.RequestHint();
                GD.Print("[coop-test] guest asks for a hint");
            }
            if (s.Hints > 0) _ptHintEcho = true;
            if (_testLateJoinClient && PuzzleFullsApplied > 0 && _ptFullAt < 0) _ptFullAt = now;
            // A second after the state arrived: the doors have had a frame to open.
            if (_testLateJoinClient && _ptFullAt > 0 && now - _ptFullAt >= 1.0 && !_ptLateState)
            {
                _ptLateState = twin > 0 && s.Solved(twin);
                _ptDoorOpen = twin > 0 && map.DoorIsOpen(twin);
                _ptPlayersMatch = map.Plan.Players == 2;
                GD.Print($"[coop-test] late joiner state: twin solved={_ptLateState} door={_ptDoorOpen} players={map.Plan.Players} hash={s.Hash()}");
            }
            // Everyone in: let the team go on a few seconds later.
            if (_ptWaitUntil > 0 && Coop.Roster.Count >= 3 && _ptWaitUntil > now + 8) _ptWaitUntil = now + 8;
            _ptTier = s.Tier;
        }

        if (!_ptLeft && st.InRestRoom && st.PuzzleTiers.Count > 0)
        {
            _ptLeft = true;
            _ptLeftAt = now;
            _ptExit = st.PuzzleExitReason;
            GD.Print($"[coop-test] left the puzzle ({_ptExit})");
        }
        if (_ptLeft)
        {
            // Clients report first; the host once they have gone (or after 20 s).
            if (Coop.IsClient && now - _ptLeftAt >= 3) { TestResult(true, ""); return true; }
            if (Coop.IsHost && now - _ptLeftAt >= 3 && (_remotes.Count == 0 || now - _ptLeftAt >= 20)) { TestResult(true, ""); return true; }
        }
        return true;
    }

    /// <summary>The test's steering: the guest to plate B, the host to plate A
    /// once it has seen B pressed; both hold after the twin room until the
    /// late joiner is in.</summary>
    private Vector3? PuzzleTestSteer(Vector3 me)
    {
        if (PuzzleMap.Current is not { } map || map.State.Finished) return null;
        double now = Now;
        if (_ptWaitUntil > 0 && now < _ptWaitUntil && !_testLateJoinClient) return Vector3.Zero;
        int twin = map.Plan.Rooms.FindIndex(r => r.Kind == RoomKind.Twin);
        if (twin <= 0 || map.State.Solved(twin) || map.State.Frontier != twin || map.Plan.RoomAt(me) != twin) return null;
        RoomPlan room = map.Plan.Rooms[twin];
        // The guest's bot takes plate B on its own (the second slot); the host
        // waits by the door until it has seen B held for a moment, then its
        // bot takes plate A.
        if (Coop.IsClient || _ptPlateSeen && now - _ptBSeenAt >= 1.0) return null;
        Vector3 d = room.World(room.EntryCell) - me;
        d.Y = 0f;
        return d.Length() < 0.3f ? Vector3.Zero : d.Length() < 1f ? d : d.Normalized();
    }

    private string? PuzzleTestLine(string role, List<string> problems)
    {
        ParsePuzzleTest();
        if (!_ptOn) return null;
        if (!_ptLeft) problems.Add("never left the puzzle map for the rest room");
        if (!_ptExit.StartsWith("all:") && role == "host") problems.Add($"left by '{_ptExit}', not with everyone in an exit");
        string fields;
        if (_testLateJoinClient)
        {
            if (PuzzleFullsApplied == 0) problems.Add("no puzzle state arrived");
            if (!_ptLateState) problems.Add("the twin room was not solved in the state it got");
            if (!_ptDoorOpen) problems.Add("the twin room's door was shut");
            if (!_ptPlayersMatch) problems.Add("the puzzle was not built for the host's player count");
            fields = $"role=late puzzle_full={PuzzleFullsApplied} twin_solved={(_ptLateState ? 1 : 0)} door_open={(_ptDoorOpen ? 1 : 0)} players_match={(_ptPlayersMatch ? 1 : 0)}";
        }
        else if (role == "host")
        {
            if (!_ptPlateSeen) problems.Add("never saw the guest press plate B");
            if (!_ptTwinTogether) problems.Add("the twin room was not solved by two players together");
            if (PuzzleAsksFromClients == 0) problems.Add("no puzzle action came from a client");
            if (LateJoins == 0) problems.Add("nobody joined during the puzzle");
            fields = $"role=host plate_seen={(_ptPlateSeen ? 1 : 0)} twin_together={(_ptTwinTogether ? 1 : 0)} client_asks={PuzzleAsksFromClients} late_joins={LateJoins}";
        }
        else
        {
            if (!_ptPlateEcho) problems.Add("never saw its own plate press come back from the host");
            if (!_ptTwinSolved) problems.Add("the twin room was never solved");
            if (!_ptHintEcho) problems.Add("its hint never came back from the host");
            if (PuzzleSlotsApplied == 0) problems.Add("no puzzle state arrived");
            fields = $"role=client plate_echo={(_ptPlateEcho ? 1 : 0)} twin_solved={(_ptTwinSolved ? 1 : 0)} hint_echo={(_ptHintEcho ? 1 : 0)} slots={PuzzleSlotsApplied}";
        }
        return $"{fields} tier={_ptTier} exit={(_ptExit.Length > 0 ? _ptExit : "-")} puzzles={_main?.Stages.PuzzlesVisited ?? 0}";
    }
}
