using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>
/// The playfield for one stage: collision floor, wall, terrain, landmarks and
/// dressing, built from a <see cref="StageDef"/> and its <see cref="PropTheme"/>.
///
/// Two layers, deliberately separate:
///
/// <b>Gameplay geometry</b> is built here from primitives — the floor polygon,
/// the wall, mounds, platforms, ramps, ridges and a few solid landmarks. All of
/// it collides, and all of it is placed by this file's own rejection sampler so
/// that it never overlaps itself or the clear zone at the centre.
///
/// <b>Dressing</b> comes from <see cref="PropFactory"/>: the themed floor shader,
/// sky, fog, sun, and a dense scatter of trees, rocks and grass that does
/// <em>not</em> collide. Scatter that lands inside a terrain footprint is culled
/// afterwards, so no tree grows through a platform.
///
/// Swapping in a hand-built level later means replacing this one node; nothing
/// else refers to its contents — the director finds the floor by raycast and the
/// player by collision. The arena is rebuilt on every phase change, so it must
/// be safe to free and replace mid-run: everything it creates is a child, and
/// the only state it leaves behind is <see cref="Current"/>.
/// </summary>
public sealed partial class Arena : Node3D
{
    /// <summary>Stage of the arena currently in the tree. Static because
    /// clamping and steering are asked about by code that has no reference to
    /// the scene root, and there is only ever one arena.</summary>
    public static StageDef Current { get; private set; } = Stages.Run[0];

    /// <summary>Usable play radius: distance from the centre to the inside of
    /// the nearest wall. On a polygon that is the in-radius, not the corner
    /// distance, so anything kept inside it is inside on every side.</summary>
    public static float Radius => Current.InRadius;

    /// <summary>Where the player arrives and where the portal opens. Always
    /// inside the stage's clear zone, so it can never be buried in terrain.</summary>
    public static Vector3 Centre => Vector3.Zero;

    /// <summary>Point the clamp at a stand-in shape while no arena is in the
    /// tree (the rest room between maps). The next arena built resets it.</summary>
    public static void UseBounds(StageDef bounds) => Current = bounds;

    /// <summary>Collision for the wall reaches far above anything drawn. The jump
    /// apex is 6 m with an air jump on top, and a wall the player can hop over
    /// leads to a floor that is not there.</summary>
    private const float WallColliderHeight = 40f;
    private const float WallHeight = 4f;

    /// <summary>How far the visual floor runs past the wall, so the edge of the
    /// map reads as a fence in a landscape rather than the edge of a plate.</summary>
    private const float FloorSkirt = 60f;

    private readonly StageDef _stage;
    private readonly PropTheme _theme;
    private readonly RandomNumberGenerator _rng = new();

    // Footprints of everything solid placed so far, so features do not overlap
    // each other or the clear zone, and so scatter can be culled out of them.
    private readonly List<(Vector2 At, float Radius)> _footprints = new();

    private StaticBody3D _body = null!;

    public Arena() : this(Stages.Run[0]) { }

    public Arena(StageDef stage)
    {
        _stage = stage;
        _theme = PropTheme.Get(stage.Theme);
        Current = stage;
    }

    public override void _Ready()
    {
        using var timing = Prof.Scope("Startup.arena");
        Name = "Arena";
        Current = _stage;
        _rng.Seed = (ulong)_stage.LayoutSeed;

        // One body for every solid piece. Hundreds of StaticBody3D nodes would
        // each be a physics object; one body with many shapes is one.
        _body = new StaticBody3D { Name = "Solids" };
        AddChild(_body);

        BuildGround();
        BuildWall();
        BuildCentrePad();
        BuildLandforms();   // Arena.Landforms.cs: hand-built maps only
        BuildTerrain();
        BuildDressing();
    }

    // ---------------------------------------------------------------- floor

    private int Sides => Mathf.Max(3, _stage.Sides);

    /// <summary>Angle of the middle of edge <paramref name="k"/>. Corners sit
    /// half a step either side, so edge 0 faces +X on every polygon.</summary>
    private float EdgeAngle(int k) => Mathf.Tau * k / Sides;

    private Vector3 Corner(int k, float circumradius)
    {
        float a = Mathf.Tau * (k - 0.5f) / Sides;
        return new Vector3(Mathf.Cos(a) * circumradius, 0f, Mathf.Sin(a) * circumradius);
    }

    /// <summary>
    /// Collision only. The floor the player sees is the theme's shader disc,
    /// which overhangs the wall; the floor the player stands on is this exact
    /// polygon, whose edge is the wall's inner face. Enemy spawns find ground by
    /// raycast, so a ray that misses the polygon — outside the wall — is
    /// correctly rejected even though the visual floor continues out there.
    /// </summary>
    private void BuildGround()
    {
        if (_stage.Underwater) { BuildReefGround(); return; }
        var points = new Vector3[Sides * 2];
        for (int k = 0; k < Sides; k++)
        {
            Vector3 a = Corner(k, _stage.Radius);
            points[k * 2] = a;
            points[k * 2 + 1] = a + Vector3.Down;
        }
        _body.AddChild(new CollisionShape3D { Name = "Ground", Shape = new ConvexPolygonShape3D { Points = points } });
    }

    private void BuildWall()
    {
        if (_stage.Underwater) return; // The seabed mesh rises into colliding mountain cliffs.
        float visibleHeight = _stage.Layout == "city" ? 80f : WallHeight;
        float solidHeight = _stage.Layout == "city" ? 80f : WallColliderHeight;
        StandardMaterial3D wall = Flat(_stage.Wall, emission: 0.35f);
        float edge = 2f * _stage.Radius * Mathf.Sin(Mathf.Pi / Sides);
        for (int k = 0; k < Sides; k++)
        {
            float a = EdgeAngle(k);
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Basis basis = XAlong(Tangent(dir));
            // Centred half a metre outside the edge, so the inner face is the
            // edge itself and the floor polygon is wholly inside the walls.
            Vector3 centre = dir * (_stage.InRadius + 0.5f);
            float length = edge + 1.2f;

            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(length, visibleHeight, 1f) },
                Transform = new Transform3D(basis, centre + Vector3.Up * (visibleHeight * 0.5f)),
                MaterialOverride = wall,
            });
            _body.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(length, solidHeight, 1f) },
                Transform = new Transform3D(basis, centre + Vector3.Up * (solidHeight * 0.5f - (_stage.Layout == "city" ? 0f : 1f))),
            });
        }
    }

    /// <summary>A faint ring at the centre: where the run starts, where each
    /// new map is entered, and where the portal will open. Marking it from the
    /// first second means the portal appearing there later reads as something
    /// the map promised rather than something random.</summary>
    private void BuildCentrePad()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 2.9f, OuterRadius = 3.2f, RingSegments = 48 },
            Position = new Vector3(0f, 0.04f, 0f),
            Scale = new Vector3(1f, 0.1f, 1f),
            MaterialOverride = Flat(_stage.Wall, emission: 0.8f),
        });
    }

    // -------------------------------------------------------------- terrain

    private void BuildTerrain()
    {
        if (_stage.Layout == "city") { SnapshotFeatures(); return; }
        if (_stage.Underwater)
        {
            SnapshotFeatures();
            if (_stage.HazardPools > 0) PlaceHazards();
            return;
        }
        // Largest features first: rejection sampling fills big holes before
        // small things can fragment the space they need.
        for (int i = 0; i < _stage.Platforms; i++) PlacePlatform();
        for (int i = 0; i < _stage.Ridges; i++) PlaceRidge();
        for (int i = 0; i < _stage.Mounds; i++) PlaceMound();
        for (int i = 0; i < _stage.Landmarks; i++) PlaceLandmark();
        SnapshotFeatures();   // for the map views
        // Last, so a map without hazards draws exactly the layout it always did.
        if (_stage.HazardPools > 0) PlaceHazards();
    }

    /// <summary>Flat hazard pools (Game.MapHazards). They reserve a footprint
    /// like any feature, so nothing solid stands in one and scatter is culled.</summary>
    private void PlaceHazards()
    {
        var pools = new List<Vector3>();
        for (int i = 0; i < _stage.HazardPools; i++)
            if (NextHazardSpot(out Vector3 at)) pools.Add(at);
        AddChild(new MapHazards(_stage, pools));
    }

    /// <summary>
    /// A flat-topped hill. Slopes on every side at about 20°: steep enough that
    /// a slide down one gains real speed, shallow enough that enemies — which
    /// cannot jump — still walk over it rather than piling against it.
    /// </summary>
    private void PlaceMound()
    {
        float h = _stage.MoundHeight * _rng.RandfRange(0.8f, 1.25f);
        float top = _rng.RandfRange(3.5f, 7f);
        float bottom = top + h / Mathf.Tan(Mathf.DegToRad(20f));
        if (!TryPlace(bottom + 1f, out Vector3 at)) return;

        var mesh = new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = h, RadialSegments = 28, Rings = 1 };
        // The theme's own ground shader: a hill is the floor rising, and the
        // shader is world-space, so the two meet without a seam.
        AddSolid(mesh, new Transform3D(Basis.Identity, at + Vector3.Up * (h * 0.5f)), PropFactory.FloorMaterial(_theme));

        // A bright lip on the top edge. From a third-person camera at 26°, the
        // shading difference between a slope and the floor is too small to see;
        // the lip is what says "this is raised".
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = top - 0.14f, OuterRadius = top + 0.14f, RingSegments = 32 },
            Position = at + Vector3.Up * (h + 0.01f),
            Scale = new Vector3(1f, 0.3f, 1f),
            MaterialOverride = Flat(_stage.Wall, emission: 0.45f),
        });
    }

    /// <summary>
    /// A raised slab with one ramp and a pair of pillars on its back corners.
    /// Height is under the 6 m jump apex so the player can always hop up, while
    /// enemies have to find the ramp — which is what makes the top worth
    /// standing on for a few seconds. The pillars make it a landmark you can
    /// navigate by from across the map.
    /// </summary>
    private void PlacePlatform()
    {
        float h = _stage.PlatformHeight * _rng.RandfRange(0.9f, 1.1f);
        float size = _rng.RandfRange(8f, 12f);
        float rampLength = h / Mathf.Tan(Mathf.DegToRad(19f));
        float footprint = size * 0.5f + rampLength * 0.6f + 1.5f;
        if (!TryPlace(footprint, out Vector3 at)) return;

        // The group is offset so the platform and its ramp together are centred
        // on the footprint, rather than the ramp hanging out beyond it.
        var yaw = new Basis(Vector3.Up, _rng.RandfRange(0f, Mathf.Tau));
        Vector3 rampSide = yaw * Vector3.Left;
        Vector3 platform = at - rampSide * (rampLength * 0.5f);

        AddSolid(new BoxMesh { Size = new Vector3(size, h, size) },
                 new Transform3D(yaw, platform + Vector3.Up * (h * 0.5f)), Flat(_stage.Terrain));

        // Lighter top plate: the surface you land on should be the brightest
        // part of the shape, so the target of a jump is unmistakable.
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(size - 0.3f, 0.04f, size - 0.3f) },
            Transform = new Transform3D(yaw, platform + Vector3.Up * (h + 0.02f)),
            MaterialOverride = Flat(_stage.Terrain.Lightened(0.3f), emission: 0.1f),
        });

        // PrismMesh with the apex over its +X edge is a wedge rising toward +X.
        // Turned so +X points back at the platform, its high edge meets the top.
        var ramp = new PrismMesh { LeftToRight = 1f, Size = new Vector3(rampLength, h, 3.8f) };
        Vector3 rampCentre = platform + rampSide * (size * 0.5f + rampLength * 0.5f - 0.05f);
        AddSolid(ramp, new Transform3D(XAlong(-rampSide), rampCentre + Vector3.Up * (h * 0.5f)),
                 Flat(_stage.Terrain.Darkened(0.12f)));

        // Two pillars on the far corners, standing on the platform top.
        Vector3 back = -rampSide * (size * 0.5f - 0.9f);
        Vector3 across = yaw * Vector3.Forward * (size * 0.5f - 0.9f);
        foreach (Vector3 corner in new[] { back + across, back - across })
            AddProp(PropFactory.Kind.Pillar, platform + corner + Vector3.Up * h, 0.8f, collide: true);
    }

    /// <summary>
    /// A long roof-shaped ridge: up one side, down the other, about 17° each.
    /// Pure movement terrain. Walking over it is a small detour; sliding off the
    /// far side is free speed, so the map rewards whoever reads the slopes.
    /// </summary>
    private void PlaceRidge()
    {
        float h = _stage.RidgeHeight * _rng.RandfRange(0.85f, 1.15f);
        float length = 2f * h / Mathf.Tan(Mathf.DegToRad(17f));
        float width = _rng.RandfRange(7f, 12f);
        float footprint = Mathf.Max(length, width) * 0.5f + 1f;
        if (!TryPlace(footprint, out Vector3 at)) return;

        var basis = new Basis(Vector3.Up, _rng.RandfRange(0f, Mathf.Tau));
        var mesh = new PrismMesh { LeftToRight = 0.5f, Size = new Vector3(length, h, width) };
        AddSolid(mesh, new Transform3D(basis, at + Vector3.Up * (h * 0.5f)), PropFactory.FloorMaterial(_theme));

        // A glowing crest line, for the same reason as the mound lip: it is the
        // only cue at a glance that the ground ahead tilts.
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.26f, 0.08f, width) },
            Transform = new Transform3D(basis, at + Vector3.Up * (h + 0.02f)),
            MaterialOverride = Flat(_stage.Wall, emission: 0.45f),
        });
    }

    /// <summary>
    /// A solid landmark from the theme's own kit: a big rock formation, or a
    /// ruin of pillars around an arch. These are the "points of interest" that
    /// give a large map a shape you can remember, and the few obstacles that
    /// actually block — kept well apart so there are always open lanes.
    /// </summary>
    private void PlaceLandmark()
    {
        bool ruin = _rng.Randf() < 0.4f;
        float r = ruin ? 6f : 4f;
        if (!TryPlace(r + 2f, out Vector3 at)) return;

        if (!ruin)
        {
            // A formation: one big rock and two smaller ones leaning on it.
            AddProp(PropFactory.Kind.Rock, at, _rng.RandfRange(2.2f, 3.0f), collide: true);
            for (int i = 0; i < 2; i++)
            {
                float a = _rng.RandfRange(0f, Mathf.Tau);
                AddProp(PropFactory.Kind.Rock, at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.6f,
                        _rng.RandfRange(1.1f, 1.6f), collide: true);
            }
            return;
        }

        // A ruin: an arch you can run through, flanked by broken pillars.
        float yaw = _rng.RandfRange(0f, Mathf.Tau);
        AddProp(PropFactory.Kind.Arch, at, 1.6f, collide: false, yaw: yaw);
        var side = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
        for (int i = 0; i < 4; i++)
        {
            float a = yaw + Mathf.Pi * (0.25f + 0.5f * i);
            Vector3 p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 5f;
            // Keep the doorway clear: nothing directly in line with the arch.
            if (Mathf.Abs((p - at).Normalized().Dot(side)) > 0.9f) continue;
            AddProp(PropFactory.Kind.Pillar, p, _rng.RandfRange(0.9f, 1.3f), collide: true);
        }
    }

    /// <summary>
    /// One theme prop placed by hand, optionally with a cylinder collider sized
    /// from the mesh's own bounds — a kit model and a procedural one differ in
    /// scale by an order of magnitude, so a hard-coded radius would be wrong for
    /// one of them.
    /// </summary>
    private void AddProp(PropFactory.Kind kind, Vector3 at, float scale, bool collide, float? yaw = null)
    {
        Mesh mesh = PropFactory.PropMesh(kind, _theme, _rng.RandiRange(0, 3));
        bool kit = _theme.UsesKit(kind);
        if (kit && _theme.KitScale.TryGetValue(kind, out float k)) scale *= k;

        var basis = new Basis(Vector3.Up, yaw ?? _rng.RandfRange(0f, Mathf.Tau)).Scaled(Vector3.One * scale);
        AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            Transform = new Transform3D(basis, at),
            MaterialOverride = kit ? null : ToonMaterials.Prop,
        });
        if (!collide) return;

        Aabb box = mesh.GetAabb();
        float radius = Mathf.Max(0.4f, Mathf.Max(box.Size.X, box.Size.Z) * 0.5f * scale * 0.75f);
        float height = Mathf.Max(1f, (box.Position.Y + box.Size.Y) * scale);
        _body.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = radius, Height = height },
            Position = at + Vector3.Up * (height * 0.5f),
        });
    }

    /// <summary>
    /// Rejection-sample a spot for a feature of radius <paramref name="r"/>:
    /// inside the walls, outside the clear zone, and not overlapping anything
    /// already placed. Gives up quietly after a bounded number of tries — a map
    /// with one ridge fewer than asked for is fine; a hang at load is not.
    /// </summary>
    private bool TryPlace(float r, out Vector3 at)
    {
        float max = _stage.InRadius - r - 4f;
        float min = _stage.ClearRadius + r;
        for (int attempt = 0; attempt < 60 && max > min; attempt++)
        {
            float a = _rng.RandfRange(0f, Mathf.Tau);
            float d = Mathf.Sqrt(_rng.RandfRange(min * min / (max * max), 1f)) * max;
            var p = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
            // A wide gap between features: these are the lanes the player kites
            // down, and a map packed edge to edge would have none.
            if (Overlaps(p, r, 5f)) continue;
            if (_stage.Underwater && !UnderwaterTerrain.LevelPatch(new Vector3(p.X, 0f, p.Y), r)) continue;
            _footprints.Add((p, r));
            at = new Vector3(p.X, 0f, p.Y);
            if (_stage.Underwater) at.Y = UnderwaterTerrain.Height(at);
            return true;
        }
        at = Vector3.Zero;
        return false;
    }

    /// <summary>
    /// The nearest point to <paramref name="p"/> (searching outward in rings)
    /// where something of radius <paramref name="r"/> stands on open floor, clear
    /// of terrain and inside the walls. For things other systems place without
    /// knowing this map's layout.
    /// </summary>
    public Vector3 ClearSpotNear(Vector3 p, float r)
    {
        var flat = new Vector2(p.X, p.Z);
        float limit = _stage.InRadius - r - 2f;
        for (float step = 0f; step < 40f; step += 2f)
            for (int i = 0; i < 12; i++)
            {
                float a = Mathf.Tau * i / 12f;
                Vector2 c = flat + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * step;
                if (c.Length() > limit) c = c.Normalized() * limit;
                if (!Overlaps(c, r, 0.5f)) return new Vector3(c.X, p.Y, c.Y);
                if (step == 0f) break;
            }
        return p;
    }

    private bool Overlaps(Vector2 p, float r, float gap)
    {
        foreach ((Vector2 other, float otherR) in _footprints)
            if (p.DistanceTo(other) < r + otherR + gap) return true;
        return false;
    }

    private void AddSolid(Mesh mesh, Transform3D xform, Material material)
    {
        AddChild(new MeshInstance3D { Mesh = mesh, Transform = xform, MaterialOverride = material });
        // The mesh's own convex hull: exact for every primitive used here, and it
        // means the collision cannot drift from the drawn shape when one changes.
        _body.AddChild(new CollisionShape3D { Shape = mesh.CreateConvexShape(), Transform = xform });
    }

}
