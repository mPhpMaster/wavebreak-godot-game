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
    public static void Refresh()
    {
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
        Overlap(origin, range, into);
        if (into.Count == 0) return 0;

        Vector3 forward = Projectile.FlatDirection(heading, Vector3.Forward);
        float threshold = Mathf.Cos(Mathf.DegToRad(Mathf.Clamp(halfAngleDegrees, 0f, 180f)));

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
        if (Source is null && Manager is { } manager)
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
        d.Y = 0f;
        if (d.LengthSquared() <= reachSq) into.Add(target);
    }

    private static void Consider(IDamageable target, Vector3 from, ref float bestSq, ref IDamageable? best)
    {
        if (!target.IsAlive) return;

        Vector3 d = target.Position - from;
        d.Y = 0f;

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

/// <summary>
/// The player's weapons: what they own, when each one goes off, and the hit
/// reporting that everything downstream of a hit listens to.
///
/// A <see cref="Node"/> rather than a plain object because the scene root builds
/// it with the rest of the world and because pooled projectiles need somewhere in
/// the tree to live — and specifically a plain <c>Node</c> rather than a
/// <see cref="Node3D"/>, since a transform-less parent leaves its 3D children in
/// world space, which is exactly what a projectile wants and what parenting them
/// to the player would ruin.
///
/// It does not run its own <c>_Process</c>. The scene root ticks it in a defined
/// order, after enemies have moved, so weapons fire at where things actually are
/// rather than at where they were on whichever frame the scene tree happened to
/// visit them.
/// </summary>
public sealed partial class WeaponSystem : Node
{
    /// <summary>How many distinct weapons the player may carry. The cap is what
    /// makes a level-up a decision: without one, every offer is a yes.</summary>
    public const int MaxWeapons = 6;

    /// <summary>Ceiling on shots in flight. Reaching it drops new shots rather
    /// than the frame rate.</summary>
    public const int ProjectileCapacity = 512;

    /// <summary>Publish <see cref="EnemyDamaged"/> for every landed hit. On by
    /// default: lifesteal, damage meters and on-hit effects all read it, and the
    /// target itself has already written the real damage back into the info by
    /// the time it goes out.</summary>
    public static bool PublishHitEvents { get; set; } = true;

    /// <summary>Publish a floating number per landed hit. Split from
    /// <see cref="PublishHitEvents"/> so the number can be silenced — for a
    /// performance test, or if the HUD would rather draw them itself — without
    /// also silencing the mechanics.</summary>
    public static bool PublishDamageNumbers { get; set; } = true;

    /// <summary>Below this, a hit is not worth a number on screen.</summary>
    private const float PopupThreshold = 0.5f;

    private static readonly Color CritColor = new(1.00f, 0.85f, 0.30f);

    private readonly List<WeaponRuntime> _weapons = new(MaxWeapons);
    private readonly StatSheet _standaloneStats = new();

    private Player? _player;
    private StatSheet? _stats;
    private Rng? _rng;

    // ------------------------------------------------------------------ state

    /// <summary>Everything the player is carrying, in acquisition order.</summary>
    public IReadOnlyList<WeaponRuntime> Weapons => _weapons;

    /// <summary>True when no new weapon can be taken — only upgrades.</summary>
    public bool IsFull => _weapons.Count >= MaxWeapons;

    /// <summary>The player node, when there is one. Auras parent themselves to it.</summary>
    public Node3D? Body => _player;

    /// <summary>
    /// The sheet weapons are resolved against — the player's, or a private empty
    /// one when there is no player. The fallback exists so a headless test can
    /// tick weapons without building an avatar, and so a frame between runs
    /// cannot null-reference.
    /// </summary>
    public StatSheet Stats => _stats ?? _standaloneStats;

    /// <summary>
    /// The random source crit rolls come from. Assign the run's seeded
    /// <see cref="Rng"/> so a replayed seed crits on the same shots.
    ///
    /// Defaulted rather than reached for: combat resolving this out of the scene
    /// root would couple the whole layer to the composition root for one number,
    /// and it would stop building — and stop being testable — on its own.
    /// </summary>
    public Rng Rng
    {
        get => _rng ??= new Rng(Time.GetTicksUsec());
        set => _rng = value;
    }

    /// <summary>Where shots come from: mid-torso, not the feet.</summary>
    public Vector3 MuzzlePosition { get; private set; }

    /// <summary>Facing, flattened onto the ground plane. Tracks the player each
    /// tick; assign it directly when driving weapons without one.</summary>
    public Vector3 AimDirection { get; set; } = Vector3.Forward;

    /// <summary>Shots fired this run. Cheap, and the results screen wants it.</summary>
    public int ShotsFired { get; private set; }

    public WeaponSystem()
    {
        Name = "WeaponSystem";

        // Ticked by the scene root in a defined order; see the class note.
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    public override void _Ready() => Projectile.Initialize(this, ProjectileCapacity);

    public override void _ExitTree()
    {
        Clear();
        Projectile.Shutdown();
        Targeting.Reset();
    }

    // ------------------------------------------------------------- ownership

    /// <summary>
    /// Take a weapon, or upgrade it if it is already owned.
    ///
    /// Levelling instead of duplicating is what stops the offer pool turning into
    /// six copies of the same gun, and it is why the weapon cap is meaningful.
    /// A weapon that is owned at maximum level, or a new one when the slots are
    /// full, is silently ignored — <see cref="CanAdd"/> is how a caller avoids
    /// offering either.
    /// </summary>
    public void Add(WeaponDef def)
    {
        if (def is null || string.IsNullOrEmpty(def.Id)) return;

        WeaponRuntime? owned = Find(def.Id);
        if (owned is not null)
        {
            if (owned.LevelUp())
                EventBus.Publish(new WeaponAcquired(def.Id, owned.Level));
            return;
        }

        if (IsFull) return;

        WeaponRuntime runtime = new(def);
        _weapons.Add(runtime);
        EventBus.Publish(new WeaponAcquired(def.Id, runtime.Level));
    }

    /// <summary>True when <see cref="Add"/> would actually change something:
    /// a free slot for a new weapon, or headroom on an owned one.</summary>
    public bool CanAdd(WeaponDef def)
    {
        if (def is null || string.IsNullOrEmpty(def.Id)) return false;

        WeaponRuntime? owned = Find(def.Id);
        return owned is null ? !IsFull : owned.CanLevel;
    }

    public bool Has(string id) => Find(id) is not null;

    /// <summary>Current level of a weapon, or 0 when it is not owned.</summary>
    public int LevelOf(string id) => Find(id)?.Level ?? 0;

    /// <summary>Drop every weapon and everything they put in the world.</summary>
    public void Clear()
    {
        for (int i = 0; i < _weapons.Count; i++) _weapons[i].Dispose();
        _weapons.Clear();
    }

    private WeaponRuntime? Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        for (int i = 0; i < _weapons.Count; i++)
            if (_weapons[i].Def.Id == id) return _weapons[i];

        return null;
    }

    // ------------------------------------------------------------------ tick

    /// <summary>Advance every weapon and every shot in flight by one frame.</summary>
    public void Tick(float dt)
    {
        if (dt <= 0f) return;

        Resolve();
        Targeting.Refresh();

        for (int i = 0; i < _weapons.Count; i++)
            _weapons[i].Tick(dt, this);

        Projectile.UpdateAll(dt);
    }

    /// <summary>Re-reads the player once per tick. Doing it per shot would mean an
    /// interop call and a validity check for every projectile in a volley.</summary>
    private void Resolve()
    {
        Player? player = Player.Instance;

        if (player is null || !GodotObject.IsInstanceValid(player))
        {
            _player = null;
            _stats = null;
            return;
        }

        _player = player;
        _stats = player.Stats;
        MuzzlePosition = player.CenterPosition;

        Vector3 aim = player.AimDirection;
        aim.Y = 0f;
        if (aim.LengthSquared() > 1e-6f) AimDirection = aim.Normalized();
    }

    // ---------------------------------------------------------------- firing

    /// <summary>
    /// The direction a weapon should fire in: at the nearest target inside
    /// <paramref name="range"/>, or straight ahead when there is nothing there.
    ///
    /// Firing regardless is deliberate. Weapons that hold fire until something is
    /// in range feel broken during the quiet seconds between waves, and a shot
    /// into empty space costs nothing.
    /// </summary>
    internal Vector3 AimAt(Vector3 origin, float range)
    {
        IDamageable? target = Targeting.Nearest(origin, range);
        if (target is not null)
        {
            Vector3 to = target.Position - origin;
            to.Y = 0f;
            if (to.LengthSquared() > 1e-6f) return to.Normalized();
        }

        Vector3 aim = AimDirection;
        aim.Y = 0f;
        return aim.LengthSquared() > 1e-6f ? aim.Normalized() : Vector3.Forward;
    }

    /// <summary>
    /// Builds the hit one shot carries.
    ///
    /// The crit roll happens here, once per shot, before the hit is cloned onto
    /// each target it reaches — so a piercing shot or a blast crits as a whole.
    /// Crit chance is the player's plus the weapon's own, which is what lets a
    /// weapon be characterised as "crits a lot" without an item to say so.
    /// </summary>
    internal DamageInfo BuildDamage(WeaponDef def, int level)
    {
        StatSheet stats = Stats;

        float amount = def.DamageAtLevel(level) * stats.GetMultiplier(Stat.Damage);
        bool crit = Rng.Chance(stats.Get(Stat.CritChance) + def.CritChance);
        if (crit) amount *= 1f + stats.Get(Stat.CritDamage);

        return new DamageInfo
        {
            Amount = amount,
            Element = def.Element,
            Crit = crit,
            Knockback = def.Knockback * stats.Get(Stat.Knockback),
            ProcCoefficient = def.ProcCoefficient,
            Source = def.Id,
        };
    }

    internal void NoteShots(int count) => ShotsFired += count;

    /// <summary>
    /// Announce a hit that landed.
    ///
    /// Called after the target has had <see cref="IDamageable.TakeDamage"/>, so
    /// <paramref name="info"/> holds what actually happened rather than what was
    /// attempted — overkill already clamped, dodges already nullified. Everything
    /// that reacts to damage reads it from here, which is why nothing in combat
    /// needs a reference to the HUD, the inventory or the player's health.
    /// </summary>
    internal void ReportHit(IDamageable target, DamageInfo info)
    {
        if (info.Nullified || info.Amount <= 0f) return;

        if (PublishHitEvents)
            EventBus.Publish(new EnemyDamaged(target, info));

        if (!PublishDamageNumbers || info.Amount < PopupThreshold) return;

        EventBus.Publish(new PopupRequested(
            target.Position + Vector3.Up * 0.35f,
            Mathf.RoundToInt(Mathf.Max(1f, info.Amount)).ToString(),
            info.Crit ? CritColor : ColorOf(info.Element),
            info.Crit ? 1.35f : 0.95f));
    }

    /// <summary>Damage numbers carry the element as colour. It is the only channel
    /// an untextured game has for telling the player which of their six weapons
    /// is doing the work.</summary>
    private static Color ColorOf(Element element) => element switch
    {
        Element.Fire => new Color(1.00f, 0.55f, 0.20f),
        Element.Ice => new Color(0.55f, 0.85f, 1.00f),
        Element.Lightning => new Color(0.95f, 0.90f, 0.35f),
        Element.Poison => new Color(0.60f, 0.95f, 0.35f),
        _ => new Color(0.96f, 0.96f, 0.96f),
    };
}
