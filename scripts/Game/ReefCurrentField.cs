using System.Collections.Generic;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>
/// Water currents that come and go on an underwater map.
///
/// A fixed number of slots each run an endless cycle: a random gap, then one
/// current somewhere in open water (telegraph, active, fade-out), then the next
/// gap. Everything about cycle k of slot i comes from a hash of the run seed,
/// the map, the phase, i and k, and its times are on the phase clock, so a
/// co-op host and its clients agree without sending anything: they share the
/// seed, build the same static map, and the host already keeps the phase clock
/// in step. Nothing per-frame goes over the wire; each machine pushes its own
/// swimmer, and remote swimmers look carried because their owners were.
///
/// Placement only trusts geometry every peer builds identically (terrain, cave,
/// rocks, ruins, the wreck); scatter props, whose density follows the graphics
/// preset, are ignored, and currents keep well clear of the seabed anyway.
/// </summary>
public sealed partial class ReefCurrentField : Node3D
{
    public static ReefCurrentField? Instance { get; private set; }

    /// <summary>Currents live at once, at most (one per slot).</summary>
    public const int Slots = 5;
    public const float MinLength = 15f, MaxLength = 40f;
    public const float MinActive = 8f, MaxActive = 20f;
    public const float MinGap = 4f, MaxGap = 14f;
    /// <summary>Clearance kept from the seabed, the cave and solids, beyond the radius.</summary>
    public const float FloorClearance = 2f;
    /// <summary>Clearance kept below the lowest wave, beyond the radius.</summary>
    public const float SurfaceClearance = 3f;

    private readonly List<ReefCurrentFlow>[] _cycles = new List<ReefCurrentFlow>[Slots];
    private readonly float[] _cycleEnds = new float[Slots];
    private readonly List<ReefCurrentFlow> _forced = new();
    private readonly List<ReefCurrentFlow> _live = new();
    private ulong _seed;
    private int _physicsFrames;
    private float _clock;

    /// <summary>Seeded cycles run (off while another self-check moves the swimmer).</summary>
    public bool RandomEnabled { get; set; } = true;
    /// <summary>Currents alive (telegraphing, pushing or fading) this frame.</summary>
    public IReadOnlyList<ReefCurrentFlow> Live => _live;
    /// <summary>Phase-clock time the field last evaluated.</summary>
    public float Clock => _clock;
    public int PlacementsTried { get; private set; }
    public int PlacementsRejected { get; private set; }

    private readonly bool _probe;
    private readonly ulong? _seedOverride;

    public ReefCurrentField() { }

    /// <summary>A silent copy for the self-check: same placement rules, no
    /// visuals, not the map's field; optionally on another seed.</summary>
    internal ReefCurrentField(ulong? seed) { _probe = true; _seedOverride = seed; }

    public override void _EnterTree() { if (!_probe) Instance = this; }
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public override void _Ready()
    {
        Name = _probe ? "ReefCurrentProbe" : "ReefCurrentField";
        string[] args = OS.GetCmdlineUserArgs();
        bool otherCheck = false;
        foreach (string arg in args)
            if ((arg.StartsWith("--check-") || arg.StartsWith("--coop-check-")) && arg != "--check-currents") otherCheck = true;
        // Self-checks that place the swimmer by hand must not be pushed by a
        // current that happens to arrive; --check-currents tests them itself.
        RandomEnabled = !otherCheck || System.Array.IndexOf(args, "--currents") >= 0;
        RunState? run = Main.Instance?.Run;
        // Not the phase number: host and client may build the map either side of it.
        _seed = Mix((run?.Seed ?? 1UL) ^ Hash(Arena.Current.Id) ^ (ulong)Arena.Current.LayoutSeed);
        if (_seedOverride is { } seed) _seed = Mix(seed ^ Hash(Arena.Current.Id) ^ (ulong)Arena.Current.LayoutSeed);
        for (int i = 0; i < Slots; i++) { _cycles[i] = new List<ReefCurrentFlow>(); _cycleEnds[i] = 0f; }
        if (_probe) { RandomEnabled = false; SetPhysicsProcess(false); return; }
        AddChild(new ReefCurrentStreams(this));
        AddChild(new ReefCurrentAudio(this));
    }

    public static float PhaseClock => Main.Instance?.Run?.PhaseSeconds ?? 0f;

    public override void _PhysicsProcess(double delta)
    {
        using var timing = Prof.Scope("Reef.currents");
        _physicsFrames++;
        _clock = PhaseClock;
        Refresh(_clock);
        // Co-op agreement test: every peer prints the same line for the same run.
        if (_physicsFrames == 6 && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--currents-fingerprint") >= 0)
            GD.Print($"CURRENTS FINGERPRINT map={Arena.Current.Id} {Fingerprint(600f)}");
    }

    /// <summary>A hash of every cycle up to <paramref name="horizon"/> seconds: where,
    /// which way, how strong and when. Equal on peers that agree.</summary>
    public string Fingerprint(float horizon)
    {
        EnsureUntil(horizon);
        ulong h = 1469598103934665603UL;
        int flows = 0;
        void Add(float v) { h ^= (ulong)System.BitConverter.SingleToInt32Bits(v); h *= 1099511628211UL; }
        foreach (List<ReefCurrentFlow> slot in _cycles)
            foreach (ReefCurrentFlow f in slot)
            {
                if (f.Born > horizon) continue;
                Add(f.Born); Add(f.ActiveSeconds); Add(f.Strength); Add(f.Radius);
                Add(f.From.X); Add(f.From.Y); Add(f.From.Z); Add(f.To.X); Add(f.To.Y); Add(f.To.Z);
                if (!f.Empty) flows++;
            }
        return $"flows={flows} hash={h:x16}";
    }

    private void Refresh(float t)
    {
        _live.Clear();
        // Placement asks the physics space; give the map's static shapes a few
        // physics frames to be registered before the first current is placed.
        if (RandomEnabled && _physicsFrames > 3)
            for (int slot = 0; slot < Slots; slot++)
            {
                if (CurrentOf(slot, t) is { } flow && flow.Alive(t)) _live.Add(flow);
            }
        for (int i = _forced.Count - 1; i >= 0; i--)
        {
            if (t >= _forced[i].Dies) { _forced.RemoveAt(i); continue; }
            if (_forced[i].Alive(t)) _live.Add(_forced[i]);
        }
    }

    /// <summary>Sum of every current's push at a point (m/s), at the field's clock.</summary>
    public static Vector3 VelocityAt(Vector3 at)
    {
        if (Instance is not { } field || !Arena.Current.Underwater) return Vector3.Zero;
        Vector3 push = Vector3.Zero;
        foreach (ReefCurrentFlow flow in field._live) push += flow.VelocityAt(at, field._clock);
        return push;
    }

    /// <summary>Place a current now (self-checks): it starts
    /// telegraphing at once and is not part of the seeded cycles.</summary>
    public ReefCurrentFlow Force(Vector3 from, Vector3 to, float radius, float strength, float activeSeconds, ReefCurrentKind kind)
    {
        var flow = new ReefCurrentFlow
        {
            Kind = kind, From = from, To = to, Radius = radius, Strength = strength,
            Born = PhaseClock, ActiveSeconds = activeSeconds,
        };
        _forced.Add(flow);
        Refresh(_clock = PhaseClock);
        return flow;
    }

    public void ClearForced() { _forced.Clear(); Refresh(_clock); }

    // ------------------------------------------------------------- schedule

    /// <summary>The cycle of <paramref name="slot"/> alive at or next after
    /// <paramref name="t"/>, generating cycles up to it. Null while the slot is
    /// in a gap whose cycle found no open water.</summary>
    public ReefCurrentFlow? CurrentOf(int slot, float t)
    {
        List<ReefCurrentFlow> cycles = _cycles[slot];
        EnsureUntil(t);
        // Usually the last one or two; the clock only moves forward except for
        // small co-op corrections, so a short backward scan is enough.
        for (int i = cycles.Count - 1; i >= 0; i--)
        {
            ReefCurrentFlow flow = cycles[i];
            if (flow.Dies <= t) return i + 1 < cycles.Count ? cycles[i + 1] : null;
            if (flow.Born <= t) return flow;
        }
        return cycles.Count > 0 ? cycles[0] : null;
    }

    /// <summary>Every cycle of a slot generated so far (the self-check reads them).</summary>
    public IReadOnlyList<ReefCurrentFlow> Cycles(int slot) => _cycles[slot];

    /// <summary>Generate cycles until every slot reaches past <paramref name="t"/>,
    /// always extending the slot that ends first. That order does not depend on
    /// when the field is asked, so a late joiner that jumps straight to minute
    /// five makes exactly the cycles a peer that played through made.</summary>
    private void EnsureUntil(float t)
    {
        while (true)
        {
            int first = 0;
            for (int i = 1; i < Slots; i++) if (_cycleEnds[i] < _cycleEnds[first]) first = i;
            if (_cycleEnds[first] > t) return;
            GenerateCycle(first);
        }
    }

    private void GenerateCycle(int slot)
    {
        List<ReefCurrentFlow> cycles = _cycles[slot];
        int cycle = cycles.Count == 0 ? 0 : cycles[^1].Cycle + 1;
        var rng = new Core.Rng(Mix(_seed ^ ((ulong)slot * 0x9E3779B97F4A7C15UL) ^ ((ulong)cycle * 0xC2B2AE3D27D4EB4FUL)));
        // The first wave is staggered so the map does not fill at once.
        float gap = cycle == 0 ? 4f + slot * 3.5f + rng.Range(0f, 5f) : rng.Range(MinGap, MaxGap);
        float born = _cycleEnds[slot] + gap;
        float active = rng.Range(MinActive, MaxActive);
        float end = born + ReefCurrentFlow.TelegraphSeconds + active + ReefCurrentFlow.FadeSeconds;
        ReefCurrentFlow? flow = Place(rng);
        if (flow is not null)
        {
            flow.Born = born; flow.ActiveSeconds = active; flow.Slot = slot; flow.Cycle = cycle;
            // Do not stack two currents on the same water.
            bool crowded = false;
            foreach (List<ReefCurrentFlow> other in _cycles)
                foreach (ReefCurrentFlow live in other)
                    if (!live.Empty && live.Born < end && born < live.Dies && Segments(live, flow) < live.Radius + flow.Radius + 4f)
                        crowded = true;
            if (crowded) { flow = null; PlacementsRejected++; }
        }
        // A cycle that found no water is a placeholder with no life: the slot rests.
        cycles.Add(flow ?? new ReefCurrentFlow { Born = born, ActiveSeconds = active, Slot = slot, Cycle = cycle, Radius = 0f, From = Vector3.Zero, To = Vector3.Zero });
        if (flow is null) cycles[^1].Strength = 0f;
        _cycleEnds[slot] = end;
    }

    private static float Segments(ReefCurrentFlow a, ReefCurrentFlow b)
    {
        float best = float.MaxValue;
        for (int i = 0; i <= 8; i++)
        {
            Vector3 p = a.From.Lerp(a.To, i / 8f);
            Vector3 line = b.To - b.From;
            float s = Mathf.Clamp((p - b.From).Dot(line) / Mathf.Max(0.01f, line.LengthSquared()), 0f, 1f);
            best = Mathf.Min(best, p.DistanceTo(b.From + line * s));
        }
        return best;
    }

    // ------------------------------------------------------------ placement

    private ReefCurrentFlow? Place(Core.Rng rng)
    {
        float roll = rng.Float();
        ReefCurrentKind kind = roll < 0.5f ? ReefCurrentKind.Horizontal : roll < 0.7f ? ReefCurrentKind.Diagonal
            : roll < 0.87f ? ReefCurrentKind.Updraft : ReefCurrentKind.Downdraft;
        for (int attempt = 0; attempt < 14; attempt++)
        {
            PlacementsTried++;
            bool vertical = kind is ReefCurrentKind.Updraft or ReefCurrentKind.Downdraft;
            float radius = vertical ? rng.Range(3f, 4.5f) : rng.Range(3.5f, 5.5f);
            float length = rng.Range(MinLength, MaxLength);
            float reach = Arena.Radius - 32f;
            float angle = rng.Range(0f, Mathf.Tau), distance = Mathf.Sqrt(rng.Float()) * reach;
            var centre = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            float floor = ReefCavePath.Floor(centre) + radius + FloorClearance;
            float ceiling = WaterSurfaceHeight.BaseHeight - 2.4f - radius - SurfaceClearance;
            Vector3 direction;
            if (vertical)
            {
                length = Mathf.Min(length, ceiling - floor - 2f);
                if (length < MinLength) { PlacementsRejected++; continue; }
                direction = kind == ReefCurrentKind.Updraft ? Vector3.Up : Vector3.Down;
                centre.Y = rng.Range(floor + length * 0.5f, ceiling - length * 0.5f);
            }
            else
            {
                float yaw = rng.Range(0f, Mathf.Tau);
                float pitch = kind == ReefCurrentKind.Diagonal ? rng.Range(25f, 45f) * (rng.Chance(0.5f) ? 1f : -1f) : rng.Range(-6f, 6f);
                pitch = Mathf.DegToRad(pitch);
                direction = new Vector3(Mathf.Cos(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Sin(yaw) * Mathf.Cos(pitch));
                float rise = Mathf.Abs(direction.Y) * length * 0.5f;
                if (ceiling - floor < rise * 2f + 1f) { PlacementsRejected++; continue; }
                centre.Y = rng.Range(floor + rise, ceiling - rise);
            }
            var flow = new ReefCurrentFlow
            {
                Kind = kind, Radius = radius,
                From = centre - direction * length * 0.5f, To = centre + direction * length * 0.5f,
                Strength = kind switch
                {
                    ReefCurrentKind.Updraft => rng.Range(6f, 7.5f),
                    ReefCurrentKind.Downdraft => rng.Range(5f, 6f),
                    _ => rng.Range(6f, 7.5f),
                },
            };
            if (OpenWater(this, flow, out _)) return flow;
            PlacementsRejected++;
        }
        return null;
    }

    private static readonly HashSet<string> IgnoredBodies = new()
    {
        // Built from the graphics preset's prop density, so peers may differ;
        // all of them sit on the seabed, under the floor clearance anyway.
        "PropColliders", "GroundedReefFoundations", "SeabedSupport", "AnchoredFoundation",
    };

    /// <summary>
    /// True when the whole capsule is open water: inside the map away from the
    /// cliffs, above the seabed and the cave, below the lowest wave, and not
    /// overlapping any solid every machine builds the same way. Tested every
    /// 2.5 m along the axis, with the seabed also tested a radius to each side.
    /// </summary>
    public static bool OpenWater(Node3D node, ReefCurrentFlow flow, out string why)
    {
        why = "";
        PhysicsDirectSpaceState3D space = node.GetWorld3D().DirectSpaceState;
        var sphere = new SphereShape3D { Radius = flow.Radius * 0.85f + 0.5f };
        var query = new PhysicsShapeQueryParameters3D { Shape = sphere, CollisionMask = 1u, CollideWithAreas = false, CollideWithBodies = true };
        int steps = Mathf.Max(2, Mathf.CeilToInt(flow.Length / 2.5f));
        float lowestWave = WaterSurfaceHeight.BaseHeight - 2.4f;
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = flow.From.Lerp(flow.To, i / (float)steps);
            if (new Vector2(p.X, p.Z).Length() > Arena.Radius - 18f - flow.Radius) { why = $"near the cliffs at {p}"; return false; }
            if (p.Y + flow.Radius > lowestWave - SurfaceClearance + 0.01f) { why = $"too close to the surface at {p}"; return false; }
            for (int k = 0; k < 5; k++)
            {
                Vector3 probe = k == 0 ? p : p + new Vector3(k == 1 ? 1 : k == 2 ? -1 : 0, 0, k == 3 ? 1 : k == 4 ? -1 : 0) * flow.Radius;
                if (p.Y - flow.Radius < ReefCavePath.Floor(probe) + FloorClearance - 0.01f) { why = $"through the seabed at {p}"; return false; }
            }
            if (ReefCavePath.Contains(p, flow.Radius + 4f)) { why = $"in the cave at {p}"; return false; }
            query.Transform = new Transform3D(Basis.Identity, p);
            foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, 32))
                if (hit["collider"].AsGodotObject() is StaticBody3D body && !IgnoredBodies.Contains(body.Name))
                { why = $"inside {body.Name} at {p}"; return false; }
        }
        return true;
    }

    // ----------------------------------------------------------------- hash

    private static ulong Hash(string text)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in text) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
