using Godot;

namespace Wavebreak.Net;

/// <summary>
/// The flare on a downed teammate: a tall beam of light in a red-tinted
/// version of their colour, rising from their body and visible across the
/// map (unshaded, additive, and fog does not swallow it), plus a ring that
/// pings outward from them on the ground. Only shown while the player is
/// down; it vanishes the moment they are revived. One per
/// <see cref="RemotePlayer"/>; single-player never makes one.
/// </summary>
public sealed partial class DownFlare : Node3D
{
    private const float BeamHeight = 46f;

    private const string BeamShader = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec4 tint : source_color = vec4(1.0, 0.3, 0.3, 1.0);
uniform float pulse = 1.0;
uniform float height = 46.0;
varying float h;
void vertex() { h = VERTEX.y / height + 0.5; }
void fragment() {
    float fade = pow(1.0 - clamp(h, 0.0, 1.0), 1.35);
    float rim = 1.0 - abs(dot(normalize(NORMAL), normalize(VIEW)));
    ALBEDO = tint.rgb * (0.8 + 0.5 * pulse);
    ALPHA = tint.a * fade * (0.35 + 0.65 * rim) * (0.7 + 0.3 * pulse);
}";

    private const string RingShader = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec4 tint : source_color = vec4(1.0, 0.3, 0.3, 1.0);
void fragment() { ALBEDO = tint.rgb; ALPHA = tint.a; }";

    private static Shader? _beamShader, _ringShader;

    private readonly RemotePlayer _owner;
    private readonly Color _tint;
    private ShaderMaterial _beamMat = null!, _pingMat = null!, _ringMat = null!;
    private MeshInstance3D _ping = null!;
    private float _time;

    public DownFlare(RemotePlayer owner, Color slotColor)
    {
        _owner = owner;
        _tint = slotColor.Lerp(new Color(1f, 0.25f, 0.28f), 0.45f);
        Name = "DownFlare";
    }

    public override void _Ready()
    {
        _beamShader ??= new Shader { Code = BeamShader };
        _ringShader ??= new Shader { Code = RingShader };

        _beamMat = new ShaderMaterial { Shader = _beamShader };
        _beamMat.SetShaderParameter("tint", _tint with { A = 0.9f });
        _beamMat.SetShaderParameter("height", BeamHeight);
        AddChild(new MeshInstance3D
        {
            Name = "Beam",
            Mesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.55f, Height = BeamHeight, RadialSegments = 16, Rings = 1, CapTop = false, CapBottom = false },
            MaterialOverride = _beamMat,
            Position = new Vector3(0f, BeamHeight * 0.5f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        _ringMat = new ShaderMaterial { Shader = _ringShader };
        _ringMat.SetShaderParameter("tint", _tint with { A = 0.8f });
        AddChild(new MeshInstance3D
        {
            Name = "Ring",
            Mesh = new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f, Rings = 40, RingSegments = 4 },
            MaterialOverride = _ringMat,
            Position = new Vector3(0f, 0.12f, 0f),
            Scale = new Vector3(1.3f, 0.1f, 1.3f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        _pingMat = new ShaderMaterial { Shader = _ringShader };
        _ping = new MeshInstance3D
        {
            Name = "Ping",
            Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1f, Rings = 48, RingSegments = 4 },
            MaterialOverride = _pingMat,
            Position = new Vector3(0f, 0.1f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ping);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        bool down = IsInstanceValid(_owner) && _owner.Down;
        if (Visible != down) { Visible = down; _time = 0f; }
        if (!down) return;
        _time += (float)delta;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 6f);
        _beamMat.SetShaderParameter("pulse", pulse);
        _ringMat.SetShaderParameter("tint", _tint with { A = 0.45f + 0.45f * pulse });
        // A ping every 1.2 s: grows from the body to 4.5 m and fades.
        float k = _time % 1.2f / 1.2f;
        float r = Mathf.Lerp(0.8f, 4.5f, k);
        _ping.Scale = new Vector3(r, 0.1f, r);
        _pingMat.SetShaderParameter("tint", _tint with { A = 0.85f * (1f - k) });
    }
}
