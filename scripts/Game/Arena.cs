using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// The playfield for one stage: floor, wall, terrain, scatter and lighting, all
/// built from primitives out of a <see cref="StageDef"/>.
///
/// Generated in code rather than authored as a scene because the arena's job is
/// to be legible, not pretty, and because a stage that is pure data can be
/// re-tuned or added without opening an editor. Swapping in a real level later
/// means replacing this one node; nothing else refers to its contents — the
/// director finds the floor by raycast and the player by collision.
///
/// The arena is rebuilt on every phase change, so it must be safe to free and
/// replace mid-run: everything it creates is a child, and the only state it
/// leaves behind is <see cref="Current"/>.
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

    /// <summary>Collision for the wall reaches far above anything drawn. The jump
    /// apex is 6 m with an air jump on top, and a wall the player can hop over
    /// leads to a floor that is not there.</summary>
    private const float WallColliderHeight = 40f;
    private const float WallHeight = 4f;

    private readonly StageDef _stage;
    private readonly RandomNumberGenerator _rng = new();

    // Footprints of everything solid placed so far, so features do not overlap
    // each other or the clear zone. A list scan is fine: there are a few dozen.
    private readonly List<(Vector2 At, float Radius)> _footprints = new();

    private StaticBody3D _body = null!;

    public Arena() : this(Stages.Run[0]) { }

    public Arena(StageDef stage)
    {
        _stage = stage;
        Current = stage;
    }

    public override void _Ready()
    {
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
        BuildTerrain();
        BuildScatter();
        BuildLighting();
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

    private void BuildGround()
    {
        // A fan of triangles rather than a CylinderMesh: the walls are placed by
        // edge angle, and a mesh whose corner angles are an engine convention
        // would leave the floor and the wall disagreeing about where the edge is.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var points = new Vector3[Sides * 2];
        for (int k = 0; k < Sides; k++)
        {
            Vector3 a = Corner(k, _stage.Radius);
            Vector3 b = Corner(k + 1, _stage.Radius);
            st.SetNormal(Vector3.Up); st.AddVertex(Vector3.Zero);
            st.SetNormal(Vector3.Up); st.AddVertex(a);
            st.SetNormal(Vector3.Up); st.AddVertex(b);
            points[k * 2] = a;
            points[k * 2 + 1] = a + Vector3.Down;
        }
        StandardMaterial3D ground = Flat(_stage.Ground);
        // Winding is irrelevant to a floor seen only from above; turning culling
        // off removes the one way this could render as nothing.
        ground.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        AddChild(new MeshInstance3D { Name = "Ground", Mesh = st.Commit(), MaterialOverride = ground });

        _body.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = points } });

        // Concentric outlines. Without something static on the floor there is no
        // sense of speed at all -- the player reads as stationary while the world
        // slides, which makes movement feel weightless.
        StandardMaterial3D grid = Flat(_stage.Grid);
        for (float r = 10f; r < _stage.InRadius - 2f; r += 10f)
            Outline(r, 0.24f, 0.015f, grid);
    }

    /// <summary>A flat polygon ring at in-radius <paramref name="r"/>. A torus
    /// on round floors, where 64 slivers would be 64 nodes for the same look.</summary>
    private void Outline(float r, float width, float y, Material material)
    {
        if (Sides >= 24)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = r - width * 0.5f, OuterRadius = r + width * 0.5f, RingSegments = 64 },
                Position = new Vector3(0f, y, 0f),
                Scale = new Vector3(1f, 0.15f, 1f),
                MaterialOverride = material,
            });
            return;
        }

        float edge = 2f * r * Mathf.Tan(Mathf.Pi / Sides);
        for (int k = 0; k < Sides; k++)
        {
            float a = EdgeAngle(k);
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(edge + width, 0.02f, width) },
                Transform = new Transform3D(XAlong(Tangent(dir)), dir * r + Vector3.Up * y),
                MaterialOverride = material,
            });
        }
    }

    private void BuildWall()
    {
        StandardMaterial3D wall = Flat(_stage.Wall, emission: 0.25f);
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
                Mesh = new BoxMesh { Size = new Vector3(length, WallHeight, 1f) },
                Transform = new Transform3D(basis, centre + Vector3.Up * (WallHeight * 0.5f)),
                MaterialOverride = wall,
            });
            _body.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D { Size = new Vector3(length, WallColliderHeight, 1f) },
                Transform = new Transform3D(basis, centre + Vector3.Up * (WallColliderHeight * 0.5f - 1f)),
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
            Position = new Vector3(0f, 0.02f, 0f),
            Scale = new Vector3(1f, 0.1f, 1f),
            MaterialOverride = Flat(_stage.Wall, emission: 0.6f),
        });
    }

    // -------------------------------------------------------------- terrain

    private void BuildTerrain()
    {
        // Largest features first: rejection sampling fills big holes before
        // small things can fragment the space they need.
        for (int i = 0; i < _stage.Platforms; i++) PlacePlatform();
        for (int i = 0; i < _stage.Ridges; i++) PlaceRidge();
        for (int i = 0; i < _stage.Mounds; i++) PlaceMound();
        for (int i = 0; i < _stage.SolidCount; i++) PlaceSolid();
    }

    /// <summary>
    /// A flat-topped hill. Slopes on every side at about 20°: steep enough that
    /// a slide down one gains real speed, shallow enough that enemies — which
    /// cannot jump — still walk over it rather than piling against it.
    /// </summary>
    private void PlaceMound()
    {
        float h = _stage.MoundHeight * _rng.RandfRange(0.8f, 1.2f);
        float top = _rng.RandfRange(3f, 5f);
        float bottom = top + h / Mathf.Tan(Mathf.DegToRad(20f));
        if (!TryPlace(bottom + 1f, out Vector3 at)) return;

        var mesh = new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = h, RadialSegments = 28, Rings = 1 };
        AddSolid(mesh, new Transform3D(Basis.Identity, at + Vector3.Up * (h * 0.5f)), Flat(_stage.Terrain));

        // A bright lip on the top edge. From a third-person camera at 26°, the
        // shading difference between a slope and the floor is too small to see;
        // the lip is what says "this is raised".
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = top - 0.12f, OuterRadius = top + 0.12f, RingSegments = 32 },
            Position = at + Vector3.Up * (h + 0.01f),
            Scale = new Vector3(1f, 0.3f, 1f),
            MaterialOverride = Flat(_stage.Wall, emission: 0.35f),
        });
    }

    /// <summary>
    /// A raised slab with one ramp. Height is under the 6 m jump apex so the
    /// player can always hop up, while enemies have to find the ramp — which is
    /// what makes the top worth standing on for a few seconds.
    /// </summary>
    private void PlacePlatform()
    {
        float h = _stage.PlatformHeight * _rng.RandfRange(0.9f, 1.1f);
        float size = _rng.RandfRange(7f, 10f);
        float rampLength = h / Mathf.Tan(Mathf.DegToRad(19f));
        float footprint = size * 0.5f + rampLength * 0.6f + 1f;
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
            MaterialOverride = Flat(_stage.Terrain.Lightened(0.25f), emission: 0.08f),
        });

        // PrismMesh with the apex over its +X edge is a wedge rising toward +X.
        // Turned so +X points back at the platform, its high edge meets the top.
        var ramp = new PrismMesh { LeftToRight = 1f, Size = new Vector3(rampLength, h, 3.6f) };
        Vector3 rampCentre = platform + rampSide * (size * 0.5f + rampLength * 0.5f - 0.05f);
        AddSolid(ramp, new Transform3D(XAlong(-rampSide), rampCentre + Vector3.Up * (h * 0.5f)),
                 Flat(_stage.Terrain.Darkened(0.12f)));
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
        float width = _rng.RandfRange(6f, 10f);
        float footprint = Mathf.Max(length, width) * 0.5f + 1f;
        if (!TryPlace(footprint, out Vector3 at)) return;

        var basis = new Basis(Vector3.Up, _rng.RandfRange(0f, Mathf.Tau));
        var mesh = new PrismMesh { LeftToRight = 0.5f, Size = new Vector3(length, h, width) };
        AddSolid(mesh, new Transform3D(basis, at + Vector3.Up * (h * 0.5f)), Flat(_stage.Terrain));

        // A glowing crest line, for the same reason as the mound lip: it is the
        // only cue at a glance that the ground ahead tilts.
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.08f, width) },
            Transform = new Transform3D(basis, at + Vector3.Up * (h + 0.02f)),
            MaterialOverride = Flat(_stage.Wall, emission: 0.35f),
        });
    }

    /// <summary>A large obstacle that blocks bodies, in the stage's style. Kept
    /// under the jump apex so it is never a wall the player cannot get over.</summary>
    private void PlaceSolid()
    {
        float h = _rng.RandfRange(2.6f, 4.8f);
        float w = _rng.RandfRange(1.2f, 2.0f);
        if (!TryPlace(w * 1.6f + 1f, out Vector3 at)) return;

        Mesh mesh = _stage.Style switch
        {
            ObstacleStyle.Pillars => new CylinderMesh { TopRadius = w * 0.8f, BottomRadius = w, Height = h, RadialSegments = 12 },
            ObstacleStyle.Shards => new PrismMesh { Size = new Vector3(w * 2f, h, w * 1.4f), LeftToRight = _rng.RandfRange(0.2f, 0.8f) },
            ObstacleStyle.Stones => new SphereMesh { Radius = w * 1.3f, Height = h, RadialSegments = 12, Rings = 6 },
            _ => new BoxMesh { Size = new Vector3(w * 2f, h, w * 2f) },
        };
        var basis = new Basis(Vector3.Up, _rng.RandfRange(0f, Mathf.Tau));
        AddSolid(mesh, new Transform3D(basis, at + Vector3.Up * (h * 0.5f)), Flat(_stage.ObstacleB, emission: 0.1f));
    }

    /// <summary>
    /// Rejection-sample a spot for a feature of radius <paramref name="r"/>:
    /// inside the walls, outside the clear zone, and not overlapping anything
    /// already placed. Gives up quietly after a bounded number of tries — a map
    /// with one ridge fewer than asked for is fine; a hang at load is not.
    /// </summary>
    private bool TryPlace(float r, out Vector3 at)
    {
        float max = _stage.InRadius - r - 3f;
        float min = _stage.ClearRadius + r;
        for (int attempt = 0; attempt < 40 && max > min; attempt++)
        {
            float a = _rng.RandfRange(0f, Mathf.Tau);
            float d = Mathf.Sqrt(_rng.RandfRange(min * min / (max * max), 1f)) * max;
            var p = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
            if (Overlaps(p, r, 2f)) continue;
            _footprints.Add((p, r));
            at = new Vector3(p.X, 0f, p.Y);
            return true;
        }
        at = Vector3.Zero;
        return false;
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

    // -------------------------------------------------------------- scatter

    private void BuildScatter()
    {
        for (int i = 0; i < _stage.ScatterCount; i++)
        {
            float a = _rng.RandfRange(0f, Mathf.Tau);
            float d = Mathf.Sqrt(_rng.Randf()) * (_stage.InRadius - 6f);
            var flat = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
            // Scatter is decoration with no collision, so poking out of a mound
            // or standing on the clear pad would only be visual noise.
            if (d < 4f || Overlaps(flat, 0.8f, 0f)) continue;

            float h = _rng.RandfRange(0.6f, 2.4f);
            float w = _rng.RandfRange(0.5f, 1.4f);
            Mesh mesh = _stage.Style switch
            {
                ObstacleStyle.Pillars => new CylinderMesh { TopRadius = w * 0.18f, BottomRadius = w * 0.25f, Height = h * 1.6f, RadialSegments = 8 },
                ObstacleStyle.Shards => new PrismMesh { Size = new Vector3(w, h * 1.3f, w * 0.6f), LeftToRight = _rng.Randf() },
                ObstacleStyle.Stones => new SphereMesh { Radius = w * 0.6f, Height = h * 0.6f, RadialSegments = 10, Rings = 5 },
                _ => new BoxMesh { Size = new Vector3(w, h, w) },
            };
            float height = mesh is CylinderMesh c ? c.Height : mesh is PrismMesh p ? p.Size.Y : mesh is SphereMesh s ? s.Height : h;

            // Shards lean; everything else stands up straight.
            Basis basis = new Basis(Vector3.Up, _rng.RandfRange(0f, Mathf.Tau));
            if (_stage.Style == ObstacleStyle.Shards)
                basis *= new Basis(Vector3.Right, _rng.RandfRange(-0.35f, 0.35f));

            AddChild(new MeshInstance3D
            {
                Mesh = mesh,
                Transform = new Transform3D(basis, new Vector3(flat.X, height * 0.5f, flat.Y)),
                MaterialOverride = Flat(_stage.ObstacleA.Lerp(_stage.ObstacleB, _rng.Randf())),
            });
        }
    }

    // ------------------------------------------------------------- lighting

    private void BuildLighting()
    {
        AddChild(new DirectionalLight3D
        {
            Rotation = new Vector3(Mathf.DegToRad(_stage.SunPitchDegrees), Mathf.DegToRad(_stage.SunYawDegrees), 0f),
            LightColor = _stage.SunColor,
            LightEnergy = _stage.SunEnergy,
            ShadowEnabled = true,
        });

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = _stage.SkyTop,
                SkyHorizonColor = _stage.SkyHorizon,
                GroundHorizonColor = _stage.GroundHorizon,
                GroundBottomColor = _stage.GroundBottom,
            } },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = _stage.AmbientEnergy,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
        };
        if (_stage.FogDensity > 0f)
        {
            env.FogEnabled = true;
            env.FogLightColor = _stage.FogColor;
            env.FogDensity = _stage.FogDensity;
            // Mostly spare the sky, or the horizon colour that sets the mood is
            // the first thing the fog erases.
            env.FogSkyAffect = 0.25f;
        }
        AddChild(new WorldEnvironment { Environment = env });
    }

    // -------------------------------------------------------------- helpers

    private static Vector3 Tangent(Vector3 radial) => new(-radial.Z, 0f, radial.X);

    /// <summary>A yaw-only basis whose local +X points along <paramref name="d"/>.
    /// Box and prism sizes here are authored with X as their long axis.</summary>
    private static Basis XAlong(Vector3 d) => new(Vector3.Up, Mathf.Atan2(-d.Z, d.X));

    /// <summary>Unshaded-ish flat material. Emission is used sparingly to make
    /// gameplay-relevant things read against the scenery.</summary>
    public static StandardMaterial3D Flat(Color color, float emission = 0f)
    {
        var m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f, Metallic = 0f };
        if (emission > 0f)
        {
            m.EmissionEnabled = true;
            m.Emission = color;
            m.EmissionEnergyMultiplier = emission;
        }
        return m;
    }

    /// <summary>Clamp a position inside the arena, used by spawning and knockback.</summary>
    public static Vector3 Clamp(Vector3 p, float margin = 2f)
    {
        var flat = new Vector2(p.X, p.Z);
        float max = Radius - margin;
        if (flat.Length() > max) flat = flat.Normalized() * max;
        return new Vector3(flat.X, p.Y, flat.Y);
    }
}
