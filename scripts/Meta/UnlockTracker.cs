using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Game;

namespace Wavebreak.Meta;

/// <summary>
/// Watches one run and turns it into profile progress.
///
/// Everything it needs is either on the bus or already counted by the system
/// that owns it (kills by RunState, slams by the player, gold by the
/// inventory), so it reads those rather than keeping second counts that could
/// disagree with the HUD. The two things nobody else tracks — boss fights and
/// "has the player been hit yet" — it derives itself from EnemyDamaged and
/// PlayerDamaged, which is why bosses need no hook in the enemy code.
///
/// Conditions are polled a few times a second rather than checked inside each
/// event handler: most metrics change continuously (time, speed), and one
/// evaluation loop over the data table is simpler than knowing which event can
/// move which metric.
///
/// Lifetime stats are written to the profile once, when the run ends — or when
/// the scene goes away without ending, so a quit still counts the kills.
/// Unlocks are written the moment they are earned, so a crash cannot take one
/// back after the toast has promised it.
/// </summary>
public sealed partial class UnlockTracker : Node
{
    private const double PollInterval = 0.2;

    private Main _main = null!;
    private double _poll;
    private bool _committed;

    private int _bosses;
    private int _bossesUntouched;
    private int _chests;
    private int _levelUntouched = 1;
    private bool _hitThisRun;
    private bool _ended;
    private bool _won;

    /// <summary>Bosses currently in a fight, and whether the player has been hit
    /// since the fight started. A boss joins on its first hit from the player.</summary>
    private readonly Dictionary<Enemy, bool> _bossFights = new();
    private readonly List<Enemy> _scratch = new();

    public void Bind(Main main) => _main = main;

    public override void _Ready()
    {
        Name = "UnlockTracker";
        EventBus.Subscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Subscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Subscribe<LevelGained>(OnLevelGained);
        EventBus.Subscribe<ChestOpened>(OnChestOpened);
        EventBus.Subscribe<SecretDiscovered>(OnSecretDiscovered);
        EventBus.Subscribe<RunEnded>(OnRunEnded);
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Unsubscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Unsubscribe<LevelGained>(OnLevelGained);
        EventBus.Unsubscribe<ChestOpened>(OnChestOpened);
        EventBus.Unsubscribe<SecretDiscovered>(OnSecretDiscovered);
        EventBus.Unsubscribe<RunEnded>(OnRunEnded);
        Commit();
    }

    // ------------------------------------------------------------- events

    private void OnEnemyDamaged(EnemyDamaged e)
    {
        if (e.Target is Enemy { IsBoss: true } boss && !_bossFights.ContainsKey(boss))
            _bossFights[boss] = false;
    }

    private void OnPlayerDamaged(PlayerDamaged e)
    {
        if (e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f) return;
        _hitThisRun = true;
        _scratch.Clear();
        _scratch.AddRange(_bossFights.Keys);
        foreach (Enemy b in _scratch) _bossFights[b] = true;
    }

    private void OnLevelGained(LevelGained e)
    {
        if (!_hitThisRun) _levelUntouched = Mathf.Max(_levelUntouched, e.Level);
    }

    private void OnChestOpened(ChestOpened e) => _chests++;

    private void OnSecretDiscovered(SecretDiscovered e)
    {
        if (Profile.FindSecret(e.Kind)) Profile.Save();
        Evaluate();
    }

    private void OnRunEnded(RunEnded e)
    {
        _ended = true;
        // Survived alone is not a win: an automated run is finished early with
        // survived=true just to exercise the run-end path. A win is surviving to
        // the clock.
        _won = e.Survived && e.Seconds >= RunState.TargetSeconds - 0.5f;
        PollBosses();
        Evaluate();
        Commit();
    }

    // --------------------------------------------------------------- tick

    public override void _Process(double delta)
    {
        _poll += delta;
        if (_poll < PollInterval) return;
        _poll = 0;
        PollBosses();
        Evaluate();
    }

    /// <summary>
    /// A boss leaves the fight list by dying (counted) or by leaving the world
    /// some other way (not counted). Bosses are never recycled mid-run, so a
    /// dead flag on a tracked boss can only mean this fight ended in a kill.
    /// </summary>
    private void PollBosses()
    {
        if (_bossFights.Count == 0) return;
        _scratch.Clear();
        foreach (var (boss, hit) in _bossFights)
        {
            bool valid = IsInstanceValid(boss);
            if (valid && boss.IsAlive) continue;
            _scratch.Add(boss);
            if (!valid || boss.Health > 0f) continue;
            _bosses++;
            if (!hit) _bossesUntouched++;
            GD.Print($"META boss down untouched={!hit}");
        }
        foreach (Enemy b in _scratch) _bossFights.Remove(b);
    }

    // ---------------------------------------------------------- measuring

    private float Measure(Metric metric)
    {
        // --invulnerable makes "never hit" trivially true; a test flag must not
        // be able to hand out the two feats that are about not being hit.
        if (Screenshot.Invulnerable && metric is Metric.BossUntouched or Metric.LevelUntouched) return 0f;

        RunState run = _main.Run;
        Player player = _main.Player;
        var inv = _main.Inventory;
        return metric switch
        {
            Metric.TotalKills => Profile.Get(Profile.TotalKills) + run.Kills,
            Metric.TotalRuns => Profile.Get(Profile.Runs) + (_ended && !_committed ? 1 : 0),
            Metric.TotalWins => Profile.Get(Profile.Wins) + (_ended && _won && !_committed ? 1 : 0),
            Metric.TotalBosses => Profile.Get(Profile.BossesKilled) + (_committed ? 0 : _bosses),
            Metric.TotalChests => Profile.Get(Profile.ChestsOpened) + (_committed ? 0 : _chests),
            Metric.RunSeconds => run.Seconds,
            Metric.RunElites => run.Elites,
            Metric.RunSlams => player.Slams,
            Metric.RunAirJumps => player.AirJumpsUsed,
            Metric.RunLevel => inv.Level,
            Metric.RunItemStacks => inv.TotalStacks,
            Metric.RunGoldHeld => inv.Gold,
            Metric.PeakSpeed => player.PeakSpeed,
            Metric.BossUntouched => _bossesUntouched,
            Metric.LevelUntouched => _levelUntouched,
            Metric.SecretsAny => Profile.FoundSecrets.Count,
            Metric.SecretsAll => SecretSpawner.AllKindsFound() ? 1 : 0,
            _ => 0f,
        };
    }

    private void Evaluate()
    {
        if (_main is null || !IsInstanceValid(_main) || _main.Run is null) return;
        foreach (UnlockDef u in Unlocks.All)
        {
            if (Profile.IsUnlocked(u.Id)) continue;
            float value = Measure(u.Metric);
            if (value < u.Threshold) continue;
            if (!Profile.Unlock(u.Id)) continue;
            Profile.Save();
            GD.Print($"META unlock {u.Id} ({Unlocks.DescribeReward(u)}) at {u.Metric}={value:0.#} t={_main.Run.Seconds:0}");
            EventBus.Publish(new UnlockEarned(u.Id));
        }
    }

    // ------------------------------------------------------------- commit

    private void Commit()
    {
        if (_committed || _main is null || !IsInstanceValid(_main) || _main.Run is null) return;
        _committed = true;

        RunState run = _main.Run;
        Profile.Add(Profile.Runs, 1);
        if (_ended && _won) Profile.Add(Profile.Wins, 1);
        Profile.Add(Profile.TotalKills, run.Kills);
        Profile.Add(Profile.ElitesKilled, run.Elites);
        Profile.Add(Profile.BossesKilled, _bosses);
        Profile.Add(Profile.ChestsOpened, _chests);
        Profile.Add(Profile.TotalSlams, _main.Player.Slams);
        Profile.Best(Profile.BestSeconds, (long)run.Seconds);
        Profile.Best(Profile.BestKills, run.Kills);
        Profile.Best(Profile.BestLevel, _main.Inventory.Level);
        Profile.Save();
        GD.Print($"META profile runs={Profile.Get(Profile.Runs)} kills={Profile.Get(Profile.TotalKills)} unlocks={Profile.UnlockedIds.Count} secrets={Profile.FoundSecrets.Count}");
    }
}
