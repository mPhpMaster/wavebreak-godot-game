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
            int capacity = def.IsBoss ? 4 : def.IsElite ? 32 : def.DirectorSpawns ? PoolCapacity : 64;
            _pools.Add(new Pool<Enemy>(this, NewEnemy, capacity));
        }

        // Build a handful of each up front. The first wave otherwise pays for
        // every instantiation inside one frame, which is the one hitch a player
        // is guaranteed to notice.
        for (int i = 0; i < _pools.Count; i++)
        {
            EnemyDef def = _defs[i];
            int warm = def.IsBoss ? 1 : def.IsElite ? 4 : def.UnlockAtSeconds <= 0f ? 24 : 8;
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

    // ------------------------------------------------------------ godot API

    public override void _EnterTree()
    {
        Instance = this;
        SetPhysicsProcess(true);
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    /// <summary>
    /// The whole enemy simulation, in one loop.
    ///
    /// Kept separate from <see cref="Tick"/> on purpose: spawning is a gameplay
    /// decision that runs on the caller's clock, but moving bodies has to happen
    /// in the physics step whatever that clock is doing.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("EnemyManager.phys");
        float dt = (float)delta;
        if (dt <= 0f) return;

        Sweep();
        RebuildGrid();
        if (DevFrozen) return;   // developer panel: freeze AI

        // Co-op (EnemyManager.Coop.cs): a client only moves puppets; the host
        // sends each enemy after the nearest living player.
        if (Net.Coop.IsClient)
        {
            PuppetPhysics(dt);
            return;
        }
        bool coop = Net.Coop.IsHost;
        if (coop) RefreshTargets();

        bool hasPlayer = PlayerRef.TryGet(out Node3D playerNode, out IDamageable? playerHit);
        Vector3 playerPosition = hasPlayer ? playerNode.GlobalPosition : Vector3.Zero;

        using (Wavebreak.Game.Prof.Scope("Enemies.step"))
        for (int i = 0; i < _alive.Count; i++)
        {
            Enemy e = _alive[i];
            if (!e.IsAlive) continue;
            Vector3 separation = SeparationFor(i, e.GlobalPosition);
            if (coop)
            {
                // Everyone down: nobody to chase; the host is about to end the run.
                if (NearestTarget(e.GlobalPosition, out Vector3 feet, out IDamageable? hit)) e.Step(dt, feet, hit, separation);
                else e.Step(dt, playerPosition, null, separation);
                continue;
            }
            e.Step(dt, playerPosition, hasPlayer ? playerHit : null, separation);
        }

        // After the enemies, so a shot fired this step moves this step.
        using (Wavebreak.Game.Prof.Scope("EnemyAttacks"))
        EnemyAttacks.Tick(dt, playerPosition, hasPlayer ? playerHit : null);
    }

    // ------------------------------------------------------------- director

    private float CreditRate(float minutes)
        => MathF.Max(0.1f, (BaseCreditRate + minutes * CreditRateGrowth) * PlayerRef.EnemyScaling
                           * SpawnRateScale * Meta.DifficultyTiers.Active.SpawnRate * Meta.Challenges.SpawnRate);

    /// <summary>
    /// Concurrent enemies allowed at a given point in the run.
    ///
    /// The opening number is the single most important one in the game. It
    /// started at 70, which saturated the arena inside the first ten seconds:
    /// the autopilot died at 1:04 of a 15-minute run with 85 enemies alive and
    /// one weapon, because the field was full before the first level-up. A
    /// survivors-like has to open sparse enough that the player's first weapon
    /// feels adequate, and get dangerous as the build comes together — the
    /// threat should be chasing the power curve, not waiting at the start of it.
    ///
    /// 10 at minute zero, +13 per minute, so: 1min 23, 3min 49, 5min 75, and
    /// the 200 ceiling (a frame-budget limit, not a design one) around minute 10.
    /// </summary>
    private int LiveCap(float minutes)
        => Math.Max(6, (int)(Math.Min(MaxAlive, (int)((10 + (int)(minutes * 13f * Meta.DifficultyTiers.Active.Crowd)) * Meta.Challenges.Crowd)) * LiveCapScale));

    /// <summary>
    /// Difficulty over time.
    ///
    /// Health is linear plus a gentle exponential. Linear alone stops meaning
    /// anything around minute eight, because player damage scales
    /// multiplicatively off items and weapon levels — an enemy that is 4x tougher
    /// against a player who is 40x stronger is not a threat, it is a speed bump.
    /// Speed is capped: past about 1.5x, enemies outrun the player's ability to
    /// kite and the game stops being about positioning.
    /// </summary>
    private void UpdateDifficulty(float runSeconds)
    {
        float minutes = MathF.Max(0f, runSeconds) / 60f;
        float scaling = PlayerRef.EnemyScaling;

        // The run's difficulty tier (Meta.DifficultyTiers) rides on top; Normal is 1.
        Meta.DifficultyTier tier = Meta.DifficultyTiers.Active;
        DifficultyHp = (1f + minutes * 0.34f) * MathF.Pow(1.075f, minutes) * scaling * PhaseHealthScale * tier.Health * Meta.Challenges.EnemyHealth;
        DifficultyDamage = (1f + minutes * 0.16f) * scaling * PhaseDamageScale * tier.Damage * Meta.Challenges.EnemyDamage;
        // The phase factor goes on after the cap: the cap is about the clock
        // never outpacing kiting, and a later map is allowed to be a step faster.
        DifficultySpeed = MathF.Min(1f + minutes * 0.022f, 1.45f) * PhaseSpeedScale * Meta.Challenges.EnemySpeed;
    }

    private void UpdateWaves(float runSeconds)
    {
        if (runSeconds >= _nextEliteAt)
        {
            _nextEliteAt = runSeconds + EliteWaveInterval;
            if (HasSpawnable(elite: true, runSeconds))
            {
                _eliteWaveUntil = runSeconds + EliteWaveDuration;
                // A lump of credits, or the wave would trickle out over the next
                // half minute at the ordinary accrual rate and never read as one.
                _credits += 12f + runSeconds / 60f * 14f;
                Announce("ELITE WAVE", new Color(1f, 0.62f, 0.2f), 1.35f);
            }
        }

        if (!TimedBosses || runSeconds < _nextBossAt) return;

        // One boss at a time. A timer that fires regardless stacks a second boss
        // on an unfinished fight, which is not a harder fight but an unreadable
        // one; the next boss waits until this one is down.
        if (BossCount > 0)
        {
            _nextBossAt = runSeconds + 5f;
            return;
        }
        _nextBossAt = runSeconds + BossWaveInterval;

        int boss = PickBoss(runSeconds);
        if (boss < 0) return;
        if (!PlayerRef.TryGet(out Node3D player, out _)) return;
        if (SpawnOne(boss, player.GlobalPosition) is null) return;
        _bossesMet.Add(_defs[boss]);
        Announce(BossTitle(_defs[boss]), new Color(1f, 0.25f, 0.25f), 1.7f);
    }

    /// <summary>The boss's name as a banner: upper case in English, its
    /// translated name in Arabic (which has no case).</summary>
    private static string BossTitle(EnemyDef def)
    {
        if (def.DisplayName is not { Length: > 0 } name) return Meta.Loc.T("BOSS");
        return Meta.Loc.IsArabic ? Meta.Loc.T(name) : name.ToUpperInvariant();
    }

    private static void Announce(string text, Color color, float scale)
    {
        if (Net.Coop.IsHost) Net.NetSession.Instance?.SendBanner(text, color, scale);
        if (!PlayerRef.TryGet(out Node3D player, out _)) return;
        EventBus.Publish(new PopupRequested(player.GlobalPosition + new Vector3(0f, 3.2f, 0f), text, color, scale));
    }

    private bool HasSpawnable(bool elite, float runSeconds)
    {
        for (int i = 0; i < _defs.Count; i++)
        {
            EnemyDef def = _defs[i];
            if (def.IsBoss || !def.DirectorSpawns || def.IsElite != elite) continue;
            if (runSeconds < def.UnlockAtSeconds) continue;
            if (StageEnemyIds is { } only && !only.Contains(def.Id)) continue;
            return true;
        }
        return false;
    }

    private int PickBoss(float runSeconds)
    {
        _weights.Clear();
        bool any = false;
        for (int i = 0; i < _defs.Count; i++)
        {
            EnemyDef def = _defs[i];
            bool ok = def.IsBoss && runSeconds >= def.UnlockAtSeconds;
            // Later bosses are the interesting ones, so weight by cost; one
            // already fought this run is a fifth as likely, so three boss
            // timers usually mean three different fights.
            float w = ok ? MathF.Max(1f, def.Cost) : 0f;
            if (_bossesMet.Contains(def)) w *= 0.2f;
            _weights.Add(w);
            any |= ok;
        }
        return any ? _rng.PickWeighted(_weights) : -1;
    }

    /// <summary>
    /// Choose what to spend the next credits on.
    ///
    /// The weight is a log-normal bump centred on a "target threat" that climbs
    /// with the clock: an enemy costing exactly the target is most likely, and
    /// anything an order of magnitude cheaper is effectively excluded. That is
    /// what retires early trash without needing an explicit expiry on every
    /// definition, and it degrades sensibly if a designer adds a cost-40 enemy
    /// with no unlock time — it simply will not be picked until the target
    /// climbs to meet it.
    /// </summary>
    private int PickAffordable(float runSeconds, float minutes)
    {
        bool eliteWave = EliteWaveActive;
        float target = 1f + minutes * 0.9f;

        _weights.Clear();
        bool any = false;

        for (int i = 0; i < _defs.Count; i++)
        {
            EnemyDef def = _defs[i];
            float cost = MathF.Max(1f, def.Cost);

            if (def.IsBoss || !def.DirectorSpawns || runSeconds < def.UnlockAtSeconds || cost > _credits
                || StageEnemyIds is { } only && !only.Contains(def.Id))
            {
                _weights.Add(0f);
                continue;
            }
            if (def.IsElite && !eliteWave)
            {
                _weights.Add(0f);
                continue;
            }

            float ln = MathF.Log(cost / target);
            float w = MathF.Exp(-ln * ln * 1.4f);

            // Newly unlocked enemies get a short spotlight so the player sees the
            // thing that just entered the run rather than meeting it at random
            // three minutes later.
            float age = runSeconds - def.UnlockAtSeconds;
            if (age < 25f) w *= 2.5f;
            if (def.IsElite) w *= 6f;

            _weights.Add(w);
            any |= w > 0f;
        }

        return any ? _rng.PickWeighted(_weights) : -1;
    }

    private Enemy? SpawnOne(int defIndex, Vector3 around)
    {
        if (defIndex < 0 || defIndex >= _pools.Count || Net.Coop.IsClient) return null;

        Pool<Enemy> pool = _pools[defIndex];
        Enemy? enemy = pool.Get();
        if (enemy is null) return null;               // pool at capacity; try again later

        if (!TryFindSpawnPoint(around, out Vector3 point))
        {
            pool.Release(enemy);
            return null;
        }

        Activate(defIndex, enemy, point);
        return enemy;
    }

    /// <summary>
    /// Put one <paramref name="def"/> at an exact point, outside the director's
    /// budget: a split, a boss's summon, a scripted encounter. Still pooled,
    /// still counted, still capped by <see cref="MaxAlive"/>. Returns null when
    /// the def is not installed or there is no room.
    /// </summary>
    public Enemy? SpawnAt(EnemyDef def, Vector3 position)
    {
        int index = _defs.IndexOf(def);
        // Co-op client: only the host puts enemies on the field.
        if (index < 0 || _living >= MaxAlive || Net.Coop.IsClient) return null;

        Enemy? enemy = _pools[index].Get();
        if (enemy is null) return null;

        Activate(index, enemy, position);
        return enemy;
    }

    private void Activate(int defIndex, Enemy enemy, Vector3 point)
    {
        EnemyDef def = _defs[defIndex];
        enemy.PoolIndex = defIndex;
        enemy.Despawned ??= _despawnHandler;
        enemy.Random = _rng;

        // Per-instance speed jitter, from the run's seeded source so replays hold.
        // Identical speeds make a group move as one rigid blob, which is the
        // single biggest tell that a crowd is not really a crowd.
        float jitter = _rng.Range(0.9f, 1.12f);
        // Bosses ride the crowd's health curve: measured on the bot, a build's
        // damage against a single big target grows about as fast as the crowd's
        // health does once "+% damage" items work, so one curve keeps a boss a
        // roughly one-minute fight wherever it appears. Per-phase scaling comes
        // from SpawnBoss, which scales DifficultyHp for that one spawn.
        enemy.Configure(def, DifficultyHp, DifficultyDamage, DifficultySpeed * jitter);
        enemy.Spawn(point);
        Meta.Challenges.DressEnemy(enemy);

        Register(enemy);
        if (Net.Coop.IsHost) Net.NetSession.Instance?.OnEnemySpawned(enemy);
    }

    /// <summary>Despawn every live enemy below <paramref name="y"/>. The content
    /// audit stages fights far under the arena; this is how it cleans up the
    /// adds and splits those fights leave behind.</summary>
    internal void DespawnBelow(float y)
    {
        for (int i = 0; i < _alive.Count; i++)
            if (_alive[i].IsAlive && _alive[i].GlobalPosition.Y < y) _alive[i].Despawn();
    }

    // ---------------------------------------------------------- placement

    /// <summary>
    /// Notice whether the level has a navigation mesh worth querying.
    ///
    /// Re-checked periodically rather than once, because a level may build or
    /// bake its navigation after the manager is configured. An empty map is the
    /// normal case — a flat arena needs no navmesh — and querying one that has no
    /// regions returns the origin for every point, which would pile every spawn
    /// on the middle of the world. Hence the region count gate.
    /// </summary>
    private void RefreshNavigation(float runSeconds)
    {
        if (runSeconds < _navCheckAt) return;
        _navCheckAt = runSeconds + 2f;

        _navUsable = false;
        if (!IsInsideTree() || GetWorld3D() is not { } world) return;

        _navMap = world.NavigationMap;
        if (!_navMap.IsValid) return;

        _navUsable = NavigationServer3D.MapGetRegions(_navMap).Count > 0;
    }

    /// <summary>
    /// Find somewhere on the ring around the player that an enemy can stand.
    ///
    /// Candidates are rejected when nothing walkable is under them, so the level
    /// geometry decides the arena bounds rather than a constant that has to be
    /// kept in sync with it. When every ring angle fails — which is what standing
    /// in a corner looks like — the last resort aims inward, back toward the
    /// middle of the world, because that is the one direction guaranteed to have
    /// floor on it.
    /// </summary>
    private bool TryFindSpawnPoint(Vector3 around, out Vector3 point)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 direction = _rng.OnUnitCircleXZ();
            float radius = _rng.Range(SpawnRingMin, SpawnRingMax);
            Vector3 candidate = around + direction * radius;

            if (OutsideArena(candidate))
            {
                // Near an edge, the opposite side of the ring is the obvious
                // second guess and keeps spawns flowing instead of stalling.
                candidate = around - direction * radius;
                if (OutsideArena(candidate)) continue;
            }

            if (!SnapToGround(ref candidate)) continue;

            point = candidate;
            return true;
        }

        // Inward fallback. Straight at the world origin from wherever the player
        // is; if the player is standing on the origin, any direction will do.
        Vector3 inward = new(-around.X, 0f, -around.Z);
        inward = inward.LengthSquared() > 0.01f ? inward.Normalized() : _rng.OnUnitCircleXZ();

        Vector3 last = around + inward * SpawnRingMin;
        if (SnapToGround(ref last))
        {
            point = last;
            return true;
        }

        last.Y = _groundKnown ? _lastGroundY : around.Y;
        point = last;
        return _groundKnown;
    }

    private bool OutsideArena(Vector3 p)
        => ArenaRadius > 0f && p.X * p.X + p.Z * p.Z > ArenaRadius * ArenaRadius;

    /// <summary>
    /// Put a spawn candidate on ground the enemy can actually stand on, or
    /// reject it.
    ///
    /// A navigation map is the real answer to "is this walkable", so it wins when
    /// the level supplies one. Otherwise a downward ray against world collision
    /// decides: a miss means empty space, which is a rejection rather than
    /// something to paper over — an enemy dropped into the void either falls
    /// forever or gets caught by its safety floor and hovers, and both look far
    /// worse than the spawn simply happening a few metres round the ring.
    /// </summary>
    private bool SnapToGround(ref Vector3 candidate)
    {
        if (_navUsable)
        {
            Vector3 onMesh = NavigationServer3D.MapGetClosestPoint(_navMap, candidate);
            Vector3 delta = onMesh - candidate;
            delta.Y = 0f;
            // A distant snap means the candidate was off the mesh entirely and
            // the returned point is some far edge; reject rather than teleport.
            if (delta.LengthSquared() > 36f) return false;
            candidate = onMesh;
            _lastGroundY = candidate.Y;
            _groundKnown = true;
            return true;
        }

        if (!IsInsideTree() || GetWorld3D() is not { } world) return false;

        _groundQuery ??= new PhysicsRayQueryParameters3D
        {
            CollisionMask = Enemy.WorldLayer,
            CollideWithAreas = false,
            CollideWithBodies = true,
        };
        _groundQuery.From = candidate + new Vector3(0f, 30f, 0f);
        _groundQuery.To = candidate + new Vector3(0f, -40f, 0f);

        Godot.Collections.Dictionary hit = world.DirectSpaceState.IntersectRay(_groundQuery);
        if (hit.Count == 0 || !hit.TryGetValue("position", out Variant where)) return false;

        float y = where.AsVector3().Y;

        // Reject anything perched well above the floor the last spawn found. That
        // is scenery — a wall cap or a crate — and an enemy standing on it would
        // walk off the edge and be held in mid-air by its own safety floor.
        if (_groundKnown && y > _lastGroundY + 2f) return false;

        candidate.Y = y;
        _lastGroundY = y;
        _groundKnown = true;
        return true;
    }

    private void CullStrays(Vector3 playerPosition)
    {
        if (_alive.Count == 0) return;

        float maxSq = DespawnRadius * DespawnRadius;
        int scanned = 0;
        int batch = Math.Min(CullBatch, _alive.Count);

        while (scanned < batch)
        {
            scanned++;
            if (_alive.Count == 0) return;
            _cullCursor = (_cullCursor + 1) % _alive.Count;

            Enemy e = _alive[_cullCursor];
            if (!e.IsAlive || e.IsBoss) continue;      // bosses are never recycled

            Vector3 delta = e.GlobalPosition - playerPosition;
            delta.Y = 0f;
            if (delta.LengthSquared() > maxSq) e.Despawn();
        }
    }

    // ------------------------------------------------------ live list churn

    private Enemy NewEnemy()
    {
        Enemy enemy = new() { Name = "Enemy" };
        enemy.Despawned = _despawnHandler ??= OnEnemyDespawned;
        return enemy;
    }

    private void PrewarmPool(int index, int count)
    {
        Pool<Enemy> pool = _pools[index];
        EnemyDef def = _defs[index];

        for (int i = 0; i < count; i++)
        {
            Enemy? enemy = pool.Get();
            if (enemy is null) break;
            enemy.PoolIndex = index;
            enemy.Despawned ??= _despawnHandler;
            // Configure now, so the mesh and material work happens here rather
            // than in the middle of the first wave.
            enemy.Configure(def, 1f, 1f, 1f);
            pool.Release(enemy);
        }
    }

    private void Register(Enemy enemy)
    {
        enemy.AliveIndex = _alive.Count;
        _alive.Add(enemy);
        _living++;
        _gridValid = false;
    }

    private void OnEnemyDespawned(Enemy enemy)
    {
        if (enemy.IsPooled) return;                  // already parked
        _pendingRelease.Add(enemy);
        if (_living > 0) _living--;
    }

    /// <summary>
    /// Return dead enemies to their pools and compact the live list.
    ///
    /// Compaction rather than swap-removal per entry: several enemies can die in
    /// one frame, and each swap would invalidate indices the other pending
    /// entries recorded. One pass over a list capped at <see cref="MaxAlive"/> is
    /// cheaper than getting that bookkeeping right.
    /// </summary>
    private void Sweep()
    {
        if (_pendingRelease.Count == 0) return;

        for (int i = 0; i < _pendingRelease.Count; i++)
            ReleaseToPool(_pendingRelease[i]);
        _pendingRelease.Clear();

        int write = 0;
        for (int read = 0; read < _alive.Count; read++)
        {
            Enemy e = _alive[read];
            if (!GodotObject.IsInstanceValid(e)) continue;
            if (e.IsPooled) continue;                // parked by the loop above
            _alive[write] = e;
            e.AliveIndex = write;
            write++;
        }
        if (write < _alive.Count) _alive.RemoveRange(write, _alive.Count - write);

        _gridValid = false;
    }

    private void ReleaseToPool(Enemy enemy)
    {
        int index = enemy.PoolIndex;
        if (index >= 0 && index < _pools.Count) _pools[index].Release(enemy);
        else if (GodotObject.IsInstanceValid(enemy)) enemy.QueueFree();
    }

    // ------------------------------------------------------- neighbour grid

    private static long CellKey(int cx, int cz) => ((long)cx << 32) | (uint)cz;

    private static int CellOf(float v) => (int)MathF.Floor(v / CellSize);

    private void RebuildGrid()
    {
        _cellHead.Clear();
        int count = _alive.Count;
        if (_cellNext.Length < count)
            Array.Resize(ref _cellNext, Math.Max(count, _cellNext.Length * 2));

        for (int i = 0; i < count; i++)
        {
            Enemy e = _alive[i];
            if (!e.IsAlive) { _cellNext[i] = -1; continue; }

            Vector3 p = e.GlobalPosition;
            long key = CellKey(CellOf(p.X), CellOf(p.Z));
            _cellNext[i] = _cellHead.TryGetValue(key, out int head) ? head : -1;
            _cellHead[key] = i;
        }
        _gridValid = true;
    }

    /// <summary>
    /// Crowd-avoidance push for the enemy at <paramref name="selfIndex"/>.
    ///
    /// Bounded to <see cref="MaxSeparationNeighbours"/> contributors: in a dense
    /// pile the direction is decided by the nearest few anyway, and the bound is
    /// what stops the cost going quadratic exactly when the frame can least
    /// afford it.
    /// </summary>
    private Vector3 SeparationFor(int selfIndex, Vector3 position)
    {
        if (!_gridValid) return Vector3.Zero;

        Vector3 push = Vector3.Zero;
        int cx = CellOf(position.X);
        int cz = CellOf(position.Z);
        float radius = SeparationRadius;
        float radiusSq = radius * radius;
        int considered = 0;

        for (int ox = -1; ox <= 1; ox++)
        for (int oz = -1; oz <= 1; oz++)
        {
            if (!_cellHead.TryGetValue(CellKey(cx + ox, cz + oz), out int i)) continue;

            while (i >= 0)
            {
                int current = i;
                i = _cellNext[i];
                if (current == selfIndex) continue;

                Enemy other = _alive[current];
                if (!other.IsAlive) continue;

                Vector3 delta = position - other.GlobalPosition;
                delta.Y = 0f;
                float distSq = delta.LengthSquared();
                if (distSq >= radiusSq) continue;

                if (distSq < 1e-5f)
                {
                    // Exactly stacked. Deterministic per-pair offset so the two
                    // separate instead of sitting in a stable draw forever.
                    float angle = (selfIndex * 2.399963f) + current * 0.7f;
                    push += new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                }
                else
                {
                    float dist = MathF.Sqrt(distSq);
                    push += delta / dist * (1f - dist / radius);
                }

                if (++considered >= MaxSeparationNeighbours) return push;
            }
        }

        return push;
    }

    private Enemy? NearestInGrid(Vector3 p, float maxRange)
    {
        int cx = CellOf(p.X);
        int cz = CellOf(p.Z);
        int span = Math.Max(1, (int)MathF.Ceiling(maxRange / CellSize));

        Enemy? best = null;
        float limitSq = maxRange * maxRange;
        float bestSq = limitSq;

        for (int ox = -span; ox <= span; ox++)
        for (int oz = -span; oz <= span; oz++)
        {
            if (!_cellHead.TryGetValue(CellKey(cx + ox, cz + oz), out int i)) continue;

            while (i >= 0)
            {
                Enemy e = _alive[i];
                i = _cellNext[i];
                if (!e.IsAlive) continue;

                float sq = (e.AimPoint - p).LengthSquared();
                if (sq >= limitSq) continue;
                sq *= PriorityFactor(e);
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = e;
            }
        }

        return best;
    }
    // ------------------------------------------------------- phase control
    // Hooks for Game.StageDirector, which runs the run as a sequence of maps.
    // Additive on purpose: with none of these touched the director behaves
    // exactly as it did with a single arena and a boss timer.

    /// <summary>When false the director never spawns a boss on its own timer;
    /// whoever cleared it is expected to call <see cref="SpawnBoss"/>.</summary>
    public bool TimedBosses { get; set; } = true;

    /// <summary>Developer panel "freeze AI": enemies hold still and the director stops spawning.</summary>
    public static bool DevFrozen { get; set; }

    /// <summary>Per-phase multipliers on top of the time curve. 1 = no change.</summary>
    public float PhaseHealthScale { get; set; } = 1f;
    public float PhaseDamageScale { get; set; } = 1f;
    public float PhaseSpeedScale { get; set; } = 1f;

    /// <summary>Fraction of the normal live cap to allow. Lowered during a phase
    /// boss fight: weapons aim at the nearest enemy, so with a full crowd around
    /// the player the boss is almost never the target and the fight cannot end.</summary>
    public float LiveCapScale { get; set; } = 1f;

    /// <summary>Multiplier on credit income and the credit bank: how fast the
    /// crowd refills. Raised by the Final Swarm. 1 = no change.</summary>
    public float SpawnRateScale { get; set; } = 1f;

    /// <summary>The current map's enemy list (StageDef.EnemyIds), or null for
    /// the whole roster. Filters ordinary and elite spawns, never bosses.</summary>
    public HashSet<string>? StageEnemyIds { get; set; }

    /// <summary>
    /// Put a specific boss on the field now, on the spawn ring around the
    /// player, and announce it. Returns null when it could not be placed this
    /// frame (pool exhausted, no floor found); callers should retry.
    /// </summary>
    public Enemy? SpawnBoss(EnemyDef def, float healthScale = 1f)
    {
        int index = _defs.IndexOf(def);
        if (index < 0 || !PlayerRef.TryGet(out Node3D player, out _)) return null;
        UpdateDifficulty(_runSeconds);
        // Scaled for this one spawn only; the next Tick recomputes it anyway.
        float hp = DifficultyHp;
        DifficultyHp = hp * MathF.Max(0.01f, healthScale);
        Enemy? boss = SpawnOne(index, player.GlobalPosition);
        DifficultyHp = hp;
        if (boss is not null)
            Announce(BossTitle(def), new Color(1f, 0.25f, 0.25f), 1.7f);
        return boss;
    }

    /// <summary>
    /// Remove every enemy without a kill and empty the credit bank, for a map
    /// change. Unlike <see cref="Reset"/> it keeps the director's wave clock,
    /// so arriving on a new map does not replay the opening elite schedule.
    /// <paramref name="breather"/> is how long before the first spawn on the
    /// new map, so the title card is read in peace.
    /// </summary>
    public void ClearField(float breather)
    {
        for (int i = 0; i < _alive.Count; i++)
        {
            Enemy e = _alive[i];
            if (GodotObject.IsInstanceValid(e) && e.IsAlive) e.Despawn();
        }
        Sweep();
        _credits = 0f;
        _spawnTimer = MathF.Max(0f, breather);
        _eliteWaveUntil = -1f;
        // The floor height learned on the old map means nothing on the new one.
        _groundKnown = false;
        _lastGroundY = 0f;
        _navCheckAt = 0f;
        _gridValid = false;
    }
}
