using System.Collections.Generic;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Progression;

namespace Wavebreak.Meta;

/// <summary>
/// The mastery half of the tracker (see <see cref="MasteryQuests"/>).
///
/// Cheap by construction: event handlers bump ints or add to small sets,
/// status applications are plain static counters (<see cref="StatusTally"/>),
/// and the merchant/microwave/pot/shrine numbers are copied from the systems
/// that already count them on the 0.2 s poll — never per frame. Measuring a
/// row reads <see cref="UnlockDef.StatKey"/>, built once, so the poll does not
/// allocate key strings.
/// </summary>
public sealed partial class UnlockTracker
{
    private int _megacrits, _jumps, _airJumps, _dashes;
    private int _merchantSpent, _merchantBuys, _microwaves, _pots, _charges, _shrineChallenges, _greed;
    private float _swarmFrom = -1f;
    private bool _victory;
    private bool _quietChecked;
    private readonly HashSet<string> _maxedWeapons = new();
    private readonly HashSet<string> _maxedTomes = new();
    private readonly HashSet<string> _feats = new();
    /// <summary>Phase bosses beaten this run, keyed by the map's profile key.</summary>
    private readonly Dictionary<string, int> _mapClears = new();

    private void SubscribeMastery()
    {
        StatusTally.Reset();
        EventBus.Subscribe<EnemyDamaged>(OnMasteryEnemyDamaged);
        EventBus.Subscribe<PlayerMoved>(OnMasteryMoved);
        EventBus.Subscribe<WeaponAcquired>(OnMasteryWeapon);
        EventBus.Subscribe<TomeAcquired>(OnMasteryTome);
        EventBus.Subscribe<PortalOpened>(OnMasteryPortal);
        EventBus.Subscribe<FinalBossDefeated>(OnMasteryFinalBoss);
    }

    private void UnsubscribeMastery()
    {
        EventBus.Unsubscribe<EnemyDamaged>(OnMasteryEnemyDamaged);
        EventBus.Unsubscribe<PlayerMoved>(OnMasteryMoved);
        EventBus.Unsubscribe<WeaponAcquired>(OnMasteryWeapon);
        EventBus.Unsubscribe<TomeAcquired>(OnMasteryTome);
        EventBus.Unsubscribe<PortalOpened>(OnMasteryPortal);
        EventBus.Unsubscribe<FinalBossDefeated>(OnMasteryFinalBoss);
    }

    // ------------------------------------------------------------- events

    private void OnMasteryEnemyDamaged(EnemyDamaged e)
    {
        if (e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f) return;
        if (e.Info.Megacrit) _megacrits++;
        if (e.Target is Enemy { IsBoss: true, IsAlive: false })
        {
            if (e.Info.Megacrit) _feats.Add(Quests.FeatBossMegacrit);
            if (e.Info.Source == "slam") _feats.Add(Quests.FeatBossSlam);
        }
    }

    private void OnMasteryMoved(PlayerMoved e)
    {
        switch (e.Verb)
        {
            case MoveVerb.Jump: _jumps++; break;
            case MoveVerb.AirJump: _airJumps++; break;
            case MoveVerb.Dash: _dashes++; break;
        }
    }

    private void OnMasteryWeapon(WeaponAcquired e)
    {
        WeaponDef? def = Catalog.WeaponById(e.Id) ?? MetaContent.WeaponById(e.Id);
        if (def is not null && e.NewLevel >= def.MaxLevel) _maxedWeapons.Add(e.Id);
    }

    private void OnMasteryTome(TomeAcquired e)
    {
        TomeDef? def = Tomes.ById(e.Id);
        if (def is not null && e.NewLevel >= def.MaxLevel) _maxedTomes.Add(e.Id);
    }

    private void OnMasteryPortal(PortalOpened e) => NoteMapClear();

    /// <summary>The run is won at this moment: count the last map's clear,
    /// start the Final Swarm clock and judge the "win while ..." feats on the
    /// state the win was earned in, not whatever the swarm adds afterwards.</summary>
    private void OnMasteryFinalBoss(FinalBossDefeated e)
    {
        if (_victory) return;
        _victory = true;
        NoteMapClear();
        if (e.SwarmFollows) _swarmFrom = e.Seconds;
        PollMastery();
        if (_main is null || !IsInstanceValid(_main)) return;
        if (_chests == 0) _feats.Add(Quests.FeatNoChestWin);
        if (_main.Inventory.WeaponLevels.Count == 1) _feats.Add(Quests.FeatOneWeaponWin);
        if (_greed >= 5) _feats.Add(Quests.FeatGreedyWin);
        if (Challenges.Active.Count >= 3) _feats.Add(Quests.FeatCrowdedWin);
        if (_main.Inventory.Level <= 25) _feats.Add(Quests.FeatLowLevelWin);
        Evaluate();
    }

    private void NoteMapClear()
    {
        string? stage = _main?.Run?.StageId;
        if (string.IsNullOrEmpty(stage)) return;
        string key = Quests.KeyMapClears(stage);   // rare event: one string per boss
        _mapClears[key] = _mapClears.GetValueOrDefault(key) + 1;
    }

    // --------------------------------------------------------------- poll

    /// <summary>Copy the counters other systems keep, and check the timed feat.
    /// Run from the 0.2 s poll and once more when the run ends.</summary>
    private void PollMastery()
    {
        if (Interactables.Instance is { } it && IsInstanceValid(it))
        {
            _merchantSpent = it.MerchantSpent;
            _merchantBuys = it.MerchantBuys;
            _microwaves = it.MicrowaveUses;
            _pots = it.PotsBroken;
        }
        if (ShrineSpawner.Instance is { } sh && IsInstanceValid(sh))
        {
            _charges = sh.Charged;
            _shrineChallenges = sh.ChallengesWon;
            _greed = sh.GreedStacks;
        }
        if (StatusTally.BossFreezes > 0) _feats.Add(Quests.FeatBossFreeze);
        if (!_quietChecked && _main?.Run is { } run && run.Seconds >= 60f)
        {
            _quietChecked = true;
            if (run.Kills == 0) _feats.Add(Quests.FeatQuietMinute);
        }
    }

    // ---------------------------------------------------------- measuring

    private bool WonThisRun => _victory || (_ended && _won);

    private static bool HardOrAbove => DifficultyTiers.IndexOf(DifficultyTiers.Active) >= 1;

    private bool TryMeasureMastery(UnlockDef u, bool live, out float value)
    {
        RunState run = _main.Run;
        long stored = u.StatKey.Length > 0 ? Profile.Get(u.StatKey) : 0;
        bool winLive = live && WonThisRun;
        value = u.Metric switch
        {
            Metric.WeaponMaxed => stored > 0 || _maxedWeapons.Contains(u.Key) ? 1 : 0,
            Metric.TomeMaxed => stored > 0 || _maxedTomes.Contains(u.Key) ? 1 : 0,
            Metric.Feat => stored > 0 || _feats.Contains(u.Key) ? 1 : 0,
            Metric.CharacterKills => stored + (live && RunMeta.Character.Id == u.Key ? run.Kills : 0),
            Metric.CharacterHardWins => stored + (winLive && HardOrAbove && RunMeta.Character.Id == u.Key ? 1 : 0),
            Metric.DifficultyWins => stored + (winLive && DifficultyTiers.Active.Id == u.Key ? 1 : 0),
            Metric.MapClears => stored + (live ? _mapClears.GetValueOrDefault(u.StatKey) : 0),
            Metric.MapsCleared => MapsCleared(live),
            Metric.ChallengeWins => stored + (winLive && Challenges.IsActive(u.Key) ? 1 : 0),
            Metric.ChallengesWon => ChallengesWon(winLive),
            Metric.TotalBurns => stored + (live ? StatusTally.Burns : 0),
            Metric.TotalFreezes => stored + (live ? StatusTally.Freezes : 0),
            Metric.TotalShocks => stored + (live ? StatusTally.Shocks : 0),
            Metric.TotalPoisons => stored + (live ? StatusTally.Poisons : 0),
            Metric.TotalMegacrits => stored + (live ? _megacrits : 0),
            Metric.RunFreezes => StatusTally.Freezes,
            Metric.RunMegacrits => _megacrits,
            Metric.TotalMerchantGold => stored + (live ? _merchantSpent : 0),
            Metric.RunMerchantBuys => _merchantBuys,
            Metric.TotalMicrowaves => stored + (live ? _microwaves : 0),
            Metric.TotalPots => stored + (live ? _pots : 0),
            Metric.RunPots => _pots,
            Metric.TotalCharges => stored + (live ? _charges : 0),
            Metric.TotalShrineChallenges => stored + (live ? _shrineChallenges : 0),
            Metric.RunGreed => _greed,
            Metric.TotalGreed => stored + (live ? _greed : 0),
            Metric.TotalJumps => stored + (live ? _jumps : 0),
            Metric.TotalAirJumps => stored + (live ? _airJumps : 0),
            Metric.TotalDashes => stored + (live ? _dashes : 0),
            Metric.RunDashes => _dashes,
            Metric.TotalSlams => stored + (live ? _main.Player.Slams : 0),
            Metric.SwarmSeconds => _swarmFrom >= 0f ? System.MathF.Max(0f, run.Seconds - _swarmFrom) : 0f,
            Metric.TotalElites => stored + (live ? run.Elites : 0),
            Metric.TotalGoldEarned => stored + (live ? _goldEarned : 0),
            Metric.RunTomesMaxed => _maxedTomes.Count,
            _ => float.NaN,
        };
        return !float.IsNaN(value);
    }

    private int MapsCleared(bool live)
    {
        int n = 0;
        foreach (string k in Quests.MapKeys)
            if (Profile.Get(k) > 0 || (live && _mapClears.ContainsKey(k))) n++;
        return n;
    }

    private int ChallengesWon(bool winLive)
    {
        int n = 0;
        string[] keys = Quests.ChallengeKeys;
        for (int i = 0; i < keys.Length; i++)
            if (Profile.Get(keys[i]) > 0 || (winLive && Challenges.IsActive(Challenges.All[i].Id))) n++;
        return n;
    }

    // ------------------------------------------------------------- commit

    private void CommitMastery()
    {
        PollMastery();
        RunState run = _main.Run;
        string who = RunMeta.Character.Id;

        foreach (string id in _maxedWeapons) Profile.Best(Quests.KeyWeaponMaxed(id), 1);
        foreach (string id in _maxedTomes) Profile.Best(Quests.KeyTomeMaxed(id), 1);
        foreach (string f in _feats) Profile.Best(Quests.KeyFeat(f), 1);
        foreach (var (key, n) in _mapClears) Profile.Add(key, n);
        Profile.Add(Quests.KeyCharacterKills(who), run.Kills);

        if (WonThisRun)
        {
            Profile.Add(Quests.KeyDifficultyWins(DifficultyTiers.Active.Id), 1);
            if (HardOrAbove) Profile.Add(Quests.KeyCharacterHardWins(who), 1);
            foreach (ChallengeDef c in Challenges.Active) Profile.Add(Quests.KeyChallengeWins(c.Id), 1);
        }

        AddLifetime(Metric.TotalBurns, StatusTally.Burns);
        AddLifetime(Metric.TotalFreezes, StatusTally.Freezes);
        AddLifetime(Metric.TotalShocks, StatusTally.Shocks);
        AddLifetime(Metric.TotalPoisons, StatusTally.Poisons);
        AddLifetime(Metric.TotalMegacrits, _megacrits);
        AddLifetime(Metric.TotalMerchantGold, _merchantSpent);
        AddLifetime(Metric.TotalMicrowaves, _microwaves);
        AddLifetime(Metric.TotalPots, _pots);
        AddLifetime(Metric.TotalCharges, _charges);
        AddLifetime(Metric.TotalShrineChallenges, _shrineChallenges);
        AddLifetime(Metric.TotalGreed, _greed);
        AddLifetime(Metric.TotalJumps, _jumps);
        AddLifetime(Metric.TotalAirJumps, _airJumps);
        AddLifetime(Metric.TotalDashes, _dashes);
        AddLifetime(Metric.TotalGoldEarned, _goldEarned);
        // TotalSlams and TotalElites are the profile's own Slams/ElitesKilled,
        // which Commit already adds.
    }

    private static void AddLifetime(Metric m, long amount)
    {
        if (amount > 0 && Quests.LifetimeKey(m) is { } key) Profile.Add(key, amount);
    }
}
