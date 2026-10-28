using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Actors;

/// <summary>
/// Owns every enemy in the run: the pools they come from, the physics step that
/// moves them, and the director that decides what shows up and when.
///
/// The director spends a credit budget rather than following a wave table. A
/// table has to be authored for every minute of every run and says nothing
/// useful when the player is fifteen seconds ahead of or behind the curve; a
/// budget that accrues with run time and is spent on whatever is affordable
/// produces the same escalation from three numbers, and adding an enemy to the
/// game is a data change with a cost on it. The preference curve is the second
/// half of that idea: rather than "unlock A, stop spawning B", the director
/// wants a *threat size* that climbs with the clock and picks enemies near it,
/// so early trash fades out on its own instead of drowning the late game.
///
/// Two structural notes worth knowing before editing this:
///
/// Enemies are stepped from here, not from their own _PhysicsProcess. That
/// hoists the player lookup and the neighbour grid out of the per-enemy loop and
/// avoids three hundred managed/native callbacks a frame.
///
/// Death is deferred. A weapon iterating <see cref="Alive"/> kills things as it
/// goes, so removing from the live list inside that loop would invalidate the
/// caller's enumerator. Dead enemies are marked, skipped, and swept at the top of
/// the next physics step.
/// </summary>
public sealed partial class EnemyManager : Node3D
{
    // -------------------------------------------------------------- tuning

    /// <summary>Hard ceiling on concurrent enemies. The frame budget, not the
    /// difficulty curve, is what this protects.</summary>
    [Export] public int MaxAlive { get; set; } = 200;

    /// <summary>Instances each ordinary enemy pool may ever create.</summary>
    [Export] public int PoolCapacity { get; set; } = 160;

    /// <summary>Inner radius of the spawn ring — comfortably past the edge of a
    /// 1280x720 view at the game's camera height, so nothing pops in on screen.</summary>
    [Export] public float SpawnRingMin { get; set; } = 25f;

    [Export] public float SpawnRingMax { get; set; } = 33f;

    /// <summary>Enemies further than this from the player are recycled. Without
    /// it, anything the player outruns stays alive forever, eating the live cap
    /// and the frame budget while contributing nothing.</summary>
    [Export] public float DespawnRadius { get; set; } = 78f;

    /// <summary>Optional cheap bound on spawn points, measured from the world
    /// origin. Zero disables it, which is the default: the ground test below is
    /// the real authority on where an enemy can stand, and a hardcoded radius
    /// that disagrees with the level is worse than no radius at all.</summary>
    [Export] public float ArenaRadius { get; set; }

    [Export] public float FirstEliteAtSeconds { get; set; } = 70f;
    [Export] public float EliteWaveInterval { get; set; } = 55f;
    [Export] public float EliteWaveDuration { get; set; } = 7f;
    [Export] public float FirstBossAtSeconds { get; set; } = 240f;
    [Export] public float BossWaveInterval { get; set; } = 240f;

    /// <summary>Credits per second at minute zero.</summary>
    [Export] public float BaseCreditRate { get; set; } = 1.5f;

    /// <summary>Extra credits per second gained for each minute elapsed.</summary>
    [Export] public float CreditRateGrowth { get; set; } = 2.6f;

    private const float SpawnInterval = 0.3f;
    private const int MaxSpawnsPerBurst = 14;
    private const float CellSize = 2f;
    private const int MaxSeparationNeighbours = 8;
    private const float SeparationRadius = 1.9f;
    private const int CullBatch = 24;
    private const float CullInterval = 0.5f;

    // --------------------------------------------------------------- state

    /// <summary>The live manager. Null outside a run.</summary>
    public static EnemyManager? Instance { get; private set; }

    private readonly List<EnemyDef> _defs = new();
    private readonly List<Pool<Enemy>> _pools = new();
    private readonly List<Enemy> _alive = new();
    private readonly List<Enemy> _pendingRelease = new();
    private readonly List<float> _weights = new();

    private Rng _rng = new(0);
    private int _living;
    private float _credits;
    private float _spawnTimer;
    private float _cullTimer;
    private int _cullCursor;
    private float _runSeconds;
    private float _nextEliteAt = float.MaxValue;
    private float _nextBossAt = float.MaxValue;
    private float _eliteWaveUntil = -1f;
    private Action<Enemy>? _despawnHandler;
    private bool _bossBarInstalled;
    private readonly HashSet<EnemyDef> _bossesMet = new();

    // Neighbour grid: cell -> index of the first enemy in it, then a linked list
    // through _cellNext. Rebuilt once per physics step and allocation-free after
    // the first frame, which a Dictionary<cell, List<Enemy>> would not be.
    private readonly Dictionary<long, int> _cellHead = new();
    private int[] _cellNext = new int[64];
    private bool _gridValid;

    private Rid _navMap;
    private bool _navUsable;
    private float _navCheckAt;
    private PhysicsRayQueryParameters3D? _groundQuery;

    // Last floor height a spawn actually landed on. Used both as the fallback
    // height and as the reference for rejecting spawns on top of scenery — the
    // player's own Y is unusable for that because the player can be mid-jump.
    private float _lastGroundY;
    private bool _groundKnown;

    // ---------------------------------------------------------- public API

    /// <summary>Enemies currently in the world.</summary>
    public int AliveCount => _living;

    /// <summary>Health multiplier the director is currently applying.</summary>
    public float DifficultyHp { get; private set; } = 1f;

    /// <summary>Move-speed multiplier the director is currently applying.</summary>
    public float DifficultySpeed { get; private set; } = 1f;

    /// <summary>Contact-damage multiplier the director is currently applying.</summary>
    public float DifficultyDamage { get; private set; } = 1f;

    /// <summary>First living boss, or null. For the boss bar and anything that
    /// wants to know whether a boss fight is on.</summary>
    public Enemy? ActiveBoss
    {
        get
        {
            for (int i = 0; i < _alive.Count; i++)
                if (_alive[i].IsAlive && _alive[i].IsBoss) return _alive[i];
            return null;
        }
    }

    /// <summary>Living bosses right now.</summary>
    public int BossCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _alive.Count; i++)
                if (_alive[i].IsAlive && _alive[i].IsBoss) n++;
            return n;
        }
    }

    /// <summary>Credits banked but not yet spent. Useful for a debug overlay.</summary>
    public float Credits => _credits;

    /// <summary>True while an elite wave is spawning.</summary>
    public bool EliteWaveActive => _runSeconds <= _eliteWaveUntil;

    /// <summary>
    /// Every living enemy, for weapon targeting.
    ///
    /// Deliberately an iterator over an index loop rather than the backing list:
    /// killing an enemy part-way through a <c>foreach</c> over this is the normal
    /// case for a piercing shot, and that must not invalidate the caller.
    /// </summary>
    public IEnumerable<Enemy> Alive
    {
        get
        {
            for (int i = 0; i < _alive.Count; i++)
            {
                Enemy e = _alive[i];
                if (e.IsAlive) yield return e;
            }
        }
    }

    /// <summary>Backing list, including enemies that died this frame and have not
    /// been swept yet — check <see cref="Enemy.IsAlive"/>. For hot paths that
    /// cannot afford the iterator allocation.</summary>
    public IReadOnlyList<Enemy> AliveList => _alive;

    /// <summary>
    /// Closest living enemy to <paramref name="p"/>, or null. Pass a
    /// <paramref name="maxRange"/> of 0 or less for "anywhere".
    /// </summary>
    public Enemy? NearestTo(Vector3 p, float maxRange)
    {
        // Bounded search goes through the neighbour grid, which turns the common
        // "what is near this projectile" question from a scan of every enemy into
        // a handful of cells. Unbounded search has no choice but to scan.
        if (maxRange > 0f && _gridValid && maxRange <= CellSize * 12f)
            return NearestInGrid(p, maxRange);

        Enemy? best = null;
        float limitSq = maxRange > 0f ? maxRange * maxRange : float.MaxValue;
        float bestSq = limitSq;
        for (int i = 0; i < _alive.Count; i++)
        {
            Enemy e = _alive[i];
            if (!e.IsAlive) continue;
            float sq = (e.AimPoint - p).LengthSquared();
            if (sq >= limitSq) continue;
            sq *= PriorityFactor(e);
            if (sq >= bestSq) continue;
            bestSq = sq;
            best = e;
        }
        return best;
    }

    /// <summary>
    /// Squared-distance discount for targeting. Weapons aim at the "nearest"
    /// enemy, and with pure distance a boss is never nearest: the trash it
    /// walks in with always is, so auto-aim spent whole fights on fodder while
    /// the boss sat at full health. A boss counts as 0.45 of its distance: the
    /// boss at 10m beats trash further than 4.5m, so the weapons work on it
    /// whenever the player has room, and go back to the crowd when it closes.
    /// (At 0.25 the crowd was starved: kills per run halved and the arena sat
    /// at the live cap for whole fights.) Elites are left on plain distance —
    /// an elite wave is a crowd, and the crowd is the threat.
    /// </summary>
    private static float PriorityFactor(Enemy e) => e.IsBoss ? 0.45f * 0.45f : 1f;

    /// <summary>
    /// Install the enemy roster and the run's random source. Safe to call again
    /// to start a fresh run; everything from the previous one is torn down.
    /// </summary>
    public void Configure(IReadOnlyList<EnemyDef> defs, Rng rng)
    {
        using var timing = Wavebreak.Game.Prof.Scope("Startup.enemyPools");
        Instance = this;
        _rng = rng ?? new Rng(0);
        _despawnHandler ??= OnEnemyDespawned;

        Reset();

        _defs.Clear();
        foreach (Pool<Enemy> pool in _pools) pool.Clear();
        _pools.Clear();

        if (defs is not null)
        {
            foreach (EnemyDef def in defs)
            {
                if (def is null) continue;
                _defs.Add(def);
            }
        }

        // Before the prewarm below builds any models.
        Visuals.BehaviourModels.EnsureRegistered();

        for (int i = 0; i < _defs.Count; i++)
        {
            EnemyDef def = _defs[i];
            int capacity = Visuals.CityGiantModels.IsGiant(def) ? 6 : def.IsBoss ? 4 : def.IsElite ? 32 : def.DirectorSpawns ? PoolCapacity : 64;
            _pools.Add(new Pool<Enemy>(this, NewEnemy, capacity));
        }

        // Build a handful of each up front. The first wave otherwise pays for
        // every instantiation inside one frame, which is the one hitch a player
        // is guaranteed to notice.
        for (int i = 0; i < _pools.Count; i++)
        {
            EnemyDef def = _defs[i];
            int warm = Visuals.CityGiantModels.IsGiant(def) ? 1 : def.IsBoss ? 1 : def.IsElite ? 4 : def.UnlockAtSeconds <= 0f ? 24 : 8;
            PrewarmPool(i, warm);
        }

        EnemyAttacks.Initialize(this);

        _nextEliteAt = FirstEliteAtSeconds;
        _nextBossAt = FirstBossAtSeconds;
        UpdateDifficulty(0f);
    }

    /// <summary>
    /// Run the director for one frame. Movement is not done here — see
    /// <see cref="_PhysicsProcess"/> and the class note.
    /// </summary>
    /// <param name="dt">Seconds since the last call.</param>
    /// <param name="runSeconds">Elapsed run time, which is the director's clock.</param>
    public void Tick(float dt, float runSeconds)
    {
        Instance ??= this;
        _runSeconds = runSeconds;
        if (_defs.Count == 0 || dt <= 0f) return;

        if (!_bossBarInstalled) _bossBarInstalled = UI.BossBar.Install(this);

        Sweep();
        // Co-op client: the host's director decides every spawn (EnemyManager.Coop.cs).
        if (Net.Coop.IsClient) return;
        UpdateDifficulty(runSeconds);
        RefreshNavigation(runSeconds);

        // Bank credits, but not without limit. An unbounded bank means a player
        // who clears the field for thirty seconds gets the whole thirty seconds
        // dumped on them at once, which reads as a bug rather than as pressure.
        float minutes = runSeconds / 60f;
        _credits += CreditRate(minutes) * dt;
        // The bank cap is also the opening burst: whatever it allows is spent on
        // the first tick. At 40 that was forty enemies in the first second, which
        // no amount of live-cap tuning downstream can undo.
        float bank = (10f + minutes * 26f) * SpawnRateScale * Meta.DifficultyTiers.Active.SpawnRate * Meta.Challenges.SpawnRate;
        if (_credits > bank) _credits = bank;

        UpdateWaves(runSeconds);
        if (DevFrozen) return;   // developer panel: no spawns while AI is frozen

        if (!PlayerRef.TryGet(out Node3D player, out _)) return;
        Vector3 origin = player.GlobalPosition;
        bool coop = Net.Coop.IsHost && _targets.Count > 0;

        _cullTimer -= dt;
        if (_cullTimer <= 0f)
        {
            _cullTimer = CullInterval;
            if (coop) CullStraysCoop();
            else CullStrays(origin);
        }

        _spawnTimer -= dt;
        if (_spawnTimer > 0f) return;
        _spawnTimer = SpawnInterval;

        int cap = LiveCap(minutes);
        int spawned = 0;
        if (coop) origin = SpawnAnchor(origin);
        while (spawned < MaxSpawnsPerBurst && _living < cap)
        {
            int index = PickAffordable(runSeconds, minutes);
            if (index < 0) break;
            if (SpawnOne(index, origin) is null) break;
            _credits -= MathF.Max(1f, _defs[index].Cost);
            spawned++;
        }
    }

    /// <summary>Recycle everything and reset the director. Called by
    /// <see cref="Configure"/> and worth calling when a run ends.</summary>
    public void Reset()
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            Enemy e = _alive[i];
            if (GodotObject.IsInstanceValid(e)) ReleaseToPool(e);
        }
        _alive.Clear();
        _pendingRelease.Clear();
        _cellHead.Clear();
        _bossesMet.Clear();
        EnemyAttacks.Clear();
        _gridValid = false;

        _living = 0;
        _credits = 0f;
        _spawnTimer = 0f;
        _cullTimer = 0f;
        _cullCursor = 0;
        _runSeconds = 0f;
        _eliteWaveUntil = -1f;
        _nextEliteAt = FirstEliteAtSeconds;
        _nextBossAt = FirstBossAtSeconds;
        _navCheckAt = 0f;
        _navUsable = false;
        _lastGroundY = 0f;
        _groundKnown = false;
        DifficultyHp = 1f;
        DifficultySpeed = 1f;
        DifficultyDamage = 1f;
        PlayerRef.Forget();
    }

}
