using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// Accumulates primitive shapes into one vertex buffer, each with its own
/// colour, and bakes them into a single <see cref="ArrayMesh"/>.
///
/// The whole art style rests on this. A character is a dozen shapes, but every
/// shape that moves rigidly with another (head with torso, horns with head, a
/// sword with the hand that holds it) is merged into one mesh, so a model costs
/// one draw call per *moving part* rather than per shape. Colour travels in the
/// vertex, not the material, which is what lets every character in the game share
/// one material — the renderer never switches state between two enemies.
///
/// Per-vertex channels:
///   COLOR   palette colour, converted to linear here so the shader can use it raw
///   UV.x    emission strength (eyes, crystals, runes); 0 for plain surfaces
///   UV.y    wind weight for props (see <see cref="Sway"/>); 0 on characters
///
/// Shapes are smooth-shaded at low segment counts (soft low-poly), except boxes
/// and <see cref="Faceted"/> shapes, which keep hard edges for rock and armour.
/// </summary>
public sealed class MeshKit
{
    private readonly List<Vector3> _v = new();
    private readonly List<Vector3> _n = new();
    private readonly List<Color> _c = new();
    private readonly List<Vector2> _uv = new();
    private readonly List<int> _i = new();

    public int VertexCount => _v.Count;

    /// <summary>When set, every subsequent shape gets per-triangle normals.</summary>
    public bool Faceted { get; set; }

    /// <summary>Wind weight written to UV.y for subsequent shapes: 0 is rigid,
    /// 1 sways fully. Foliage and grass tips set it; trunks and rocks leave it 0.</summary>
    public float Sway { get; set; }

    // ---------------------------------------------------------------- output

    public ArrayMesh ToMesh()
    {
        var mesh = new ArrayMesh();
        if (_v.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _n.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _c.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _i.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Copy another kit's geometry in, transformed. Used to bake a whole
    /// posed model into one far-LOD mesh.</summary>
    public void Append(MeshKit other, Transform3D xf)
    {
        int b = _v.Count;
        Basis nb = xf.Basis.Inverse().Transposed();
        for (int k = 0; k < other._v.Count; k++)
        {
            _v.Add(xf * other._v[k]);
            _n.Add((nb * other._n[k]).Normalized());
            _c.Add(other._c[k]);
            _uv.Add(other._uv[k]);
        }
        foreach (int idx in other._i) _i.Add(b + idx);
    }

    // ------------------------------------------------------------ primitives

    private int Vert(Transform3D xf, Basis nb, Vector3 p, Vector3 n, Color c, float emit)
    {
        _v.Add(xf * p);
        _n.Add((nb * n).Normalized());
        _c.Add(c);
        _uv.Add(new Vector2(emit, Sway));
        return _v.Count - 1;
    }

    private void Tri(int a, int b, int c)
    {
        // Godot's front faces wind clockwise.
        _i.Add(a); _i.Add(c); _i.Add(b);
    }

    /// <summary>Emits a triangle with its own three vertices and face normal, for
    /// faceted shapes.</summary>
    private void FlatTri(Transform3D xf, Vector3 a, Vector3 b, Vector3 c, Color col, float emit)
    {
        Vector3 wa = xf * a, wb = xf * b, wc = xf * c;
        Vector3 n = (wb - wa).Cross(wc - wa);
        n = n.LengthSquared() > 1e-12f ? n.Normalized() : Vector3.Up;
        int s = _v.Count;
        _v.Add(wa); _v.Add(wb); _v.Add(wc);
        _n.Add(n); _n.Add(n); _n.Add(n);
        _c.Add(col); _c.Add(col); _c.Add(col);
        var uv = new Vector2(emit, Sway);
        _uv.Add(uv); _uv.Add(uv); _uv.Add(uv);
        _i.Add(s); _i.Add(s + 2); _i.Add(s + 1); // same winding as Tri()
    }

    private static Color Lin(Color c) => c.SrgbToLinear();

    /// <summary>
    /// A grid of (rings+1) x (segs+1) vertices produced by <paramref name="point"/>,
    /// stitched into quads. Every rounded shape below is one of these.
    /// <paramref name="point"/> returns position and normal for (u in 0..1 around,
    /// v in 0..1 bottom to top).
    /// </summary>
    private void Lathe(Transform3D xf, int segs, int rings, Color col, float emit,
                       System.Func<float, float, (Vector3 p, Vector3 n)> point, Color? topColor = null)
    {
        Color lc = Lin(col);
        Color tc = topColor is { } t ? Lin(t) : lc;
        if (Faceted)
        {
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segs; s++)
            {
                float u0 = (float)s / segs, u1 = (float)(s + 1) / segs;
                float v0 = (float)r / rings, v1 = (float)(r + 1) / rings;
                var p00 = point(u0, v0).p; var p10 = point(u1, v0).p;
                var p01 = point(u0, v1).p; var p11 = point(u1, v1).p;
                Color c0 = lc.Lerp(tc, (v0 + v1) * 0.5f);
                FlatTri(xf, p00, p01, p10, c0, emit);
                FlatTri(xf, p10, p01, p11, c0, emit);
            }
            return;
        }

        Basis nb = xf.Basis.Inverse().Transposed();
        int start = _v.Count;
        for (int r = 0; r <= rings; r++)
        {
            float v = (float)r / rings;
            Color c = lc.Lerp(tc, v);
            for (int s = 0; s <= segs; s++)
            {
                var (p, n) = point((float)s / segs, v);
                Vert(xf, nb, p, n, c, emit);
            }
        }
        int row = segs + 1;
        for (int r = 0; r < rings; r++)
        for (int s = 0; s < segs; s++)
        {
            int a = start + r * row + s;
            int b = a + 1;
            int c2 = a + row;
            int d = c2 + 1;
            Tri(a, c2, b);
            Tri(b, c2, d);
        }
    }

    /// <summary>Ellipsoid centred at the transform origin.</summary>
    public void Ellipsoid(Transform3D xf, Vector3 radii, Color col, float emit = 0f,
                          int segs = 10, int rings = 7, Color? topColor = null)
    {
        Lathe(xf, segs, rings, col, emit, (u, v) =>
        {
            float th = u * Mathf.Tau;
            float ph = (v - 0.5f) * Mathf.Pi;
            Vector3 unit = new(Mathf.Cos(ph) * Mathf.Cos(th), Mathf.Sin(ph), Mathf.Cos(ph) * Mathf.Sin(th));
            Vector3 p = unit * radii;
            Vector3 n = new Vector3(unit.X / radii.X, unit.Y / radii.Y, unit.Z / radii.Z).Normalized();
            return (p, n);
        }, topColor);
    }

    public void Sphere(Vector3 at, float r, Color col, float emit = 0f, int segs = 10, int rings = 7)
        => Ellipsoid(new Transform3D(Basis.Identity, at), new Vector3(r, r, r), col, emit, segs, rings);

    /// <summary>
    /// A tapered tube from <paramref name="a"/> to <paramref name="b"/> with
    /// rounded (hemisphere-ish) ends — limbs, necks, tails, horns.
    /// <paramref name="rb"/> of zero makes a cone/spike.
    /// </summary>
    public void Tube(Vector3 a, Vector3 b, float ra, float rb, Color col, float emit = 0f,
                     int segs = 8, bool caps = true, Color? endColor = null)
    {
        Vector3 axis = b - a;
        float len = axis.Length();
        if (len < 1e-5f) return;
        Basis basis = BasisAlong(axis / len);
        var xf = new Transform3D(basis, a);
        float slope = (ra - rb) / len;
        Lathe(xf, segs, 1, col, emit, (u, v) =>
        {
            float th = u * Mathf.Tau;
            float r = Mathf.Lerp(ra, rb, v);
            Vector3 radial = new(Mathf.Cos(th), 0f, Mathf.Sin(th));
            return (radial * r + Vector3.Up * (v * len), (radial + Vector3.Up * slope).Normalized());
        }, endColor);
        if (!caps) return;
        if (ra > 0.002f) Ellipsoid(xf, new Vector3(ra, ra, ra), col, emit, segs, 4);
        if (rb > 0.002f)
            Ellipsoid(new Transform3D(basis, b), new Vector3(rb, rb, rb), endColor ?? col, emit, segs, 4);
    }

    /// <summary>Cone with a flat base: spikes, horns, crystal tips, hats.</summary>
    public void Cone(Vector3 baseCentre, Vector3 tip, float r, Color col, float emit = 0f, int segs = 6,
                     Color? tipColor = null)
    {
        Vector3 axis = tip - baseCentre;
        float len = axis.Length();
        if (len < 1e-5f) return;
        var xf = new Transform3D(BasisAlong(axis / len), baseCentre);
        float slope = r / len;
        Lathe(xf, segs, 1, col, emit, (u, v) =>
        {
            float th = u * Mathf.Tau;
            Vector3 radial = new(Mathf.Cos(th), 0f, Mathf.Sin(th));
            return (radial * (r * (1f - v)) + Vector3.Up * (v * len), (radial + Vector3.Up * slope).Normalized());
        }, tipColor);
        Disc(xf, r, col, emit, segs, down: true);
    }

    /// <summary>Flat-capped cylinder (prism at low segment counts).</summary>
    public void Cylinder(Transform3D xf, float rBottom, float rTop, float height, Color col, float emit = 0f,
                         int segs = 8, Color? topColor = null)
    {
        float slope = (rBottom - rTop) / Mathf.Max(1e-4f, height);
        Lathe(xf, segs, 1, col, emit, (u, v) =>
        {
            float th = u * Mathf.Tau;
            Vector3 radial = new(Mathf.Cos(th), 0f, Mathf.Sin(th));
            return (radial * Mathf.Lerp(rBottom, rTop, v) + Vector3.Up * (v * height),
                    (radial + Vector3.Up * slope).Normalized());
        }, topColor);
        Disc(xf, rBottom, col, emit, segs, down: true);
        Disc(xf.TranslatedLocal(Vector3.Up * height), rTop, topColor ?? col, emit, segs, down: false);
    }

    private void Disc(Transform3D xf, float r, Color col, float emit, int segs, bool down)
    {
        if (r < 0.002f) return;
        Color lc = Lin(col);
        Basis nb = xf.Basis.Inverse().Transposed();
        Vector3 n = down ? Vector3.Down : Vector3.Up;
        int centre = Vert(xf, nb, Vector3.Zero, n, lc, emit);
        int first = _v.Count;
        for (int s = 0; s <= segs; s++)
        {
            float th = (float)s / segs * Mathf.Tau;
            Vert(xf, nb, new Vector3(Mathf.Cos(th) * r, 0f, Mathf.Sin(th) * r), n, lc, emit);
        }
        for (int s = 0; s < segs; s++)
        {
            if (down) Tri(centre, first + s, first + s + 1);
            else Tri(centre, first + s + 1, first + s);
        }
    }

    /// <summary>Axis-aligned (in <paramref name="xf"/>) box, always hard-edged.</summary>
    public void Box(Transform3D xf, Vector3 size, Color col, float emit = 0f, Color? topColor = null)
    {
        Color lc = Lin(col);
        Color tc = topColor is { } t ? Lin(t) : lc;
        Vector3 h = size * 0.5f;
        Basis nb = xf.Basis.Inverse().Transposed();
        void Face(Vector3 n, Vector3 u, Vector3 v)
        {
            Vector3 c = n * new Vector3(h.X, h.Y, h.Z).Abs().Dot(n.Abs());
            Vector3 hu = u * h.Dot(u.Abs()), hv = v * h.Dot(v.Abs());
            Color fc = n.Y > 0.5f ? tc : n.Y < -0.5f ? lc : lc;
            Color top = n.Y > 0.5f ? tc : tc;
            int a = Vert(xf, nb, c - hu - hv, n, fc, emit);
            int b = Vert(xf, nb, c + hu - hv, n, fc, emit);
            int d = Vert(xf, nb, c + hu + hv, n, n.Y < -0.5f ? fc : top, emit);
            int e = Vert(xf, nb, c - hu + hv, n, n.Y < -0.5f ? fc : top, emit);
            Tri(a, d, b);
            Tri(a, e, d);
        }
        Face(Vector3.Up, Vector3.Right, Vector3.Back);
        Face(Vector3.Down, Vector3.Right, Vector3.Forward);
        Face(Vector3.Right, Vector3.Back, Vector3.Up);
        Face(Vector3.Left, Vector3.Forward, Vector3.Up);
        Face(Vector3.Back, Vector3.Left, Vector3.Up);
        Face(Vector3.Forward, Vector3.Right, Vector3.Up);
    }

    public void Box(Vector3 centre, Vector3 size, Color col, float emit = 0f, Color? topColor = null)
        => Box(new Transform3D(Basis.Identity, centre), size, col, emit, topColor);

    /// <summary>
    /// Irregular faceted lump: a low sphere whose vertices are pushed in and out
    /// by a seeded hash. Rocks, rubble, a golem's knuckles.
    /// </summary>
    public void Lump(Transform3D xf, Vector3 radii, Color col, Color topColor, uint seed,
                     float roughness = 0.22f, int segs = 7, int rings = 5, float emit = 0f)
    {
        bool was = Faceted;
        Faceted = true;
        Lathe(xf, segs, rings, col, emit, (u, v) =>
        {
            float th = u * Mathf.Tau;
            float ph = (v - 0.5f) * Mathf.Pi;
            Vector3 unit = new(Mathf.Cos(ph) * Mathf.Cos(th), Mathf.Sin(ph), Mathf.Cos(ph) * Mathf.Sin(th));
            // Seam and poles share a hash so the lump stays closed.
            int su = (int)Mathf.Round(u * segs) % segs;
            int sv = (int)Mathf.Round(v * rings);
            if (sv == 0 || sv == rings) su = 0;
            float j = 1f + (Hash(seed, su, sv) - 0.5f) * 2f * roughness;
            Vector3 p = unit * radii * j;
            if (p.Y < -radii.Y * 0.35f) p.Y = -radii.Y * 0.35f; // flat bottom sits on the ground
            return (p, unit);
        }, topColor);
        Faceted = was;
    }

    // ---------------------------------------------------------------- helpers

    public static float Hash(uint seed, int a, int b)
    {
        uint h = seed * 374761393u + (uint)a * 668265263u + (uint)b * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>A basis whose +Y points along <paramref name="dir"/>.</summary>
    public static Basis BasisAlong(Vector3 dir)
    {
        Vector3 up = dir.Normalized();
        Vector3 helper = Mathf.Abs(up.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 x = helper.Cross(up).Normalized();
        Vector3 z = x.Cross(up).Normalized();
        return new Basis(x, up, z);
    }
}
