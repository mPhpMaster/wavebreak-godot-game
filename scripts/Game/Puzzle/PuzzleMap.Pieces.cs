using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// The pieces: each room's <see cref="PuzzleKind"/> builds its own once from
/// the plan (with <see cref="Kit"/>), then draws them from the state array
/// every frame. Sounds come from comparing the array with the last one drawn,
/// so every machine hears the same plate, crate, door and bell whoever caused
/// them. This file keeps what every room shares: pillars, spike strips, the
/// hint marker, the SOLVED popup and the exits.
///
/// Nothing relies on colour alone: a pressed plate sinks, a lit lamp stands
/// tall and an unlit one is a stub, a gust raises its arrows, a mirror's
/// angle is its shape, bells and weights carry carved symbols or numbers, a
/// lit crystal rises and spins.
/// </summary>
public sealed partial class PuzzleMap
{
    // Piece colliders sit on the player's layer (TallLayer) and the guards'
    // (PieceLayer), never the camera's: its spring arm ignores them, so a bell
    // or a mirror behind the player never pulls the camera into the back of
    // their head, while guards walk round them instead of through them.

    /// <summary>Per room: what its kind's Build returned (null for the start room and the vault).</summary>
    private readonly List<object?> _views = new();
    private readonly List<List<Node3D>> _spikes = new();
    private int[] _prev = Array.Empty<int>();
    private int _drawnVersion = -1;
    private Node3D _hintMarker = null!;
    private Label3D _hintLabel = null!;
    private Node3D _hintArrow = null!;
    private int _hintSeqSeen;
    private Vector3? _hintAt;
    private int _hintSide = -1;
    private float _hintRecheck;
    private float _hintFor;

    /// <summary>Kinds add their nodes here while their room is built.</summary>
    internal void PutPiece(Node n) => Put(n);

    internal void AddPieceCollider(CollisionShape3D cs) => _tall.AddChild(cs);

    /// <summary>What room <paramref name="r"/>'s kind built (tests, captures).</summary>
    public object? ViewOf(int r) => r >= 0 && r < _views.Count ? _views[r] : null;

    private void BuildPieces()
    {
        foreach (RoomPlan room in Plan.Rooms)
        {
            _into = _roomRoots[room.Index];
            for (int c = 0; c < RoomPlan.N * RoomPlan.N; c++) if (room.Pillar[c]) Pillar(room.World(c));
            _views.Add(room.Def?.Build(Kit, room));
            var spikes = new List<Node3D>();
            foreach (int cell in room.Spikes) spikes.Add(SpikeStrip(room.World(cell)));
            _spikes.Add(spikes);
            _into = null;
        }
        RegisterBlockers();
        _hintMarker = new Node3D { Name = "HintMarker", Visible = false };
        AddChild(_hintMarker);
        var cone = Mesh(new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0f, Height = 0.8f, RadialSegments = 12 }, Vector3.Zero, Glow(new Color(1f, 0.95f, 0.5f), 2.2f), false);
        _hintMarker.AddChild(cone);
        _hintLabel = new Label3D
        {
            Text = "?",
            FontSize = 72,
            PixelSize = 0.01f,
            OutlineSize = 14,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0f, 0.95f, 0f),
            Modulate = new Color(1f, 0.97f, 0.7f),
            OutlineModulate = new Color(0.2f, 0.15f, 0.05f),
        };
        _hintMarker.AddChild(_hintLabel);
        // Pushed pieces: a flat arrow beside the marker shows which way to push.
        _hintArrow = new Node3D { Name = "HintArrow", Position = new Vector3(0f, -1.6f, 0f) };
        _hintMarker.AddChild(_hintArrow);
        var flat = Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.45f, Height = 1.1f, RadialSegments = 3 }, new Vector3(1.1f, 0f, 0f), Glow(new Color(1f, 0.95f, 0.5f), 2.2f), false);
        flat.Rotation = new Vector3(0f, 0f, -Mathf.Pi * 0.5f);
        _hintArrow.AddChild(flat);
        _prev = (int[])State.V.Clone();
    }

    // ------------------------------------------------------------ building

    private void Pillar(Vector3 at)
    {
        Put(Mesh(new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.72f, Height = 2.3f, RadialSegments = 10 }, at + Vector3.Up * 1.15f, Flat(Wall.Lightened(0.12f))));
        Put(Mesh(new BoxMesh { Size = new Vector3(1.6f, 0.25f, 1.6f) }, at + Vector3.Up * 2.38f, Flat(WallCap)));
        Put(Mesh(new BoxMesh { Size = new Vector3(1.6f, 0.2f, 1.6f) }, at + Vector3.Up * 0.1f, Flat(WallCap)));
        _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.75f, Height = 2.6f }, Position = at + Vector3.Up * 1.3f });
        _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.75f, Height = 20f }, Position = at + Vector3.Up * 12.6f });
    }

    private Node3D SpikeStrip(Vector3 at)
    {
        Put(Mesh(new BoxMesh { Size = new Vector3(1.9f, 0.06f, 1.9f) }, at + Vector3.Up * 0.03f, Flat(new Color(0.32f, 0.30f, 0.30f)), false));
        var group = new Node3D { Position = at + Vector3.Down * 0.6f };
        Put(group);
        var tip = Flat(Meta.Accessibility.Danger(new Color(0.85f, 0.3f, 0.25f)));
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
                group.AddChild(Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.16f, Height = 0.6f, RadialSegments = 6 }, new Vector3(x * 0.55f, 0.3f, z * 0.55f), tip));
        return group;
    }

    // ------------------------------------------------------------- syncing

    /// <summary>The whole state came from the host (late join, new host):
    /// draw it as it is, without replaying a sound for every change in it.</summary>
    public void Adopted()
    {
        Array.Copy(State.V, _prev, _prev.Length);
        _drawnVersion = State.Version - 1;
        _silent = true;
    }

    private bool _silent;

    private void SyncPieces(float dt)
    {
        bool changed = State.Version != _drawnVersion;
        if (changed)
        {
            if (!_silent) PlaySounds();
            _silent = false;
            _drawnVersion = State.Version;
        }
        float t = Elapsed;
        for (int r = 0; r < Plan.Rooms.Count; r++)
        {
            RoomPlan room = Plan.Rooms[r];
            if (room.Def is { } def && _views[r] is { } view) def.Sync(Kit, view, room, State, t, dt, changed);
            // Spikes: rattle, then up, then down.
            List<Node3D> spikes = _spikes[r];
            for (int i = 0; i < spikes.Count; i++)
            {
                bool up = SpikesUp(room, t);
                bool warn = SpikesWarn(room, t);
                Node3D s = spikes[i];
                float y = up ? 0f : warn ? -0.42f + Mathf.Sin(t * 40f) * 0.04f : -0.6f;
                Vector3 home = room.World(room.Spikes[i]);
                s.Position = new Vector3(home.X, Mathf.MoveToward(s.Position.Y, y, dt * (up ? 12f : 4f)), home.Z);
            }
        }
        SyncDoors(dt);
        SyncHint(dt, changed);
        SyncExits();
        SyncRewards(dt);   // PuzzleMap.Rewards.cs
        Array.Copy(State.V, _prev, _prev.Length);
    }

    /// <summary>Sounds for whatever changed in the array since it was last drawn.</summary>
    private void PlaySounds()
    {
        if (_prev.Length != State.V.Length) return;
        foreach (RoomPlan room in Plan.Rooms)
        {
            int s = room.Slot;
            int Was(int o) => _prev[s + o];
            int Now(int o) => State.V[s + o];
            if (room.Def is { } def && _views[room.Index] is { } view) def.Sounds(view, room, Was, Now);
            if (Now(PuzzleState.RSolved) != 0 && Was(PuzzleState.RSolved) == 0 && room.Kind != RoomKind.Start)
            {
                AudioDirector.PlayPuzzle(PuzzleSound.Solved);
                EventBus.Publish(new PopupRequested(room.Centre + Vector3.Up * 3.2f, Meta.Loc.T("SOLVED"), Teal, 1.6f));
                GD.Print($"[puzzle] room {room.Index} ({room.KindId}) solved at {Elapsed:0.0}s");
            }
        }
    }

    // ------------------------------------------------------------- hints

    private void SyncHint(float dt, bool changed)
    {
        int seq = State.V[PuzzleState.GHintSeq];
        if (seq != _hintSeqSeen)
        {
            _hintSeqSeen = seq;
            _hintFor = 20f;
        }
        _hintFor = Mathf.Max(0f, _hintFor - dt);
        int r = State.V[PuzzleState.GHintRoom];
        if (_hintFor > 0f && r > 0 && r < Plan.Rooms.Count && !State.Solved(r) && (changed || _hintFor > 19.9f || !_hintMarker.Visible || _hintRecheck <= 0f))
        {
            PuzzleHint? h = HintFor(r);
            _hintAt = h?.At;
            _hintSide = h?.ArrowSide ?? -1;
            if (h is { } hint) _hintLabel.Text = hint.Label;
            _hintRecheck = 0.5f;
        }
        _hintRecheck -= dt;
        if (_hintFor <= 0f || r <= 0 || r >= Plan.Rooms.Count || State.Solved(r)) _hintAt = null;
        Vector3? at = _hintAt;
        _hintMarker.Visible = at is not null;
        _hintArrow.Visible = at is not null && _hintSide >= 0;
        if (_hintSide >= 0) _hintArrow.Rotation = new Vector3(0f, -Mathf.Atan2(RoomPlan.Dz[_hintSide], RoomPlan.Dx[_hintSide]), 0f);
        if (at is { } p) _hintMarker.Position = p + Vector3.Up * (3.1f + Mathf.Sin(Elapsed * 4f) * 0.2f);
    }

    /// <summary>What a hint marks in room <paramref name="r"/>, from its kind's
    /// solver run on the room as it stands now.</summary>
    public PuzzleHint? HintFor(int r)
    {
        RoomPlan room = Plan.Rooms[r];
        Vector3? me = Player.Instance is { } p && IsInstanceValid(p) ? p.GlobalPosition : null;
        return room.Def?.HintAt(State, room, me, Elapsed);
    }

    /// <summary>Where a hint points in room <paramref name="r"/> (tests).</summary>
    public Vector3? HintTarget(int r) => HintFor(r)?.At;

    /// <summary>The hint marker is up (captures, checks).</summary>
    public bool HintShown => _hintMarker.Visible;

    // ------------------------------------------------------------- exits

    private void SyncExits()
    {
        if (_exit is not null) _exit.Visible = State.Finished;
        if (_exitSign is not null) _exitSign.Visible = State.Finished;
        if (State.SkipOpen && (_skip is null || !IsInstanceValid(_skip)))
        {
            // In the first unsolved room, a step in from its entrance: never
            // behind a closed door from where the team is.
            RoomPlan room = Plan.Rooms[Mathf.Clamp(State.Frontier, 1, Plan.Rooms.Count - 1)];
            int side = room.EntrySide;
            Vector3 at = room.Centre + new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]) * 11.4f;
            Vector3 across = new(RoomPlan.Dz[side], 0f, RoomPlan.Dx[side]);
            at += across * 5.6f;
            _skip = new Portal { Name = "SkipPortal" };
            AddChild(_skip);
            _skip.Position = at;
            AddChild(new Label3D
            {
                Name = "SkipSign",
                Text = Meta.Loc.T("SKIP"),
                FontSize = 64,
                PixelSize = 0.012f,
                OutlineSize = 16,
                Modulate = new Color(1f, 0.95f, 0.85f),
                OutlineModulate = new Color(0.3f, 0.12f, 0.1f, 0.95f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = at + new Vector3(0f, 5.8f, 0f),
            });
            GD.Print($"[puzzle] skip portal open at {Elapsed:0}s in room {room.Index}");
        }
        if (_skip is not null && IsInstanceValid(_skip)) _skip.Visible = State.SkipOpen;
    }
}
