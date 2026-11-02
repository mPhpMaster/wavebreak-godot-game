using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// The fixed part of a puzzle map: sea, floors, walls, doorways and their
/// doors, lights, signs and a little dressing, all from primitives and the
/// Kenney nature kit's statues. Walls are two colliders: a low one on the
/// world layer (enemies and the camera see it) and a tall one on the layer the
/// player alone collides with, so nobody hops a wall past a puzzle and the
/// camera still looks over the walls.
/// </summary>
public sealed partial class PuzzleMap
{
    /// <summary>Low, so the camera looks over a room from behind the player;
    /// the tall collider above it keeps everyone from hopping over.</summary>
    private const float WallHeight = 1.4f;
    private const float DoorWidth = 3.6f;
    /// <summary>Collides with the player only (Player.CollisionMask has it).</summary>
    private const uint TallLayer = 1u << 6;

    private static readonly Color Stone = new(0.86f, 0.79f, 0.64f);
    private static readonly Color StoneDark = new(0.77f, 0.69f, 0.54f);
    private static readonly Color Wall = new(0.50f, 0.62f, 0.66f);
    private static readonly Color WallCap = new(0.88f, 0.86f, 0.78f);
    private static readonly Color Bronze = new(0.74f, 0.53f, 0.30f);
    private static readonly Color BronzeDark = new(0.50f, 0.34f, 0.20f);
    private static readonly Color Sea = new(0.30f, 0.72f, 0.82f);
    private static readonly Color Teal = new(0.40f, 0.95f, 0.86f);
    private static readonly Color Warm = new(1f, 0.82f, 0.55f);

    private StaticBody3D _solids = null!;
    private StaticBody3D _tall = null!;
    private Portal? _exit;
    private Portal? _skip;
    private Label3D? _exitSign;

    private sealed class DoorView
    {
        public StaticBody3D Low = null!, High = null!;
        public Node3D Slab = null!;
        public float Open;
        public bool Opened;
    }

    /// <summary>Door i stands between room i and i + 1; null where there is
    /// only an arch (the start room's way out).</summary>
    private readonly List<DoorView?> _doors = new();

    private void BuildWorld()
    {
        PropTheme theme = PropTheme.Get("meadow");
        AddChild(PropFactory.MakeWorldEnvironment(theme));
        AddChild(PropFactory.MakeSun(theme));
        _solids = new StaticBody3D { Name = "Solids" };
        _tall = new StaticBody3D { Name = "TallWalls", CollisionLayer = TallLayer, CollisionMask = 0 };
        AddChild(_solids);
        AddChild(_tall);

        // One floor collider under every room, a little wider than the rooms.
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        foreach (RoomPlan r in Plan.Rooms)
        {
            min = new Vector2(Mathf.Min(min.X, r.Centre.X), Mathf.Min(min.Y, r.Centre.Z));
            max = new Vector2(Mathf.Max(max.X, r.Centre.X), Mathf.Max(max.Y, r.Centre.Z));
        }
        Vector2 size = max - min + Vector2.One * (RoomPlan.Pitch + 6f);
        Vector2 mid = (min + max) * 0.5f;
        _solids.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(size.X, 1f, size.Y) },
            Position = new Vector3(mid.X, -0.5f, mid.Y),
        });
        var sea = Mesh(new PlaneMesh { Size = new Vector2(size.X + 260f, size.Y + 260f) }, new Vector3(mid.X, -0.45f, mid.Y),
                       new StandardMaterial3D { AlbedoColor = Sea, Roughness = 0.15f, Metallic = 0.1f, EmissionEnabled = true, Emission = Sea.Darkened(0.45f), EmissionEnergyMultiplier = 0.25f }, false);
        AddChild(sea);

        var built = new HashSet<Vector2I>();
        var dark = new List<Transform3D>();
        var rng = new Core.Rng(Plan.Seed ^ 0xDEC0UL);
        for (int i = 0; i < Plan.Rooms.Count; i++)
        {
            RoomPlan room = Plan.Rooms[i];
            BuildFloor(room, dark);
            for (int side = 0; side < 4; side++)
            {
                var key = new Vector2I(room.MacroX * 2 + RoomPlan.Dx[side], room.MacroZ * 2 + RoomPlan.Dz[side]);
                if (!built.Add(key)) continue;
                bool door = side == room.ExitSide || side == room.EntrySide;
                BuildWall(room, side, door);
            }
            AddChild(new OmniLight3D
            {
                Position = room.Centre + new Vector3(0f, 7f, 0f),
                LightColor = Warm,
                LightEnergy = 0.7f,
                OmniRange = 17f,
                ShadowEnabled = false,
            });
            BuildSign(room, i);
            if (room.Kind is RoomKind.Start or RoomKind.Reward) Dress(room, rng);
        }
        // Checker tiles: one multimesh for the whole map.
        var tiles = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new BoxMesh { Size = new Vector3(1.94f, 0.04f, 1.94f) } };
        tiles.InstanceCount = dark.Count;
        for (int i = 0; i < dark.Count; i++) tiles.SetInstanceTransform(i, dark[i]);
        AddChild(new MultiMeshInstance3D { Multimesh = tiles, MaterialOverride = Flat(StoneDark), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        for (int i = 0; i < Plan.Rooms.Count - 1; i++) _doors.Add(i == 0 ? null : BuildDoor(i));

        // The vault's way out, open once every room is solved.
        RoomPlan vault = Plan.Vault;
        int back = vault.EntrySide;
        _exit = new Portal { Name = "PuzzleExit" };
        AddChild(_exit);
        _exit.Position = vault.Centre - new Vector3(RoomPlan.Dx[back], 0f, RoomPlan.Dz[back]) * 4.5f;
        _exit.Visible = false;
        AddChild(_exitSign = new Label3D
        {
            Name = "ExitSign",
            Visible = false,
            Text = Meta.Loc.T("EXIT"),
            FontSize = 64,
            PixelSize = 0.012f,
            OutlineSize = 16,
            Modulate = new Color(1f, 0.97f, 0.85f),
            OutlineModulate = new Color(0.10f, 0.20f, 0.30f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = _exit.Position + new Vector3(0f, 5.8f, 0f),
        });
    }

    private void BuildFloor(RoomPlan room, List<Transform3D> dark)
    {
        float half = RoomPlan.N * RoomPlan.Cell * 0.5f;
        AddChild(Mesh(new BoxMesh { Size = new Vector3(half * 2f + 1f, 0.3f, half * 2f + 1f) }, room.Centre + new Vector3(0f, -0.15f, 0f), Flat(Stone), false));
        for (int c = 0; c < RoomPlan.N * RoomPlan.N; c++)
            if ((RoomPlan.X(c) + RoomPlan.Z(c)) % 2 == 0)
                dark.Add(new Transform3D(Basis.Identity, room.World(c) + new Vector3(0f, 0.005f, 0f)));
    }

    private void BuildWall(RoomPlan room, int side, bool door)
    {
        var dir = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
        var along = new Vector3(Mathf.Abs(RoomPlan.Dz[side]), 0f, Mathf.Abs(RoomPlan.Dx[side]));
        Vector3 mid = room.Centre + dir * (RoomPlan.Pitch * 0.5f);
        float length = RoomPlan.Pitch + 1f;
        if (!door)
        {
            WallSegment(mid, along, length);
            Lantern(mid + along * 4.8f);
            Lantern(mid - along * 4.8f);
            return;
        }
        // The threshold: floor across the gap between the two rooms' floors.
        AddChild(Mesh(new BoxMesh { Size = along * (DoorWidth + 0.4f) + new Vector3(1.4f, 0.3f, 1.4f) - along * 1.4f }, mid + Vector3.Down * 0.15f, Flat(StoneDark), false));
        float seg = (length - DoorWidth) * 0.5f;
        float offset = DoorWidth * 0.5f + seg * 0.5f;
        WallSegment(mid + along * offset, along, seg);
        WallSegment(mid - along * offset, along, seg);
        // Door posts with a lamp each, so a doorway reads as one from across
        // the room. No lintel: the camera often looks through a doorway.
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 post = mid + along * s * (DoorWidth * 0.5f + 0.4f);
            AddChild(Mesh(new BoxMesh { Size = new Vector3(1.2f, WallHeight + 0.7f, 1.2f) }, post + Vector3.Up * ((WallHeight + 0.7f) * 0.5f), Flat(WallCap)));
            AddChild(Mesh(new SphereMesh { Radius = 0.24f, Height = 0.48f, RadialSegments = 10, Rings = 5 }, post + Vector3.Up * (WallHeight + 0.95f), Glow(Teal, 1.8f), false));
        }
    }

    private void WallSegment(Vector3 mid, Vector3 along, float length)
    {
        Vector3 size = along * length + new Vector3(1f, 0f, 1f) - along;
        AddChild(Mesh(new BoxMesh { Size = size + Vector3.Up * WallHeight }, mid + Vector3.Up * (WallHeight * 0.5f), Flat(Wall)));
        AddChild(Mesh(new BoxMesh { Size = size + new Vector3(0.2f, 0.18f, 0.2f) }, mid + Vector3.Up * (WallHeight + 0.09f), Flat(WallCap)));
        _solids.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size + Vector3.Up * WallHeight }, Position = mid + Vector3.Up * (WallHeight * 0.5f) });
        _tall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size + Vector3.Up * 28f }, Position = mid + Vector3.Up * (WallHeight + 14f) });
    }

    /// <summary>A little lantern on the wall top.</summary>
    private void Lantern(Vector3 at)
    {
        AddChild(Mesh(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 0.6f, RadialSegments = 6 }, at + Vector3.Up * (WallHeight + 0.4f), Flat(BronzeDark)));
        AddChild(Mesh(new SphereMesh { Radius = 0.2f, Height = 0.4f, RadialSegments = 10, Rings = 5 }, at + Vector3.Up * (WallHeight + 0.85f), Glow(Warm, 2.6f), false));
        AddChild(Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.26f, Height = 0.22f, RadialSegments = 8 }, at + Vector3.Up * (WallHeight + 1.12f), Flat(BronzeDark)));
    }

    private DoorView BuildDoor(int i)
    {
        RoomPlan room = Plan.Rooms[i];
        int side = room.ExitSide;
        var dir = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
        var along = new Vector3(Mathf.Abs(RoomPlan.Dz[side]), 0f, Mathf.Abs(RoomPlan.Dx[side]));
        Vector3 mid = room.Centre + dir * (RoomPlan.Pitch * 0.5f);
        var view = new DoorView
        {
            Low = new StaticBody3D { Name = $"Door{i}" },
            High = new StaticBody3D { Name = $"DoorTall{i}", CollisionLayer = TallLayer, CollisionMask = 0 },
            Slab = new Node3D { Name = $"DoorSlab{i}", Position = mid },
        };
        Vector3 size = along * DoorWidth + new Vector3(0.6f, WallHeight + 0.4f, 0.6f) - along * 0.6f;
        view.Low.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = mid + Vector3.Up * (size.Y * 0.5f) });
        view.High.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size + Vector3.Up * 22f }, Position = mid + Vector3.Up * (size.Y + 11f) });
        AddChild(view.Low);
        AddChild(view.High);
        AddChild(view.Slab);
        view.Slab.AddChild(Mesh(new BoxMesh { Size = size }, Vector3.Up * (size.Y * 0.5f), Flat(Bronze)));
        // A lock emblem on both faces: ring and bar. Shape, not colour, says "shut".
        foreach (float f in new[] { -1f, 1f })
        {
            var ring = Mesh(new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.6f, Rings = 20, RingSegments = 8 }, Vector3.Up * 1.55f + dir * f * 0.32f, Flat(BronzeDark));
            ring.Rotation = side % 2 == 0 ? new Vector3(0f, 0f, Mathf.Pi * 0.5f) : new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
            view.Slab.AddChild(ring);
            view.Slab.AddChild(Mesh(new BoxMesh { Size = along * 0.3f + new Vector3(0.12f, 0.9f, 0.12f) - along * 0.12f }, Vector3.Up * 0.85f + dir * f * 0.32f, Flat(BronzeDark)));
        }
        return view;
    }

    /// <summary>A sign over the doorway into each room: its number and name.</summary>
    private void BuildSign(RoomPlan room, int i)
    {
        string text = room.Kind switch
        {
            RoomKind.Start => Meta.Loc.T(Plan.Name).ToUpperInvariant(),
            RoomKind.Reward => Meta.Loc.T(PuzzleText.Title(room.Kind)),
            _ => Meta.Loc.F("{0}/{1}  ·  {2}", i, Plan.PuzzleRooms, Meta.Loc.T(PuzzleText.Title(room.Kind))),
        };
        Vector3 at = room.Kind == RoomKind.Start ? SpawnPoint + ArrivalFacing * 7.5f + Vector3.Up * 3.6f
            : room.Centre + new Vector3(RoomPlan.Dx[room.EntrySide], 0f, RoomPlan.Dz[room.EntrySide]) * 8.6f + Vector3.Up * 3.7f;
        AddChild(new Label3D
        {
            Name = $"Sign{i}",
            Text = text,
            FontSize = 44,
            PixelSize = 0.009f,
            // Read from across a room; gone when the camera is on top of it.
            VisibilityRangeBegin = 7f,
            OutlineSize = 12,
            Modulate = new Color(1f, 0.97f, 0.88f),
            OutlineModulate = new Color(0.12f, 0.2f, 0.26f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = at,
        });
    }

    /// <summary>Statues and plants in the corners of the start room and the vault.</summary>
    private void Dress(RoomPlan room, Core.Rng rng)
    {
        string kit = ArtAssets.Root + "kenney/nature-kit/";
        string[] models = { "statue_column.glb", "statue_obelisk.glb", "statue_columnDamaged.glb", "plant_bushLarge.glb", "statue_block.glb" };
        float c = RoomPlan.N * RoomPlan.Cell * 0.5f - 1.2f;
        foreach (Vector2 corner in new[] { new Vector2(c, c), new Vector2(-c, c), new Vector2(c, -c), new Vector2(-c, -c) })
        {
            string m = models[rng.RangeInt(0, models.Length)];
            Kit(kit + m, m.StartsWith("plant") ? 1.3f : 2.6f, room.World(corner), rng.Range(0f, Mathf.Tau));
        }
    }

    /// <summary>An imported model <paramref name="height"/> metres tall; skipped quietly when missing.</summary>
    private void Kit(string path, float height, Vector3 at, float yaw)
    {
        if (!ArtAssets.UseImported || !ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene) return;
        if (scene.Instantiate() is not Node3D model) return;
        Aabb? box = null;
        void Walk(Node n, Transform3D xf)
        {
            Transform3D here = n is Node3D n3 ? xf * n3.Transform : xf;
            if (n is MeshInstance3D { Mesh: not null } mi)
            {
                Aabb b = here * mi.GetAabb();
                box = box is { } acc ? acc.Merge(b) : b;
            }
            foreach (Node ch in n.GetChildren()) Walk(ch, here);
        }
        foreach (Node ch in model.GetChildren()) Walk(ch, Transform3D.Identity);
        float h = box is { } bb ? MathF.Max(0.05f, bb.Size.Y) : 1f;
        float s = height / h;
        float floor = box is { } fb ? fb.Position.Y * s : 0f;
        model.Scale = Vector3.One * s;
        model.Position = at - new Vector3(0f, floor, 0f);
        model.Rotation = new Vector3(0f, yaw, 0f);
        AddChild(model);
    }

    /// <summary>Open door i (animated in <see cref="SyncDoors"/>).</summary>
    private void SyncDoors(float dt)
    {
        for (int i = 0; i < _doors.Count; i++)
        {
            if (_doors[i] is not { } d) continue;
            bool open = State.DoorOpen(i);
            if (open && !d.Opened)
            {
                d.Opened = true;
                d.Low.ProcessMode = ProcessModeEnum.Disabled;
                foreach (Node n in d.Low.GetChildren()) if (n is CollisionShape3D cs) cs.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
                foreach (Node n in d.High.GetChildren()) if (n is CollisionShape3D cs) cs.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
                Audio.AudioDirector.PlayPuzzle(Audio.PuzzleSound.Door);
            }
            float target = open ? 1f : 0f;
            if (Mathf.IsEqualApprox(d.Open, target)) continue;
            d.Open = Mathf.MoveToward(d.Open, target, dt * 0.9f);
            d.Slab.Position = new Vector3(d.Slab.Position.X, -(WallHeight + 0.5f) * d.Open, d.Slab.Position.Z);
            d.Slab.Visible = d.Open < 0.99f;
        }
    }

    /// <summary>Has door i opened (tests)?</summary>
    public bool DoorIsOpen(int i) => i >= 0 && i < _doors.Count && (_doors[i]?.Opened ?? true);

    // ----------------------------------------------------------- helpers

    private static MeshInstance3D Mesh(Mesh mesh, Vector3 at, Material mat, bool shadow = true) => new()
    {
        Mesh = mesh,
        Position = at,
        MaterialOverride = mat,
        CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private static StandardMaterial3D Flat(Color c) => new() { AlbedoColor = c, Roughness = 0.85f };

    private static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
}
