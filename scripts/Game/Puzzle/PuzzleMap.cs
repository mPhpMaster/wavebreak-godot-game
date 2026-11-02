using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Data;

namespace Wavebreak.Game.Puzzle;

/// <summary>What a player can ask a puzzle to do. Sent as an int in co-op.</summary>
public enum PuzzleAction { None, Push, Mirror, Lever, Bell, Echo, Reset, Hint }

/// <summary>
/// A puzzle map in the scene: built by <see cref="StageDirector"/> in place
/// of a map (like the rest room), from a <see cref="PuzzlePlan"/>, holding the
/// <see cref="PuzzleState"/> and the puzzle clock.
///
/// <b>Who decides.</b> The host (or the single player) runs the rules: each
/// frame it reads where every player stands (plates, wind plates, twin plates,
/// rooms entered) and applies the actions players ask for (push, turn, pull,
/// strike, echo, reset, hint). Clients ask the host and draw whatever the state
/// array says; changed slots arrive as pairs (Net/NetSession.Puzzle.cs). Each
/// machine still handles its own body: the wind pushes it, spikes hurt it, and
/// it claims its own reward.
///
/// <b>Leaving.</b> Through the vault's EXIT portal once every room is solved,
/// or through the SKIP portal, which opens after
/// <see cref="PuzzleState.SkipSeconds"/> so nobody is ever stuck. In co-op the
/// host leaves when everyone stands in one of them, or
/// <see cref="ExitTimeout"/> seconds after the first one stepped in, the same
/// rule as the rest room's CONTINUE portal.
/// </summary>
public sealed partial class PuzzleMap : Node3D
{
    public static PuzzleMap? Current { get; private set; }

    /// <summary>Co-op: seconds after the first player steps into an exit
    /// before the host moves on without the rest.</summary>
    public const float ExitTimeout = 20f;

    /// <summary>How close a player has to stand to use a piece.</summary>
    public const float Reach = 2.3f;

    public PuzzlePlan Plan { get; }
    public PuzzleState State { get; }
    public string NextMapName { get; }

    /// <summary>Puzzle seconds since arrival (the run clock is held meanwhile).</summary>
    public float Elapsed { get; private set; }

    /// <summary>The clamp shape while this map stands.</summary>
    public StageDef Bounds { get; }

    /// <summary>Host or single-player: this machine runs the rules.</summary>
    public static bool Authority => !Net.Coop.IsClient;

    // Co-op exit status, as RestRoom keeps it (Inside below 0: single-player).
    public static int Inside { get; private set; } = -1;
    public static int Total { get; private set; }
    public static float Countdown { get; private set; } = -1f;

    public static void SetStatus(int inside, int total, float countdown)
    {
        Inside = inside;
        Total = total;
        Countdown = countdown;
    }

    private float _exitCountdown = -1f;
    private float _statusIn;
    private bool _interactRequested;
    private bool _hintRequested;
    private float _hintCooldown;
    private float _pushHeld;
    private float _pushCooldown;
    private float _spikeTick;
    private float _gustSoundIn;

    /// <summary>Counters for the self-checks and the summary line.</summary>
    public int AsksSent { get; private set; }
    public int AsksApplied { get; private set; }

    public PuzzleMap(PuzzlePlan plan, string nextMapName)
    {
        Plan = plan;
        State = new PuzzleState(plan);
        NextMapName = nextMapName;
        Bounds = new StageDef
        {
            Id = "puzzle",
            DisplayName = plan.Name,
            Sides = 24,
            Radius = plan.Reach + 4f,
            Theme = "meadow",
            Merchants = 0,
            Microwaves = 0,
            Breakables = 0,
        };
    }

    public override void _Ready()
    {
        Name = "PuzzleMap";
        Current = this;
        SetStatus(Net.Coop.InRun ? 0 : -1, 0, -1f);
        MapBlockers.Begin(this);
        BuildWorld();     // PuzzleMap.Build.cs
        BuildPieces();    // PuzzleMap.Pieces.cs
        // Guards collide with the pieces (they used to walk through them).
        Enemy.ExtraWorldMask = PieceLayer;
        CapCamera();
        SyncVisibility();
        var layer = new CanvasLayer { Name = "PuzzleUI", Layer = 3 };
        AddChild(layer);
        layer.AddChild(new UI.PuzzleHud(this));
        GD.Print($"[puzzle] map {Plan.PuzzleIndex + 1} '{Plan.Name}' d={Plan.Difficulty} players={Plan.Players} rooms={Plan.Describe()} " +
                 $"gold<={Plan.GoldSeconds:0}s silver<={Plan.SilverSeconds:0}s skip@{Plan.SkipSeconds:0}s");
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Current, this)) Current = null;
        MapBlockers.End(this);
        if (Enemy.ExtraWorldMask == PieceLayer) Enemy.ExtraWorldMask = 0;
        RestoreCamera();
    }

    private float _cameraMax = -1f, _cameraDistance;

    /// <summary>Indoors the camera zooms out no farther than
    /// <see cref="CameraMaxIndoors"/>, so it stays under the ceiling; its
    /// spring arm pulls in at walls, door slabs and beams (all world layer).</summary>
    private void CapCamera()
    {
        if (PlayerCamera.Instance is not { } cam || !IsInstanceValid(cam)) return;
        _cameraMax = cam.MaxDistance;
        _cameraDistance = cam.Distance;
        cam.MaxDistance = Mathf.Min(cam.MaxDistance, CameraMaxIndoors);
        cam.Distance = Mathf.Min(cam.Distance, cam.MaxDistance);
    }

    private void RestoreCamera()
    {
        if (_cameraMax < 0f || PlayerCamera.Instance is not { } cam || !IsInstanceValid(cam)) return;
        // Give back the zoom the player had, unless they changed it in here.
        if (cam.Distance >= cam.MaxDistance - 0.01f) cam.Distance = _cameraDistance;
        cam.MaxDistance = _cameraMax;
        _cameraMax = -1f;
    }

    /// <summary>Long runs of pillars (the twin room's divider, rows in the
    /// crate rooms) as blockers, so guards steer round the end of a wall of
    /// pieces instead of pressing into it (Game/MapBlockers). Single pieces
    /// need nothing: the guards slide round them.</summary>
    private void RegisterBlockers()
    {
        var tint = new Color(0.5f, 0.6f, 0.65f);
        foreach (RoomPlan room in Plan.Rooms)
        {
            var used = new bool[RoomPlan.N * RoomPlan.N];
            for (int c = 0; c < used.Length; c++)
            {
                if (!room.Pillar[c] || used[c]) continue;
                int x = RoomPlan.X(c), z = RoomPlan.Z(c);
                int lx = 1, lz = 1;
                while (RoomPlan.InGrid(x + lx, z) && room.Pillar[RoomPlan.CellOf(x + lx, z)] && !used[RoomPlan.CellOf(x + lx, z)]) lx++;
                if (lx == 1)
                    while (RoomPlan.InGrid(x, z + lz) && room.Pillar[RoomPlan.CellOf(x, z + lz)] && !used[RoomPlan.CellOf(x, z + lz)]) lz++;
                for (int i = 0; i < lx; i++) for (int k = 0; k < lz; k++) used[RoomPlan.CellOf(x + i, z + k)] = true;
                if (lx * lz < 2) continue;
                Vector3 a = room.World(c), b = room.World(RoomPlan.CellOf(x + lx - 1, z + lz - 1));
                Vector3 mid = (a + b) * 0.5f;
                var half = new Vector2((lx - 1) * RoomPlan.Cell * 0.5f + 0.75f, (lz - 1) * RoomPlan.Cell * 0.5f + 0.75f);
                MapBlockers.Add(new MapBlockers.Blocker(new Vector2(mid.X, mid.Z), half, 0f, 2.6f, MapBlockers.Shape.Box, tint));
            }
        }
    }

    /// <summary>Where the team arrives: the start room, back from its exit door.</summary>
    public Vector3 SpawnPoint
    {
        get
        {
            RoomPlan s = Plan.Start;
            int side = s.ExitSide;
            return s.Centre - new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]) * 6f + Vector3.Up * 0.3f;
        }
    }

    /// <summary>Which way the start room's door is, for the camera on arrival.</summary>
    public Vector3 ArrivalFacing
    {
        get
        {
            int side = Plan.Start.ExitSide;
            return new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
        }
    }

    // ------------------------------------------------------------- clock

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Main.Instance?.Run is { Over: true }) return;
        Elapsed += dt;
        _hintCooldown = Mathf.Max(0f, _hintCooldown - dt);

        Player? me = Player.Instance;
        if (me is not null && IsInstanceValid(me))
        {
            StepLocal(me, dt);
        }
        if (Authority) StepHost(dt);
        SyncPieces(dt);   // PuzzleMap.Pieces.cs
        SyncVisibility(); // PuzzleMap.Build.cs
    }

    /// <summary>Client: the host's clock (twice a second).</summary>
    public void NetClock(float elapsed, int inside, int total, float countdown)
    {
        if (Mathf.Abs(elapsed - Elapsed) > 0.25f) Elapsed = elapsed;
        SetStatus(inside, total, countdown);
    }

    /// <summary>Late join / new host: the clock as the host had it.</summary>
    public void SetElapsed(float elapsed) => Elapsed = elapsed;

    // ------------------------------------------------------------- host

    private readonly List<Vector3> _positions = new();

    private void StepHost(float dt)
    {
        _positions.Clear();
        int connected = 1;
        if (Player.Instance is { IsAlive: true } me) _positions.Add(me.GlobalPosition);
        if (Net.Coop.InRun && Net.NetSession.Instance is { } net) connected += net.PuzzlePositions(_positions);
        foreach (int room in State.Step(_positions, Elapsed, connected)) SpawnGuards(room);
        Flush();
    }

    /// <summary>Host: send what changed this frame.</summary>
    public void Flush()
    {
        if (State.Dirty.Count == 0) return;
        if (Net.Coop.IsHost && Net.NetSession.Instance is { } net)
        {
            var pairs = new int[State.Dirty.Count * 2];
            int i = 0;
            foreach (int slot in State.Dirty)
            {
                pairs[i++] = slot;
                pairs[i++] = State.V[slot];
            }
            net.SendPuzzleSet(pairs);
        }
        State.Dirty.Clear();
    }

    /// <summary>Host: a few weak enemies the first time the team enters a room,
    /// so weapons and movement still matter while the puzzle is the point.</summary>
    private void SpawnGuards(int r)
    {
        RoomPlan room = Plan.Rooms[r];
        if (room.Enemies <= 0 || EnemyManager.Instance is not { } em) return;
        List<EnemyDef> weak = WeakEnemies();
        if (weak.Count == 0) return;
        var rng = new Core.Rng(Plan.Seed ^ (ulong)(r * 7919 + 13));
        int placed = 0;
        // In the open strip round the puzzle grid, in the hall's corners
        // farthest from the way in: inside the room, never on a piece.
        float k = RoomPlan.Half - 3.4f;
        var corners = new List<Vector3>
        {
            room.World(new Vector2(k, k)), room.World(new Vector2(-k, k)), room.World(new Vector2(k, -k)), room.World(new Vector2(-k, -k)),
        };
        Vector3 entry = room.EntrySide >= 0 ? room.Doorway(room.EntrySide) : room.Centre;
        corners.Sort((a, b) => b.DistanceSquaredTo(entry).CompareTo(a.DistanceSquaredTo(entry)));
        for (int i = 0; i < room.Enemies; i++)
        {
            Vector3 at = corners[(i + rng.RangeInt(0, 2)) % 4] + new Vector3(rng.Range(-1f, 1f), 0.5f, rng.Range(-1f, 1f));
            if (em.SpawnAt(weak[rng.RangeInt(0, weak.Count)], at) is not null) placed++;
        }
        GD.Print($"[puzzle] room {r} ({room.Kind}) entered: {placed} guards; rooms drawn here {RoomsShown}/{Plan.Rooms.Count}");
    }

    private static List<EnemyDef>? _weak;

    /// <summary>The cheapest ordinary land enemies of the opening minutes.</summary>
    public static List<EnemyDef> WeakEnemies()
    {
        if (_weak is not null) return _weak;
        var list = new List<EnemyDef>();
        foreach (EnemyDef e in Catalog.Enemies)
            if (!e.IsBoss && !e.IsElite && !e.UnderwaterOnly && e.UnlockAtSeconds <= 60f && !Visuals.CityGiantModels.IsGiant(e))
                list.Add(e);
        list.Sort((a, b) => a.Cost.CompareTo(b.Cost));
        if (list.Count > 4) list.RemoveRange(4, list.Count - 4);
        return _weak = list;
    }

    /// <summary>Host: apply an action, from this machine or from a client.</summary>
    public void Apply(PuzzleAction action, int room, int a, int b)
    {
        if (!Authority || room < 0 || room >= Plan.Rooms.Count) return;
        switch (action)
        {
            case PuzzleAction.Push: State.Push(room, a, b, Elapsed); break;
            case PuzzleAction.Mirror: State.TurnMirror(room, a, Elapsed); break;
            case PuzzleAction.Lever: State.PullLever(room, a, Elapsed); break;
            case PuzzleAction.Bell: State.StrikeBell(room, a, Elapsed); break;
            case PuzzleAction.Echo: State.ShowOrder(room, Elapsed + 0.4f); break;
            case PuzzleAction.Reset: State.ResetCrates(room); break;
            case PuzzleAction.Hint: State.Hint(room, Elapsed); break;
            default: return;
        }
        AsksApplied++;
        Flush();
    }

    /// <summary>Ask for an action: applied here on the host, sent to it from a client.</summary>
    public void Ask(PuzzleAction action, int room, int a, int b = 0)
    {
        AsksSent++;
        if (Authority) Apply(action, room, a, b);
        else Net.NetSession.Instance?.SendPuzzleAsk((int)action, room, a, b);
    }

    // ------------------------------------------------------------ exits

    /// <summary>Is the vault's EXIT open (every room solved)?</summary>
    public bool ExitOpen => State.Finished;

    /// <summary>Inside an exit that is open: EXIT once finished, SKIP while it is open.</summary>
    public bool InAnyExit(Vector3 p)
        => ExitOpen && _exit is not null && _exit.Contains(p)
           || State.SkipOpen && _skip is not null && IsInstanceValid(_skip) && _skip.Contains(p);

    /// <summary>Where the bot (and the HUD arrow) should head to leave.</summary>
    public Vector3? ExitTarget => ExitOpen && _exit is not null ? _exit.GlobalPosition
        : State.SkipOpen && _skip is not null && IsInstanceValid(_skip) ? _skip.GlobalPosition : null;

    public Vector3? SkipPosition => State.SkipOpen && _skip is not null && IsInstanceValid(_skip) ? _skip.GlobalPosition : null;

    /// <summary>Host / single-player: time to leave? <paramref name="why"/> is
    /// "exit" or "skip" (single-player), "all" or "timeout" (co-op).</summary>
    public bool WantsToLeave(float dt, out string why)
    {
        why = "";
        Player? me = Player.Instance;
        bool meIn = me is not null && IsInstanceValid(me) && InAnyExit(me.GlobalPosition);
        if (!Net.Coop.InRun)
        {
            if (!meIn) return false;
            why = ExitOpen ? "exit" : "skip";
            return true;
        }

        int inside = meIn ? 1 : 0, total = 1;
        if (Net.NetSession.Instance is { } net)
        {
            (int rIn, int rTotal) = net.CountRemotesInside(InAnyExit);
            inside += rIn;
            total += rTotal;
        }
        if (inside >= total) { why = "all"; return true; }
        if (inside > 0)
        {
            if (_exitCountdown < 0f) _exitCountdown = ExitTimeout;
            _exitCountdown -= dt;
            if (_exitCountdown <= 0f) { why = "timeout"; return true; }
        }
        else _exitCountdown = -1f;

        SetStatus(inside, total, _exitCountdown);
        _statusIn -= dt;
        if (_statusIn <= 0f)
        {
            _statusIn = 0.5f;
            Net.NetSession.Instance?.SendPuzzleClock(Elapsed, inside, total, _exitCountdown);
        }
        return false;
    }

    /// <summary>Host: the team is leaving. Anything unfinished becomes a skip,
    /// and the final state goes out before the next message.</summary>
    public void Close()
    {
        if (!State.Finished) State.Skip(Elapsed);
        Flush();
    }

    // ----------------------------------------------------------- local

    /// <summary>Press interact next frame (the autopilot).</summary>
    public void RequestInteract() => _interactRequested = true;

    /// <summary>Ask for a hint next frame (the autopilot, tests).</summary>
    public void RequestHint() => _hintRequested = true;

    /// <summary>Seconds before another hint may be asked for.</summary>
    public float HintCooldown => _hintCooldown;

    /// <summary>The room this player stands in, or -1 between rooms.</summary>
    public int RoomOf(Vector3 p) => Plan.RoomAt(p);

    /// <summary>The room the HUD talks about: where you stand if it is a
    /// puzzle room, else the first unsolved one.</summary>
    public int FocusRoom
    {
        get
        {
            int r = Player.Instance is { } me ? RoomOf(me.GlobalPosition) : -1;
            if (r > 0) return r;
            return State.Frontier;
        }
    }

    private void StepLocal(Player me, float dt)
    {
        Vector3 at = me.GlobalPosition;
        int r = RoomOf(at);
        bool control = me.IsAlive && !Net.Coop.LocalHold && !GetTree().Paused;

        // Hint: the help key (H, or the pad's help button).
        bool hint = _hintRequested || control && Input.IsActionJustPressed("help_toggle");
        _hintRequested = false;
        if (hint && _hintCooldown <= 0f && !State.Finished)
        {
            int room = r > 0 && r < Plan.Rooms.Count - 1 ? r : State.Frontier;
            if (room > 0 && room < Plan.Rooms.Count - 1)
            {
                Ask(PuzzleAction.Hint, room, 0);
                _hintCooldown = 8f;
            }
        }

        UpdateFocus(me, r);
        bool interact = _interactRequested || control && Input.IsActionJustPressed("interact");
        _interactRequested = false;
        if (interact && Focus is { } f) Use(f);

        if (r < 0) return;
        RoomPlan room0 = Plan.Rooms[r];
        if (control) StepPush(me, room0, r, dt);
        StepWind(me, room0, dt);
        StepSpikes(me, room0, dt);
    }

    /// <summary>Walking into a crate along a grid line for a moment pushes it one cell.</summary>
    private void StepPush(Player me, RoomPlan room, int r, float dt)
    {
        _pushCooldown = Mathf.Max(0f, _pushCooldown - dt);
        if (room.Kind != RoomKind.Crates)
        {
            _pushHeld = 0f;
            return;
        }
        Vector3 wish = me.MoveWish;
        int side = -1;
        if (wish.LengthSquared() > 0.25f)
        {
            if (Mathf.Abs(wish.X) > Mathf.Abs(wish.Z) * 1.4f) side = wish.X > 0 ? 0 : 2;
            else if (Mathf.Abs(wish.Z) > Mathf.Abs(wish.X) * 1.4f) side = wish.Z > 0 ? 1 : 3;
        }
        int crate = -1;
        if (side >= 0)
        {
            int[] crates = State.Crates(room);
            Vector3 p = me.GlobalPosition;
            for (int i = 0; i < crates.Length; i++)
            {
                Vector3 c = room.World(crates[i]);
                Vector3 d = c - p;
                float along = d.X * RoomPlan.Dx[side] + d.Z * RoomPlan.Dz[side];
                float across = Mathf.Abs(d.X * RoomPlan.Dz[side] - d.Z * RoomPlan.Dx[side]);
                if (along > 0.6f && along < 1.9f && across < 0.75f) { crate = i; break; }
            }
        }
        if (crate < 0 || _pushCooldown > 0f)
        {
            _pushHeld = 0f;
            return;
        }
        _pushHeld += dt;
        if (_pushHeld < 0.16f) return;
        _pushHeld = 0f;
        _pushCooldown = 0.4f;
        Ask(PuzzleAction.Push, r, crate, side);
    }

    /// <summary>Gusts shove this player out of the ring while they blow.</summary>
    private void StepWind(Player me, RoomPlan room, float dt)
    {
        if (room.Kind != RoomKind.Wind) return;
        Vector3 p = me.GlobalPosition;
        for (int i = 0; i < room.WindCentres.Length; i++)
        {
            Vector3 d = p - room.World(room.WindCentres[i]);
            d.Y = 0f;
            float dist = d.Length();
            if (dist <= room.WindInner || dist >= room.WindOuter + 0.3f || p.Y > 2.5f) continue;
            if (!PuzzleGen.Gusting(room, i, Elapsed)) continue;
            Vector3 outward = dist > 0.01f ? d / dist : Vector3.Right;
            me.ApplyKnockback(outward * 52f * dt);
            _gustSoundIn -= dt;
            if (_gustSoundIn <= 0f)
            {
                _gustSoundIn = 1.0f;
                AudioDirector.PlayPuzzle(PuzzleSound.Gust);
            }
        }
    }

    /// <summary>Raised spikes sting whoever stands on them (a tenth of max health).</summary>
    private void StepSpikes(Player me, RoomPlan room, float dt)
    {
        _spikeTick = Mathf.Max(0f, _spikeTick - dt);
        if (room.Spikes.Length == 0 || !me.IsAlive || me.GlobalPosition.Y > 0.9f || _spikeTick > 0f) return;
        int cell = room.CellAt(me.GlobalPosition);
        if (Array.IndexOf(room.Spikes, cell) < 0 || !SpikesUp(room, Elapsed)) return;
        _spikeTick = 0.8f;
        me.Health.TakeDamage(new Combat.DamageInfo
        {
            Amount = me.Health.Max * 0.1f,
            CanCrit = false,
            Source = "spikes",
        });
    }

    /// <summary>Spikes are up for 1.2 s of every 3.6 s (after a 0.5 s rattle).</summary>
    public static bool SpikesUp(RoomPlan room, float t) => (t + room.SpikePhase) % 3.6f >= 2.4f;

    public static bool SpikesWarn(RoomPlan room, float t) { float p = (t + room.SpikePhase) % 3.6f; return p >= 1.9f && p < 2.4f; }
}
