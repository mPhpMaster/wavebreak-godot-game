using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Game;

/// <summary>
/// The big solid shapes of the current map (cliffs, buildings, rock walls),
/// as oriented boxes on the floor plane, for the systems that have to reason
/// about them without a physics query:
///
/// - <b>enemy steering</b> (<see cref="Detour"/>): enemies steer straight at the
///   player with no pathfinding, which is fine around a pillar and hopeless
///   around a 30 m building. When the straight line to the target crosses a
///   blocker, the enemy walks to the nearer silhouette corner of that box
///   instead; from there the line clears or the next corner takes over. That
///   is a visibility-graph walk one box deep, which is all a map of separate
///   convex blockers needs.
/// - <b>placement</b> (<see cref="Inside"/>): the spawn ring, chests, pots and
///   stations reject points inside a blocker, so nothing spawns in a wall or
///   on a roof.
/// - <b>the maps</b> (minimap, Tab big map): the shapes are drawn as the map's
///   landmarks.
///
/// Registered by <see cref="Arena"/> while it builds (only maps with a
/// <see cref="Data.StageDef.Layout"/> have any) and cleared when that arena
/// leaves the tree, so every other map runs with an empty list and pays one
/// count check per enemy.
/// </summary>
public static class MapBlockers
{
    public enum Shape { Box, Round }

    /// <summary>One blocker: centre, half extents along its own axes, yaw (the
    /// local X axis is (cos, sin) in world XZ), top height, and how to draw it.</summary>
    public readonly struct Blocker
    {
        public readonly Vector2 Centre;
        public readonly Vector2 Half;
        public readonly float Cos, Sin, Top, Bound;
        public readonly Shape Look;
        public readonly Color Tint;

        public Blocker(Vector2 centre, Vector2 half, float yaw, float top, Shape look, Color tint)
        {
            Centre = centre;
            Half = half;
            Cos = MathF.Cos(yaw);
            Sin = MathF.Sin(yaw);
            Top = top;
            Bound = half.Length();
            Look = look;
            Tint = tint;
        }

        public Vector2 ToLocal(Vector2 p)
        {
            Vector2 d = p - Centre;
            return new Vector2(d.X * Cos + d.Y * Sin, -d.X * Sin + d.Y * Cos);
        }

        public Vector2 ToWorld(Vector2 l) => Centre + new Vector2(l.X * Cos - l.Y * Sin, l.X * Sin + l.Y * Cos);

        /// <summary>The four floor corners in world XZ, grown by <paramref name="grow"/>.</summary>
        public Vector2[] Corners(float grow = 0f)
        {
            float x = Half.X + grow, z = Half.Y + grow;
            return new[] { ToWorld(new Vector2(-x, -z)), ToWorld(new Vector2(x, -z)), ToWorld(new Vector2(x, z)), ToWorld(new Vector2(-x, z)) };
        }
    }

    /// <summary>A named region of the map, for the big map's labels and the
    /// minimap's "you are here" line.</summary>
    public readonly record struct Zone(string Name, Vector2 Centre, float Radius);

    private static readonly List<Blocker> _all = new();
    private static readonly List<Zone> _zones = new();
    private static object? _owner;

    public static IReadOnlyList<Blocker> All => _all;
    public static IReadOnlyList<Zone> Zones => _zones;
    public static int Count => _all.Count;

    /// <summary>Test switch (--check-boundary's baseline): enemies ignore the blockers.</summary>
    public static bool SteeringOff { get; set; }

    /// <summary>Bumped on every change, so map views know to redraw their base.</summary>
    public static int Version { get; private set; }

    /// <summary>Clearance kept between an enemy's centre and a blocker's face
    /// when testing whether the straight line is blocked.</summary>
    private const float Margin = 1.4f;

    /// <summary>How far past the corner the detour waypoint sits.</summary>
    private const float CornerOut = 1.6f;

    public static void Begin(object owner)
    {
        _owner = owner;
        _all.Clear();
        _zones.Clear();
        Version++;
    }

    /// <summary>Clear, if <paramref name="owner"/> is the arena that registered them.</summary>
    public static void End(object owner)
    {
        if (!ReferenceEquals(owner, _owner)) return;
        _owner = null;
        _all.Clear();
        _zones.Clear();
        Version++;
    }

    public static void Add(Blocker b)
    {
        _all.Add(b);
        Version++;
    }

    public static void AddZone(Zone z) => _zones.Add(z);

    /// <summary>True when the floor point is inside a blocker grown by
    /// <paramref name="margin"/> (and below its top).</summary>
    public static bool Inside(Vector3 p, float margin = 0f)
    {
        if (_all.Count == 0) return false;
        var flat = new Vector2(p.X, p.Z);
        for (int i = 0; i < _all.Count; i++)
        {
            Blocker b = _all[i];
            if (flat.DistanceSquaredTo(b.Centre) > (b.Bound + margin) * (b.Bound + margin)) continue;
            Vector2 l = b.ToLocal(flat);
            if (MathF.Abs(l.X) < b.Half.X + margin && MathF.Abs(l.Y) < b.Half.Y + margin && p.Y < b.Top + 0.5f) return true;
        }
        return false;
    }

    /// <summary>The zone the point is in, or null.</summary>
    public static Zone? ZoneAt(Vector3 p)
    {
        var flat = new Vector2(p.X, p.Z);
        Zone? best = null;
        float bestD = float.MaxValue;
        foreach (Zone z in _zones)
        {
            float d = flat.DistanceTo(z.Centre);
            if (d < z.Radius && d < bestD) { best = z; bestD = d; }
        }
        return best;
    }

    /// <summary>
    /// Where to walk to get from <paramref name="from"/> toward
    /// <paramref name="to"/> around the first blocker in the way: a waypoint
    /// past one of that box's corners, or null when the straight line is clear.
    /// <paramref name="memo"/> keeps the chosen blocker and corner between
    /// calls, so an enemy halfway round a building does not flip sides.
    /// </summary>
    public static Vector3? Detour(Vector3 from, Vector3 to, ref int memo)
    {
        if (_all.Count == 0) return null;
        var p = new Vector2(from.X, from.Z);
        var t = new Vector2(to.X, to.Z);
        int hit = FirstBlocking(p, t, out _);
        if (hit < 0) { memo = -1; return null; }

        Blocker b = _all[hit];
        Vector2[] corners = b.Corners(Margin + CornerOut);
        Vector2 lp = b.ToLocal(p);
        bool inside = MathF.Abs(lp.X) < b.Half.X + Margin && MathF.Abs(lp.Y) < b.Half.Y + Margin;

        // The two silhouette corners as seen from p: the extreme angles about
        // the direction to the box centre. From inside the margin (pushed in by
        // the crowd) every corner is a candidate.
        Vector2 toC = b.Centre - p;
        int lo = -1, hi = -1;
        float loA = float.MaxValue, hiA = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector2 d = corners[i] - p;
            if (d.LengthSquared() < 1.0f) continue;          // standing on it: look past it
            float a = MathF.Atan2(toC.X * d.Y - toC.Y * d.X, toC.X * d.X + toC.Y * d.Y);
            if (a < loA) { loA = a; lo = i; }
            if (a > hiA) { hiA = a; hi = i; }
        }

        int pick = -1;
        float pickCost = float.MaxValue;
        int memoCorner = memo >= 0 && memo / 4 == hit ? memo % 4 : -1;
        float memoCost = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            if (!inside && i != lo && i != hi) continue;
            Vector2 c = corners[i];
            float cost = p.DistanceTo(c) + c.DistanceTo(t);
            // A corner tucked into the next blocker is a dead end.
            if (InsideOther(c, hit)) cost += 1000f;
            if (i == memoCorner) memoCost = cost;
            if (cost < pickCost) { pickCost = cost; pick = i; }
        }
        if (pick < 0) { memo = -1; return null; }
        // Hysteresis: keep the side already taken unless the other is clearly shorter.
        if (memoCorner >= 0 && memoCost < 1000f && pickCost > memoCost * 0.8f) pick = memoCorner;

        memo = hit * 4 + pick;
        Vector2 w = corners[pick];
        return new Vector3(w.X, from.Y, w.Y);
    }

    /// <summary>
    /// A move direction that does not run into a blocker: <paramref name="dir"/>
    /// itself when the next 10 m along it are clear, else the direction to the
    /// detour corner. For the autopilot, which thinks in directions.
    /// </summary>
    public static Vector3 Deflect(Vector3 from, Vector3 dir, ref int memo, float look = 10f)
    {
        if (_all.Count == 0 || dir.LengthSquared() < 1e-6f) return dir;
        Vector3 flatDir = new Vector3(dir.X, 0f, dir.Z).Normalized();
        if (Detour(from, from + flatDir * look, ref memo) is not { } w) return dir;
        Vector3 d = w - from;
        d.Y = 0f;
        return d.LengthSquared() > 1e-4f ? d.Normalized() * dir.Length() : dir;
    }

    /// <summary>Index of the blocker the segment enters first (grown by the
    /// margin), or -1.</summary>
    public static int FirstBlocking(Vector2 p, Vector2 t, out float enter)
    {
        enter = float.MaxValue;
        int best = -1;
        Vector2 seg = t - p;
        float segLenSq = seg.LengthSquared();
        for (int i = 0; i < _all.Count; i++)
        {
            Blocker b = _all[i];
            // Cheap reject: the box's bounding circle is nowhere near the segment.
            float reach = b.Bound + Margin;
            Vector2 toC = b.Centre - p;
            float u = segLenSq > 1e-6f ? Mathf.Clamp(toC.Dot(seg) / segLenSq, 0f, 1f) : 0f;
            if ((p + seg * u).DistanceSquaredTo(b.Centre) > reach * reach) continue;

            // An end already inside the margin (an enemy pressed against the
            // face, or a player hugging it) would make every line "blocked";
            // test such a pair against the bare box instead.
            float grow = InBox(b, p, Margin) || InBox(b, t, Margin) ? 0.25f : Margin;
            if (SegmentEnters(b, p, t, grow, out float tEnter) && tEnter < enter)
            {
                enter = tEnter;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Liang-Barsky clip of the segment against the grown box, in the box frame.</summary>
    private static bool SegmentEnters(in Blocker b, Vector2 p0, Vector2 p1, float grow, out float tEnter)
    {
        Vector2 a = b.ToLocal(p0), c = b.ToLocal(p1);
        Vector2 d = c - a;
        float t0 = 0f, t1 = 1f;
        float hx = b.Half.X + grow, hz = b.Half.Y + grow;
        tEnter = 0f;
        if (!Clip(-d.X, a.X + hx, ref t0, ref t1)) return false;
        if (!Clip(d.X, hx - a.X, ref t0, ref t1)) return false;
        if (!Clip(-d.Y, a.Y + hz, ref t0, ref t1)) return false;
        if (!Clip(d.Y, hz - a.Y, ref t0, ref t1)) return false;
        tEnter = t0;
        return true;
    }

    private static bool Clip(float den, float num, ref float t0, ref float t1)
    {
        if (MathF.Abs(den) < 1e-7f) return num >= 0f;
        float r = num / den;
        if (den < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
        else { if (r < t0) return false; if (r < t1) t1 = r; }
        return true;
    }

    private static bool InBox(in Blocker b, Vector2 p, float grow)
    {
        Vector2 l = b.ToLocal(p);
        return MathF.Abs(l.X) < b.Half.X + grow && MathF.Abs(l.Y) < b.Half.Y + grow;
    }

    private static bool InsideOther(Vector2 p, int except)
    {
        for (int i = 0; i < _all.Count; i++)
        {
            if (i == except) continue;
            Blocker b = _all[i];
            if (p.DistanceSquaredTo(b.Centre) > (b.Bound + 1f) * (b.Bound + 1f)) continue;
            Vector2 l = b.ToLocal(p);
            if (MathF.Abs(l.X) < b.Half.X + 0.6f && MathF.Abs(l.Y) < b.Half.Y + 0.6f) return true;
        }
        return false;
    }
}
