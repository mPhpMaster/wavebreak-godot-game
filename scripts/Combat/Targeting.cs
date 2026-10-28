using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat;



/// <summary>
/// The set of things weapons are allowed to shoot at, and the queries they ask
/// about it.
///
/// Snapshotted once per frame into one managed list and indexed with a spatial
/// hash. Area queries are the hot path here — every projectile in flight asks
/// "what am I touching" every frame — and five hundred projectiles against five
/// hundred enemies is a quarter of a million distance tests a frame without an
/// index.
///
/// Nearest-target queries are handed straight to <see cref="EnemyManager"/>
/// instead. It already keeps a finer neighbour grid for its own steering, and
/// duplicating that here would mean maintaining two indexes over the same data
/// and picking the wrong one to trust the day they disagree.
///
/// Every query this class answers itself is on the XZ plane. Targets differ in
/// height and in where their origin sits, and a hit test that cares about that is
/// one that silently stops working when someone changes a capsule.
/// </summary>
public static class Targeting
{
    /// <summary>Radius assumed for a target when testing overlap. Targets expose
    /// a position through <see cref="IDamageable"/> and nothing else, so hit tests
    /// add this to their own radius rather than pretending enemies are points.</summary>
    public static float AssumedTargetRadius { get; set; } = 0.5f;

    /// <summary>Override the target source. Set this and the enemy manager is not
    /// consulted; the seam exists so headless tests can drive combat with a list
    /// of fakes and no scene tree at all.</summary>
    public static Func<List<IDamageable>, int>? Source { get; set; }

    /// <summary>
    /// Told about every area a weapon tests: centre, radius, and for a swing its
    /// heading and the cosine of its half-arc (-1 for a full circle). Things that
    /// weapons should break but never aim at (pots, crates) listen here instead of
    /// joining the target list, so no weapon can ever lock on to one.
    /// </summary>
    public static Action<Vector3, float, Vector3, float>? AreaProbe { get; set; }

    /// <summary>Below this many targets a linear scan beats the index, and
    /// building buckets is pure overhead.</summary>
    private const int GridThreshold = 24;

    /// <summary>Cell edge in metres. Around the width of a blast, so a typical
    /// area query touches a handful of cells rather than dozens.</summary>
    private const float CellSize = 4f;

    private static readonly List<IDamageable> LiveTargets = new(512);
    private static readonly Dictionary<long, List<int>> Cells = new();
    private static readonly Stack<List<int>> CellPool = new();

    private static ulong _frame = ulong.MaxValue;
    private static bool _gridBuilt;
    private static bool _useGrid;

    /// <summary>Everything alive this frame. Read-only; the list is rebuilt in
    /// place, so callers must not hold on to it across frames.</summary>
    public static IReadOnlyList<IDamageable> Live
    {
        get { EnsureFresh(); return LiveTargets; }
    }

    // --------------------------------------------------------------- refresh

    /// <summary>Rebuild the target set. Called once per frame by
    /// <see cref="WeaponSystem.Tick"/>, before anything queries it.</summary>
    public static int Revision { get; private set; }
    public static void Refresh()
    {
        Revision++;
        LiveTargets.Clear();
        ReleaseCells();
        _gridBuilt = false;
        _useGrid = false;
        _frame = Engine.GetProcessFrames();

        if (Source is not null)
        {
            Source(LiveTargets);
            return;
        }

        EnemyManager? manager = Manager;
        if (manager is null) return;

        // AliveList over the Alive iterator: this runs every frame and the
        // iterator allocates. The list can hold enemies that died earlier in the
        // frame and have not been swept, which is what the IsAlive test is for.
        IReadOnlyList<Enemy> alive = manager.AliveList;
        for (int i = 0; i < alive.Count; i++)
        {
            Enemy enemy = alive[i];
            if (enemy.IsAlive) LiveTargets.Add(enemy);
        }
    }

    private static void EnsureFresh()
    {
        if (Engine.GetProcessFrames() != _frame) Refresh();
    }

    /// <summary>Forget everything. Call when a run ends so the next one does not
    /// inherit a handle to a freed manager.</summary>
    public static void Reset()
    {
        Source = null;
        LiveTargets.Clear();
        ReleaseCells();
        _frame = ulong.MaxValue;
        _gridBuilt = false;
        _useGrid = false;
    }

    private static EnemyManager? Manager
    {
        get
        {
            EnemyManager? m = EnemyManager.Instance;
            return m is not null && GodotObject.IsInstanceValid(m) ? m : null;
        }
    }

    // ---------------------------------------------------------------- queries

    /// <summary>
    /// Live targets within <paramref name="radius"/> of a point, written into
    /// <paramref name="into"/>. The list is cleared first and reused, so pass a
    /// buffer rather than allocating one per call.
    /// </summary>
    /// <returns>How many were found.</returns>
    public static int Overlap(Vector3 centre, float radius, List<IDamageable> into)
    {
        AreaProbe?.Invoke(centre, radius, Vector3.Zero, -1f);
        return OverlapTargets(centre, radius, into);
    }

    private static int OverlapTargets(Vector3 centre, float radius, List<IDamageable> into)
    {
        EnsureFresh();
        EnsureGrid();
        into.Clear();

        float reach = radius + AssumedTargetRadius;
        float reachSq = reach * reach;

        if (!_useGrid)
        {
            for (int i = 0; i < LiveTargets.Count; i++)
                AddIfInside(LiveTargets[i], centre, reachSq, into);
            return into.Count;
        }

        int x0 = CellOf(centre.X - reach), x1 = CellOf(centre.X + reach);
        int z0 = CellOf(centre.Z - reach), z1 = CellOf(centre.Z + reach);

        for (int x = x0; x <= x1; x++)
        {
            for (int z = z0; z <= z1; z++)
            {
                if (!Cells.TryGetValue(KeyOf(x, z), out List<int>? bucket)) continue;
                for (int i = 0; i < bucket.Count; i++)
                    AddIfInside(LiveTargets[bucket[i]], centre, reachSq, into);
            }
        }

        return into.Count;
    }

    /// <summary>
    /// Live targets inside a wedge: within <paramref name="range"/> of the origin
    /// and within <paramref name="halfAngleDegrees"/> of the heading. Anything
    /// standing on top of the origin counts, because a swing cannot miss a target
    /// with no direction to it.
    /// </summary>
    public static int Cone(Vector3 origin, Vector3 heading, float range, float halfAngleDegrees, List<IDamageable> into)
    {
        Vector3 forward = Projectile.FlatDirection(heading, Vector3.Forward);
        float threshold = Mathf.Cos(Mathf.DegToRad(Mathf.Clamp(halfAngleDegrees, 0f, 180f)));
        AreaProbe?.Invoke(origin, range, forward, threshold);

        OverlapTargets(origin, range, into);
        if (into.Count == 0) return 0;

        for (int i = into.Count - 1; i >= 0; i--)
        {
            Vector3 to = into[i].Position - origin;
            to.Y = 0f;

            if (to.LengthSquared() < 1e-6f) continue;
            if (to.Normalized().Dot(forward) >= threshold) continue;

            into[i] = into[^1];
            into.RemoveAt(into.Count - 1);
        }

        return into.Count;
    }

    /// <summary>
    /// The closest live target within <paramref name="maxRange"/>, or null.
    ///
    /// Delegated to the enemy manager's neighbour grid, which is finer than the
    /// one here and is rebuilt for steering anyway. Homing projectiles call this
    /// every frame, each, so it is worth going to the index that already exists
    /// rather than maintaining a second one. Falls back to a scan of the frame's
    /// snapshot when there is no manager — the case where a test has installed
    /// its own <see cref="Source"/>, and where the counts are small.
    /// </summary>
    public static IDamageable? Nearest(Vector3 from, float maxRange)
    {
        if (Source is null && !Game.Arena.Current.Underwater && !Game.Arena.IsCity && Manager is { } manager)
            return manager.NearestTo(from, maxRange + AssumedTargetRadius);

        EnsureFresh();

        float bestSq = (maxRange + AssumedTargetRadius) * (maxRange + AssumedTargetRadius);
        IDamageable? best = null;

        for (int i = 0; i < LiveTargets.Count; i++)
            Consider(LiveTargets[i], from, ref bestSq, ref best);

        return best;
    }

    private static void AddIfInside(IDamageable target, Vector3 centre, float reachSq, List<IDamageable> into)
    {
        if (!target.IsAlive) return;

        Vector3 d = target.Position - centre;
        if (!Game.Arena.Current.Underwater && !Game.Arena.IsCity) d.Y = 0f;
        if (d.LengthSquared() <= reachSq) into.Add(target);
    }

    private static void Consider(IDamageable target, Vector3 from, ref float bestSq, ref IDamageable? best)
    {
        if (!target.IsAlive) return;

        Vector3 d = target.Position - from;
        if (!Game.Arena.Current.Underwater && !Game.Arena.IsCity) d.Y = 0f;

        float sq = d.LengthSquared();
        if (sq >= bestSq) return;

        bestSq = sq;
        best = target;
    }

    // ------------------------------------------------------------------ index

    private static void EnsureGrid()
    {
        if (_gridBuilt) return;
        _gridBuilt = true;

        _useGrid = LiveTargets.Count >= GridThreshold;
        if (!_useGrid) return;

        for (int i = 0; i < LiveTargets.Count; i++)
        {
            Vector3 p = LiveTargets[i].Position;
            long key = KeyOf(CellOf(p.X), CellOf(p.Z));

            if (!Cells.TryGetValue(key, out List<int>? bucket))
            {
                bucket = CellPool.Count > 0 ? CellPool.Pop() : new List<int>(8);
                Cells[key] = bucket;
            }

            bucket.Add(i);
        }
    }

    private static void ReleaseCells()
    {
        foreach (KeyValuePair<long, List<int>> entry in Cells)
        {
            entry.Value.Clear();
            CellPool.Push(entry.Value);
        }
        Cells.Clear();
    }

    private static int CellOf(float v) => Mathf.FloorToInt(v / CellSize);

    private static long KeyOf(int x, int z) => ((long)x << 32) | (uint)z;

}

