using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

/// <summary>
/// The rest room between maps:
///
///   boss down -> portal -> REST ROOM -> CONTINUE portal -> next map
///
/// The last map has no portal (its boss is the win and the Final Swarm
/// follows), so there is never a rest room after it.
///
/// <b>Clock.</b> The run clock is held for the whole stay: <see cref="Main"/>
/// skips <see cref="RunState.Tick"/> and the enemy director while
/// <see cref="InRestRoom"/>. The room is a menu with legs; it must not cost
/// difficulty-curve time, and the phase timer of the next map starts at 0 on
/// arrival as always.
///
/// <b>Leaving.</b> Single-player: step into the CONTINUE portal. Co-op: the
/// host moves everyone on when every connected player stands in the portal.
/// As soon as one player is in, a <see cref="RestExitTimeout"/> countdown
/// starts, so one idle teammate cannot hold the team for ever. The count and
/// the countdown are sent to clients for the HUD ("In the portal 1/2").
///
/// <b>Co-op.</b> The host decides when the team enters and leaves (it sends
/// RestEnter, then the usual Phase message); every machine builds its own room
/// with its own merchant stock, priced from its own gold, the same way
/// shrines and stations were already per player. A player who arrives downed
/// is revived on the spot, at full health.
/// </summary>
public sealed partial class StageDirector
{
    /// <summary>Co-op: seconds after the first player steps into the CONTINUE
    /// portal before the host moves on without the rest.</summary>
    public const float RestExitTimeout = 30f;

    private const float RestStatusInterval = 0.25f;

    private RestRoom? _rest;
    private int _restNext = -1;
    private float _restCountdown = -1f;
    private float _restStatusTimer;

    /// <summary>--no-rest-room restores the old portal-to-next-map step (for
    /// comparisons); everything else gets the rest room.</summary>
    public static bool RestRoomEnabled { get; } = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-rest-room") < 0;

    public bool InRestRoom => State == PhaseState.RestRoom;

    /// <summary>The rest room in the tree, or null.</summary>
    public RestRoom? Rest => _rest is not null && GodotObject.IsInstanceValid(_rest) && !_rest.IsQueuedForDeletion() ? _rest : null;

    public int RestRoomsVisited { get; private set; }

    /// <summary>Why the last rest room was left: "portal" (single-player),
    /// "all" (every player in the portal) or "timeout". Empty before.</summary>
    public string RestExitReason { get; private set; } = "";

    /// <summary>Run clock on entering and leaving the last rest room; equal
    /// when the clock was held, which the self-check verifies.</summary>
    public float RestClockIn { get; private set; }
    public float RestClockOut { get; private set; }

    private void EnterRestRoom()
    {
        if (_index >= _stages.Count - 1 && !_loopRestart) { EnterPhase(_index + 1); return; }
        _restNext = NextIndex();

        if (_portal is not null && GodotObject.IsInstanceValid(_portal)) _portal.QueueFree();
        _portal = null;
        _boss = null;
        // Freed at the end of this frame but left in the tree until then: the
        // shrine and chest spawners still ask their (doomed) nodes for a
        // transform this frame, which a removed node cannot answer. Both
        // floors are flat at the origin, so the one-frame overlap is harmless.
        if (_arena is not null && GodotObject.IsInstanceValid(_arena)) _arena.QueueFree();
        _arena = null;
        // The chests went with the map; forget them now rather than when the
        // freed map is noticed (a removed chest has no transform to ask).
        ChestSpawner.Instance?.Clear();
        FreeRestRoom();

        // Orbs still on the floor were earned; they follow into the room.
        Pickup.AttractAll();
        _enemies.ClearField(ArrivalBreather);
        ApplyScaling(1f);

        _rest = new RestRoom(_index + 1, _stages[_restNext].DisplayName);
        _root.AddChild(_rest);
        _root.MoveChild(_rest, 0);
        Arena.UseBounds(RestRoom.Bounds);

        _player.Teleport(RestRoom.SpawnPoint + Net.Coop.SpawnOffset);
        _camera.FaceDirection(Vector3.Forward);
        _camera.SnapToTarget();

        // Co-op: arriving downed means arriving revived. The machine that owns
        // the body does it, as with every other revive.
        if (Net.Coop.InRun && !_player.IsAlive) _player.Health.Revive(1f);

        Interactables.Instance?.UseRestRoom(_rest, _index + 1, _rest.Stations);

        State = PhaseState.RestRoom;
        _restCountdown = -1f;
        _restStatusTimer = 0f;
        RestRoomsVisited++;
        RestClockIn = _run.Seconds;
        Log($"rest room after phase {_index + 1}; next '{_stages[_restNext].DisplayName}' (clock held at {_run.Clock})");

        if (Net.Coop.IsHost) Net.NetSession.Instance?.SendRestRoom(_index);
        if (Net.Coop.InRun) Net.NetSession.Instance?.PlaceRemotes(RestRoom.SpawnPoint);
    }

    /// <summary>Host and single-player: is it time to go on?</summary>
    private void StepRestRoom(float dt)
    {
        if (Rest is not { } room) { LeaveRestRoom("lost"); return; }
        bool meIn = room.Exit.Contains(_player.GlobalPosition);

        if (!Net.Coop.InRun)
        {
            if (meIn) LeaveRestRoom("portal");
            return;
        }

        int inside = meIn ? 1 : 0, total = 1;
        if (Net.NetSession.Instance is { } net)
        {
            (int rIn, int rTotal) = net.CountRemotesInside(room.Exit);
            inside += rIn;
            total += rTotal;
        }

        if (inside >= total) { LeaveRestRoom("all"); return; }
        if (inside > 0)
        {
            if (_restCountdown < 0f) _restCountdown = RestExitTimeout;
            _restCountdown -= dt;
            if (_restCountdown <= 0f) { LeaveRestRoom("timeout"); return; }
        }
        else _restCountdown = -1f;

        RestRoom.SetStatus(inside, total, _restCountdown);
        _restStatusTimer -= dt;
        if (_restStatusTimer <= 0f)
        {
            _restStatusTimer = RestStatusInterval;
            Net.NetSession.Instance?.SendRestStatus(inside, total, _restCountdown);
        }
    }

    /// <summary>The map after this one; back to the first after an endless
    /// loop's last boss (which also clears the flag).</summary>
    private int NextIndex()
    {
        if (!_loopRestart) return _index + 1;
        _loopRestart = false;
        return 0;
    }

    private bool _loopRestart;

    private void LeaveRestRoom(string why)
    {
        RestExitReason = why;
        RestClockOut = _run.Seconds;
        Log($"leaving the rest room ({why}) -> phase {_restNext + 1}/{_stages.Count}");
        EnterPhase(Mathf.Clamp(_restNext, 0, _stages.Count - 1));
    }

    /// <summary>Called by <see cref="EnterPhase"/>: a new map replaces the room.</summary>
    private void FreeRestRoom()
    {
        if (_rest is not null && GodotObject.IsInstanceValid(_rest))
        {
            _root.RemoveChild(_rest);
            _rest.QueueFree();
        }
        _rest = null;
        _restCountdown = -1f;
    }

    // ------------------------------------------------------------- co-op

    /// <summary>Client: the host took the team into the rest room after
    /// clearing phase <paramref name="clearedIndex"/> + 1.</summary>
    public void NetRestRoom(int clearedIndex)
    {
        if (State == PhaseState.RestRoom || clearedIndex < 0 || clearedIndex >= _stages.Count - 1) return;
        if (clearedIndex != _index)
        {
            // A missed Phase message: line the index up before entering.
            Log($"host is resting after phase {clearedIndex + 1}, local phase was {_index + 1}");
            _index = clearedIndex;
        }
        Log("host moved the team into the rest room");
        EnterRestRoom();
    }

    // --------------------------------------------------------------- dev

    /// <summary>Straight into the rest room after the current map (captures,
    /// self-check, developer panel). False on the last map or with the room off.</summary>
    public bool DevEnterRestRoom()
    {
        if (_run.Over || !RestRoomEnabled || _index >= _stages.Count - 1 || State == PhaseState.RestRoom) return false;
        _boss?.Despawn();
        _boss = null;
        Log("[dev] rest room");
        EnterRestRoom();
        return true;
    }
}
