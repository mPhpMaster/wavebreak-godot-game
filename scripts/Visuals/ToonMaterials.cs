using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// The handful of materials every procedural model and prop shares.
///
/// One character material for the whole cast is the performance decision the
/// rest of the visuals are built around: colour is in the vertices (see
/// <see cref="MeshKit"/>), and the things that differ per instance — hit flash,
/// death dissolve, a glow tint — are <c>instance uniform</c>s, set with
/// <see cref="GeometryInstance3D.SetInstanceShaderParameter"/>. Those live in a
/// per-instance buffer, so two hundred enemies flashing independently still
/// share one material and never force the renderer to re-bind state between them.
///
/// The look: two soft light bands with cool-tinted shadows (the shadow side is
/// tinted, not just darkened, which is most of what reads as "stylised"), a rim
/// light so silhouettes separate from the floor at the gameplay camera's
/// distance, and a thin inverted-hull outline in a dark shade of the surface
/// colour.
/// </summary>
public static class ToonMaterials
{
    // Shared by the character, outline and prop shaders, so a hit, a dissolve
    // and the lighting bands behave identically on everything.
    private const string Common = @"
instance uniform float flash = 0.0;
instance uniform float dissolve = 0.0;
instance uniform vec4 glow : source_color = vec4(0.0, 0.0, 0.0, 0.0);
varying vec3 v_obj;
varying float v_emit;

float wb_hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}
float wb_noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(wb_hash(i + vec3(0,0,0)), wb_hash(i + vec3(1,0,0)), f.x),
	               mix(wb_hash(i + vec3(0,1,0)), wb_hash(i + vec3(1,1,0)), f.x), f.y),
	           mix(mix(wb_hash(i + vec3(0,0,1)), wb_hash(i + vec3(1,0,1)), f.x),
	               mix(wb_hash(i + vec3(0,1,1)), wb_hash(i + vec3(1,1,1)), f.x), f.y), f.z);
}
";

    private const string ToonLight = @"
uniform vec3 shadow_tint : source_color = vec3(0.42, 0.40, 0.62);
void light() {
	float ndl = dot(NORMAL, LIGHT);
	// Two soft steps: lit, a narrow half-tone, and a tinted shadow side.
	float lit = smoothstep(0.02, 0.12, ndl) * 0.75 + smoothstep(0.45, 0.6, ndl) * 0.25;
	float shade = smoothstep(0.2, 0.6, ATTENUATION);
	vec3 band = mix(shadow_tint * 0.32, vec3(1.0), lit * shade);
	DIFFUSE_LIGHT += band * LIGHT_COLOR / PI * mix(1.0, ATTENUATION, 0.35);
}
";

    private const string CharacterCode = "shader_type spatial;\nrender_mode cull_back, specular_disabled;\n" + Common + ToonLight + @"
uniform float rim_strength = 0.55;
uniform float sway_amount = 0.0;
void vertex() {
	v_obj = VERTEX;
	v_emit = UV.x;
	if (sway_amount > 0.0) {
		vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
		float ph = TIME * 1.6 + origin.x * 0.21 + origin.z * 0.17;
		VERTEX.x += sin(ph) * sway_amount * UV.y * VERTEX.y * 0.25;
		VERTEX.z += cos(ph * 0.8) * sway_amount * UV.y * VERTEX.y * 0.18;
	}
}
void fragment() {
	vec3 base = COLOR.rgb;
	float edge = 0.0;
	if (dissolve > 0.0) {
		float n = wb_noise(v_obj * 7.0) * 0.7 + wb_noise(v_obj * 19.0) * 0.3;
		if (n < dissolve) discard;
		edge = 1.0 - smoothstep(0.0, 0.08, n - dissolve);
	}
	ALBEDO = mix(base, vec3(1.0), flash * 0.75);
	ROUGHNESS = 1.0;
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	EMISSION = base * v_emit * 2.2
	         + vec3(1.0, 0.95, 0.9) * flash * 1.4
	         + mix(base, vec3(1.0), 0.6) * rim * rim_strength
	         + glow.rgb * glow.a * (0.35 + rim)
	         + vec3(1.0, 0.55, 0.25) * edge * 4.0;
}
";

    private const string OutlineCode = "shader_type spatial;\nrender_mode unshaded, cull_front, specular_disabled;\n" + Common + @"
uniform float width = 0.022;
uniform float sway_amount = 0.0;
void vertex() {
	v_obj = VERTEX;
	if (sway_amount > 0.0) {
		vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
		float ph = TIME * 1.6 + origin.x * 0.21 + origin.z * 0.17;
		VERTEX.x += sin(ph) * sway_amount * UV.y * VERTEX.y * 0.25;
		VERTEX.z += cos(ph * 0.8) * sway_amount * UV.y * VERTEX.y * 0.18;
	}
	VERTEX += NORMAL * width;
}
void fragment() {
	if (dissolve > 0.0 && wb_noise(v_obj * 7.0) * 0.7 + wb_noise(v_obj * 19.0) * 0.3 < dissolve + 0.05) discard;
	ALBEDO = COLOR.rgb * 0.16 + vec3(0.02, 0.015, 0.04) + vec3(flash * 0.4);
}
";

    /// <summary>Additive ground glow under elites and bosses. Spun and pulsed by
    /// TIME in the shader, so it costs the CPU nothing per frame.</summary>
    private const string AuraCode = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
instance uniform vec4 glow : source_color = vec4(1.0, 0.6, 0.2, 1.0);
void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float a = atan(p.y, p.x);
	float ring = smoothstep(0.62, 0.8, r) * (1.0 - smoothstep(0.86, 1.0, r));
	float spokes = pow(0.5 + 0.5 * sin(a * 7.0 + TIME * 2.2), 6.0) * smoothstep(0.3, 0.75, r) * (1.0 - smoothstep(0.8, 0.95, r));
	float fill = (1.0 - smoothstep(0.0, 0.9, r)) * 0.18;
	float pulse = 0.8 + 0.2 * sin(TIME * 3.0);
	ALBEDO = glow.rgb * (ring + spokes * 0.6 + fill) * pulse * 1.6;
	ALPHA = 1.0;
}
";

    /// <summary>Grass and flowers: double-sided, swaying, coloured per MultiMesh
    /// instance through the custom-data channel (vertex colour carries only the
    /// root-to-tip shading, so one white mesh serves every theme).</summary>
    private const string FoliageCode = "shader_type spatial;\nrender_mode cull_disabled, specular_disabled;\n" + Common + ToonLight + @"
varying vec3 v_tint;
void vertex() {
	v_tint = INSTANCE_CUSTOM.rgb;
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float ph = TIME * 2.1 + origin.x * 0.35 + origin.z * 0.27;
	VERTEX.x += sin(ph) * 0.06 * UV.y;
	VERTEX.z += cos(ph * 0.7) * 0.05 * UV.y;
	v_emit = UV.x;
	// Grass blades lit like the ground they grow from, not like thin cards:
	// an up-facing normal keeps a field from flickering between bands.
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), 0.75));
}
void fragment() {
	vec3 c = COLOR.rgb * v_tint;
	ALBEDO = c;
	ROUGHNESS = 1.0;
	EMISSION = c * v_emit * 1.5;
}
";

    /// <summary>World-space ground: slow two-colour noise, worn patches, speckle
    /// and optional slab joints or glowing cracks, under the same light bands.</summary>
    private const string FloorCode = "shader_type spatial;\nrender_mode specular_disabled;\n" + Common + ToonLight + @"
uniform vec3 color_a : source_color;
uniform vec3 color_b : source_color;
uniform vec3 color_patch : source_color;
uniform vec3 color_line : source_color;
uniform float patch_amount = 0.6;
uniform float line_strength = 0.0;
uniform float slab_size = 4.0;
uniform float line_glow = 0.0;
varying vec3 v_world;
void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
float fbm(vec2 p) {
	float v = 0.0, a = 0.5;
	for (int i = 0; i < 4; i++) { v += a * wb_noise(vec3(p, 0.37 * float(i))); p *= 2.03; a *= 0.5; }
	return v;
}
void fragment() {
	vec2 p = v_world.xz;
	float big = fbm(p * 0.045);
	float mid = fbm(p * 0.21 + 13.0);
	float fine = wb_noise(vec3(p * 3.1, 0.0));
	vec3 col = mix(color_a, color_b, smoothstep(0.3, 0.7, big));
	col = mix(col, color_patch, smoothstep(0.58, 0.7, mid) * patch_amount);
	col *= 0.93 + 0.14 * fine;
	float line = 0.0;
	if (line_strength > 0.0) {
		// Jittered slabs: offset every other row, warp the joints a little.
		vec2 q = p / slab_size;
		q.x += step(1.0, mod(floor(q.y), 2.0)) * 0.5;
		q += (vec2(fbm(p * 0.18), fbm(p * 0.18 + 7.0)) - 0.5) * 0.7;
		vec2 f = abs(fract(q) - 0.5);
		float joint = 1.0 - smoothstep(0.465, 0.492, max(f.x, f.y));
		line = (1.0 - joint) * line_strength * smoothstep(0.25, 0.6, mid + 0.2);
		col = mix(col, color_line * (line_glow > 0.0 ? 0.6 : 1.0), line * (line_glow > 0.0 ? 0.6 : 0.55));
	}
	ALBEDO = col;
	ROUGHNESS = 1.0;
	EMISSION = color_line * line * line_glow * (0.7 + 0.3 * sin(TIME * 1.3 + p.x * 0.2));
}
";

    private static ShaderMaterial? _character, _characterFlat, _outline, _aura, _prop, _foliage;
    private static Shader? _floor;

    /// <summary>Props: the character shading with wind sway on flagged vertices
    /// and a slightly thicker outline (props are bigger and further away).</summary>
    public static ShaderMaterial Prop => _prop ??= MakeProp();

    public static ShaderMaterial Foliage => _foliage ??= new ShaderMaterial { Shader = new Shader { Code = FoliageCode } };

    public static Shader FloorShader => _floor ??= new Shader { Code = FloorCode };

    // Overlay passes for imported models, whose own materials we leave alone.
    private const string OverlayCode = @"shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec4 color : source_color = vec4(1.0);
uniform float fill = 0.0;
uniform float rim_power = 2.5;
void fragment() {
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), rim_power);
	ALBEDO = color.rgb * (fill + rim) * color.a;
}
";

    private static Shader? _overlayShader;
    private static ShaderMaterial? _flashOverlay;
    private static readonly System.Collections.Generic.Dictionary<Color, ShaderMaterial> Rims = new();

    /// <summary>White pop for a hit on an imported model.</summary>
    public static ShaderMaterial FlashOverlay => _flashOverlay ??= Overlay(new Color(1f, 0.95f, 0.9f, 1f), 0.75f);

    /// <summary>Coloured rim light marking an imported elite or boss.</summary>
    public static ShaderMaterial RimOverlay(Color c)
    {
        if (Rims.TryGetValue(c, out ShaderMaterial? m) && GodotObject.IsInstanceValid(m)) return m;
        m = Overlay(new Color(c.R, c.G, c.B, 0.8f), 0f, 3.5f);
        Rims[c] = m;
        return m;
    }

    private static ShaderMaterial Overlay(Color c, float fill, float rimPower = 2.5f)
    {
        _overlayShader ??= new Shader { Code = OverlayCode };
        var m = new ShaderMaterial { Shader = _overlayShader };
        m.SetShaderParameter("color", c);
        m.SetShaderParameter("fill", fill);
        m.SetShaderParameter("rim_power", rimPower);
        return m;
    }

    private static ShaderMaterial MakeProp()
    {
        var m = new ShaderMaterial { Shader = Character.Shader };
        m.SetShaderParameter("sway_amount", 0.5f);
        m.SetShaderParameter("rim_strength", 0.25f);
        var o = new ShaderMaterial { Shader = ((ShaderMaterial)Character.NextPass).Shader };
        o.SetShaderParameter("sway_amount", 0.5f);
        o.SetShaderParameter("width", 0.04f);
        m.NextPass = o;
        return m;
    }

    /// <summary>Character material with the outline pass. Near LOD.</summary>
    public static ShaderMaterial Character => _character ??= MakeCharacter(outline: true);

    /// <summary>Same shading, no outline. Far LOD, props, ghosts of the cast.</summary>
    public static ShaderMaterial CharacterNoOutline => _characterFlat ??= MakeCharacter(outline: false);

    public static ShaderMaterial Aura => _aura ??= new ShaderMaterial { Shader = new Shader { Code = AuraCode } };

    private static ShaderMaterial MakeCharacter(bool outline)
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = CharacterCode } };
        if (outline)
        {
            _outline ??= new ShaderMaterial { Shader = new Shader { Code = OutlineCode } };
            m.NextPass = _outline;
        }
        return m;
    }

    /// <summary>Exposed for the prop and floor shaders, so the whole world is lit
    /// by the same bands as the cast that walks on it.</summary>
    internal const string SharedCommon = Common;
    internal const string SharedLight = ToonLight;
}
