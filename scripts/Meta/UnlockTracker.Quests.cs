using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;

namespace Wavebreak.Meta;

/// <summary>
/// The quest half of the tracker (see <see cref="Quests"/>): the counters the
/// original unlocks never needed — kills per weapon, slam kills, distance slid,
/// crits, which phase bosses fell, whether a phase was played clean — and the
/// lifetime/best-value writes that go with them.
///
/// Everything comes off the bus. Kills are attributed from
/// <see cref="EnemyDamaged"/>: an enemy that is no longer alive right after a
/// reported hit died to that hit, and a corpse nullifies every later hit, so a
/// kill can be counted exactly once without a hook in the enemy code.
/// </summary>
public sealed partial class UnlockTracker
{
    // ---- this run
    private readonly Dictionary<string, int> _weaponKills = new();
    private int _slamKills;
    private float _slideMetres;
    private int _shrinesThisRun;
    private int _crits;
    private int _goldEarned;
    private float _healed;
    private int _phase;
    private bool _hitThisPhase;
    private bool _phase1Clean;
    private bool _finalBossDown;
    private readonly HashSet<int> _phaseBosses = new();
    private readonly HashSet<int> _cleanPhases = new();

    private void SubscribeQuests()
    {
        EventBus.Subscribe<EnemyDamaged>(OnQuestEnemyDamaged);
        EventBus.Subscribe<PlayerDamaged>(OnQuestPlayerDamaged);
        EventBus.Subscribe<PlayerHealed>(OnQuestHealed);
        EventBus.Subscribe<GoldGained>(OnQuestGold);
        EventBus.Subscribe<PhaseStarted>(OnQuestPhaseStarted);
        EventBus.Subscribe<PortalOpened>(OnQuestPortalOpened);
        EventBus.Subscribe<SecretDiscovered>(OnQuestSecret);
    }

    private void UnsubscribeQuests()
    {
        EventBus.Unsubscribe<EnemyDamaged>(OnQuestEnemyDamaged);
        EventBus.Unsubscribe<PlayerDamaged>(OnQuestPlayerDamaged);
        EventBus.Unsubscribe<PlayerHealed>(OnQuestHealed);
        EventBus.Unsubscribe<GoldGained>(OnQuestGold);
        EventBus.Unsubscribe<PhaseStarted>(OnQuestPhaseStarted);
        EventBus.Unsubscribe<PortalOpened>(OnQuestPortalOpened);
        EventBus.Unsubscribe<SecretDiscovered>(OnQuestSecret);
    }

    // ------------------------------------------------------------- events

    private void OnQuestEnemyDamaged(EnemyDamaged e)
    {
        if (e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f) return;
        if (e.Info.Crit) _crits++;
        if (e.Target is null || e.Target.IsAlive) return;

        string source = e.Info.Source ?? "";
        if (source == "slam") _slamKills++;
        else if (Catalog.WeaponById(source) is not null || MetaContent.WeaponById(source) is not null)
            _weaponKills[source] = _weaponKills.GetValueOrDefault(source) + 1;
    }

    private void OnQuestPlayerDamaged(PlayerDamaged e)
    {
        if (e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f) return;
        _hitThisPhase = true;
        if (_phase <= 1) _phase1Clean = false;
    }

    private void OnQuestHealed(PlayerHealed e)
    {
        if (e.Amount > 0f) _healed += e.Amount;
    }

    private void OnQuestGold(GoldGained e)
    {
        if (e.Amount > 0) _goldEarned += e.Amount;
    }

    private void OnQuestPhaseStarted(PhaseStarted e)
    {
        if (e.Phase == 1 && _phase == 0) _phase1Clean = !_hitThisRun;
        _phase = Mathf.Max(_phase, e.Phase);
        _hitThisPhase = false;
    }

    /// <summary>A portal opens exactly when a phase boss (not the last) dies.</summary>
    private void OnQuestPortalOpened(PortalOpened e) => PhaseCleared(e.Phase);

    private void OnQuestSecret(SecretDiscovered e) => _shrinesThisRun++;

    private void PhaseCleared(int phase)
    {
        if (phase <= 0 || !_phaseBosses.Add(phase)) return;
        if (!_hitThisPhase && !Screenshot.Invulnerable) _cleanPhases.Add(phase);
        GD.Print($"META quest phase {phase} boss down clean={!_hitThisPhase}");
    }

    // --------------------------------------------------------------- tick

    /// <summary>Per-frame measurements: distance slid, and the final boss (which
    /// opens no portal — the stage director goes straight to victory).</summary>
    private void TickQuests(double delta)
    {
        if (_main is null || !IsInstanceValid(_main)) return;
        Player player = _main.Player;
        if (player is not null && IsInstanceValid(player) && player.IsSliding)
            _slideMetres += player.PlanarVelocity.Length() * (float)delta;

        if (!_finalBossDown && _main.Stages is { State: StageDirector.PhaseState.Victory })
        {
            _finalBossDown = true;
            PhaseCleared(Mathf.Max(_phase, _main.Run.PhaseCount));
        }
    }

    // ---------------------------------------------------------- measuring

    /// <summary>The value of one quest condition right now, lifetime totals
    /// including the run in progress (until it has been committed).</summary>
    private float MeasureUnlock(UnlockDef u)
    {
        if (Screenshot.Invulnerable && u.Metric is Metric.PhaseUntouched or Metric.UntouchedPhaseWin) return 0f;
        bool live = !_committed;
        switch (u.Metric)
        {
            case Metric.WeaponKills:
                return Profile.Get(Quests.KeyWeaponKills(u.Key)) + (live ? _weaponKills.GetValueOrDefault(u.Key) : 0);
            case Metric.RunKills: return _main.Run.Kills;
            case Metric.RunSlamKills: return _slamKills;
            case Metric.TotalSlamKills: return Profile.Get(Quests.KeySlamKills) + (live ? _slamKills : 0);
            case Metric.RunSlideMetres: return _slideMetres;
            case Metric.TotalSlideMetres: return Profile.Get(Quests.KeySlideMetres) + (live ? (long)_slideMetres : 0);
            case Metric.PhaseBossBeaten:
                return Profile.Get(Quests.KeyPhaseBoss(u.Key)) > 0 || (int.TryParse(u.Key, out int p) && _phaseBosses.Contains(p)) ? 1 : 0;
            case Metric.CharacterWins:
                return Profile.Get(Quests.KeyCharacterWins(u.Key)) + (live && WonAs(u.Key) ? 1 : 0);
            case Metric.CharactersWon:
                return Quests.CharactersWon() + (live && _ended && _won && Profile.Get(Quests.KeyCharacterWins(RunMeta.Character.Id)) == 0 ? 1 : 0);
            case Metric.RunChests: return _chests;
            case Metric.PhaseUntouched:
                return Profile.Get(Quests.KeyPhaseUntouched(u.Key)) > 0 || (int.TryParse(u.Key, out int c) && _cleanPhases.Contains(c)) ? 1 : 0;
            case Metric.UntouchedPhaseWin:
                return Profile.Get(Quests.KeyUntouchedWin) > 0 || (_ended && _won && _phase1Clean) ? 1 : 0;
            case Metric.RunShrines: return _shrinesThisRun;
            case Metric.TotalShrines: return Profile.Get(Quests.KeyShrines) + (live ? _shrinesThisRun : 0);
            case Metric.RunPhase: return _phase;
            case Metric.TotalCrits: return Profile.Get(Quests.KeyCrits) + (live ? _crits : 0);
            case Metric.RunGoldEarned: return _goldEarned;
            case Metric.RunMaxedWeapons: return MaxedWeapons();
            case Metric.RunHealed: return _healed;
            default: return Measure(u.Metric);
        }
    }

    private bool WonAs(string characterId) => _ended && _won && RunMeta.Character.Id == characterId;

    private int MaxedWeapons()
    {
        int n = 0;
        foreach (var (id, level) in _main.Inventory.WeaponLevels)
        {
            WeaponDef? def = Catalog.WeaponById(id) ?? MetaContent.WeaponById(id);
            if (def is not null && level >= def.MaxLevel) n++;
        }
        return n;
    }

    // ------------------------------------------------------------- commit

    private void CommitQuests()
    {
        foreach (var (weapon, kills) in _weaponKills) Profile.Add(Quests.KeyWeaponKills(weapon), kills);
        Profile.Add(Quests.KeySlamKills, _slamKills);
        Profile.Add(Quests.KeySlideMetres, (long)_slideMetres);
        Profile.Add(Quests.KeyShrines, _shrinesThisRun);
        Profile.Add(Quests.KeyCrits, _crits);
        foreach (int p in _phaseBosses) Profile.Best(Quests.KeyPhaseBoss(p.ToString()), 1);
        foreach (int p in _cleanPhases) Profile.Best(Quests.KeyPhaseUntouched(p.ToString()), 1);
        if (_ended && _won)
        {
            Profile.Add(Quests.KeyCharacterWins(RunMeta.Character.Id), 1);
            if (_phase1Clean && !Screenshot.Invulnerable) Profile.Best(Quests.KeyUntouchedWin, 1);
        }

        // Personal bests of every single-run metric any unlock uses, for the
        // codex's "best" line. Measured before _committed flips the totals.
        var seen = new HashSet<Metric>();
        foreach (UnlockDef u in Unlocks.All)
        {
            if (!Quests.IsRunMetric(u.Metric) || !seen.Add(u.Metric)) continue;
            Profile.Best(Quests.KeyBest(u.Metric), (long)MeasureUnlock(u));
        }
    }
}
