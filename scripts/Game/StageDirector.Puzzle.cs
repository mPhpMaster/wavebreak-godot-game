using Godot;
using Wavebreak.Core;
using Wavebreak.Game.Puzzle;

namespace Wavebreak.Game;

/// <summary>
/// The puzzle route (Meta/Routes "puzzle"): before every action map comes a
/// puzzle map, with a rest room on either side of it:
///
///   PUZZLE 1 -> rest -> MAP 1 -> boss portal -> rest -> PUZZLE 2 -> rest -> MAP 2 ... -> MAP n (final boss)
///
/// The action maps are exactly Classic's, at Classic's difficulty, so the
/// route adds a different kind of play between them rather than a different
/// curve. A puzzle map stands in for the map node like the rest room does
/// (<see cref="PuzzleMap"/>), and like the rest room it holds the run clock:
/// thinking costs nothing on the difficulty curve. The few guards it sends are
/// sized for the map that follows.
///
/// <see cref="_index"/> stays the action map's index throughout: puzzle i
/// leads into map i, so the HUD, the refills and the boss plan see the run
/// they always did. Endless ignores the route's puzzles (an endless loop is
/// about the clock), so there it plays as Classic.
/// </summary>
public sealed partial class StageDirector
{
    private PuzzleMap? _puzzle;
    /// <summary>The rest room leads into a puzzle (after an action map) rather than a map.</summary>
    private bool _restIntoPuzzle;
    /// <summary>The rest room we are in came after a puzzle (it leads to map <see cref="_index"/>).</summary>
    private bool _restAfterPuzzle;

    /// <summary>This run alternates puzzle and action maps.</summary>
    public static bool PuzzleRoute => Meta.Routes.Active?.Id == Meta.Routes.PuzzleId && !Meta.EndlessMode.Active;

    public bool InPuzzle => State == PhaseState.Puzzle;

    /// <summary>The puzzle map in the tree, or null.</summary>
    public PuzzleMap? Puzzle => _puzzle is not null && GodotObject.IsInstanceValid(_puzzle) && !_puzzle.IsQueuedForDeletion() ? _puzzle : null;

    public int PuzzlesVisited { get; private set; }

    /// <summary>The rest room we are in came after a puzzle (late join reads it).</summary>
    public bool RestAfterPuzzle => State == PhaseState.RestRoom && _restAfterPuzzle;

    /// <summary>How the last puzzle map was left: "exit"/"skip" (single
    /// player), "all"/"timeout" (co-op), with the tier after a colon.</summary>
    public string PuzzleExitReason { get; private set; } = "";

    /// <summary>Tiers of the puzzles left so far this run, in order.</summary>
    public System.Collections.Generic.List<RewardTier> PuzzleTiers { get; } = new();

    /// <summary>Players a puzzle is built for: everyone in the session, the
    /// same count on every machine.</summary>
    private static int PuzzlePlayers => Net.Coop.InRun ? Mathf.Max(1, Net.Coop.Roster.Count) : 1;

    /// <summary>--puzzle-rooms, or the rooms a check forced (kept for a rebuild).</summary>
    private string? _puzzleForced;

    /// <summary>The finished puzzle's tier is already in <see cref="PuzzleTiers"/>.</summary>
    private bool _puzzleTierRecorded;

    private void EnterPuzzle(int index, int players = -1, string? forced = null)
    {
        _enemies.Quiet = false;
        _index = Mathf.Clamp(index, 0, _stages.Count - 1);
        FreeRestRoom();
        FreePuzzle();
        if (_portal is not null && GodotObject.IsInstanceValid(_portal)) _portal.QueueFree();
        _portal = null;
        _boss = null;
        _bossDef = null;
        if (_arena is not null && GodotObject.IsInstanceValid(_arena))
        {
            _root.RemoveChild(_arena);
            _arena.QueueFree();
        }
        _arena = null;
        ChestSpawner.Instance?.Clear();
        Interactables.Instance?.Clear();

        Actors.Pickup.AttractAll();
        _enemies.ClearField(ArrivalBreather);
        // The guards are sized for the map this puzzle leads into.
        ApplyScaling(1f);

        if (players < 1) players = PuzzlePlayers;
        _puzzleForced = forced ?? PuzzleGen.ForcedRooms;
        PuzzlePlan plan = PlanPuzzle(players);
        _puzzle = new PuzzleMap(plan, NextAfterPuzzle);
        _root.AddChild(_puzzle);
        _root.MoveChild(_puzzle, 0);
        Arena.UseBounds(_puzzle.Bounds);

        _player.Teleport(_puzzle.SpawnPoint + Net.Coop.SpawnOffset);
        _camera.FaceDirection(_puzzle.ArrivalFacing);
        _camera.SnapToTarget();
        if (Net.Coop.InRun && !_player.IsAlive) _player.Health.Revive(1f);

        State = PhaseState.Puzzle;
        PuzzlesVisited++;
        Log($"puzzle {_index + 1}/{_stages.Count} '{plan.Name}' (d={plan.Difficulty}, {plan.PuzzleRooms} rooms: {plan.Describe()}, players={players}); then '{NextAfterPuzzle}'");
        EventBus.Publish(new PuzzleStarted(_index + 1, _stages.Count, plan.Name));
        if (Net.Coop.IsHost) Net.NetSession.Instance?.SendPuzzleEnter(_index, players);
        if (Net.Coop.InRun) Net.NetSession.Instance?.PlaceRemotes(_puzzle.SpawnPoint);
    }

    /// <summary>Host and single-player: leave when the team stands in an exit.</summary>
    private void StepPuzzle(float dt)
    {
        if (Puzzle is not { } map) { LeavePuzzle("lost"); return; }
        if (map.WantsToLeave(dt, out string why)) LeavePuzzle(why);
    }

    /// <summary>The plan for puzzle <see cref="_index"/> of this run (every machine builds the same).</summary>
    private PuzzlePlan PlanPuzzle(int players)
    {
        int tier = Meta.DifficultyTiers.IndexOf(Meta.DifficultyTiers.Active);
        return PuzzleGen.ForRun(_run.Seed, _index, tier, players, _puzzleForced, gauntlet: GauntletRoute);
    }

    /// <summary>Where the team goes after this puzzle (the map it stands
    /// before; in the Puzzle Gauntlet, the next puzzle).</summary>
    private string NextAfterPuzzle => GauntletRoute
        ? (_index + 1 < _stages.Count ? PuzzleGen.NameFor(_run.Seed, _index + 1) : "")
        : _stages[_index].DisplayName;

    private void LeavePuzzle(string why)
    {
        if (Puzzle is { } map) map.Close();   // unfinished becomes a skip; the final state goes out first
        RewardTier tier = Puzzle?.State.Tier ?? RewardTier.Skipped;
        PuzzleExitReason = $"{why}:{tier}";
        if (GauntletRoute)
        {
            LeaveGauntletPuzzle(why, tier);   // StageDirector.Gauntlet.cs
            return;
        }
        Log($"leaving the puzzle ({why}, {tier}) -> map {_index + 1}/{_stages.Count}");
        if (RestRoomEnabled) EnterRestRoom(afterPuzzle: true);
        else EnterPhase(_index);
    }

    /// <summary>The puzzle map leaves the tree (any new map, room or puzzle).
    /// Every machine pays its own player here: an unclaimed prize, or a skip's
    /// consolation.</summary>
    private void FreePuzzle()
    {
        if (_puzzle is not null && GodotObject.IsInstanceValid(_puzzle))
        {
            if (!_puzzleTierRecorded) PuzzleTiers.Add(_puzzle.State.Tier == RewardTier.None ? RewardTier.Skipped : _puzzle.State.Tier);
            _puzzleTierRecorded = false;
            _puzzle.GrantOnLeave();
            _root.RemoveChild(_puzzle);
            _puzzle.QueueFree();
        }
        _puzzle = null;
        PuzzleMap.SetStatus(-1, 0, -1f);
    }

    // ------------------------------------------------------------- co-op

    /// <summary>Client: the host entered puzzle <paramref name="index"/>,
    /// built for <paramref name="players"/> players.</summary>
    public void NetPuzzle(int index, int players)
    {
        if (index < 0 || index >= _stages.Count) return;
        if (State == PhaseState.Puzzle && _index == index && Puzzle is { } have)
        {
            if (have.Plan.Players != players) RebuildPuzzle(players);
            return;
        }
        Log($"host moved to puzzle {index + 1}");
        EnterPuzzle(index, players);
    }

    /// <summary>Client already on this puzzle, built for another player count
    /// (a late joiner counts itself; the host built it before): the same map
    /// for the host's count, in place. Nothing is paid and nobody moves,
    /// unless they now stand outside every room and corridor.</summary>
    private void RebuildPuzzle(int players)
    {
        if (_puzzle is not null && GodotObject.IsInstanceValid(_puzzle))
        {
            _root.RemoveChild(_puzzle);
            _puzzle.QueueFree();
        }
        PuzzlePlan plan = PlanPuzzle(players);
        _puzzle = new PuzzleMap(plan, NextAfterPuzzle);
        _root.AddChild(_puzzle);
        _root.MoveChild(_puzzle, 0);
        Arena.UseBounds(_puzzle.Bounds);
        if (!plan.Walkable(_player.GlobalPosition)) _player.Teleport(_puzzle.SpawnPoint + Net.Coop.SpawnOffset);
        Log($"puzzle {_index + 1} rebuilt for {players} players (the host's count)");
    }

    // --------------------------------------------------------------- dev

    /// <summary>Straight into the puzzle before the current map (captures,
    /// the developer panel, checks), with <paramref name="rooms"/> forcing its
    /// rooms (kind ids, comma separated). False once the run is over.</summary>
    public bool DevEnterPuzzle(int index = -1, string? rooms = null)
    {
        if (_run.Over || Net.Coop.IsClient) return false;
        _boss?.Despawn();
        _boss = null;
        Log("[dev] puzzle" + (rooms is null ? "" : $" ({rooms})"));
        EnterPuzzle(index >= 0 ? index : Mathf.Max(0, _index), -1, rooms);
        return true;
    }
}
