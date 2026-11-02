using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// The pieces: built once from the plan, then redrawn from the state array
/// whenever its version moves (and animated every frame). Sounds come from
/// comparing the array with the last one drawn, so every machine hears the
/// same plate, crate, door and bell whoever caused them.
///
/// Nothing relies on colour alone: a pressed plate sinks, a lit lamp stands
/// tall and an unlit one is a stub, a gust raises its arrows, a mirror's
/// angle is its shape, each bell carries its own carved symbol, the twin
/// plates are lettered, and a lit crystal rises and spins.
/// </summary>
public sealed partial class PuzzleMap
{
    private sealed class PlateView
    {
        public Node3D Top = null!;
        public StandardMaterial3D Mat = null!;
        public float Shown;
    }

    private sealed class RoomView
    {
        public readonly List<StaticBody3D> CrateBodies = new();
        public readonly List<Node3D> CrateMeshes = new();
        public readonly List<PlateView> Plates = new();
        public readonly List<Node3D> Mirrors = new();
        public Node3D? Beam;
        public int BeamBits = -1;
        public Node3D? Receiver;
        public StandardMaterial3D? ReceiverMat;
        public bool Lit;
        public readonly List<List<Node3D>> Arrows = new();
        public readonly List<StandardMaterial3D> RingMats = new();
        public readonly List<Node3D> Bells = new();
        public readonly List<StandardMaterial3D> BellMats = new();
        public readonly List<Node3D> Handles = new();
        public readonly List<Node3D> Lamps = new();
        public readonly List<StandardMaterial3D> LampMats = new();
        public readonly List<Node3D> LampMarks = new();
        public readonly List<Node3D> Latches = new();
        public readonly List<Node3D> Spikes = new();
        public int ShowStep = -1;
    }

    // Piece colliders sit on the player-only layer (TallLayer): the camera's
    // spring arm ignores them, so a bell or a mirror behind the player never
    // pulls the camera into the back of their head. Guards walk through them.
    private readonly List<RoomView> _views = new();
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

    private static readonly Color PlateIdle = new(0.70f, 0.66f, 0.58f);
    private static readonly Color Mirror = new(0.86f, 0.94f, 1f);
    private static readonly Color BeamColour = new(1f, 0.92f, 0.55f);
    private static readonly Color Crate = new(0.70f, 0.50f, 0.30f);
    private static readonly Color CrateBand = new(0.45f, 0.30f, 0.18f);

    private static readonly float[] BellPitch = { 1f, 1.122f, 1.26f, 1.335f, 1.498f, 1.682f };

    private void BuildPieces()
    {
        foreach (RoomPlan room in Plan.Rooms)
        {
            var v = new RoomView();
            _views.Add(v);
            for (int c = 0; c < RoomPlan.N * RoomPlan.N; c++) if (room.Pillar[c]) Pillar(room.World(c));
            switch (room.Kind)
            {
                case RoomKind.Crates: BuildCrates(room, v); break;
                case RoomKind.Mirrors: BuildMirrors(room, v); break;
                case RoomKind.Wind: BuildWind(room, v); break;
                case RoomKind.Runes: BuildRunes(room, v); break;
                case RoomKind.Levers: BuildLevers(room, v); break;
                case RoomKind.Twin: BuildTwin(room, v); break;
            }
            foreach (int cell in room.Spikes) v.Spikes.Add(SpikeStrip(room.World(cell)));
        }
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
        // Crates: a flat arrow beside the marker shows which way to push.
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
        AddChild(Mesh(new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.72f, Height = 2.3f, RadialSegments = 10 }, at + Vector3.Up * 1.15f, Flat(Wall.Lightened(0.12f))));
        AddChild(Mesh(new BoxMesh { Size = new Vector3(1.6f, 0.25f, 1.6f) }, at + Vector3.Up * 2.38f, Flat(WallCap)));
        AddChild(Mesh(new BoxMesh { Size = new Vector3(1.6f, 0.2f, 1.6f) }, at + Vector3.Up * 0.1f, Flat(WallCap)));
        _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.75f, Height = 2.6f }, Position = at + Vector3.Up * 1.3f });
        _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.75f, Height = 20f }, Position = at + Vector3.Up * 12.6f });
    }

    private PlateView Plate(Vector3 at, bool round = false)
    {
        var view = new PlateView { Mat = Flat(PlateIdle) };
        AddChild(Mesh(round ? new CylinderMesh { TopRadius = 1.0f, BottomRadius = 1.05f, Height = 0.1f, RadialSegments = 24 }
                            : new BoxMesh { Size = new Vector3(1.9f, 0.1f, 1.9f) }, at + Vector3.Up * 0.05f, Flat(BronzeDark), false));
        view.Top = new Node3D { Position = at + Vector3.Up * 0.16f };
        AddChild(view.Top);
        view.Top.AddChild(Mesh(round ? new CylinderMesh { TopRadius = 0.78f, BottomRadius = 0.82f, Height = 0.16f, RadialSegments = 24 }
                                     : new BoxMesh { Size = new Vector3(1.5f, 0.16f, 1.5f) }, Vector3.Zero, view.Mat, false));
        // A weight mark: a squat dome on the pad.
        view.Top.AddChild(Mesh(new SphereMesh { Radius = 0.3f, Height = 0.3f, RadialSegments = 12, Rings = 4 }, Vector3.Up * 0.08f, Flat(BronzeDark), false));
        return view;
    }

    private void BuildCrates(RoomPlan room, RoomView v)
    {
        foreach (int p in room.Plates) v.Plates.Add(Plate(room.World(p)));
        foreach (int c in room.CrateStart)
        {
            var body = new StaticBody3D { Name = "Crate", Position = room.World(c) + Vector3.Up * 0.85f, CollisionLayer = TallLayer, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.7f, 1.7f, 1.7f) } });
            AddChild(body);
            v.CrateBodies.Add(body);
            var mesh = new Node3D { Name = "CrateMesh", Position = body.Position };
            AddChild(mesh);
            mesh.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.66f, 1.66f, 1.66f) }, Vector3.Zero, Flat(Crate)));
            foreach (float y in new[] { -0.55f, 0.55f })
                mesh.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.72f, 0.18f, 1.72f) }, Vector3.Up * y, Flat(CrateBand)));
            mesh.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.2f, 1.72f, 1.72f) }, Vector3.Zero, Flat(CrateBand)));
            v.CrateMeshes.Add(mesh);
        }
        // The reset bell: a small post with a circling arrow.
        Vector3 at = room.World(room.ResetCell);
        AddChild(Mesh(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.4f, Height = 1.1f, RadialSegments = 10 }, at + Vector3.Up * 0.55f, Flat(WallCap)));
        var ring = Mesh(new TorusMesh { InnerRadius = 0.32f, OuterRadius = 0.46f, Rings = 18, RingSegments = 6 }, at + Vector3.Up * 1.55f, Flat(Bronze));
        ring.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
        AddChild(ring);
        var head = Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.16f, Height = 0.3f, RadialSegments = 3 }, at + new Vector3(0.4f, 1.62f, 0f), Flat(Bronze));
        head.Rotation = new Vector3(0f, 0f, Mathf.Pi);
        AddChild(head);
        _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.45f, Height = 1.6f }, Position = at + Vector3.Up * 0.8f });
    }

    private void BuildMirrors(RoomPlan room, RoomView v)
    {
        // Emitter: a lamp housing on the wall, its lens facing in.
        Vector3 e = room.World(room.EmitterCell);
        var inward = new Vector3(RoomPlan.Dx[room.EmitterDir], 0f, RoomPlan.Dz[room.EmitterDir]);
        AddChild(Mesh(new BoxMesh { Size = new Vector3(1.3f, 1.6f, 1.3f) }, e + Vector3.Up * 0.8f, Flat(WallCap)));
        var lens = Mesh(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 0.3f, RadialSegments = 16 }, e + Vector3.Up * 1.15f + inward * 0.7f, Glow(BeamColour, 3f), false);
        lens.Rotation = room.EmitterDir % 2 == 0 ? new Vector3(0f, 0f, Mathf.Pi * 0.5f) : new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
        AddChild(lens);
        _tall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.3f, 1.6f, 1.3f) }, Position = e + Vector3.Up * 0.8f });

        // Crystal: a hexagonal prism on a plinth; rises and spins when lit.
        Vector3 r = room.World(room.ReceiverCell);
        AddChild(Mesh(new BoxMesh { Size = new Vector3(1.3f, 0.7f, 1.3f) }, r + Vector3.Up * 0.35f, Flat(WallCap)));
        v.ReceiverMat = Flat(Teal.Darkened(0.35f));
        v.Receiver = new Node3D { Position = r + Vector3.Up * 1.2f };
        AddChild(v.Receiver);
        v.Receiver.AddChild(Mesh(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.42f, Height = 0.9f, RadialSegments = 6 }, Vector3.Up * 0.2f, v.ReceiverMat));
        v.Receiver.AddChild(Mesh(new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.05f, Height = 0.5f, RadialSegments = 6 }, Vector3.Down * 0.5f, v.ReceiverMat));
        _tall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.3f, 1.6f, 1.3f) }, Position = r + Vector3.Up * 0.8f });

        for (int i = 0; i < room.MirrorCells.Length; i++)
        {
            Vector3 at = room.World(room.MirrorCells[i]);
            AddChild(Mesh(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.6f, Height = 0.7f, RadialSegments = 12 }, at + Vector3.Up * 0.35f, Flat(WallCap)));
            var turn = new Node3D { Position = at + Vector3.Up * 1.15f, Rotation = new Vector3(0f, MirrorAngle(room.MirrorStart[i]), 0f) };
            AddChild(turn);
            turn.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.6f, 1.0f, 0.12f) }, Vector3.Zero, new StandardMaterial3D
            {
                AlbedoColor = Mirror, Metallic = 0.35f, Roughness = 0.1f,
                EmissionEnabled = true, Emission = Mirror, EmissionEnergyMultiplier = 0.35f,
            }));
            turn.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.72f, 0.1f, 0.2f) }, Vector3.Up * 0.55f, Flat(Bronze)));
            turn.AddChild(Mesh(new BoxMesh { Size = new Vector3(1.72f, 0.1f, 0.2f) }, Vector3.Down * 0.55f, Flat(Bronze)));
            v.Mirrors.Add(turn);
            _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.62f, Height = 1.8f }, Position = at + Vector3.Up * 0.9f });
        }
        v.Beam = new Node3D { Name = "Beam" };
        AddChild(v.Beam);
    }

    /// <summary>Y rotation of a mirror plate: "/" is +45 degrees, "\" is -45.</summary>
    private static float MirrorAngle(int o) => o == 0 ? Mathf.Pi * 0.25f : -Mathf.Pi * 0.25f;

    private void RebuildBeam(RoomPlan room, RoomView v, int bits)
    {
        if (v.Beam is null) return;
        foreach (Node n in v.Beam.GetChildren()) n.QueueFree();
        (List<int> cells, bool hit) = PuzzleSolver.Beam(room, bits);
        var mat = Glow(BeamColour, 4f);
        Vector3 Point(int i) => i < cells.Count ? room.World(cells[i]) + Vector3.Up * 1.15f : Vector3.Zero;
        for (int i = 0; i + 1 < cells.Count; i++)
        {
            Vector3 a = Point(i), b = Point(i + 1);
            if (i + 1 == cells.Count - 1 && !hit && room.Pillar[cells[i + 1]]) b = a.Lerp(b, 0.55f);
            Segment(v.Beam, a, b, mat);
        }
        // Off the edge of the floor: run the beam into the wall.
        if (!hit && cells.Count > 0)
        {
            int last = cells[^1];
            (int side, bool _) = LastSide(room, bits);
            if (PuzzleSolver.Step(last, side) < 0) Segment(v.Beam, Point(cells.Count - 1), Point(cells.Count - 1) + new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]) * 1.0f, mat);
        }
    }

    private static (int Side, bool Hit) LastSide(RoomPlan room, int bits)
    {
        int cur = room.EmitterCell, side = room.EmitterDir;
        for (int guard = 0; guard < 400; guard++)
        {
            int next = PuzzleSolver.Step(cur, side);
            if (next < 0 || next == room.ReceiverCell || room.Pillar[next]) return (side, next == room.ReceiverCell);
            int m = Array.IndexOf(room.MirrorCells, next);
            if (m >= 0) side = PuzzleSolver.Reflect(side, (bits >> m) & 1);
            cur = next;
        }
        return (side, false);
    }

    private static void Segment(Node3D parent, Vector3 a, Vector3 b, Material mat)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 0.01f) return;
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.24f, 0.24f, len + 0.24f) },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mi);
        mi.GlobalPosition = (a + b) * 0.5f;
        mi.LookAt(b, Mathf.Abs(d.Normalized().Y) > 0.9f ? Vector3.Right : Vector3.Up);
    }

    private void BuildWind(RoomPlan room, RoomView v)
    {
        for (int i = 0; i < room.WindCentres.Length; i++)
        {
            Vector3 c = room.World(room.WindCentres[i]);
            var ringMat = Flat(new Color(0.40f, 0.68f, 0.76f));
            var ring = Mesh(new TorusMesh { InnerRadius = room.WindInner, OuterRadius = room.WindOuter, Rings = 64, RingSegments = 8 }, c + Vector3.Up * 0.02f, ringMat, false);
            ring.Scale = new Vector3(1f, 0.03f, 1f);
            AddChild(ring);
            v.RingMats.Add(ringMat);
            v.Plates.Add(Plate(c, round: true));
            var arrows = new List<Node3D>();
            int count = Mathf.RoundToInt(room.WindOuter * 2.2f);
            for (int k = 0; k < count; k++)
            {
                float a = Mathf.Tau * k / count;
                var arrow = new Node3D { Position = c, Rotation = new Vector3(0f, -a, 0f) };
                AddChild(arrow);
                // A flat arrowhead pointing outward along +X.
                var head = Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.28f, Height = 0.6f, RadialSegments = 3 }, Vector3.Zero, Glow(new Color(0.85f, 0.97f, 1f), 1.2f), false);
                head.Rotation = new Vector3(0f, 0f, -Mathf.Pi * 0.5f);
                arrow.AddChild(head);
                arrows.Add(arrow);
            }
            v.Arrows.Add(arrows);
        }
    }

    private void BuildRunes(RoomPlan room, RoomView v)
    {
        for (int i = 0; i < room.Bells.Length; i++)
        {
            Vector3 at = room.World(room.Bells[i]);
            AddChild(Mesh(new BoxMesh { Size = new Vector3(0.9f, 1.0f, 0.9f) }, at + Vector3.Up * 0.5f, Flat(WallCap)));
            // Two posts and a beam, the bell hanging under it.
            foreach (float s in new[] { -0.4f, 0.4f })
                AddChild(Mesh(new BoxMesh { Size = new Vector3(0.1f, 1.1f, 0.1f) }, at + new Vector3(s, 1.5f, 0f), Flat(BronzeDark)));
            AddChild(Mesh(new BoxMesh { Size = new Vector3(0.95f, 0.1f, 0.12f) }, at + Vector3.Up * 2.05f, Flat(BronzeDark)));
            var mat = Flat(Bronze);
            var bell = new Node3D { Position = at + Vector3.Up * 1.55f };
            AddChild(bell);
            bell.AddChild(Mesh(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.36f, Height = 0.55f, RadialSegments = 14 }, Vector3.Zero, mat));
            v.Bells.Add(bell);
            v.BellMats.Add(mat);
            // The bell's symbol floats above it, turning slowly so it reads from anywhere.
            var symbol = Symbol(room.BellSymbols[i], mat);
            symbol.Position = at + Vector3.Up * 2.75f;
            AddChild(symbol);
            v.Bells.Add(symbol);
            _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.55f, Height = 2.2f }, Position = at + Vector3.Up * 1.1f });
        }
        // The echo bell in the middle: rings the order again.
        Vector3 c = room.Centre;
        AddChild(Mesh(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.65f, Height = 0.8f, RadialSegments = 12 }, c + Vector3.Up * 0.4f, Flat(WallCap)));
        AddChild(Mesh(new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.55f, Height = 0.6f, RadialSegments = 14 }, c + Vector3.Up * 1.15f, Flat(BronzeDark)));
        for (int k = 0; k < 3; k++)
        {
            var wave = Mesh(new TorusMesh { InnerRadius = 0.3f + k * 0.22f, OuterRadius = 0.36f + k * 0.22f, Rings = 20, RingSegments = 4 }, c + Vector3.Up * 1.9f, Glow(Teal, 1.2f), false);
            wave.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
            AddChild(wave);
        }
        _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.65f, Height = 1.6f }, Position = c + Vector3.Up * 0.8f });
    }

    /// <summary>A carved symbol, standing up, about 0.8 m across.</summary>
    private static Node3D Symbol(RuneSymbol s, Material mat)
    {
        var n = new Node3D();
        MeshInstance3D Prism(int sides, float r, float rollZ = 0f)
        {
            var m = Mesh(new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.14f, RadialSegments = sides }, Vector3.Zero, mat);
            m.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, rollZ);
            n.AddChild(m);
            return m;
        }
        switch (s)
        {
            case RuneSymbol.Triangle: Prism(3, 0.45f, Mathf.Pi * 0.5f); break;
            case RuneSymbol.Circle: Prism(24, 0.36f); break;
            case RuneSymbol.Square: n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.62f, 0.62f, 0.14f) }, Vector3.Zero, mat)); break;
            case RuneSymbol.Diamond:
                var d = Mesh(new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.14f) }, Vector3.Zero, mat);
                d.Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.25f);
                n.AddChild(d);
                break;
            case RuneSymbol.Star:
                Prism(3, 0.45f, Mathf.Pi * 0.5f);
                Prism(3, 0.45f, -Mathf.Pi * 0.5f);
                break;
            default:   // Cross
                n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.7f, 0.2f, 0.14f) }, Vector3.Zero, mat));
                n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.2f, 0.7f, 0.14f) }, Vector3.Zero, mat));
                break;
        }
        return n;
    }

    private void BuildLevers(RoomPlan room, RoomView v)
    {
        for (int i = 0; i < room.Levers.Length; i++)
        {
            Vector3 at = room.World(room.Levers[i]);
            // The handle swings along the row's facing: towards the lamps when pulled.
            Vector2 toLamps = (room.Lamps[i] - room.Levers[i]).Normalized();
            float yaw = Mathf.Atan2(toLamps.X, toLamps.Y);
            AddChild(Mesh(new BoxMesh { Size = new Vector3(0.9f, 0.5f, 0.6f) }, at + Vector3.Up * 0.25f, Flat(WallCap)));
            var pivot = new Node3D { Position = at + Vector3.Up * 0.5f, Rotation = new Vector3(0f, yaw, 0f) };
            AddChild(pivot);
            var handle = new Node3D { Rotation = new Vector3(-0.6f, 0f, 0f) };
            pivot.AddChild(handle);
            handle.AddChild(Mesh(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = 1.1f, RadialSegments = 8 }, Vector3.Up * 0.55f, Flat(BronzeDark)));
            handle.AddChild(Mesh(new SphereMesh { Radius = 0.17f, Height = 0.34f, RadialSegments = 10, Rings = 5 }, Vector3.Up * 1.12f, Flat(Bronze)));
            v.Handles.Add(handle);
            _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.45f, Height = 1.2f }, Position = at + Vector3.Up * 0.6f });
        }
        for (int i = 0; i < room.Lamps.Length; i++)
        {
            Vector3 at = room.World(room.Lamps[i]);
            AddChild(Mesh(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.42f, Height = 0.5f, RadialSegments = 10 }, at + Vector3.Up * 0.25f, Flat(WallCap)));
            var mat = Flat(new Color(0.35f, 0.38f, 0.42f));
            var lamp = new Node3D { Position = at + Vector3.Up * 0.5f };
            AddChild(lamp);
            lamp.AddChild(Mesh(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.3f, Height = 1.6f, RadialSegments = 6 }, Vector3.Up * 0.8f, mat));
            v.Lamps.Add(lamp);
            v.LampMats.Add(mat);
            var mark = Mesh(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0f, Height = 0.4f, RadialSegments = 8 }, at + Vector3.Up * 2.8f, Glow(Warm, 1.8f), false);
            mark.Visible = false;
            AddChild(mark);
            v.LampMarks.Add(mark);
            _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.42f, Height = 1.6f }, Position = at + Vector3.Up * 0.8f });
        }
    }

    private void BuildTwin(RoomPlan room, RoomView v)
    {
        int k = 0;
        foreach (int cell in new[] { room.PlateA, room.PlateB })
        {
            Vector3 at = room.World(cell);
            v.Plates.Add(Plate(at));
            AddChild(new Label3D
            {
                Text = k == 0 ? "A" : "B",
                FontSize = 96,
                PixelSize = 0.012f,
                OutlineSize = 18,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = at + Vector3.Up * 2.3f,
                Modulate = new Color(1f, 0.97f, 0.88f),
                OutlineModulate = new Color(0.15f, 0.2f, 0.25f),
            });
            var latch = Mesh(new TorusMesh { InnerRadius = 1.05f, OuterRadius = 1.25f, Rings = 32, RingSegments = 6 }, at + Vector3.Up * 0.12f, Glow(Warm, 1.6f), false);
            latch.Visible = false;
            AddChild(latch);
            v.Latches.Add(latch);
            k++;
        }
    }

    private Node3D SpikeStrip(Vector3 at)
    {
        AddChild(Mesh(new BoxMesh { Size = new Vector3(1.9f, 0.06f, 1.9f) }, at + Vector3.Up * 0.03f, Flat(new Color(0.32f, 0.30f, 0.30f)), false));
        var group = new Node3D { Position = at + Vector3.Down * 0.6f };
        AddChild(group);
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
        for (int r = 0; r < Plan.Rooms.Count; r++) SyncRoom(Plan.Rooms[r], _views[r], dt, changed);
        SyncDoors(dt);
        SyncHint(dt, changed);
        SyncExits();
        SyncRewards(dt);   // PuzzleMap.Rewards.cs
        Array.Copy(State.V, _prev, _prev.Length);
    }

    private void SyncRoom(RoomPlan room, RoomView v, float dt, bool changed)
    {
        int pressed = State.Get(room, PuzzleState.RPressed);
        float t = Elapsed;
        switch (room.Kind)
        {
            case RoomKind.Crates:
            {
                int[] crates = State.Crates(room);
                for (int i = 0; i < crates.Length; i++)
                {
                    Vector3 target = room.World(crates[i]) + Vector3.Up * 0.85f;
                    if (changed) v.CrateBodies[i].Position = target;
                    v.CrateMeshes[i].Position = v.CrateMeshes[i].Position.MoveToward(target, dt * 7f);
                }
                for (int i = 0; i < v.Plates.Count; i++) DrawPlate(v.Plates[i], (pressed >> i & 1) != 0, dt);
                break;
            }
            case RoomKind.Mirrors:
            {
                int bits = State.Get(room, PuzzleState.RA);
                for (int i = 0; i < v.Mirrors.Count; i++)
                {
                    float target = MirrorAngle((bits >> i) & 1);
                    Vector3 rot = v.Mirrors[i].Rotation;
                    v.Mirrors[i].Rotation = new Vector3(0f, Mathf.MoveToward(rot.Y, target, dt * 6f), 0f);
                }
                if (bits != v.BeamBits)
                {
                    v.BeamBits = bits;
                    RebuildBeam(room, v, bits);
                    v.Lit = PuzzleSolver.Beam(room, bits).Hit;
                    if (v.ReceiverMat is { } m)
                    {
                        m.EmissionEnabled = v.Lit;
                        m.Emission = Teal;
                        m.EmissionEnergyMultiplier = 2.5f;
                        m.AlbedoColor = v.Lit ? Teal : Teal.Darkened(0.35f);
                    }
                }
                if (v.Receiver is { } rec)
                {
                    Vector3 home = room.World(room.ReceiverCell) + Vector3.Up * (v.Lit ? 1.65f : 1.2f);
                    rec.Position = rec.Position.MoveToward(home, dt * 2f);
                    if (v.Lit) rec.RotateY(dt * 2.2f);
                }
                break;
            }
            case RoomKind.Wind:
            {
                int touched = State.Get(room, PuzzleState.RA);
                for (int i = 0; i < room.WindCentres.Length; i++)
                {
                    DrawPlate(v.Plates[i], (touched >> i & 1) != 0, dt);
                    float cycle = PuzzleGen.WindCycle(room, i, t);
                    bool gust = cycle >= room.Calm;
                    bool warn = !gust && cycle >= room.Calm - 0.6f;
                    float span = room.WindOuter - room.WindInner;
                    for (int k = 0; k < v.Arrows[i].Count; k++)
                    {
                        Node3D a = v.Arrows[i][k];
                        float run = gust ? ((t * 2.4f + k * 0.37f) % 1f) : 0.5f;
                        float height = gust ? 0.9f : warn ? 0.45f + 0.15f * Mathf.Sin(t * 30f) : 0.08f;
                        a.GetChild<Node3D>(0).Position = new Vector3(room.WindInner + 0.4f + run * (span - 0.8f), height, 0f);
                        a.GetChild<Node3D>(0).Scale = Vector3.One * (gust ? 1.2f : 0.8f);
                    }
                    v.RingMats[i].AlbedoColor = gust ? new Color(0.62f, 0.88f, 0.98f) : new Color(0.40f, 0.68f, 0.76f);
                }
                break;
            }
            case RoomKind.Runes:
            {
                bool showing = State.RuneShowing(room, t, out int lit);
                int step = -1;
                int from = State.Get(room, PuzzleState.RB);
                if (showing && from >= 0) step = Mathf.FloorToInt((t - from / 10f) / PuzzleGen.RuneBeat);
                if (step != v.ShowStep)
                {
                    v.ShowStep = step;
                    if (lit >= 0) AudioDirector.PlayPuzzle(PuzzleSound.Bell, BellPitch[lit % BellPitch.Length]);
                }
                for (int i = 0; i < room.Bells.Length; i++)
                {
                    bool on = i == lit || i == _struckBell && _struckFor > 0f;
                    StandardMaterial3D m = v.BellMats[i];
                    m.EmissionEnabled = on;
                    m.Emission = Warm;
                    m.EmissionEnergyMultiplier = 2.6f;
                    Node3D bell = v.Bells[i * 2], symbol = v.Bells[i * 2 + 1];
                    bell.Rotation = new Vector3(0f, 0f, on ? Mathf.Sin(t * 22f) * 0.25f : 0f);
                    symbol.RotateY(dt * (on ? 3f : 0.6f));
                    symbol.Scale = Vector3.One * (on ? 1.35f : 1f);
                }
                _struckFor = Mathf.Max(0f, _struckFor - dt);
                break;
            }
            case RoomKind.Levers:
            {
                int pulled = State.Get(room, PuzzleState.RA);
                int lit = PuzzleSolver.Lamps(room, pulled);
                for (int i = 0; i < v.Handles.Count; i++)
                {
                    float target = (pulled >> i & 1) != 0 ? 0.6f : -0.6f;
                    Vector3 rot = v.Handles[i].Rotation;
                    v.Handles[i].Rotation = new Vector3(Mathf.MoveToward(rot.X, target, dt * 5f), 0f, 0f);
                }
                for (int i = 0; i < v.Lamps.Count; i++)
                {
                    bool on = (lit >> i & 1) != 0;
                    float sy = Mathf.MoveToward(v.Lamps[i].Scale.Y, on ? 1f : 0.35f, dt * 3f);
                    v.Lamps[i].Scale = new Vector3(1f, sy, 1f);
                    StandardMaterial3D m = v.LampMats[i];
                    m.AlbedoColor = on ? Warm : new Color(0.35f, 0.38f, 0.42f);
                    m.EmissionEnabled = on;
                    m.Emission = Warm;
                    m.EmissionEnergyMultiplier = 1.8f;
                    // Standing at a lever marks the lamps it flips.
                    bool mark = Focus is { Action: PuzzleAction.Lever } f && f.Room == room.Index && (room.LeverMasks[f.Index] >> i & 1) != 0;
                    v.LampMarks[i].Visible = mark;
                    if (mark) v.LampMarks[i].Position = room.World(room.Lamps[i]) + Vector3.Up * (2.8f + Mathf.Sin(t * 5f) * 0.12f);
                }
                break;
            }
            case RoomKind.Twin:
            {
                bool assist = State.Get(room, PuzzleState.RAssist) != 0;
                for (int i = 0; i < 2; i++)
                {
                    bool held = State.TwinHeld(room, i, t);
                    DrawPlate(v.Plates[i], held || State.Get(room, PuzzleState.RSolved) != 0, dt);
                    int until = State.Get(room, i == 0 ? PuzzleState.RA : PuzzleState.RB);
                    float left = until >= 0 ? until / 10f - t : 0f;
                    bool latched = assist && (pressed >> i & 1) == 0 && left > 0f && State.Get(room, PuzzleState.RSolved) == 0;
                    v.Latches[i].Visible = latched;
                    if (latched) v.Latches[i].Scale = Vector3.One * Mathf.Clamp(left / room.Latch, 0.15f, 1f);
                }
                break;
            }
        }
        // Spikes: rattle, then up, then down.
        for (int i = 0; i < v.Spikes.Count; i++)
        {
            bool up = SpikesUp(room, t);
            bool warn = SpikesWarn(room, t);
            Node3D s = v.Spikes[i];
            float y = up ? 0f : warn ? -0.42f + Mathf.Sin(t * 40f) * 0.04f : -0.6f;
            Vector3 home = room.World(room.Spikes[i]);
            s.Position = new Vector3(home.X, Mathf.MoveToward(s.Position.Y, y, dt * (up ? 12f : 4f)), home.Z);
        }
    }

    private static void DrawPlate(PlateView p, bool pressed, float dt)
    {
        p.Shown = Mathf.MoveToward(p.Shown, pressed ? 1f : 0f, dt * 8f);
        Vector3 pos = p.Top.Position;
        p.Top.Position = new Vector3(pos.X, 0.16f - 0.11f * p.Shown, pos.Z);
        p.Mat.AlbedoColor = PlateIdle.Lerp(Teal, p.Shown);
        p.Mat.EmissionEnabled = p.Shown > 0.5f;
        p.Mat.Emission = Teal;
        p.Mat.EmissionEnergyMultiplier = 1.2f;
    }

    private int _struckBell = -1;
    private float _struckFor;

    /// <summary>Sounds for whatever changed in the array since it was last drawn.</summary>
    private void PlaySounds()
    {
        if (_prev.Length != State.V.Length) return;
        foreach (RoomPlan room in Plan.Rooms)
        {
            int s = room.Slot;
            int Was(int o) => _prev[s + o];
            int Now(int o) => State.V[s + o];
            switch (room.Kind)
            {
                case RoomKind.Crates:
                    for (int i = 0; i < room.CrateStart.Length; i++)
                        if (Was(PuzzleState.RA + i) != Now(PuzzleState.RA + i)) { AudioDirector.PlayPuzzle(PuzzleSound.Thunk); break; }
                    break;
                case RoomKind.Mirrors:
                case RoomKind.Levers:
                    if (Was(PuzzleState.RA) != Now(PuzzleState.RA)) AudioDirector.PlayPuzzle(PuzzleSound.Click, room.Kind == RoomKind.Levers ? 0.8f : 1.2f);
                    break;
                case RoomKind.Runes:
                    if (Now(PuzzleState.RA) > Was(PuzzleState.RA))
                    {
                        int bell = room.Sequence[Mathf.Clamp(Now(PuzzleState.RA) - 1, 0, room.Sequence.Length - 1)];
                        _struckBell = bell;
                        _struckFor = 0.5f;
                        AudioDirector.PlayPuzzle(PuzzleSound.Bell, BellPitch[bell % BellPitch.Length]);
                    }
                    if (Now(PuzzleState.RFails) > Was(PuzzleState.RFails)) AudioDirector.PlayPuzzle(PuzzleSound.Wrong);
                    break;
            }
            if ((Now(PuzzleState.RPressed) & ~Was(PuzzleState.RPressed)) != 0 && room.Kind is RoomKind.Crates or RoomKind.Twin or RoomKind.Wind)
                AudioDirector.PlayPuzzle(PuzzleSound.Click);
            if (Now(PuzzleState.RSolved) != 0 && Was(PuzzleState.RSolved) == 0 && room.Kind != RoomKind.Start)
            {
                AudioDirector.PlayPuzzle(PuzzleSound.Solved);
                EventBus.Publish(new PopupRequested(room.Centre + Vector3.Up * 3.2f, Meta.Loc.T("SOLVED"), Teal, 1.6f));
                GD.Print($"[puzzle] room {room.Index} ({room.Kind}) solved at {Elapsed:0.0}s");
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
        Vector3? at = null;
        _hintArrowSide = -1;
        if (_hintFor > 0f && r > 0 && r < Plan.Rooms.Count && !State.Solved(r) && (changed || _hintFor > 19.9f || !_hintMarker.Visible || _hintRecheck <= 0f))
        {
            _hintAt = HintTarget(r);
            _hintSide = _hintArrowSide;
            _hintRecheck = 0.5f;
        }
        _hintRecheck -= dt;
        if (_hintFor <= 0f || r <= 0 || r >= Plan.Rooms.Count || State.Solved(r)) _hintAt = null;
        at = _hintAt;
        _hintMarker.Visible = at is not null;
        _hintArrow.Visible = _hintSide >= 0;
        if (_hintSide >= 0) _hintArrow.Rotation = new Vector3(0f, -Mathf.Atan2(RoomPlan.Dz[_hintSide], RoomPlan.Dx[_hintSide]), 0f);
        if (at is { } p) _hintMarker.Position = p + Vector3.Up * (3.1f + Mathf.Sin(Elapsed * 4f) * 0.2f);
    }

    /// <summary>The piece a hint points at in room <paramref name="r"/>, from
    /// the solver run on the room as it stands now.</summary>
    public Vector3? HintTarget(int r)
    {
        RoomPlan room = Plan.Rooms[r];
        Player? me = Player.Instance;
        switch (room.Kind)
        {
            case RoomKind.Crates:
            {
                int[] crates = State.Crates(room);
                int from = me is not null ? room.CellAt(me.GlobalPosition) : room.EntryCell;
                if (from < 0 || PuzzleSolver.Solid(room, from) || Array.IndexOf(crates, from) >= 0) from = room.EntryCell;
                List<int>? moves = PuzzleSolver.SolveCrates(room, from, crates, out _, 60000);
                if (moves is null) return room.World(room.ResetCell);
                int p = from;
                foreach (int side in moves)
                {
                    int next = PuzzleSolver.Step(p, side);
                    int hit = Array.IndexOf(crates, next);
                    if (hit >= 0)
                    {
                        _hintLabel.Text = "!";
                        _hintArrowSide = side;
                        return room.World(next) + Vector3.Up * 0.6f;
                    }
                    p = next;
                }
                return room.World(room.Plates[0]);
            }
            case RoomKind.Mirrors:
            {
                _hintLabel.Text = "?";
                int bits = State.Get(room, PuzzleState.RA);
                int answer = PuzzleSolver.SolveMirrors(room, bits);
                for (int i = 0; i < room.MirrorCells.Length; i++)
                    if (answer >= 0 && ((answer ^ bits) >> i & 1) != 0) return room.World(room.MirrorCells[i]);
                return null;
            }
            case RoomKind.Levers:
            {
                _hintLabel.Text = "?";
                int answer = PuzzleSolver.SolveLevers(room, State.Get(room, PuzzleState.RA));
                for (int i = 0; i < room.Levers.Length; i++)
                    if (answer > 0 && (answer >> i & 1) != 0) return room.World(room.Levers[i]);
                return null;
            }
            case RoomKind.Runes:
            {
                _hintLabel.Text = "!";
                int progress = State.Get(room, PuzzleState.RA);
                return room.World(room.Bells[room.Sequence[Mathf.Clamp(progress, 0, room.Sequence.Length - 1)]]);
            }
            case RoomKind.Twin:
                _hintLabel.Text = "A";
                return room.World(room.PlateA);
            case RoomKind.Wind:
            {
                _hintLabel.Text = "!";
                int touched = State.Get(room, PuzzleState.RA);
                for (int i = 0; i < room.WindCentres.Length; i++)
                    if ((touched >> i & 1) == 0) return room.World(room.WindCentres[i]);
                return null;
            }
        }
        return null;
    }

    /// <summary>Crates: the side the hinted crate is pushed towards, or -1.</summary>
    private int _hintArrowSide = -1;

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
            Vector3 at = room.Centre + new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]) * 4.6f;
            Vector3 across = new(RoomPlan.Dz[side], 0f, RoomPlan.Dx[side]);
            at += across * 4.2f;
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
