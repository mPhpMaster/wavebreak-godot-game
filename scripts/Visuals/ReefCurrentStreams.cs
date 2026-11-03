using Godot;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.Visuals;

/// <summary>
/// How a <see cref="ReefCurrentFlow"/> looks: pale sea-foam ribbons and bubbles
/// streaming along the capsule in a slow helix, inside a faint glassy tube with
/// bands sliding the way the water goes. The tube appears first and brightens
/// through the telegraph, so a current is seen coming before it pushes.
///
/// Deliberately unlike any weapon effect: no hard core, no flash, no tint of a
/// damage element; additive and fog-free so it reads from across the basin,
/// faded out by distance by hand. Particle counts follow the graphics preset;
/// with reduce-flashes on, the telegraph glows steadily instead of pulsing.
/// </summary>
public sealed partial class ReefCurrentStreams : Node3D
{
    private const int Views = 8;
    private const int MaxStreaks = 110, MaxBubbles = 48;
    private static readonly Color Foam = new(0.74f, 1f, 0.9f);

    private readonly ReefCurrentField _field;
    private readonly View[] _views = new View[Views];
    private float _time;
    private float _density = 1f;

    /// <summary>Streak instances drawn last frame across all currents (the self-check reads it).</summary>
    public int StreaksDrawn { get; private set; }
    public int MaxStreaksPerCurrent => Mathf.RoundToInt(MaxStreaks * _density);

    private sealed class View
    {
        public MultiMeshInstance3D Streaks = null!, Bubbles = null!;
        public MeshInstance3D Shell = null!;
        public ShaderMaterial ShellMaterial = null!;
    }

    public ReefCurrentStreams(ReefCurrentField field) => _field = field;

    public override void _Ready()
    {
        Name = "ReefCurrentStreams";
        _density = Density();
        GraphicsSettings.Changed += OnGraphicsChanged;
        Mesh ribbon = Ribbon();
        var bubble = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 6, Rings = 3 };
        var streakMaterial = new ShaderMaterial { Shader = new Shader { Code = StreakShader } };
        streakMaterial.SetShaderParameter("tint", Foam);
        var bubbleMaterial = new ShaderMaterial { Shader = new Shader { Code = BubbleShader } };
        var shell = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 20, Rings = 1, CapTop = false, CapBottom = false };
        for (int i = 0; i < Views; i++)
        {
            var view = new View
            {
                Streaks = new MultiMeshInstance3D
                {
                    Name = $"CurrentStreaks{i}", MaterialOverride = streakMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = ribbon, InstanceCount = MaxStreaks * 13 / 10, VisibleInstanceCount = 0 },
                },
                Bubbles = new MultiMeshInstance3D
                {
                    Name = $"CurrentBubbles{i}", MaterialOverride = bubbleMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = bubble, InstanceCount = MaxBubbles * 13 / 10, VisibleInstanceCount = 0 },
                },
                ShellMaterial = new ShaderMaterial { Shader = new Shader { Code = ShellShader } },
            };
            view.ShellMaterial.SetShaderParameter("tint", Foam);
            view.Shell = new MeshInstance3D { Name = $"CurrentShell{i}", Mesh = shell, MaterialOverride = view.ShellMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            // Instances move every frame; a huge box keeps culling from guessing wrong.
            view.Streaks.CustomAabb = view.Bubbles.CustomAabb = new Aabb(new Vector3(-400, -200, -400), new Vector3(800, 400, 800));
            AddChild(view.Streaks); AddChild(view.Bubbles); AddChild(view.Shell);
            _views[i] = view;
        }
    }

    public override void _ExitTree() => GraphicsSettings.Changed -= OnGraphicsChanged;
    private void OnGraphicsChanged() => _density = Density();

    /// <summary>Share of the full particle count for the graphics preset.</summary>
    public static float Density() => GraphicsSettings.Current.Preset switch
    {
        GraphicsSettings.Low => 0.35f,
        GraphicsSettings.Medium => 0.7f,
        GraphicsSettings.Ultra => 1.3f,
        GraphicsSettings.Custom => Mathf.Clamp(GraphicsSettings.Current.PropDensity, 0.35f, 1.3f),
        _ => 1f,
    };

    public override void _Process(double delta)
    {
        using var timing = Prof.Scope("Reef.currentFx");
        _time += (float)delta;
        float clock = _field.Clock;
        StreaksDrawn = 0;
        int used = 0;
        foreach (ReefCurrentFlow flow in _field.Live)
        {
            if (used >= Views) break;
            Draw(_views[used++], flow, clock);
        }
        for (int i = used; i < Views; i++)
        {
            _views[i].Streaks.Multimesh.VisibleInstanceCount = 0;
            _views[i].Bubbles.Multimesh.VisibleInstanceCount = 0;
            _views[i].Shell.Visible = false;
        }
    }

    private void Draw(View view, ReefCurrentFlow flow, float clock)
    {
        float visible = flow.Visibility(clock);
        float push = flow.Push(clock);
        float length = flow.Length;
        Vector3 axis = flow.Direction;
        Vector3 side = Mathf.Abs(axis.Y) > 0.9f ? Vector3.Right : axis.Cross(Vector3.Up).Normalized();
        Vector3 lift = side.Cross(axis).Normalized();
        // The water seems to run faster than it pushes: easier to read the flow.
        float speed = flow.Strength * (0.35f + 1.25f * Mathf.Max(push, 0.25f * flow.Telegraph(clock)));
        float travel = _time * speed;

        int streaks = Mathf.Clamp(Mathf.RoundToInt(length * 2.6f * _density * Mathf.Max(0.25f, visible)), 4, view.Streaks.Multimesh.InstanceCount);
        MultiMesh mm = view.Streaks.Multimesh;
        float streakLength = (1.4f + flow.Strength * 0.3f) * (0.5f + 0.5f * Mathf.Max(push, 0.3f));
        for (int i = 0; i < streaks; i++)
        {
            float h1 = Hash(i * 3 + 1), h2 = Hash(i * 3 + 2), h3 = Hash(i * 3 + 3);
            float s = Mathf.PosMod(h1 + travel * (0.8f + 0.4f * h3) / length, 1f);
            float angle = h2 * Mathf.Tau + _time * 0.5f + s * 2.2f;
            float ring = Mathf.Sqrt(h3) * flow.Radius * 0.9f;
            Vector3 at = flow.From + axis * (s * length) + (side * Mathf.Cos(angle) + lift * Mathf.Sin(angle)) * ring;
            float ends = Mathf.SmoothStep(0f, 0.12f, s) * Mathf.SmoothStep(1f, 0.86f, s);
            float width = 0.1f + 0.08f * h2;
            var basis = new Basis(side * width, lift * width, axis * streakLength);
            mm.SetInstanceTransform(i, new Transform3D(basis, at));
            mm.SetInstanceColor(i, new Color(1f, 1f, 1f, visible * ends * (0.55f + 0.45f * h1)));
        }
        mm.VisibleInstanceCount = streaks;
        StreaksDrawn += streaks;

        int bubbles = Mathf.Clamp(Mathf.RoundToInt(length * 1.1f * _density * visible), 0, view.Bubbles.Multimesh.InstanceCount);
        MultiMesh bm = view.Bubbles.Multimesh;
        for (int i = 0; i < bubbles; i++)
        {
            float h1 = Hash(i * 5 + 101), h2 = Hash(i * 5 + 102), h3 = Hash(i * 5 + 103);
            float s = Mathf.PosMod(h1 + travel * (0.6f + 0.3f * h2) / length, 1f);
            float angle = h2 * Mathf.Tau + Mathf.Sin(_time * 1.7f + i) * 0.4f;
            float ring = Mathf.Sqrt(h3) * flow.Radius * 0.8f;
            Vector3 at = flow.From + axis * (s * length) + (side * Mathf.Cos(angle) + lift * Mathf.Sin(angle)) * ring
                + Vector3.Up * Mathf.Sin(_time * 2.3f + i * 1.3f) * 0.25f;
            float size = 0.14f + 0.18f * h3;
            float ends = Mathf.SmoothStep(0f, 0.1f, s) * Mathf.SmoothStep(1f, 0.9f, s);
            bm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * size), at));
            bm.SetInstanceColor(i, new Color(1f, 1f, 1f, visible * ends));
        }
        bm.VisibleInstanceCount = bubbles;

        // The tube: the telegraph's warning, then a faint wall while it runs.
        float telegraph = flow.Telegraph(clock);
        float glow = telegraph < 1f
            ? (Accessibility.ReduceFlashes ? 0.5f + 0.4f * telegraph : 0.45f + 0.35f * telegraph + 0.25f * Mathf.Sin(_time * 7f) * (1f - telegraph))
            : 0.45f * push + 0.12f;
        view.Shell.Visible = glow > 0.01f;
        view.Shell.GlobalTransform = new Transform3D(new Basis(side * flow.Radius, axis * length, lift * flow.Radius), flow.Centre);
        view.ShellMaterial.SetShaderParameter("glow", glow);
        view.ShellMaterial.SetShaderParameter("flow_length", length);
        view.ShellMaterial.SetShaderParameter("phase", travel);
    }

    private static float Hash(int i)
    {
        uint x = (uint)i * 747796405u + 2891336453u;
        x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
        return ((x >> 22) ^ x) / 4294967296f;
    }

    /// <summary>Two crossed quads along +Z (-0.5..0.5): a streak that reads from any side.</summary>
    private static ArrayMesh Ribbon()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (Vector3 across in new[] { Vector3.Right, Vector3.Up })
        {
            Vector3 a = -across * 0.5f + Vector3.Back * -0.5f, b = across * 0.5f + Vector3.Back * -0.5f;
            Vector3 c = across * 0.5f + Vector3.Back * 0.5f, d = -across * 0.5f + Vector3.Back * 0.5f;
            st.SetUV(new Vector2(0, 0)); st.AddVertex(a);
            st.SetUV(new Vector2(1, 0)); st.AddVertex(b);
            st.SetUV(new Vector2(1, 1)); st.AddVertex(c);
            st.SetUV(new Vector2(0, 0)); st.AddVertex(a);
            st.SetUV(new Vector2(1, 1)); st.AddVertex(c);
            st.SetUV(new Vector2(0, 1)); st.AddVertex(d);
        }
        return st.Commit();
    }

    private const string StreakShader = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
uniform vec4 tint : source_color;
varying float fade;
void vertex() {
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    fade = clamp(1.0 - (distance(world, CAMERA_POSITION_WORLD) - 70.0) / 80.0, 0.0, 1.0);
}
void fragment() {
    float across = 1.0 - abs(UV.x * 2.0 - 1.0);
    float head = smoothstep(0.0, 0.25, UV.y) * smoothstep(1.0, 0.7, UV.y);
    ALBEDO = tint.rgb;
    ALPHA = COLOR.a * across * across * head * fade * 0.9;
}";

    private const string BubbleShader = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, fog_disabled, shadows_disabled;
varying float fade;
void vertex() {
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    fade = clamp(1.0 - (distance(world, CAMERA_POSITION_WORLD) - 50.0) / 60.0, 0.0, 1.0);
}
void fragment() {
    float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 1.6);
    ALBEDO = vec3(0.85, 1.0, 0.97);
    ALPHA = COLOR.a * (0.15 + 0.6 * rim) * fade;
}";

    private const string ShellShader = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
uniform vec4 tint : source_color;
uniform float glow = 0.0;
uniform float flow_length = 20.0;
uniform float phase = 0.0;
varying float fade;
varying float along01;
void vertex() {
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    fade = clamp(1.0 - (distance(world, CAMERA_POSITION_WORLD) - 90.0) / 90.0, 0.0, 1.0);
    along01 = VERTEX.y + 0.5;   // the unit cylinder runs -0.5..0.5 along the flow
}
void fragment() {
    float rim = pow(1.0 - abs(dot(NORMAL, VIEW)), 2.0);
    float along = along01 * flow_length;
    float bands = smoothstep(0.55, 1.0, sin((along - phase) * 0.7));
    float ends = smoothstep(0.0, 0.2, along01) * smoothstep(1.0, 0.8, along01);
    ALBEDO = tint.rgb;
    ALPHA = glow * ends * fade * (0.03 + 0.3 * rim * rim + 0.22 * bands * rim);
}";
}
