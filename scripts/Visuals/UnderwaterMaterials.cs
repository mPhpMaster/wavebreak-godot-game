using Godot;

namespace Wavebreak.Visuals;

public static class UnderwaterMaterials
{
    public static ShaderMaterial Seabed() => new()
    {
        Shader = new Shader
        {
            Code = """
                shader_type spatial;
                varying vec3 world;
                varying float slope;
                void vertex() {
                    world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
                    slope = 1.0 - abs(NORMAL.y);
                }
                void fragment() {
                    vec2 p = world.xz;
                    float sand = sin(p.x * 0.13) * cos(p.y * 0.17) * 0.5 + 0.5;
                    vec3 base = mix(vec3(0.055, 0.19, 0.22), vec3(0.22, 0.39, 0.36), sand);
                    float a = sin(p.x * 0.79 + sin(p.y * 0.52 + TIME * 0.55));
                    float b = sin(p.y * 0.73 - cos(p.x * 0.47 - TIME * 0.4));
                    float caustic = pow(clamp(1.0 - abs(a + b) * 0.85, 0.0, 1.0), 16.0);
                    float depth = clamp((world.y + 22.0) / 42.0, 0.25, 1.0);
                    float cliff = smoothstep(0.12, 0.50, slope);
                    float strata = sin(world.y * 1.9 + sin(p.x * 0.4) + cos(p.y * 0.3)) * 0.025;
                    base = mix(base, vec3(0.12, 0.21, 0.24) + strata, cliff);
                    caustic *= 1.0 - cliff * 0.8;
                    ALBEDO = base * depth + vec3(0.06, 0.15, 0.12) * caustic;
                    EMISSION = vec3(0.04, 0.17, 0.15) * caustic * depth;
                    ROUGHNESS = 0.88;
                }
                """,
        },
    };

    public static ShaderMaterial Surface() => new()
    {
        Shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled;
                uniform float water_time = 0.0;
                uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
                varying vec3 world;
                varying float wave;
                varying vec3 wave_normal;
                void vertex() {
                    float a = VERTEX.x * 0.13 + water_time * 0.9;
                    float b = VERTEX.z * 0.17 - water_time * 0.8;
                    float c = (VERTEX.x + VERTEX.z) * 0.25 + water_time * 1.3;
                    wave = 1.2 * sin(a) + 0.75 * cos(b) + 0.35 * sin(c);
                    VERTEX.y += wave;
                    wave_normal = normalize(vec3(-0.156 * cos(a) - 0.0875 * cos(c),
                        1.0, 0.1275 * sin(b) - 0.0875 * cos(c)));
                    NORMAL = wave_normal;
                    world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
                }
                void fragment() {
                    float crest = smoothstep(0.85, 1.85, wave);
                    float glint = pow(max(0.0, sin(world.x * 1.2 + world.z * 0.7 + water_time)), 12.0);
                    vec2 distortion = wave_normal.xz * 0.018;
                    vec3 refracted = texture(screen_texture, SCREEN_UV + distortion).rgb;
                    vec3 water = mix(vec3(0.015, 0.14, 0.22), vec3(0.12, 0.56, 0.64), wave * 0.2 + 0.5);
                    water = mix(water, vec3(0.75, 0.96, 0.94), crest * (0.65 + glint * 0.35));
                    ALBEDO = mix(refracted * 0.7, water, 0.82);
                    EMISSION = water * (0.18 + crest * 0.18);
                    ALPHA = 0.94;
                }
                """,
        },
    };
}
