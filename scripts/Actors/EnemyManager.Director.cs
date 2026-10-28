using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Actors;

public sealed partial class EnemyManager
{
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

        // The rest room is enemy-free whatever tried to spawn there (a split
        // child, a summon or a shrine guardian from the map just left).
        if (Quiet && _living > 0) ClearField(0f);
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
        => Game.Arena.IsCity ? 4 : Math.Max(6, (int)(Math.Min(MaxAlive, (int)((10 + (int)(minutes * 13f * Meta.DifficultyTiers.Active.Crowd)) * Meta.Challenges.Crowd)) * LiveCapScale));

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

    private static bool CityAllowed(EnemyDef def)
        => Game.EnemyMapEligibility.Allows(def, Game.Arena.Current);

    private bool HasSpawnable(bool elite, float runSeconds)
    {
        for (int i = 0; i < _defs.Count; i++)
        {
            EnemyDef def = _defs[i];
            if (!CityAllowed(def) || def.IsBoss || !def.DirectorSpawns || def.IsElite != elite) continue;
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
            bool ok = CityAllowed(def) && def.IsBoss && runSeconds >= def.UnlockAtSeconds;
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

            if (!CityAllowed(def) || def.IsBoss || !def.DirectorSpawns || runSeconds < def.UnlockAtSeconds || cost > _credits
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
        if (!Game.EnemyMapEligibility.Allows(_defs[defIndex], Game.Arena.Current)) return null;
        if (Net.Coop.IsHost) around = SpawnAnchor(around);

        Pool<Enemy> pool = _pools[defIndex];
        Enemy? enemy = pool.Get();
        if (enemy is null) return null;               // pool at capacity; try again later

        EnemyDef spawnDefinition = _defs[defIndex];
        bool found = spawnDefinition.Id == Visuals.ReefLeviathanModels.Id
            ? TryLeviathanSpawn(spawnDefinition, around, out Vector3 point)
            : TryFindSpawnPoint(around, out point);
        if (!found
            || Visuals.CityGiantModels.IsGiant(_defs[defIndex]) && Game.MapBlockers.Inside(point, _defs[defIndex].Radius + 0.3f))
        {
            pool.Release(enemy);
            return null;
        }

        if (!SpawnSpaceFree(point, _defs[defIndex].Radius)) { _pools[defIndex].Release(enemy); return null; }
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
        if (index < 0 || _living >= MaxAlive || Net.Coop.IsClient || Quiet
            || !Game.EnemyMapEligibility.Allows(def, Game.Arena.Current)) return null;
        if (Visuals.CityGiantModels.IsGiant(def) && (!Game.Arena.IsCity || Game.MapBlockers.Inside(position, def.Radius + 0.3f))) return null;

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

}


