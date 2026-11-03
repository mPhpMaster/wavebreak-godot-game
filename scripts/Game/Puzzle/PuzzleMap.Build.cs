using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// The fixed part of a puzzle map: each room is a closed hall (floor, tall
/// walls, a ceiling, trim, torches with real lights, and props themed by the
/// room's piece type), the puzzle grid inlaid in the middle of its floor, and
/// a short corridor from its exit doorway to the next room's entrance. The
/// door in the exit doorway stays shut until the room is solved, then slides
/// up into the wall.
///
/// <b>Seeing.</b> Walls and ceilings are solid and opaque, and only the room
/// this machine's player is in and its neighbours are drawn
/// (<see cref="SyncVisibility"/>), so no other room and nothing outside is
/// ever in view; co-op players in other rooms each see their own. Static
/// shell geometry is merged into one mesh per material per room
/// (<see cref="Batch"/>) to keep draw calls low.
///
/// <b>Colliders.</b> Walls, door slabs, corridors and the ceiling are world
/// geometry (layer 1): players, enemies and the camera's spring arm all stop
/// at them, so the camera pulls in rather than looking through a wall. The
/// pieces are on <see cref="TallLayer"/> (the player) and
/// <see cref="PieceLayer"/> (the guards), never on the camera's layer, so a
/// bell behind the player does not yank the view into the back of their head.
/// </summary>
public sealed partial class PuzzleMap
{
    /// <summary>Floor to ceiling: well above the camera at its farthest zoom.</summary>
    private const float WallHeight = 14f;
    private const float DoorHeight = 5f;
    private const float DoorWidth = RoomPlan.CorridorWidth;
    /// <summary>The camera's farthest zoom indoors (the ceiling stays above it).</summary>
    public const float CameraMaxIndoors = 12f;
    /// <summary>Collides with the player only (Player.CollisionMask has it).</summary>
    private const uint TallLayer = 1u << 6;
    /// <summary>Puzzle pieces, for the guards (Enemy.ExtraWorldMask while a puzzle stands).</summary>
    public const uint PieceLayer = 1u << 7;

    private static readonly Color Stone = new(0.86f, 0.79f, 0.64f);
    private static readonly Color StoneDark = new(0.77f, 0.69f, 0.54f);
    private static readonly Color Wall = new(0.50f, 0.62f, 0.66f);
    private static readonly Color WallCap = new(0.88f, 0.86f, 0.78f);
    private static readonly Color Bronze = new(0.74f, 0.53f, 0.30f);
    private static readonly Color BronzeDark = new(0.50f, 0.34f, 0.20f);
    private static readonly Color Teal = new(0.40f, 0.95f, 0.86f);
    private static readonly Color Warm = new(1f, 0.82f, 0.55f);
    private static readonly Color Flame = new(1f, 0.62f, 0.25f);

    private StaticBody3D _solids = null!;
    private StaticBody3D _tall = null!;
    private Portal? _exit;
    private Portal? _skip;
    private Label3D? _exitSign;

    /// <summary>One node per room (its corridor out included); hidden when
    /// this machine's player is neither in it nor next door.</summary>
    private readonly List<Node3D> _roomRoots = new();
    /// <summary>Where <see cref="Put"/> adds nodes while a room is built.</summary>
    private Node3D? _into;
    private int _shownAround = -2;

    private void Put(Node n) => (_into ?? this).AddChild(n);

    private sealed class DoorView
    {
        public StaticBody3D Body = null!;
        public Node3D Slab = null!;
        public float Open;
        public bool Opened;
    }

    /// <summary>Door i stands in room i's exit doorway; null where there is
    /// only an arch (the start room's way out).</summary>
    private readonly List<DoorView?> _doors = new();

    /// <summary>How a room kind dresses its hall.</summary>
    private readonly record struct Look(Color Floor, Color Wall, Color Low, Color Trim, Color Ceiling, Color Light);

    /// <summary>Which drawn surface (Game/Puzzle/HallSurfaces) each part of a hall wears.</summary>
    private readonly record struct Surfaces(HallSurfaces.Kind Floor, HallSurfaces.Kind Wall, HallSurfaces.Kind Low, HallSurfaces.Kind Ceiling);

    /// <summary>How a room's hall is dressed: its kind's style; the start room
    /// is plain sandstone and the vault ochre and gold.</summary>
    private static HallStyle StyleOf(RoomPlan room) => room.Kind switch
    {
        RoomKind.Start => HallStyle.Plain,
        RoomKind.Reward => HallStyle.Vault,
        _ => room.Def?.Style ?? HallStyle.Plain,
    };

    private static Surfaces SurfacesFor(HallStyle style) => style switch
    {
        // Workshop: boards underfoot, brick walls, panelled dado, a timber ceiling.
        HallStyle.Workshop => new(HallSurfaces.Kind.Planks, HallSurfaces.Kind.Brick, HallSurfaces.Kind.Planks, HallSurfaces.Kind.Planks),
        // Foundry: iron floor plates and sooty brick.
        HallStyle.Foundry => new(HallSurfaces.Kind.Slabs, HallSurfaces.Kind.Brick, HallSurfaces.Kind.Ashlar, HallSurfaces.Kind.Planks),
        // Frost: polished marble like ice, dressed stone.
        HallStyle.Frost => new(HallSurfaces.Kind.Marble, HallSurfaces.Kind.Ashlar, HallSurfaces.Kind.Ashlar, HallSurfaces.Kind.Plaster),
        // Ember: iron slabs and brick, a timber roof blackened by smoke.
        HallStyle.Ember => new(HallSurfaces.Kind.Slabs, HallSurfaces.Kind.Brick, HallSurfaces.Kind.Brick, HallSurfaces.Kind.Planks),
        // Counting house: boards, dressed stone, panelling.
        HallStyle.Counting => new(HallSurfaces.Kind.Planks, HallSurfaces.Kind.Ashlar, HallSurfaces.Kind.Planks, HallSurfaces.Kind.Planks),
        // Aqueduct: wet flagstones and brick.
        HallStyle.Aqueduct => new(HallSurfaces.Kind.Flagstone, HallSurfaces.Kind.Brick, HallSurfaces.Kind.Ashlar, HallSurfaces.Kind.Plaster),
        // Everywhere else: flagstones, dressed stone, a brick dado, plaster above.
        _ => new(HallSurfaces.Kind.Flagstone, HallSurfaces.Kind.Ashlar, HallSurfaces.Kind.Brick, HallSurfaces.Kind.Plaster),
    };

    private static Look LookFor(HallStyle style) => style switch
    {
        // Workshop: plank floor, brick, timber.
        HallStyle.Workshop => new(new(0.56f, 0.41f, 0.27f), new(0.56f, 0.37f, 0.29f), new(0.36f, 0.24f, 0.18f), new(0.44f, 0.29f, 0.18f), new(0.24f, 0.16f, 0.11f), new(1f, 0.76f, 0.48f)),
        // Observatory: blue-grey stone, silver trim, cool light.
        HallStyle.Observatory => new(new(0.52f, 0.56f, 0.63f), new(0.36f, 0.42f, 0.53f), new(0.23f, 0.27f, 0.36f), new(0.80f, 0.84f, 0.90f), new(0.11f, 0.13f, 0.21f), new(0.80f, 0.88f, 1f)),
        // Gallery of winds: pale sea-green stone.
        HallStyle.Gallery => new(new(0.62f, 0.70f, 0.68f), new(0.40f, 0.55f, 0.56f), new(0.26f, 0.37f, 0.39f), new(0.86f, 0.89f, 0.82f), new(0.17f, 0.23f, 0.25f), new(0.88f, 0.96f, 1f)),
        // Bell temple: slate and violet, gold trim.
        HallStyle.Temple => new(new(0.44f, 0.42f, 0.48f), new(0.39f, 0.33f, 0.43f), new(0.24f, 0.20f, 0.27f), new(0.82f, 0.65f, 0.36f), new(0.13f, 0.10f, 0.15f), new(1f, 0.72f, 0.46f)),
        // Foundry: iron floor, sooty brick, copper.
        HallStyle.Foundry => new(new(0.44f, 0.43f, 0.42f), new(0.42f, 0.34f, 0.30f), new(0.26f, 0.22f, 0.20f), new(0.74f, 0.46f, 0.28f), new(0.15f, 0.13f, 0.12f), new(1f, 0.64f, 0.36f)),
        // Frost: ice-blue floor, pale walls, white trim, cold light.
        HallStyle.Frost => new(new(0.72f, 0.82f, 0.88f), new(0.50f, 0.60f, 0.70f), new(0.33f, 0.41f, 0.50f), new(0.92f, 0.96f, 1f), new(0.14f, 0.18f, 0.24f), new(0.82f, 0.92f, 1f)),
        // Aqueduct: green-grey stone, mossy trim.
        HallStyle.Aqueduct => new(new(0.55f, 0.60f, 0.56f), new(0.34f, 0.48f, 0.46f), new(0.22f, 0.33f, 0.32f), new(0.72f, 0.80f, 0.64f), new(0.12f, 0.18f, 0.17f), new(0.84f, 1f, 0.92f)),
        // Ember: soot and brick red, copper trim, firelight.
        HallStyle.Ember => new(new(0.44f, 0.36f, 0.32f), new(0.46f, 0.27f, 0.21f), new(0.28f, 0.16f, 0.12f), new(0.88f, 0.56f, 0.26f), new(0.14f, 0.08f, 0.06f), new(1f, 0.62f, 0.34f)),
        // Counting house: honey boards, brass.
        HallStyle.Counting => new(new(0.62f, 0.49f, 0.34f), new(0.47f, 0.41f, 0.31f), new(0.31f, 0.24f, 0.16f), new(0.88f, 0.72f, 0.36f), new(0.20f, 0.15f, 0.10f), new(1f, 0.86f, 0.60f)),
        // Garden: green stone, cream trim.
        HallStyle.Garden => new(new(0.54f, 0.60f, 0.44f), new(0.38f, 0.49f, 0.35f), new(0.24f, 0.32f, 0.21f), new(0.86f, 0.88f, 0.68f), new(0.14f, 0.20f, 0.12f), new(0.96f, 1f, 0.82f)),
        // Vault: ochre and gold.
        HallStyle.Vault => new(new(0.70f, 0.60f, 0.42f), new(0.52f, 0.41f, 0.26f), new(0.34f, 0.26f, 0.16f), new(0.94f, 0.77f, 0.38f), new(0.21f, 0.16f, 0.10f), new(1f, 0.86f, 0.52f)),
        // Antechamber: sandstone.
        _ => new(new(0.76f, 0.68f, 0.55f), new(0.60f, 0.53f, 0.44f), new(0.43f, 0.36f, 0.29f), new(0.87f, 0.81f, 0.67f), new(0.28f, 0.23f, 0.18f), new(1f, 0.80f, 0.52f)),
    };

    private void BuildWorld()
    {
        // Indoors: no sky. A dim, steep "skylight" keeps the pieces' shadows
        // (shadow settings from the graphics page apply to it); the walls
        // and ceiling do not cast, so the room is lit by its torches.
        AddChild(new WorldEnvironment { Name = "Environment", Environment = IndoorEnvironment() });
        AddChild(new DirectionalLight3D
        {
            Name = "Skylight",
            Rotation = new Vector3(Mathf.DegToRad(-68f), Mathf.DegToRad(35f), 0f),
            LightColor = new Color(1f, 0.93f, 0.82f),
            LightEnergy = 0.45f,
            ShadowEnabled = true,
            ShadowBlur = 1.5f,
            DirectionalShadowMaxDistance = 45f,
        });
        Meta.GraphicsSettings.ApplyScene(this);

        _solids = new StaticBody3D { Name = "Solids" };
        _tall = new StaticBody3D { Name = "Pieces", CollisionLayer = TallLayer | PieceLayer, CollisionMask = 0 };
        AddChild(_solids);
        AddChild(_tall);

        // One floor collider under every room and corridor.
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

        bool low = Meta.GraphicsSettings.Current.Preset == Meta.GraphicsSettings.Low;
        for (int i = 0; i < Plan.Rooms.Count; i++)
        {
            RoomPlan room = Plan.Rooms[i];
            var root = new Node3D { Name = $"Room{i}" };
            AddChild(root);
            _roomRoots.Add(root);
            _into = root;
            var rng = new Core.Rng(Plan.Seed ^ 0xDEC0UL ^ (ulong)(i * 7349 + 1));
            BuildRoom(room, i, LookFor(StyleOf(room)), rng, low);
            _into = null;
        }

        for (int i = 0; i < Plan.Rooms.Count - 1; i++) _doors.Add(i == 0 ? null : BuildDoor(i));

        // The vault's way out, open once every room is solved.
        RoomPlan vault = Plan.Vault;
        int back = vault.EntrySide;
        _exit = new Portal { Name = "PuzzleExit" };
        AddChild(_exit);
        _exit.Position = vault.Centre - new Vector3(RoomPlan.Dx[back], 0f, RoomPlan.Dz[back]) * 6f;
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

    private static Godot.Environment IndoorEnvironment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = new Color(0.02f, 0.02f, 0.03f),
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = new Color(0.62f, 0.56f, 0.50f),
        AmbientLightEnergy = 0.55f,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        TonemapWhite = 6f,
        GlowEnabled = true,
        GlowIntensity = 0.7f,
        GlowBloom = 0.05f,
        GlowHdrThreshold = 1.1f,
        AdjustmentEnabled = true,
        AdjustmentSaturation = 1.1f,
        AdjustmentContrast = 1.05f,
    };

    // ------------------------------------------------------------ rooms

    private void BuildRoom(RoomPlan room, int i, Look look, Core.Rng rng, bool low)
    {
        var batch = new Batch();
        Vector3 c = room.Centre;
        float h = RoomPlan.Half, t = RoomPlan.WallThick, outer = h + t;
        Surfaces kinds = SurfacesFor(StyleOf(room));
        StandardMaterial3D floor = HallSurfaces.Material(kinds.Floor, look.Floor), wall = HallSurfaces.Material(kinds.Wall, look.Wall);
        StandardMaterial3D lowWall = HallSurfaces.Material(kinds.Low, look.Low), ceiling = HallSurfaces.Material(kinds.Ceiling, look.Ceiling);
        StandardMaterial3D trim = Detail(look.Trim, 0.25f), pilaster = HallSurfaces.Material(HallSurfaces.Kind.Ashlar, look.Wall.Darkened(0.12f));
        StandardMaterial3D inlay = HallSurfaces.Material(HallSurfaces.Kind.Marble, Stone);

        // Floor: the hall's own, the puzzle grid inlaid in its middle (light
        // stone and a checker, as the pieces were drawn for), a trim frame.
        batch.Box(c + Vector3.Down * 0.15f, new Vector3(outer * 2f, 0.3f, outer * 2f), floor);
        float g = RoomPlan.N * RoomPlan.Cell * 0.5f;
        if (room.Kind is not (RoomKind.Start or RoomKind.Reward))
        {
            batch.Box(c + Vector3.Up * 0.005f, new Vector3(g * 2f, 0.01f, g * 2f), inlay);
            var tiles = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new BoxMesh { Size = new Vector3(1.94f, 0.04f, 1.94f) } };
            var dark = new List<Transform3D>();
            for (int cell = 0; cell < RoomPlan.N * RoomPlan.N; cell++)
                if ((RoomPlan.X(cell) + RoomPlan.Z(cell)) % 2 == 0)
                    dark.Add(new Transform3D(Basis.Identity, room.World(cell) + new Vector3(0f, 0.005f, 0f)));
            tiles.InstanceCount = dark.Count;
            for (int k = 0; k < dark.Count; k++) tiles.SetInstanceTransform(k, dark[k]);
            Put(new MultiMeshInstance3D { Multimesh = tiles, MaterialOverride = HallSurfaces.Material(HallSurfaces.Kind.Marble, StoneDark), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        else
        {
            // A rug of light stone where a grid would be.
            batch.Box(c + Vector3.Up * 0.005f, new Vector3(g * 1.4f, 0.01f, g * 1.4f), inlay);
        }
        foreach (int s in new[] { -1, 1 })
        {
            batch.Box(c + new Vector3(s * (g + 0.35f), 0.02f, 0f), new Vector3(0.7f, 0.04f, g * 2f + 1.4f), trim);
            batch.Box(c + new Vector3(0f, 0.02f, s * (g + 0.35f)), new Vector3(g * 2f + 1.4f, 0.04f, 0.7f), trim);
        }

        // Walls, with a doorway on the entry and exit sides.
        for (int side = 0; side < 4; side++)
        {
            bool door = side == room.EntrySide || side == room.ExitSide;
            var dir = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
            var along = new Vector3(Mathf.Abs(RoomPlan.Dz[side]), 0f, Mathf.Abs(RoomPlan.Dx[side]));
            Vector3 mid = c + dir * (h + t * 0.5f);
            float length = outer * 2f;
            if (!door) WallBox(batch, mid, along, length, 0f, WallHeight, wall);
            else
            {
                float seg = (length - DoorWidth) * 0.5f;
                float off = DoorWidth * 0.5f + seg * 0.5f;
                WallBox(batch, mid + along * off, along, seg, 0f, WallHeight, wall);
                WallBox(batch, mid - along * off, along, seg, 0f, WallHeight, wall);
                WallBox(batch, mid, along, DoorWidth, DoorHeight, WallHeight, wall);
                // A frame round the doorway on the inside face.
                Vector3 face = c + dir * (h - 0.15f);
                foreach (float s in new[] { -1f, 1f })
                    batch.Box(face + along * s * (DoorWidth * 0.5f + 0.35f) + Vector3.Up * (DoorHeight * 0.5f + 0.3f), along * 0.7f + new Vector3(0.5f, DoorHeight + 0.6f, 0.5f) - along * 0.5f, trim);
                batch.Box(face + Vector3.Up * (DoorHeight + 0.45f), along * (DoorWidth + 1.4f) + new Vector3(0.5f, 0.9f, 0.5f) - along * 0.5f, trim);
            }
            // Wainscot, a band at head height, a mid band and a cornice.
            Vector3 inner = c + dir * h;
            Band(batch, inner, dir, along, 0f, 1.4f, 0.12f, lowWall, door);
            Band(batch, inner, dir, along, 1.4f, 1.6f, 0.22f, trim, door);
            Band(batch, inner, dir, along, 6.6f, 6.85f, 0.18f, trim, false, DoorHeight);
            Band(batch, inner, dir, along, WallHeight - 0.7f, WallHeight, 0.45f, trim, false, DoorHeight);
            // Pilasters every 7.5 m, clear of the doorway.
            foreach (float a in new[] { -12.5f, -7.5f, 7.5f, 12.5f })
            {
                Vector3 at = inner + along * a - dir * 0.25f;
                batch.Box(at + Vector3.Up * (WallHeight * 0.5f), along * 1.1f + new Vector3(0.5f, WallHeight, 0.5f) - along * 0.5f, pilaster, shadow: true);
                batch.Box(at + Vector3.Up * 0.4f - dir * 0.06f, along * 1.4f + new Vector3(0.62f, 0.8f, 0.62f) - along * 0.62f, trim);
            }
            // Torches on the pilasters at 7.5 m: every other one carries a light.
            foreach (float a in new[] { -7.5f, 7.5f })
            {
                bool lit = !low ? a > 0f : a > 0f && side % 2 == 0;
                Torch(batch, inner + along * a - dir * 0.5f, dir, look.Light, lit);
            }
        }

        // Ceiling and its beams (both solid: the camera's arm stops under them).
        batch.Box(c + Vector3.Up * (WallHeight + 0.3f), new Vector3(outer * 2f, 0.6f, outer * 2f), ceiling);
        _solids.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(outer * 2f, 0.6f, outer * 2f) }, Position = c + Vector3.Up * (WallHeight + 0.3f) });
        foreach (float x in new[] { -7.5f, 0f, 7.5f })
        {
            Vector3 at = c + new Vector3(x, WallHeight - 0.35f, 0f);
            batch.Box(at, new Vector3(0.8f, 0.7f, h * 2f), HallSurfaces.Material(HallSurfaces.Kind.Planks, look.Ceiling.Lightened(0.15f)));
            _solids.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.8f, 0.7f, h * 2f) }, Position = at });
        }

        // A chandelier over the middle: the room's main light.
        Chandelier(batch, c + Vector3.Up * 9.5f, look);

        if (i < Plan.Rooms.Count - 1) BuildCorridor(batch, room, look, kinds);
        BuildSign(room, i);
        Dress(room, look, rng);
        batch.Commit(_into!);
    }

    /// <summary>A wall box from <paramref name="y0"/> to <paramref name="y1"/>, drawn and solid.</summary>
    private void WallBox(Batch batch, Vector3 mid, Vector3 along, float length, float y0, float y1, Material mat)
    {
        Vector3 size = along * length + new Vector3(RoomPlan.WallThick, y1 - y0, RoomPlan.WallThick) - along * RoomPlan.WallThick;
        Vector3 at = mid + Vector3.Up * ((y0 + y1) * 0.5f);
        batch.Box(at, size, mat, shadow: false);
        _solids.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = at });
    }

    /// <summary>A horizontal band on the inner face of a wall, broken by the
    /// doorway when it is low enough to cross it (<paramref name="gap"/>), or
    /// passing above a doorway lower than <paramref name="clear"/>.</summary>
    private static void Band(Batch batch, Vector3 inner, Vector3 dir, Vector3 along, float y0, float y1, float depth, Material mat, bool gap, float clear = 0f)
    {
        float full = RoomPlan.Half * 2f;
        Vector3 Size(float len) => along * len + new Vector3(depth, y1 - y0, depth) - along * depth;
        Vector3 at = inner - dir * (depth * 0.5f) + Vector3.Up * ((y0 + y1) * 0.5f);
        if (!gap || y0 >= DoorHeight)
        {
            batch.Box(at, Size(full), mat, shadow: false);
            return;
        }
        float seg = (full - DoorWidth - 1.4f) * 0.5f;
        float off = (DoorWidth + 1.4f) * 0.5f + seg * 0.5f;
        batch.Box(at + along * off, Size(seg), mat, shadow: false);
        batch.Box(at - along * off, Size(seg), mat, shadow: false);
    }

    /// <summary>A wall torch: bracket, bowl and flame; a real light when <paramref name="lit"/>.</summary>
    private void Torch(Batch batch, Vector3 at, Vector3 dir, Color light, bool lit)
    {
        Vector3 y = Vector3.Up * 4.4f;
        batch.Box(at + y - dir * 0.15f, new Vector3(0.3f, 0.5f, 0.3f), Shared(BronzeDark));
        batch.Add(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.14f, Height = 0.35f, RadialSegments = 10 }, new Transform3D(Basis.Identity, at + y - dir * 0.45f + Vector3.Up * 0.3f), Shared(BronzeDark));
        batch.Add(new SphereMesh { Radius = 0.22f, Height = 0.6f, RadialSegments = 8, Rings = 4 }, new Transform3D(Basis.Identity, at + y - dir * 0.45f + Vector3.Up * 0.62f), SharedGlow(Flame, 3.2f));
        if (!lit) return;
        Put(new OmniLight3D
        {
            Position = at + y - dir * 1.1f + Vector3.Up * 0.8f,
            LightColor = light,
            LightEnergy = 2.2f,
            OmniRange = 15f,
            OmniAttenuation = 1.2f,
            ShadowEnabled = false,
        });
    }

    private void Chandelier(Batch batch, Vector3 at, Look look)
    {
        var iron = Shared(new Color(0.16f, 0.14f, 0.12f));
        batch.Add(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = WallHeight - at.Y, RadialSegments = 6 }, new Transform3D(Basis.Identity, at + Vector3.Up * ((WallHeight - at.Y) * 0.5f)), iron);
        batch.Add(new TorusMesh { InnerRadius = 1.9f, OuterRadius = 2.1f, Rings = 24, RingSegments = 6 }, new Transform3D(Basis.Identity, at), iron);
        for (int k = 0; k < 8; k++)
        {
            float a = Mathf.Tau * k / 8f;
            Vector3 p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2f;
            batch.Add(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 0.4f, RadialSegments = 6 }, new Transform3D(Basis.Identity, p + Vector3.Up * 0.2f), Shared(WallCap));
            batch.Add(new SphereMesh { Radius = 0.1f, Height = 0.26f, RadialSegments = 6, Rings = 3 }, new Transform3D(Basis.Identity, p + Vector3.Up * 0.5f), SharedGlow(Flame, 3f));
        }
        Put(new OmniLight3D
        {
            Position = at + Vector3.Down * 0.5f,
            LightColor = look.Light,
            LightEnergy = 2.4f,
            OmniRange = 26f,
            OmniAttenuation = 0.9f,
            ShadowEnabled = false,
        });
    }

    /// <summary>From room <paramref name="room"/>'s exit doorway to the next
    /// room's entrance: floor, walls, ceiling and one lamp.</summary>
    private void BuildCorridor(Batch batch, RoomPlan room, Look look, Surfaces kinds)
    {
        int side = room.ExitSide;
        var dir = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
        var along = new Vector3(Mathf.Abs(RoomPlan.Dz[side]), 0f, Mathf.Abs(RoomPlan.Dx[side]));
        float start = RoomPlan.Half + RoomPlan.WallThick, len = RoomPlan.Corridor;
        Vector3 mid = room.Centre + dir * (start + len * 0.5f);
        float w = DoorWidth, ht = DoorHeight;
        Vector3 Size(float across, float y, float lengthwise) => along * across + dir.Abs() * lengthwise + Vector3.Up * y;
        batch.Box(mid + Vector3.Down * 0.15f, Size(w + 2f, 0.3f, len + 0.2f), HallSurfaces.Material(kinds.Floor, look.Floor.Darkened(0.1f)));
        batch.Box(mid + Vector3.Up * 0.01f, Size(w - 1.2f, 0.02f, len + 0.2f), Shared(look.Trim.Darkened(0.2f)));
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 at = mid + along * s * (w * 0.5f + 0.5f) + Vector3.Up * (ht * 0.5f + 0.25f);
            Vector3 size = Size(1f, ht + 0.5f, len + 0.2f);
            batch.Box(at, size, HallSurfaces.Material(HallSurfaces.Kind.Brick, look.Low), shadow: false);
            _solids.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = at });
        }
        Vector3 roof = mid + Vector3.Up * (ht + 0.3f);
        batch.Box(roof, Size(w + 2f, 0.6f, len + 0.2f), HallSurfaces.Material(kinds.Ceiling, look.Ceiling), shadow: false);
        _solids.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Size(w + 2f, 0.6f, len + 0.2f) }, Position = roof });
        // Ribs across the ceiling and a lamp in the middle.
        for (int k = -1; k <= 1; k++)
            batch.Box(mid + dir * (k * 2.6f) + Vector3.Up * (ht - 0.15f), Size(w, 0.3f, 0.4f), Shared(look.Trim.Darkened(0.25f)));
        batch.Add(new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 8, Rings = 4 }, new Transform3D(Basis.Identity, mid + Vector3.Up * (ht - 0.5f)), SharedGlow(Warm, 2.4f));
        Put(new OmniLight3D { Position = mid + Vector3.Up * (ht - 0.9f), LightColor = look.Light, LightEnergy = 1.4f, OmniRange = 7.5f, ShadowEnabled = false });
    }

    private DoorView BuildDoor(int i)
    {
        RoomPlan room = Plan.Rooms[i];
        int side = room.ExitSide;
        var dir = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
        var along = new Vector3(Mathf.Abs(RoomPlan.Dz[side]), 0f, Mathf.Abs(RoomPlan.Dx[side]));
        Vector3 mid = room.Doorway(side);
        var view = new DoorView
        {
            Body = new StaticBody3D { Name = $"Door{i}" },
            Slab = new Node3D { Name = $"DoorSlab{i}", Position = mid },
        };
        Vector3 size = along * DoorWidth + new Vector3(0.6f, DoorHeight, 0.6f) - along * 0.6f;
        view.Body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = mid + Vector3.Up * (size.Y * 0.5f) });
        AddChild(view.Body);
        _roomRoots[i].AddChild(view.Slab);
        // Timber boards, mapped on the slab itself so the grain rides up with it.
        view.Slab.AddChild(Mesh(new BoxMesh { Size = size }, Vector3.Up * (size.Y * 0.5f), HallSurfaces.Material(HallSurfaces.Kind.Planks, Bronze, world: false)));
        // Studded bands and a lock emblem on both faces: ring and bar. Shape, not colour, says "shut".
        foreach (float f in new[] { -1f, 1f })
        {
            foreach (float y in new[] { 0.7f, DoorHeight - 0.7f })
                view.Slab.AddChild(Mesh(new BoxMesh { Size = along * DoorWidth + new Vector3(0.1f, 0.28f, 0.1f) - along * 0.1f }, Vector3.Up * y + dir * f * 0.33f, Shared(BronzeDark)));
            var ring = Mesh(new TorusMesh { InnerRadius = 0.5f, OuterRadius = 0.72f, Rings = 20, RingSegments = 8 }, Vector3.Up * 2.6f + dir * f * 0.32f, Shared(BronzeDark));
            ring.Rotation = side % 2 == 0 ? new Vector3(0f, 0f, Mathf.Pi * 0.5f) : new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
            view.Slab.AddChild(ring);
            view.Slab.AddChild(Mesh(new BoxMesh { Size = along * 0.34f + new Vector3(0.12f, 1.1f, 0.12f) - along * 0.12f }, Vector3.Up * 1.75f + dir * f * 0.32f, Shared(BronzeDark)));
        }
        return view;
    }

    /// <summary>A sign over the doorway into each room: its number and name.</summary>
    private void BuildSign(RoomPlan room, int i)
    {
        string text = room.Kind switch
        {
            RoomKind.Start => Meta.Loc.T(Plan.Name).ToUpperInvariant(),
            RoomKind.Reward => Meta.Loc.T(PuzzleText.Title(room)),
            _ => Meta.Loc.F("{0}/{1}  ·  {2}", i, Plan.PuzzleRooms, Meta.Loc.T(PuzzleText.Title(room))),
        };
        // Over the exit in the start room (the way you face on arrival), else
        // over the entrance, read from inside the room.
        int side = room.Kind == RoomKind.Start ? room.ExitSide : room.EntrySide;
        Vector3 at = room.Centre + new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]) * (RoomPlan.Half - 1.2f) + Vector3.Up * (DoorHeight + 2.2f);
        Put(new Label3D
        {
            Name = $"Sign{i}",
            Text = text,
            FontSize = 48,
            PixelSize = 0.011f,
            // Read from across a room; gone when the camera is on top of it.
            VisibilityRangeBegin = 6f,
            OutlineSize = 12,
            Modulate = new Color(1f, 0.97f, 0.88f),
            OutlineModulate = new Color(0.12f, 0.2f, 0.26f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = at,
        });
    }

    // ------------------------------------------------------------- props

    /// <summary>Props along the walls, themed by the room's piece type: the
    /// strip between the puzzle grid and the walls stays clear to walk, and
    /// nothing stands in front of a doorway.</summary>
    private void Dress(RoomPlan room, Look look, Core.Rng rng)
    {
        string nature = ArtAssets.Root + "kenney/nature-kit/";
        string grave = ArtAssets.Root + "kenney/graveyard-kit/";
        float h = RoomPlan.Half;
        // Corners: one big piece each.
        HallStyle style = StyleOf(room);
        string[] corner = style switch
        {
            HallStyle.Observatory => new[] { nature + "statue_ring.glb", nature + "statue_obelisk.glb" },
            HallStyle.Temple => new[] { grave + "pillar-obelisk.glb", nature + "statue_obelisk.glb" },
            HallStyle.Gallery or HallStyle.Garden or HallStyle.Aqueduct => new[] { grave + "pillar-large.glb", nature + "plant_bushLarge.glb" },
            HallStyle.Foundry or HallStyle.Ember => new[] { grave + "pillar-large.glb" },
            HallStyle.Workshop or HallStyle.Counting => new[] { "crates" },
            HallStyle.Frost => new[] { nature + "statue_obelisk.glb", grave + "pillar-obelisk.glb" },
            HallStyle.Vault => new[] { nature + "statue_column.glb", nature + "statue_obelisk.glb" },
            _ => new[] { nature + "statue_column.glb", nature + "statue_columnDamaged.glb", nature + "statue_obelisk.glb" },
        };
        foreach (Vector2 k in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            Vector3 at = room.World(k * (h - 1.6f));
            string pick = corner[rng.RangeInt(0, corner.Length)];
            if (pick == "crates") CrateStack(at, rng, look);
            else KitModel(pick, pick.Contains("plant") ? 2.2f : 4.2f, at, rng.Range(0f, Mathf.Tau));
            PropCollider(at, 1.1f, 4f);
        }
        // Wall slots: between the pilasters, clear of doorways.
        for (int side = 0; side < 4; side++)
        {
            bool door = side == room.EntrySide || side == room.ExitSide;
            var dir = new Vector3(RoomPlan.Dx[side], 0f, RoomPlan.Dz[side]);
            var along = new Vector3(-RoomPlan.Dz[side], 0f, RoomPlan.Dx[side]);
            foreach (float a in door ? new[] { -10f, 10f } : new[] { -10f, -3.6f, 3.6f, 10f })
            {
                Vector3 at = room.Centre + dir * (h - 1.1f) + along * a;
                float yaw = Mathf.Atan2(-dir.X, -dir.Z);
                WallProp(style, at, dir, along, yaw, rng, look, nature, grave);
            }
            // Braziers flanking a doorway.
            if (door)
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 at = room.Centre + dir * (h - 1.3f) + along * s * (DoorWidth * 0.5f + 1.6f);
                    Brazier(at);
                    PropCollider(at, 0.5f, 1.5f);
                }
        }
    }

    /// <summary>An iron bowl of fire on a stand.</summary>
    private void Brazier(Vector3 at)
    {
        var iron = Shared(new Color(0.20f, 0.18f, 0.17f));
        Put(Mesh(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.32f, Height = 1.0f, RadialSegments = 8 }, at + Vector3.Up * 0.5f, iron));
        Put(Mesh(new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.25f, Height = 0.4f, RadialSegments = 12 }, at + Vector3.Up * 1.15f, iron));
        var fire = Mesh(new SphereMesh { Radius = 0.38f, Height = 0.9f, RadialSegments = 8, Rings = 4 }, at + Vector3.Up * 1.55f, SharedGlow(Flame, 3f), false);
        Put(fire);
    }

    private void WallProp(HallStyle style, Vector3 at, Vector3 dir, Vector3 along, float yaw, Core.Rng rng, Look look, string nature, string grave)
    {
        switch (style)
        {
            case HallStyle.Workshop:
                if (rng.Chance(0.5f)) CrateStack(at, rng, look);
                else Barrels(at, along, rng);
                PropCollider(at, 1.1f, 2.4f);
                break;
            case HallStyle.Observatory:
            case HallStyle.Frost:
                Crystal(at, rng);
                PropCollider(at, 0.8f, 2.6f);
                break;
            case HallStyle.Gallery:
            case HallStyle.Garden:
            case HallStyle.Aqueduct:
                Banner(at, dir, look, rng);
                KitModel(nature + "plant_bush.glb", 1.1f, at - dir * 0.2f, rng.Range(0f, Mathf.Tau));
                break;
            case HallStyle.Temple:
                KitModel(grave + (rng.Chance(0.5f) ? "lantern-candle.glb" : "urn-round.glb"), 1.3f, at, rng.Range(0f, Mathf.Tau));
                Banner(at, dir, look, rng);
                PropCollider(at, 0.6f, 1.4f);
                break;
            case HallStyle.Foundry:
                Gear(at, dir, along, rng, look);
                PropCollider(at, 0.9f, 2.4f);
                break;
            case HallStyle.Ember:
                Brazier(at);
                Banner(at, dir, look, rng);
                PropCollider(at, 0.5f, 1.5f);
                break;
            case HallStyle.Counting:
                if (rng.Chance(0.5f)) Barrels(at, along, rng);
                else KitModel(ArtAssets.Root + "kenney/mini-dungeon/chest.glb", 1.1f, at, yaw);
                PropCollider(at, 1.0f, 1.6f);
                break;
            case HallStyle.Vault:
                KitModel(ArtAssets.Root + "kenney/mini-dungeon/chest.glb", 1.1f, at, yaw);
                PropCollider(at, 0.8f, 1.2f);
                break;
            default:
                KitModel(grave + "urn-round.glb", 1.2f, at, rng.Range(0f, Mathf.Tau));
                Banner(at, dir, look, rng);
                PropCollider(at, 0.6f, 1.2f);
                break;
        }
    }

    /// <summary>A prop the players (not the camera) bump into.</summary>
    private void PropCollider(Vector3 at, float radius, float height)
        => _tall.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = height }, Position = at + Vector3.Up * (height * 0.5f) });

    private void CrateStack(Vector3 at, Core.Rng rng, Look look)
    {
        var wood = Shared(new Color(0.62f, 0.45f, 0.28f));
        var band = Shared(new Color(0.40f, 0.27f, 0.16f));
        int n = rng.RangeInt(2, 4);
        for (int k = 0; k < n; k++)
        {
            float s = k < 2 ? 1.1f : 0.9f;
            Vector3 p = at + (k == 0 ? new Vector3(-0.55f, 0f, 0f) : k == 1 ? new Vector3(0.6f, 0f, 0.1f) : new Vector3(0f, 1.1f, 0f));
            var box = Mesh(new BoxMesh { Size = Vector3.One * s }, p + Vector3.Up * (s * 0.5f), wood);
            box.Rotation = new Vector3(0f, rng.Range(-0.3f, 0.3f), 0f);
            Put(box);
            var strap = Mesh(new BoxMesh { Size = new Vector3(s + 0.04f, 0.12f, s + 0.04f) }, Vector3.Up * 0.25f * s, band);
            box.AddChild(strap);
        }
    }

    private void Barrels(Vector3 at, Vector3 along, Core.Rng rng)
    {
        var wood = Shared(new Color(0.50f, 0.33f, 0.20f));
        var hoop = Shared(new Color(0.25f, 0.24f, 0.24f));
        foreach (float s in new[] { -0.6f, 0.6f })
        {
            Vector3 p = at + along * s;
            Put(Mesh(new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.45f, Height = 1.2f, RadialSegments = 12 }, p + Vector3.Up * 0.6f, wood));
            foreach (float y in new[] { 0.25f, 0.95f })
                Put(Mesh(new CylinderMesh { TopRadius = 0.47f, BottomRadius = 0.47f, Height = 0.08f, RadialSegments = 12 }, p + Vector3.Up * y, hoop, false));
        }
    }

    private void Crystal(Vector3 at, Core.Rng rng)
    {
        var tint = new Color(0.62f, 0.86f, 1f);
        Put(Mesh(new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.8f, Height = 0.5f, RadialSegments = 8 }, at + Vector3.Up * 0.25f, Shared(WallCap)));
        for (int k = 0; k < 3; k++)
        {
            float hgt = rng.Range(1.0f, 2.1f);
            var m = Mesh(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.26f, Height = hgt, RadialSegments = 6 }, at + new Vector3((k - 1) * 0.3f, 0.5f + hgt * 0.5f, rng.Range(-0.2f, 0.2f)),
                new StandardMaterial3D { AlbedoColor = tint, EmissionEnabled = true, Emission = tint, EmissionEnergyMultiplier = 0.9f, Roughness = 0.2f, Metallic = 0.3f });
            m.Rotation = new Vector3(rng.Range(-0.2f, 0.2f), 0f, rng.Range(-0.25f, 0.25f));
            Put(m);
        }
    }

    /// <summary>A long banner hanging on the wall from the mid band.</summary>
    private void Banner(Vector3 at, Vector3 dir, Look look, Core.Rng rng)
    {
        Vector3 wall = at + dir * 0.95f;
        var cloth = Shared(look.Trim.Lerp(new Color(0.6f, 0.15f, 0.12f), rng.Range(0.3f, 0.7f)));
        var size = new Vector3(Mathf.Abs(dir.Z) * 1.6f + 0.06f * Mathf.Abs(dir.X), 4.2f, Mathf.Abs(dir.X) * 1.6f + 0.06f * Mathf.Abs(dir.Z));
        Put(Mesh(new BoxMesh { Size = size }, wall + Vector3.Up * 4.4f, cloth, false));
        var rail = new Vector3(size.X * 1.15f, 0.12f, size.Z * 1.15f);
        Put(Mesh(new BoxMesh { Size = rail }, wall + Vector3.Up * 6.55f, Shared(BronzeDark), false));
    }

    /// <summary>Foundry dressing: a big gear on the wall and a pipe down to the floor.</summary>
    private void Gear(Vector3 at, Vector3 dir, Vector3 along, Core.Rng rng, Look look)
    {
        var iron = Shared(new Color(0.30f, 0.29f, 0.28f));
        var copper = Shared(look.Trim);
        Vector3 wall = at + dir * 0.8f;
        float r = rng.Range(0.9f, 1.4f);
        var gear = Mesh(new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.25f, RadialSegments = 14 }, wall + Vector3.Up * 3.6f, iron);
        gear.Rotation = dir.X != 0f ? new Vector3(0f, 0f, Mathf.Pi * 0.5f) : new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
        Put(gear);
        var hub = Mesh(new CylinderMesh { TopRadius = r * 0.3f, BottomRadius = r * 0.3f, Height = 0.4f, RadialSegments = 10 }, Vector3.Zero, copper);
        gear.AddChild(hub);
        Put(Mesh(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.18f, Height = 6f, RadialSegments = 8 }, wall + along * (r + 0.4f) + Vector3.Up * 3f, copper));
        Put(Mesh(new BoxMesh { Size = new Vector3(1.2f, 1.1f, 1.2f) }, at + Vector3.Up * 0.55f, iron));
    }

    /// <summary>An imported model <paramref name="height"/> metres tall; skipped quietly when missing.</summary>
    private void KitModel(string path, float height, Vector3 at, float yaw)
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
        Put(model);
    }

    // -------------------------------------------------------- visibility

    /// <summary>Draw the room this machine's player is in (a corridor counts
    /// as the nearer room) and the rooms either side of it; hide the rest.
    /// The walls already block the view, so this is about not drawing what
    /// cannot be seen, and about the lights.</summary>
    private void SyncVisibility()
    {
        Vector3? at = Actors.Player.Instance is { } me && IsInstanceValid(me) ? me.GlobalPosition : null;
        int r = at is { } p ? Plan.NearestRoom(p) : 0;
        if (r == _shownAround) return;
        _shownAround = r;
        for (int i = 0; i < _roomRoots.Count; i++) _roomRoots[i].Visible = Math.Abs(i - r) <= 1;
    }

    /// <summary>Rooms drawn right now (tests and the perf line).</summary>
    public int RoomsShown
    {
        get
        {
            int n = 0;
            foreach (Node3D r in _roomRoots) if (r.Visible) n++;
            return n;
        }
    }

    /// <summary>Open door i (animated in <see cref="SyncDoors"/>): it slides up into the wall.</summary>
    private void SyncDoors(float dt)
    {
        for (int i = 0; i < _doors.Count; i++)
        {
            if (_doors[i] is not { } d) continue;
            bool open = State.DoorOpen(i);
            if (open && !d.Opened)
            {
                d.Opened = true;
                foreach (Node n in d.Body.GetChildren()) if (n is CollisionShape3D cs) cs.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
                Audio.AudioDirector.PlayPuzzle(Audio.PuzzleSound.Door);
                if (Actors.Player.Instance is { } me && IsInstanceValid(me) && me.GlobalPosition.DistanceTo(d.Slab.GlobalPosition) < 24f)
                    Actors.PlayerCamera.Instance?.Shake(0.08f, 0.9f);
            }
            float target = open ? 1f : 0f;
            if (Mathf.IsEqualApprox(d.Open, target)) continue;
            d.Open = Mathf.MoveToward(d.Open, target, dt * 0.7f);
            // Eased: a heavy start, a soft stop in the lintel.
            float e = d.Open * d.Open * (3f - 2f * d.Open);
            d.Slab.Position = new Vector3(d.Slab.Position.X, (DoorHeight + 0.1f) * e, d.Slab.Position.Z);
            d.Slab.Visible = d.Open < 0.99f;
        }
    }

    /// <summary>Has door i opened (tests)?</summary>
    public bool DoorIsOpen(int i) => i >= 0 && i < _doors.Count && (_doors[i]?.Opened ?? true);

    /// <summary>How far door i has slid open, 0 to 1 (captures).</summary>
    public float DoorOpenness(int i) => i >= 0 && i < _doors.Count ? _doors[i]?.Open ?? 1f : 0f;

    // ----------------------------------------------------------- helpers

    /// <summary>Static meshes merged per material: one draw per material per room.</summary>
    private sealed class Batch
    {
        private readonly Dictionary<(Material Mat, bool Shadow), SurfaceTool> _tools = new();

        public void Box(Vector3 centre, Vector3 size, Material mat, bool shadow = false)
            => Add(new BoxMesh { Size = size }, new Transform3D(Basis.Identity, centre), mat, shadow);

        public void Add(Mesh mesh, Transform3D xf, Material mat, bool shadow = false)
        {
            if (!_tools.TryGetValue((mat, shadow), out SurfaceTool? st))
            {
                st = new SurfaceTool();
                st.Begin(Godot.Mesh.PrimitiveType.Triangles);
                _tools[(mat, shadow)] = st;
            }
            st.AppendFrom(mesh, 0, xf);
        }

        public void Commit(Node3D parent)
        {
            foreach (((Material mat, bool shadow), SurfaceTool st) in _tools)
                parent.AddChild(new MeshInstance3D
                {
                    Name = "Shell",
                    Mesh = st.Commit(),
                    MaterialOverride = mat,
                    CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                });
            _tools.Clear();
        }
    }

    private static MeshInstance3D Mesh(Mesh mesh, Vector3 at, Material mat, bool shadow = true) => new()
    {
        Mesh = mesh,
        Position = at,
        MaterialOverride = mat,
        CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private static readonly Dictionary<Color, StandardMaterial3D> SharedCache = new();
    private static readonly Dictionary<(Color, float), StandardMaterial3D> GlowCache = new();
    private static readonly Dictionary<(Color, float), StandardMaterial3D> DetailCache = new();

    /// <summary>A plain matte colour of its own (pieces animate theirs).</summary>
    private static StandardMaterial3D Flat(Color c) => new() { AlbedoColor = c, Roughness = 0.85f };

    /// <summary>A plain matte colour shared by everything static, so a room's
    /// shell merges into few meshes (never animate one of these).</summary>
    private static StandardMaterial3D Shared(Color c)
    {
        if (!SharedCache.TryGetValue(c, out StandardMaterial3D? m) || !IsInstanceValid(m)) SharedCache[c] = m = Flat(c);
        return m;
    }

    private static StandardMaterial3D SharedGlow(Color c, float energy)
    {
        if (!GlowCache.TryGetValue((c, energy), out StandardMaterial3D? m) || !IsInstanceValid(m)) GlowCache[(c, energy)] = m = Glow(c, energy);
        return m;
    }

    private static NoiseTexture2D? _grain;

    /// <summary>Stone or plaster: the colour broken up by a soft, world-mapped grain.</summary>
    private static StandardMaterial3D Detail(Color c, float scale)
    {
        if (DetailCache.TryGetValue((c, scale), out StandardMaterial3D? m) && IsInstanceValid(m)) return m;
        // Shared by every room that asks for this colour (never animated).
        _grain ??= new NoiseTexture2D
        {
            Width = 256,
            Height = 256,
            Seamless = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.035f, FractalOctaves = 2 },
            ColorRamp = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(0.80f, 0.80f, 0.80f), Colors.White } },
        };
        m = new StandardMaterial3D
        {
            AlbedoColor = c,
            AlbedoTexture = _grain,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = Vector3.One * scale,
            Roughness = 0.9f,
        };
        DetailCache[(c, scale)] = m;
        return m;
    }

    private static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
}
