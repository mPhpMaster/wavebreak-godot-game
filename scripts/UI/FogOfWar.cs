using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Net;
using Wavebreak.Visuals;

namespace Wavebreak.UI;

/// <summary>
/// Fog of war for the minimap and the Tab big map: a map starts unexplored,
/// and what the team has been near is revealed for the rest of that map.
///
/// Two layers, for two jobs:
///
/// - <b>The picture</b>: a texture of the whole map at 1.25 m a pixel (the
///   floor, the wall, raised features, and the blockers of hand-built maps
///   drawn as shapes), mixed per pixel with a dark fog by how well each pixel
///   has been seen. Reveals are soft-edged circles (<see cref="RevealRadius"/>)
///   stamped round every player this machine knows about, its own and the
///   teammates' puppets, whenever one has moved a couple of metres. Uploaded
///   to the GPU at most about 7 times a second, only when something changed.
/// - <b>The record</b>: a coarse grid of 5 m cells, one bit each (about 750
///   bytes on the largest map). This is what co-op shares
///   (Net/NetSession.Fog.cs): each player sends its bits to the host when they
///   changed, the host ORs them and sends the team's mask back, so a player
///   who joins late, or whose puppet view missed a teammate's detour, still
///   sees everything the team has explored.
///
/// Rules on the maps (Minimap, BigMap): enemies, chests and shrines in fog are
/// not drawn; you, teammates, pings and SOS markers always are; the portal is
/// drawn whenever it is open (it opens at the map's centre, where every map is
/// entered, so it is always somewhere the team has been). The rest room has no
/// fog.
/// </summary>
public sealed partial class FogOfWar : Node
{
    public static FogOfWar? Instance { get; private set; }

    public const float CellMeters = 5f;
    public const float PixelMeters = 1.25f;
    /// <summary>How far round a player the map is revealed.</summary>
    public const float RevealRadius = 32f;
    private const float SoftEdge = 6f;
    private const float StampEvery = 0.2f;
    private const float StampMove = 2f;
    private const float UploadEvery = 0.15f;

    /// <summary>False before the first map is ready and in the rest room:
    /// everything counts as revealed then.</summary>
    public bool Active { get; private set; }

    /// <summary>Half the side of the mapped square, in metres, centred on the origin.</summary>
    public float Extent { get; private set; }
    public int Cells { get; private set; }
    public int Pixels { get; private set; }

    /// <summary>Identifies the current map for the network: phase and map id.
    /// A mask for another map is ignored.</summary>
    public int Token { get; private set; }

    /// <summary>Bumped on every reset (new map).</summary>
    public int Version { get; private set; }

    public ImageTexture? Texture { get; private set; }

    /// <summary>Coarse cells revealed, and how many of them came from the
    /// team's mask rather than from this machine (the co-op test reads it).</summary>
    public int CellsRevealed { get; private set; }
    public int CellsFromTeam { get; private set; }

    /// <summary>Set when this machine revealed a new cell since the last send.</summary>
    internal bool LocalChanged;

    private byte[] _bits = Array.Empty<byte>();
    private byte[] _seen = Array.Empty<byte>();
    private byte[] _base = Array.Empty<byte>();
    private byte[] _kind = Array.Empty<byte>();   // 0 outside, 1 floor, 2 wall band
    private Color _floorFlat;
    private byte[] _rgba = Array.Empty<byte>();
    private Image? _img;
    private bool _dirty;
    private float _uploadIn;
    private float _stampIn;
    private int _arenaVersion = -1;
    private readonly Dictionary<long, Vector2> _lastStamp = new();

    private static readonly Color FogInk = new(0.075f, 0.095f, 0.14f);

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        Name = "FogOfWar";
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        using var timing = Prof.Scope("UI.fog");
        float dt = (float)delta;
        if (Main.Instance is not { } main || main.Stages is null) return;
        if (main.Stages.InRestRoom) { Active = false; return; }
        if (Arena.FeaturesVersion != _arenaVersion) Reset(main);
        if (!Active) return;

        if (!GetTree().Paused || Coop.Active)
        {
            _stampIn -= dt;
            if (_stampIn <= 0f)
            {
                _stampIn = StampEvery;
                StampPlayers();
            }
        }
        if (Coop.InRun) NetSession.Instance?.FogTick(dt);

        _uploadIn -= dt;
        if (_dirty && _uploadIn <= 0f && _img is not null && Texture is not null)
        {
            _uploadIn = UploadEvery;
            _dirty = false;
            _img.SetData(Pixels, Pixels, false, Image.Format.Rgba8, _rgba);
            Texture.Update(_img);
        }
    }

    // ================================================================ query

    /// <summary>True when the team has seen this point (always true without fog).</summary>
    public static bool IsRevealed(Vector3 p)
    {
        if (Instance is not { Active: true } f) return true;
        return f.SeenAt(p.X, p.Z) >= 96;
    }

    public byte SeenAt(float x, float z)
    {
        int i = (int)((x + Extent) / PixelMeters), j = (int)((z + Extent) / PixelMeters);
        if (i < 0 || j < 0 || i >= Pixels || j >= Pixels) return 0;
        return _seen[j * Pixels + i];
    }

    /// <summary>World XZ to texture UV (0..1).</summary>
    public Vector2 Uv(float x, float z) => new((x + Extent) / (2f * Extent), (z + Extent) / (2f * Extent));

    /// <summary>A copy of the coarse bits, for sending.</summary>
    internal byte[] Bits => (byte[])_bits.Clone();

    /// <summary>Share of the map's cells revealed, 0..1 (for logs and tests).</summary>
    public float Explored
    {
        get
        {
            int inside = 0, seen = 0;
            for (int j = 0; j < Cells; j++)
                for (int i = 0; i < Cells; i++)
                {
                    var c = CellCentre(i, j);
                    if (Arena.EdgeExcess(new Vector3(c.X, 0f, c.Y)) > 0f) continue;
                    inside++;
                    if (Bit(i, j)) seen++;
                }
            return inside > 0 ? (float)seen / inside : 0f;
        }
    }

    // ================================================================ reveal

    private void StampPlayers()
    {
        if (Player.Instance is { } me && IsInstanceValid(me) && me.IsInsideTree())
            StampFor(0, me.GlobalPosition);
        if (Coop.InRun && NetSession.Instance is { } s)
            foreach (RemotePlayer r in s.Remotes.Values)
                if (IsInstanceValid(r) && r.HasState) StampFor(r.Slot + 1, r.GlobalPosition);
    }

    private void StampFor(long who, Vector3 at)
    {
        var flat = new Vector2(at.X, at.Z);
        if (_lastStamp.TryGetValue(who, out Vector2 last) && last.DistanceSquaredTo(flat) < StampMove * StampMove) return;
        _lastStamp[who] = flat;
        Reveal(flat, RevealRadius);
    }

    /// <summary>Reveal a soft circle (and the coarse cells under it).</summary>
    public void Reveal(Vector2 at, float radius)
    {
        if (!Active) return;
        int i0 = Math.Max(0, (int)((at.X - radius + Extent) / PixelMeters));
        int i1 = Math.Min(Pixels - 1, (int)((at.X + radius + Extent) / PixelMeters));
        int j0 = Math.Max(0, (int)((at.Y - radius + Extent) / PixelMeters));
        int j1 = Math.Min(Pixels - 1, (int)((at.Y + radius + Extent) / PixelMeters));
        for (int j = j0; j <= j1; j++)
        {
            float z = -Extent + (j + 0.5f) * PixelMeters;
            for (int i = i0; i <= i1; i++)
            {
                float x = -Extent + (i + 0.5f) * PixelMeters;
                float d = MathF.Sqrt((x - at.X) * (x - at.X) + (z - at.Y) * (z - at.Y));
                if (d >= radius) continue;
                byte v = (byte)(255f * Math.Clamp((radius - d) / SoftEdge, 0f, 1f));
                int k = j * Pixels + i;
                if (v <= _seen[k]) continue;
                _seen[k] = v;
                Compose(k);
            }
        }

        // Coarse cells whose centre is well inside the circle.
        float inner = radius - CellMeters * 0.5f;
        int c0 = Math.Max(0, (int)((at.X - radius + Extent) / CellMeters)), c1 = Math.Min(Cells - 1, (int)((at.X + radius + Extent) / CellMeters));
        int r0 = Math.Max(0, (int)((at.Y - radius + Extent) / CellMeters)), r1 = Math.Min(Cells - 1, (int)((at.Y + radius + Extent) / CellMeters));
        for (int j = r0; j <= r1; j++)
            for (int i = c0; i <= c1; i++)
            {
                if (Bit(i, j) || CellCentre(i, j).DistanceTo(at) > inner) continue;
                SetBit(i, j);
                CellsRevealed++;
                LocalChanged = true;
            }
    }

    /// <summary>OR the team's bits in. Returns how many cells were new here.</summary>
    internal int Merge(byte[] bits)
    {
        if (!Active || bits.Length != _bits.Length) return 0;
        int fresh = 0;
        for (int b = 0; b < bits.Length; b++)
        {
            int add = bits[b] & ~_bits[b];
            if (add == 0) continue;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((add & (1 << bit)) == 0) continue;
                int idx = b * 8 + bit;
                if (idx >= Cells * Cells) break;
                RevealCell(idx % Cells, idx / Cells);
                fresh++;
            }
            _bits[b] |= (byte)add;
        }
        CellsRevealed += fresh;
        CellsFromTeam += fresh;
        return fresh;
    }

    private void RevealCell(int ci, int cj)
    {
        int i0 = Math.Max(0, (int)(ci * CellMeters / PixelMeters)), i1 = Math.Min(Pixels - 1, (int)((ci + 1) * CellMeters / PixelMeters));
        int j0 = Math.Max(0, (int)(cj * CellMeters / PixelMeters)), j1 = Math.Min(Pixels - 1, (int)((cj + 1) * CellMeters / PixelMeters));
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int k = j * Pixels + i;
                if (_seen[k] == 255) continue;
                _seen[k] = 255;
                Compose(k);
            }
    }

    private bool Bit(int i, int j) { int idx = j * Cells + i; return (_bits[idx >> 3] & (1 << (idx & 7))) != 0; }
    private void SetBit(int i, int j) { int idx = j * Cells + i; _bits[idx >> 3] |= (byte)(1 << (idx & 7)); }
    private Vector2 CellCentre(int i, int j) => new(-Extent + (i + 0.5f) * CellMeters, -Extent + (j + 0.5f) * CellMeters);

    // ================================================================ build

    private void Reset(Main main)
    {
        using var timing = Prof.Scope("Startup.fogReset");
        _arenaVersion = Arena.FeaturesVersion;
        StageDef s = Arena.Current;
        Extent = s.Radius + 4f;
        Pixels = Mathf.CeilToInt(2f * Extent / PixelMeters);
        Cells = Mathf.CeilToInt(2f * Extent / CellMeters);
        _bits = new byte[(Cells * Cells + 7) / 8];
        _seen = new byte[Pixels * Pixels];
        _base = new byte[Pixels * Pixels * 4];
        _kind = new byte[Pixels * Pixels];
        _rgba = new byte[Pixels * Pixels * 4];
        _lastStamp.Clear();
        CellsRevealed = 0;
        CellsFromTeam = 0;
        LocalChanged = false;
        // string.GetHashCode differs per process; the token must not.
        uint h = 2166136261u;
        foreach (char ch in s.Id) h = (h ^ ch) * 16777619u;
        Token = main.Run.Phase * 1000 + (int)(h % 997u);
        BuildBase(s);
        for (int k = 0; k < Pixels * Pixels; k++) Compose(k);
        _img = Image.CreateFromData(Pixels, Pixels, false, Image.Format.Rgba8, _rgba);
        Texture = ImageTexture.CreateFromImage(_img);
        Active = true;
        Version++;
        _stampIn = 0f;
        GD.Print($"[fog] map {s.Id} token={Token} cells={Cells}x{Cells} ({_bits.Length} bytes) texture={Pixels}px");
    }

    /// <summary>The map as a picture: floor inside the wall, the wall band,
    /// raised features, blockers as filled shapes with an outline.</summary>
    private void BuildBase(StageDef s)
    {
        PropTheme theme = PropTheme.Get(s.Theme);
        Color floor = theme.FloorA.Lerp(new Color(0.34f, 0.42f, 0.50f), 0.45f);
        Color wall = s.Wall.Lightened(0.15f);
        _floorFlat = floor;
        for (int j = 0; j < Pixels; j++)
        {
            float z = -Extent + (j + 0.5f) * PixelMeters;
            for (int i = 0; i < Pixels; i++)
            {
                float x = -Extent + (i + 0.5f) * PixelMeters;
                float edge = Arena.EdgeExcess(new Vector3(x, 0f, z));
                int k = (j * Pixels + i) * 4;
                if (edge > 1.5f) { _base[k + 3] = 0; continue; }
                _kind[j * Pixels + i] = (byte)(edge > -2f ? 2 : 1);
                // A faint grid every 20 m, like a chart.
                bool grid = (Mathf.PosMod(x, 20f) < PixelMeters || Mathf.PosMod(z, 20f) < PixelMeters);
                Color c = edge > -2f ? wall : grid ? floor.Lightened(0.06f) : floor;
                Put(k, c);
            }
        }

        Color raised = floor.Lightened(0.22f);
        foreach ((Vector2 at, float r) in Arena.Features)
            FillCircle(at, r * 0.85f, raised, raised.Darkened(0.25f));

        foreach (MapBlockers.Blocker b in MapBlockers.All)
        {
            Color fill = b.Tint.Darkened(0.12f);
            Color line = new(0.05f, 0.06f, 0.09f);
            if (b.Look == MapBlockers.Shape.Round) FillCircle(b.Centre, b.Half.X, fill, line);
            else FillBox(b, fill, line);
        }
    }

    private void FillCircle(Vector2 at, float r, Color fill, Color line)
    {
        ForBox(at.X - r, at.Y - r, at.X + r, at.Y + r, (k, p) =>
        {
            float d = p.DistanceTo(at);
            if (d > r) return;
            Put(k, d > r - PixelMeters * 1.2f ? line : fill);
        });
    }

    private void FillBox(MapBlockers.Blocker b, Color fill, Color line)
    {
        float reach = b.Bound;
        ForBox(b.Centre.X - reach, b.Centre.Y - reach, b.Centre.X + reach, b.Centre.Y + reach, (k, p) =>
        {
            Vector2 l = b.ToLocal(p);
            float ex = b.Half.X - MathF.Abs(l.X), ez = b.Half.Y - MathF.Abs(l.Y);
            if (ex < 0f || ez < 0f) return;
            Put(k, MathF.Min(ex, ez) < PixelMeters * 1.2f ? line : fill);
        });
    }

    private void ForBox(float x0, float z0, float x1, float z1, Action<int, Vector2> each)
    {
        int i0 = Math.Max(0, (int)((x0 + Extent) / PixelMeters)), i1 = Math.Min(Pixels - 1, (int)((x1 + Extent) / PixelMeters));
        int j0 = Math.Max(0, (int)((z0 + Extent) / PixelMeters)), j1 = Math.Min(Pixels - 1, (int)((z1 + Extent) / PixelMeters));
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int k = (j * Pixels + i) * 4;
                if (_base[k + 3] == 0) continue;
                each(k, new Vector2(-Extent + (i + 0.5f) * PixelMeters, -Extent + (j + 0.5f) * PixelMeters));
            }
    }

    private void Put(int k, Color c)
    {
        _base[k] = (byte)(Math.Clamp(c.R, 0f, 1f) * 255f);
        _base[k + 1] = (byte)(Math.Clamp(c.G, 0f, 1f) * 255f);
        _base[k + 2] = (byte)(Math.Clamp(c.B, 0f, 1f) * 255f);
        _base[k + 3] = 255;
    }

    /// <summary>Displayed pixel: the base map faded into the fog by how well it was seen.</summary>
    private void Compose(int pixel)
    {
        int k = pixel * 4;
        if (_base[k + 3] == 0)
        {
            _rgba[k] = _rgba[k + 1] = _rgba[k + 2] = _rgba[k + 3] = 0;
            _dirty = true;
            return;
        }
        float t = _seen[pixel] / 255f;
        // Unseen ground shows only the map's outline: the wall band at 30%
        // and a flat floor tint, never what stands on it.
        bool wallBand = _kind[pixel] == 2;
        float hint = wallBand ? 0.3f : 0.06f;
        float r0 = Mathf.Lerp(FogInk.R, wallBand ? _base[k] / 255f : _floorFlat.R, hint);
        float g0 = Mathf.Lerp(FogInk.G, wallBand ? _base[k + 1] / 255f : _floorFlat.G, hint);
        float b0 = Mathf.Lerp(FogInk.B, wallBand ? _base[k + 2] / 255f : _floorFlat.B, hint);
        _rgba[k] = (byte)(255f * Mathf.Lerp(r0, _base[k] / 255f, t));
        _rgba[k + 1] = (byte)(255f * Mathf.Lerp(g0, _base[k + 1] / 255f, t));
        _rgba[k + 2] = (byte)(255f * Mathf.Lerp(b0, _base[k + 2] / 255f, t));
        _rgba[k + 3] = (byte)(225 + 30 * t);
        _dirty = true;
    }
}
