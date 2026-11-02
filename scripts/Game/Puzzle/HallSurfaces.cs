using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// Surfaces for the puzzle halls, drawn here rather than downloaded: flagstone
/// floors with grout, ashlar and brick walls in courses, timber boards, iron
/// slabs, plaster and a pale marble for the puzzle grid. Each is a tileable
/// greyscale-ish albedo (the room's palette colour multiplies it, so one
/// texture serves every kind of hall) and a normal map taken from the same
/// height field: bevelled block edges, sunk grout, a little surface grain.
///
/// <b>Mapping.</b> World-space triplanar, so a texel is the same size on a
/// 30 m floor, a 14 m wall and a 1 m door frame, and merged shell meshes need
/// no UVs. Godot projects the walls with the texture's u horizontal and v
/// vertical, so the courses run level on every wall.
///
/// <b>Cost.</b> Each texture is generated once per resolution and kept, and
/// each (surface, colour) material is shared by every room that asks for it.
/// The resolution follows the graphics preset: 128 px with no normal maps on
/// Low, 256 on Medium and High, 512 on Ultra. Mipmapped, so distant walls do
/// not shimmer.
/// </summary>
internal static class HallSurfaces
{
    public enum Kind { Flagstone, Ashlar, Brick, Planks, Slabs, Plaster, Marble }

    private readonly record struct Pair(ImageTexture Albedo, ImageTexture? Normal);

    private static readonly Dictionary<(Kind, int, bool), Pair> TextureCache = new();
    private static readonly Dictionary<(Kind, Color, int, bool, bool), StandardMaterial3D> MaterialCache = new();

    /// <summary>Metres one tile of the texture covers.</summary>
    private static float TileMetres(Kind k) => k switch
    {
        Kind.Brick => 2f,
        Kind.Planks => 3f,
        Kind.Plaster => 6f,
        _ => 4f,
    };

    private static int Resolution => Meta.GraphicsSettings.Current.Preset switch
    {
        Meta.GraphicsSettings.Low => 128,
        Meta.GraphicsSettings.Ultra => 512,
        _ => 256,
    };

    private static bool NormalMaps => Meta.GraphicsSettings.Current.Preset != Meta.GraphicsSettings.Low;

    /// <summary>The shared material for <paramref name="kind"/> in
    /// <paramref name="tint"/>. <paramref name="world"/> false maps it in the
    /// mesh's own space instead, for something that moves (a door slab).</summary>
    public static StandardMaterial3D Material(Kind kind, Color tint, bool world = true)
    {
        int res = Resolution;
        bool normals = NormalMaps;
        if (MaterialCache.TryGetValue((kind, tint, res, normals, world), out StandardMaterial3D? m) && GodotObject.IsInstanceValid(m)) return m;
        Pair tex = Textures(kind, res, normals);
        m = new StandardMaterial3D
        {
            AlbedoColor = tint,
            AlbedoTexture = tex.Albedo,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = world,
            Uv1Scale = Vector3.One / TileMetres(kind),
            Roughness = kind switch { Kind.Marble => 0.5f, Kind.Slabs => 0.7f, Kind.Planks => 0.82f, _ => 0.93f },
            Metallic = kind == Kind.Slabs ? 0.25f : 0f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        if (tex.Normal is not null)
        {
            m.NormalEnabled = true;
            m.NormalTexture = tex.Normal;
            m.NormalScale = kind is Kind.Marble or Kind.Plaster ? 0.5f : 0.9f;
        }
        MaterialCache[(kind, tint, res, normals, world)] = m;
        return m;
    }

    private static Pair Textures(Kind kind, int res, bool normals)
    {
        if (TextureCache.TryGetValue((kind, res, normals), out Pair p) && GodotObject.IsInstanceValid(p.Albedo)) return p;
        ulong started = Time.GetTicksUsec();
        var f = new Field(res);
        var rng = new Core.Rng(0x5701_E000UL + (ulong)kind * 977UL);
        float[] grain = Noise(res, 3f, 4, 11 + (int)kind);
        float[] stain = Noise(res, 0.6f, 3, 23 + (int)kind);
        float px = res / TileMetres(kind);   // pixels per metre
        switch (kind)
        {
            case Kind.Flagstone:
                Masonry(f, rng, grain, stain, RowsBetween(rng, res, 0.55f * px, 1.05f * px), 0.7f * px, 1.5f * px,
                        groutPx: Math.Max(1f, 0.035f * px), bevelPx: Math.Max(1f, 0.06f * px), toneVar: 0.16f, wood: false);
                break;
            case Kind.Ashlar:
                Masonry(f, rng, grain, stain, RowsEven(res, 0.66f * px), 0.9f * px, 1.7f * px,
                        groutPx: Math.Max(1f, 0.03f * px), bevelPx: Math.Max(1f, 0.07f * px), toneVar: 0.14f, wood: false);
                break;
            case Kind.Brick:
                Masonry(f, rng, grain, stain, RowsEven(res, 0.2f * px), 0.5f * px, 0.5f * px,
                        groutPx: Math.Max(1f, 0.025f * px), bevelPx: Math.Max(1f, 0.03f * px), toneVar: 0.2f, wood: false, halfBond: true);
                break;
            case Kind.Planks:
                Masonry(f, rng, grain, stain, RowsEven(res, 0.25f * px), 0.9f * px, 2.0f * px,
                        groutPx: Math.Max(1f, 0.012f * px), bevelPx: 1f, toneVar: 0.22f, wood: true);
                break;
            case Kind.Slabs:
                Masonry(f, rng, grain, stain, RowsEven(res, 1f * px), 1f * px, 1f * px,
                        groutPx: Math.Max(1f, 0.02f * px), bevelPx: Math.Max(1f, 0.04f * px), toneVar: 0.08f, wood: false, aligned: true);
                break;
            case Kind.Plaster:
                for (int i = 0; i < f.Height.Length; i++)
                {
                    f.Height[i] = 0.5f + grain[i] * 0.25f;
                    f.Tone[i] = 0.86f + (grain[i] - 0.5f) * 0.12f + (stain[i] - 0.5f) * 0.12f;
                }
                break;
            case Kind.Marble:
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        int i = y * res + x;
                        // Integer cycles across the tile keep the veins seamless.
                        float vein = Mathf.Abs(Mathf.Sin(Mathf.Tau * (x * 2f / res + y * 1f / res + stain[i] * 2.2f)));
                        float line = 1f - Mathf.SmoothStep(0f, 0.08f, vein);
                        f.Height[i] = 0.5f + grain[i] * 0.05f;
                        f.Tone[i] = 0.93f + (grain[i] - 0.5f) * 0.06f - line * 0.12f;
                    }
                break;
        }
        var albedo = Image.CreateEmpty(res, res, false, Image.Format.Rgb8);
        byte[] a = new byte[res * res * 3];
        for (int i = 0; i < res * res; i++)
        {
            float t = Mathf.Clamp(f.Tone[i], 0f, 1f);
            float warm = f.Warm[i];
            a[i * 3] = ToByte(t * (1f + warm * 0.05f));
            a[i * 3 + 1] = ToByte(t);
            a[i * 3 + 2] = ToByte(t * (1f - warm * 0.05f));
        }
        albedo.SetData(res, res, false, Image.Format.Rgb8, a);
        albedo.GenerateMipmaps();
        ImageTexture? normal = null;
        if (normals)
        {
            float strength = kind switch { Kind.Marble or Kind.Plaster => 1.2f, _ => 2.4f } * res / 256f;
            byte[] n = new byte[res * res * 3];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float dx = (f.H(x + 1, y) - f.H(x - 1, y)) * strength;
                    float dy = (f.H(x, y + 1) - f.H(x, y - 1)) * strength;
                    // OpenGL convention (green up): up the image is -y.
                    var v = new Vector3(-dx, dy, 1f).Normalized();
                    int i = (y * res + x) * 3;
                    n[i] = ToByte(v.X * 0.5f + 0.5f);
                    n[i + 1] = ToByte(v.Y * 0.5f + 0.5f);
                    n[i + 2] = ToByte(v.Z * 0.5f + 0.5f);
                }
            var nimg = Image.CreateEmpty(res, res, false, Image.Format.Rgb8);
            nimg.SetData(res, res, false, Image.Format.Rgb8, n);
            nimg.GenerateMipmaps();
            normal = ImageTexture.CreateFromImage(nimg);
        }
        p = new Pair(ImageTexture.CreateFromImage(albedo), normal);
        TextureCache[(kind, res, normals)] = p;
        GD.Print($"[puzzle] hall surface {kind} {res}px{(normals ? " +normal" : "")} in {(Time.GetTicksUsec() - started) / 1000.0:0.0} ms");
        return p;
    }

    private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

    /// <summary>Height, tone and a warm/cool shift per pixel, wrapping at the edges.</summary>
    private sealed class Field
    {
        public readonly int Res;
        public readonly float[] Height, Tone, Warm;

        public Field(int res)
        {
            Res = res;
            Height = new float[res * res];
            Tone = new float[res * res];
            Warm = new float[res * res];
        }

        public float H(int x, int y) => Height[((y % Res + Res) % Res) * Res + (x % Res + Res) % Res];
    }

    /// <summary>Seamless fractal noise, 0 to 1, at <paramref name="res"/>: one
    /// engine call at no more than 128 px (the seamless blend is the slow
    /// part), stretched with wrapping bilinear filtering.</summary>
    private static float[] Noise(int res, float frequencyPerTile, int octaves, int seed)
    {
        int n = Math.Min(res, 128);
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Seed = seed,
            // The engine samples one unit per pixel.
            Frequency = frequencyPerTile * 8f / n,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = octaves,
        };
        Image img = noise.GetSeamlessImage(n, n, false, false, 0.1f, true);
        if (img.GetFormat() != Image.Format.L8) img.Convert(Image.Format.L8);
        byte[] data = img.GetData();
        var outv = new float[res * res];
        float step = (float)n / res;
        for (int y = 0; y < res; y++)
        {
            float fy = y * step;
            int y0 = (int)fy % n, y1 = (y0 + 1) % n;
            float ty = fy - (int)fy;
            for (int x = 0; x < res; x++)
            {
                float fx = x * step;
                int x0 = (int)fx % n, x1 = (x0 + 1) % n;
                float tx = fx - (int)fx;
                float top = data[y0 * n + x0] + (data[y0 * n + x1] - data[y0 * n + x0]) * tx;
                float bottom = data[y1 * n + x0] + (data[y1 * n + x1] - data[y1 * n + x0]) * tx;
                outv[y * res + x] = (top + (bottom - top) * ty) / 255f;
            }
        }
        return outv;
    }

    /// <summary>Course heights in pixels, each between the bounds, filling the tile exactly.</summary>
    private static List<int> RowsBetween(Core.Rng rng, int res, float min, float max)
    {
        var rows = new List<int>();
        int left = res;
        while (left > 0)
        {
            int h = Math.Max(2, Mathf.RoundToInt(rng.Range(min, max)));
            if (left - h < min) h = left;
            rows.Add(h);
            left -= h;
        }
        return rows;
    }

    private static List<int> RowsEven(int res, float height)
    {
        int n = Math.Max(1, Mathf.RoundToInt(res / height));
        var rows = new List<int>();
        for (int k = 0; k < n; k++) rows.Add((k + 1) * res / n - k * res / n);
        return rows;
    }

    /// <summary>
    /// Blocks in courses (stone, brick, boards): each course is cut into blocks
    /// between <paramref name="minLen"/> and <paramref name="maxLen"/> pixels
    /// that fill the tile's width exactly, shifted by a random amount (half a
    /// block with <paramref name="halfBond"/>, none with <paramref name="aligned"/>),
    /// so the tile wraps on both axes. Grout is sunk and dark, block edges are
    /// bevelled, each block has its own tone.
    /// </summary>
    private static void Masonry(Field f, Core.Rng rng, float[] grain, float[] stain, List<int> rows, float minLen, float maxLen,
                                float groutPx, float bevelPx, float toneVar, bool wood, bool halfBond = false, bool aligned = false)
    {
        int res = f.Res;
        int y0 = 0;
        for (int r = 0; r < rows.Count; r++)
        {
            int rowH = rows[r];
            // Cut the course: lengths in range, scaled to fill the width.
            var cuts = new List<float>();
            float sum = 0f;
            while (sum < res - minLen * 0.5f)
            {
                float len = rng.Range(minLen, maxLen);
                cuts.Add(len);
                sum += len;
            }
            float scale = res / sum;
            var edges = new List<float> { 0f };
            foreach (float c in cuts) edges.Add(edges[^1] + c * scale);
            float shift = aligned ? 0f : halfBond ? (r % 2) * cuts[0] * scale * 0.5f : rng.Range(0f, res);
            var tones = new float[cuts.Count];
            var warms = new float[cuts.Count];
            var phase = new float[cuts.Count];
            for (int b = 0; b < cuts.Count; b++)
            {
                tones[b] = 0.86f + rng.Range(-toneVar, toneVar) * 0.5f;
                warms[b] = rng.Range(-1f, 1f);
                phase[b] = rng.Range(0f, 1f);
            }
            for (int y = y0; y < y0 + rowH; y++)
            {
                float dy = Mathf.Min(y - y0 + 0.5f, y0 + rowH - y - 0.5f);
                for (int x = 0; x < res; x++)
                {
                    float xs = ((x + 0.5f - shift) % res + res) % res;
                    int b = 0;
                    while (b < cuts.Count - 1 && xs >= edges[b + 1]) b++;
                    float dx = Mathf.Min(xs - edges[b], edges[b + 1] - xs);
                    float d = Mathf.Min(dx, dy) - groutPx * 0.5f;
                    int i = y * res + x;
                    float g = grain[i] - 0.5f, s = stain[i] - 0.5f;
                    if (d <= 0f)
                    {
                        f.Height[i] = 0.05f + g * 0.05f;
                        f.Tone[i] = 0.42f + g * 0.1f;
                        f.Warm[i] = 0f;
                        continue;
                    }
                    float bevel = Mathf.SmoothStep(0f, bevelPx, d);
                    float tone = tones[b] + g * 0.14f + s * 0.16f;
                    if (wood)
                    {
                        // Grain along the board: whole cycles down the tile, so it wraps.
                        float ring = Mathf.Sin(Mathf.Tau * ((float)y / res * 48f + grain[i] * 1.6f + phase[b]));
                        tone += ring * 0.05f;
                    }
                    // Worn, slightly paler arrises; a darker line where the bevel turns.
                    tone += (1f - bevel) * (wood ? -0.12f : -0.06f);
                    f.Height[i] = 0.35f + 0.65f * bevel + g * (wood ? 0.03f : 0.1f);
                    f.Tone[i] = tone;
                    f.Warm[i] = warms[b];
                }
            }
            y0 += rowH;
        }
    }
}
