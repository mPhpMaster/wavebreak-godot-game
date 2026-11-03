using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Game.Puzzle;
using Wavebreak.Game.Puzzle.Kinds;

namespace Wavebreak.Net;

/// <summary>
/// The co-op puzzle check (<c>--coop-puzzle-test</c>, tests/coop.ps1
/// scenario <c>coop-puzzle</c>): a host and a guest on the puzzle route,
/// whose first puzzle opens on a lantern path then a lamp room
/// (<c>--puzzle-rooms=trail,levers</c>), then a third player who joins in the
/// middle of the puzzle. No room needs two players; either one solves.
///
///   * the lantern path is solved by the GUEST alone, by its body: the host
///     holds back in the strip by the entrance and must see tiles light from
///     the guest's avatar while it stands on none, and the guest must see its
///     own steps come back from the host;
///   * the lamp room is solved by the HOST alone (its own asks) while the
///     guest holds back;
///   * the guest asks for a hint over the network; everyone must see it;
///   * the host writes the mark file once the path is lit and both wait; the
///     late joiner must arrive on the same puzzle with the whole state (path
///     solved, its door open, the host's player count), and the team carries on;
///   * everyone claims a prize and leaves through the exit together
///     (<c>all</c>) into the rest room.
///
/// The run clock is held on puzzle maps, so this check runs on wall time.
/// </summary>
public sealed partial class NetSession
{
    private bool _ptOn, _ptParsed;
    private bool _ptTileSeen, _ptTileEcho, _ptPathByGuest, _ptPathSolved, _ptLampsByHost, _ptHintSent, _ptHintEcho;
    private bool _ptHostOnTile;
    private int _ptHostAsksInLamps = -1;
    private bool _ptLateState, _ptDoorOpen, _ptPlayersMatch, _ptLeft;
    private double _ptWaitUntil = -1, _ptLeftAt = -1, _ptFullAt = -1;
    private string _ptExit = "";
    private RewardTier _ptTier;

    private void ParsePuzzleTest()
    {
        if (_ptParsed) return;
        _ptParsed = true;
        _ptOn = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--coop-puzzle-test") >= 0;
        if (!_ptOn) return;
        Autopilot.PuzzleSteer = PuzzleTestSteer;
        Autopilot.PuzzleLeaderOverride = PuzzleTestLeader;
    }

    private static int TrailRoom(PuzzleMap map) => map.Plan.Rooms.FindIndex(r => r.Def is TrailKind);
    private static int LampRoom(PuzzleMap map) => map.Plan.Rooms.FindIndex(r => r.Def is LeversKind);

    /// <summary>Who works which room: the guest the lantern path, the host
    /// the lamp room; the late joiner neither.</summary>
    private bool? PuzzleTestLeader(int room)
    {
        if (PuzzleMap.Current is not { } map) return null;
        if (_testLateJoinClient) return false;
        if (room == TrailRoom(map)) return Coop.IsClient;
        if (room == LampRoom(map)) return Coop.IsHost;
        return null;
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
            int trail = TrailRoom(map), lamps = LampRoom(map);
            if (trail > 0)
            {
                RoomPlan room = map.Plan.Rooms[trail];
                int lit = s.Get(room, PuzzleState.RA);
                Player? me = Player.Instance;
                bool meOnTile = me is not null && room.CellAt(me.GlobalPosition) is int c && c >= 0
                                && System.Array.IndexOf(room.Layout<TrailKind.Data>().Cells, c) >= 0;
                if (Coop.IsHost && !s.Solved(trail)) _ptHostOnTile |= meOnTile && lit != 0;
                if (Coop.IsHost && lit != 0 && !meOnTile && !_ptTileSeen && _remotes.Count > 0)
                {
                    _ptTileSeen = true;
                    GD.Print("[coop-test] host sees the guest's steps light the path");
                }
                if (Coop.IsClient && !_testLateJoinClient && lit != 0) _ptTileEcho = true;
                if (s.Solved(trail) && !_ptPathSolved)
                {
                    _ptPathSolved = true;
                    _ptPathByGuest = Coop.IsHost ? !_ptHostOnTile && _ptTileSeen : true;
                    GD.Print($"[coop-test] lantern path solved (host stood on a tile: {_ptHostOnTile})");
                    if (Coop.IsHost && _testMarkFile is not null)
                    {
                        using (var f = FileAccess.Open(_testMarkFile, FileAccess.ModeFlags.Write)) f?.StoreString("trail");
                        GD.Print("[coop-test] mark written: lantern path solved");
                        _testMarkFile = null;
                    }
                    // Hold the team until the late joiner is in (or a minute has passed).
                    _ptWaitUntil = now + 75;
                }
            }
            if (lamps > 0 && Coop.IsHost)
            {
                if (s.Frontier == lamps && _ptHostAsksInLamps < 0) _ptHostAsksInLamps = map.AsksApplied;
                if (s.Solved(lamps) && !_ptLampsByHost && _ptHostAsksInLamps >= 0)
                {
                    // Solved by the host's own asks (a client's hint is an ask too, so more than one).
                    _ptLampsByHost = map.AsksApplied - _ptHostAsksInLamps >= 2;
                    GD.Print($"[coop-test] lamp room solved by the host ({map.AsksApplied - _ptHostAsksInLamps} asks)");
                }
            }
            if (Coop.IsClient && !_testLateJoinClient && _ptPathSolved && !_ptHintSent && s.Frontier < map.Plan.Rooms.Count - 1)
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
                _ptLateState = trail > 0 && s.Solved(trail);
                _ptDoorOpen = trail > 0 && map.DoorIsOpen(trail);
                _ptPlayersMatch = map.Plan.Players == 2;
                GD.Print($"[coop-test] late joiner state: path solved={_ptLateState} door={_ptDoorOpen} players={map.Plan.Players} hash={s.Hash()}");
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

    /// <summary>The test's steering: everyone holds still after the path is lit
    /// until the late joiner is in.</summary>
    private Vector3? PuzzleTestSteer(Vector3 me)
    {
        if (PuzzleMap.Current is not { } map || map.State.Finished) return null;
        double now = Now;
        if (_ptWaitUntil > 0 && now < _ptWaitUntil && !_testLateJoinClient) return Vector3.Zero;
        return null;
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
            if (!_ptLateState) problems.Add("the lantern path was not solved in the state it got");
            if (!_ptDoorOpen) problems.Add("the lantern path's door was shut");
            if (!_ptPlayersMatch) problems.Add("the puzzle was not built for the host's player count");
            fields = $"role=late puzzle_full={PuzzleFullsApplied} path_solved={(_ptLateState ? 1 : 0)} door_open={(_ptDoorOpen ? 1 : 0)} players_match={(_ptPlayersMatch ? 1 : 0)}";
        }
        else if (role == "host")
        {
            if (!_ptTileSeen) problems.Add("never saw the guest's steps light a tile");
            if (!_ptPathByGuest) problems.Add("the lantern path was not solved by the guest alone");
            if (!_ptLampsByHost) problems.Add("the lamp room was not solved by the host alone");
            if (PuzzleAsksFromClients == 0) problems.Add("no puzzle action came from a client");
            if (LateJoins == 0) problems.Add("nobody joined during the puzzle");
            fields = $"role=host tile_seen={(_ptTileSeen ? 1 : 0)} path_by_guest={(_ptPathByGuest ? 1 : 0)} lamps_by_host={(_ptLampsByHost ? 1 : 0)} client_asks={PuzzleAsksFromClients} late_joins={LateJoins}";
        }
        else
        {
            if (!_ptTileEcho) problems.Add("never saw its own steps come back from the host");
            if (!_ptPathSolved) problems.Add("the lantern path was never solved");
            if (!_ptHintEcho) problems.Add("its hint never came back from the host");
            if (PuzzleSlotsApplied == 0) problems.Add("no puzzle state arrived");
            fields = $"role=client tile_echo={(_ptTileEcho ? 1 : 0)} path_solved={(_ptPathSolved ? 1 : 0)} hint_echo={(_ptHintEcho ? 1 : 0)} slots={PuzzleSlotsApplied}";
        }
        return $"{fields} tier={_ptTier} exit={(_ptExit.Length > 0 ? _ptExit : "-")} puzzles={_main?.Stages.PuzzlesVisited ?? 0}";
    }
}
