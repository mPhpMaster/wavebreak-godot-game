using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The title backdrop: a little island at sunset. A rolling shader sea, a sand
/// island with palms, rocks and a lighthouse, gulls wheeling overhead, a few of
/// the game's own monsters bobbing in the shallows, and the selected hero on
/// the shore. Built from primitives and the enemy rigs, so it needs no assets
/// and always matches whatever the menu is showing.
/// </summary>
public sealed partial class MainMenu
{
    private const string SeaShader = @"
shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;
uniform vec3 deep : source_color = vec3(0.03, 0.22, 0.42);
uniform vec3 shallow : source_color = vec3(0.16, 0.66, 0.74);
uniform vec3 foam : source_color = vec3(1.0, 0.92, 0.88);
uniform vec3 glint : source_color = vec3(1.0, 0.62, 0.42);
varying float crest;
varying float dist;

float wave(vec2 p, vec2 dir, float freq, float speed, float amp) {
    return sin(dot(p, normalize(dir)) * freq + TIME * speed) * amp;
}

void vertex() {
    vec2 p = VERTEX.xz;
    float h = wave(p, vec2(1.0, 0.3), 0.18, 1.1, 0.45)
            + wave(p, vec2(-0.4, 1.0), 0.31, 1.7, 0.22)
            + wave(p, vec2(0.7, -0.8), 0.57, 2.3, 0.10);
    VERTEX.y += h;
    crest = h;
    dist = length(p);
    // Normal from the slope of the same sum.
    float e = 0.5;
    float hx = wave(p + vec2(e, 0.0), vec2(1.0, 0.3), 0.18, 1.1, 0.45)
             + wave(p + vec2(e, 0.0), vec2(-0.4, 1.0), 0.31, 1.7, 0.22);
    float hz = wave(p + vec2(0.0, e), vec2(1.0, 0.3), 0.18, 1.1, 0.45)
             + wave(p + vec2(0.0, e), vec2(-0.4, 1.0), 0.31, 1.7, 0.22);
    NORMAL = normalize(vec3(h - hx, e, h - hz));
}

void fragment() {
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    // Shallower and brighter close to the island.
    float near = 1.0 - smoothstep(14.0, 60.0, dist);
    vec3 col = mix(deep, shallow, clamp(near * 0.8 + crest * 0.6, 0.0, 1.0));
    col = mix(col, glint, fres * 0.18);
    float f = smoothstep(0.42, 0.62, crest) + (1.0 - smoothstep(15.5, 17.5, dist)) * (0.6 + 0.4 * sin(TIME * 2.0 + dist * 3.0));
    col = mix(col, foam, clamp(f, 0.0, 1.0) * 0.85);
    ALBEDO = col;
    ROUGHNESS = 0.18;
    METALLIC = 0.0;
    SPECULAR = 0.35;
}";

    private readonly List<(Node3D node, Vector3 home, float phase)> _bobbers = new();
    private readonly List<(Node3D node, float radius, float height, float speed, float phase)> _gulls = new();
    private OmniLight3D? _beacon;

    // ============================================================ backdrop

    private void BuildBackdrop()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color(0.10f, 0.16f, 0.42f),
                    SkyHorizonColor = new Color(1.00f, 0.62f, 0.40f),
                    SkyCurve = 0.25f,
                    GroundHorizonColor = new Color(0.95f, 0.55f, 0.50f),
                    GroundBottomColor = new Color(0.10f, 0.12f, 0.25f),
                    SunAngleMax = 40f,
                    SunCurve = 0.08f,
                } },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.55f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                GlowEnabled = true,
                GlowIntensity = 0.9f,
                GlowBloom = 0.08f,
                FogEnabled = true,
                FogLightColor = new Color(1.00f, 0.66f, 0.58f),
                FogDensity = 0.0012f,
                FogSkyAffect = 0.2f,
            },
        });
        // A low, warm sun just above the horizon, behind the island.
        AddChild(new DirectionalLight3D
        {
            Rotation = new Vector3(Mathf.DegToRad(-14f), Mathf.DegToRad(200f), 0f),
            LightColor = new Color(1.00f, 0.74f, 0.56f),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
        });
        AddChild(new DirectionalLight3D
        {
            Rotation = new Vector3(Mathf.DegToRad(-55f), Mathf.DegToRad(30f), 0f),
            LightColor = new Color(0.62f, 0.70f, 1.00f),
            LightEnergy = 0.35f,
        });

        // ---- the sea
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(420f, 420f), SubdivideWidth = 180, SubdivideDepth = 180 },
            Position = new Vector3(0f, -0.2f, 0f),
            MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = SeaShader } },
        });

        // ---- the island: a sand mound with a wet rim
        var sand = Game.Arena.Flat(new Color(0.98f, 0.84f, 0.62f));
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 14f, BottomRadius = 17.5f, Height = 1.6f, RadialSegments = 48 },
            Position = new Vector3(0f, -0.15f, 0f),
            MaterialOverride = Game.Arena.Flat(new Color(0.86f, 0.70f, 0.52f)),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 14f, Height = 5f, RadialSegments = 48, Rings = 16 },
            Position = new Vector3(0f, -0.9f, 0f),
            MaterialOverride = sand,
        });

        var rng = new RandomNumberGenerator { Seed = 2026 };
        foreach (Vector3 at in new[] { new Vector3(-6f, 0.9f, -5f), new Vector3(5.5f, 0.9f, -6.5f), new Vector3(-9f, 0.5f, 2f),
                                       new Vector3(8.5f, 0.6f, 3f), new Vector3(1.5f, 1.2f, -9f) })
            AddChild(Palm(at, rng));

        var rock = Game.Arena.Flat(new Color(0.52f, 0.50f, 0.62f));
        for (int i = 0; i < 12; i++)
        {
            float a = rng.RandfRange(0f, Mathf.Tau);
            float d = rng.RandfRange(11f, 17f);
            float s = rng.RandfRange(0.5f, 1.4f);
            AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = s, Height = s * 1.4f, RadialSegments = 7, Rings = 4 },
                Position = new Vector3(Mathf.Cos(a) * d, 0.1f, Mathf.Sin(a) * d),
                Rotation = new Vector3(rng.Randf(), rng.Randf() * Mathf.Tau, rng.Randf()),
                MaterialOverride = rock,
            });
        }

        // Shells and starfish dotted over the sand.
        Color[] shellTints = { new(1f, 0.62f, 0.68f), new(1f, 0.86f, 0.55f), new(0.98f, 0.95f, 0.92f) };
        for (int i = 0; i < 14; i++)
        {
            float a = rng.RandfRange(0f, Mathf.Tau);
            float d = rng.RandfRange(3f, 12f);
            AddChild(new MeshInstance3D
            {
                Mesh = i % 3 == 0 ? new PrismMesh { Size = new Vector3(0.45f, 0.1f, 0.45f) } : new SphereMesh { Radius = 0.18f, Height = 0.16f },
                Position = new Vector3(Mathf.Cos(a) * d, 1.15f - d * 0.06f, Mathf.Sin(a) * d),
                Rotation = new Vector3(0f, rng.Randf() * Mathf.Tau, 0f),
                MaterialOverride = Game.Arena.Flat(shellTints[i % shellTints.Length], emission: 0.15f),
            });
        }

        AddChild(Lighthouse(new Vector3(34f * (Loc.IsRtl ? -1f : 1f), 0f, -52f)));

        // A couple of far islets on the horizon.
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 9f, Height = 6f, RadialSegments = 24, Rings = 8 },
            Position = new Vector3(60f, -1.5f, -90f),
            MaterialOverride = Game.Arena.Flat(new Color(0.42f, 0.36f, 0.58f)),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 6f, Height = 4f, RadialSegments = 24, Rings = 8 },
            Position = new Vector3(78f, -1.2f, -70f),
            MaterialOverride = Game.Arena.Flat(new Color(0.48f, 0.40f, 0.62f)),
        });

        // ---- the hero, held in frame by UpdateCamera
        _hero = new Node3D { Name = "Hero" };
        AddChild(_hero);
        _hero.AddChild(new OmniLight3D { Position = new Vector3(0.8f, 2.4f, -2.2f), LightEnergy = 1.6f, OmniRange = 6f });
        TintHero(Characters.ById(Profile.SelectedCharacter) ?? Characters.Default);

        // ---- monsters wading in from the sea
        string[] wading = { "mote", "shoal_fry", "bloat_polyp", "skitterling", "brine_leech", "husk", "spume_mite" };
        for (int i = 0; i < wading.Length; i++)
        {
            if (Catalog.EnemyById(wading[i]) is not { } def) continue;
            Node3D body;
            try { body = Visuals.EnemyVisuals.Build(def); }
            catch { continue; }
            // Spread over the near-side shallows the camera looks across.
            float a = Mathf.DegToRad(10f + i * 22f) * (Loc.IsRtl ? -1f : 1f) + (Loc.IsRtl ? Mathf.Pi : 0f);
            float d = 17.5f + (i % 3) * 2.5f;
            var home = new Vector3(Mathf.Cos(a) * d, -0.1f, Mathf.Sin(a) * d);
            var holder = new Node3D { Position = home };
            holder.AddChild(body);
            // Face the island.
            holder.Rotation = new Vector3(0f, Mathf.Atan2(-home.X, -home.Z), 0f);
            AddChild(holder);
            _bobbers.Add((holder, home, rng.Randf() * Mathf.Tau));
        }

        // ---- gulls
        var white = Game.Arena.Flat(new Color(0.98f, 0.97f, 0.95f), emission: 0.2f);
        for (int i = 0; i < 6; i++)
        {
            var gull = new Node3D();
            foreach (float side in new[] { -1f, 1f })
                gull.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.05f, 0.25f) },
                    Position = new Vector3(0.4f * side, 0.12f, 0f),
                    Rotation = new Vector3(0f, 0f, 0.35f * side),
                    MaterialOverride = white,
                });
            AddChild(gull);
            _gulls.Add((gull, rng.RandfRange(10f, 26f), rng.RandfRange(11f, 18f), rng.RandfRange(0.25f, 0.45f) * (i % 2 == 0 ? 1f : -1f), rng.Randf() * Mathf.Tau));
        }

        _camera = new Camera3D { Fov = 55f, Current = true };
        AddChild(_camera);
        UpdateCamera(0f);
    }

    private static Node3D Palm(Vector3 at, RandomNumberGenerator rng)
    {
        var palm = new Node3D { Position = at, Rotation = new Vector3(0f, rng.Randf() * Mathf.Tau, 0f) };
        var bark = Game.Arena.Flat(new Color(0.62f, 0.42f, 0.30f));
        var leaf = Game.Arena.Flat(new Color(0.30f, 0.78f, 0.55f), emission: 0.08f);
        // A gently curving trunk of stacked rings.
        Vector3 top = Vector3.Zero;
        float lean = rng.RandfRange(0.10f, 0.22f);
        for (int i = 0; i < 7; i++)
        {
            top = new Vector3(lean * i * i * 0.12f, i * 0.75f, 0f);
            palm.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.30f, Height = 0.8f, RadialSegments = 8 },
                Position = top + new Vector3(0f, 0.4f, 0f),
                Rotation = new Vector3(0f, 0f, -lean * i * 0.12f),
                MaterialOverride = bark,
            });
        }
        top += new Vector3(0f, 0.9f, 0f);
        for (int i = 0; i < 7; i++)
        {
            var frond = new Node3D { Position = top, Rotation = new Vector3(0f, i * Mathf.Tau / 7f, 0f) };
            frond.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.06f, 2.6f) },
                Position = new Vector3(0f, -0.35f, 1.2f),
                Rotation = new Vector3(0.45f, 0f, 0f),
                MaterialOverride = leaf,
            });
            palm.AddChild(frond);
        }
        // Coconuts.
        var nut = Game.Arena.Flat(new Color(0.45f, 0.30f, 0.20f));
        for (int i = 0; i < 3; i++)
            palm.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.2f, Height = 0.4f },
                Position = top + new Vector3(Mathf.Cos(i * 2.1f) * 0.3f, -0.35f, Mathf.Sin(i * 2.1f) * 0.3f),
                MaterialOverride = nut,
            });
        return palm;
    }

    private Node3D Lighthouse(Vector3 at)
    {
        var tower = new Node3D { Position = at };
        var white = Game.Arena.Flat(new Color(0.96f, 0.94f, 0.92f));
        var red = Game.Arena.Flat(new Color(0.92f, 0.36f, 0.40f));
        tower.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 7f, Height = 5f, RadialSegments = 24, Rings = 8 },
            Position = new Vector3(0f, -1.2f, 0f),
            MaterialOverride = Game.Arena.Flat(new Color(0.50f, 0.46f, 0.60f)),
        });
        for (int i = 0; i < 5; i++)
            tower.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 1.6f - i * 0.18f, BottomRadius = 1.8f - i * 0.18f, Height = 2.2f, RadialSegments = 16 },
                Position = new Vector3(0f, 1.1f + i * 2.2f, 0f),
                MaterialOverride = i % 2 == 0 ? white : red,
            });
        tower.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.9f, Height = 1.2f, RadialSegments = 12 },
            Position = new Vector3(0f, 12.2f, 0f),
            MaterialOverride = Game.Arena.Flat(new Color(1.0f, 0.92f, 0.60f), emission: 2.5f),
        });
        tower.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.1f, BottomRadius = 1.2f, Height = 1.1f, RadialSegments = 12 },
            Position = new Vector3(0f, 13.35f, 0f),
            MaterialOverride = red,
        });
        _beacon = new OmniLight3D { Position = new Vector3(0f, 12.2f, 0f), LightColor = new Color(1f, 0.85f, 0.55f), LightEnergy = 3f, OmniRange = 18f };
        tower.AddChild(_beacon);
        return tower;
    }

    public override void _Process(double delta)
    {
        if (_camera is null) return;
        float dt = (float)delta;
        UpdateCamera(dt);
        _lineup?.Tick(delta, _camera, _page == "coop", _hero);
        float t = _orbit * 16f;
        foreach (var (node, home, phase) in _bobbers)
            node.Position = home + new Vector3(0f, Mathf.Sin(t * 1.7f + phase) * 0.18f, 0f);
        foreach (var (node, radius, height, speed, phase) in _gulls)
        {
            float a = phase + t * speed * 0.25f;
            node.Position = new Vector3(Mathf.Cos(a) * radius, height + Mathf.Sin(t * 0.8f + phase) * 0.8f, Mathf.Sin(a) * radius - 8f);
            node.Rotation = new Vector3(0f, -a + (speed > 0f ? 0f : Mathf.Pi), Mathf.Sin(t * 5f + phase) * 0.25f);
        }
        if (_beacon is not null) _beacon.LightEnergy = 2.2f + Mathf.Sin(t * 1.3f) * 1.2f;
    }

    /// <summary>A slow sway along the beach rather than an orbit: the sea and
    /// sunset stay on screen behind the menu. The hero is pinned in frame.</summary>
    private void UpdateCamera(float dt)
    {
        _orbit += dt * 0.06f;
        float side = Loc.IsRtl ? -1f : 1f;
        float sway = Mathf.Sin(_orbit * 1.3f) * 0.12f;
        // High over the near beach, looking out across the island to the sea
        // and the sunset; the island sits right of the menu column.
        var pos = new Vector3(-6f * side + Mathf.Sin(sway) * 6f, 11f + Mathf.Sin(_orbit * 0.9f) * 0.5f, 27f);
        _camera.Position = pos;
        _camera.LookAt(new Vector3(7f * side, 0.5f, -6f), Vector3.Up);

        if (_hero is not null)
        {
            // On the sand, in the middle of the open strip right of the panels
            // (990 px wide UI), whatever the window size: cast a ray through
            // that screen point and stand where it meets the beach.
            Vector2 view = GetViewport().GetVisibleRect().Size;
            float sx = 990f + (view.X - 990f) * 0.5f;
            if (side < 0f) sx = view.X - sx;
            var screen = new Vector2(sx, view.Y * 0.74f);
            Vector3 from = _camera.ProjectRayOrigin(screen), dir = _camera.ProjectRayNormal(screen);
            float tHit = Mathf.Abs(dir.Y) > 1e-4f ? (1.0f - from.Y) / dir.Y : 15f;
            _hero.Position = from + dir * Mathf.Clamp(tHit, 6f, 40f);
            _hero.Scale = Vector3.One * 1.9f;
            Vector3 toCam = (pos - _hero.Position) with { Y = 0f };
            if (toCam.LengthSquared() > 0.01f)
                _hero.Rotation = new Vector3(0f, Mathf.Atan2(toCam.X, toCam.Z) + Mathf.Pi + 0.25f * side, 0f);
        }
    }
}
