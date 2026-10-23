using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Game;

namespace Wavebreak.Meta;

/// <summary>
/// Pays out <see cref="Pearls"/> at the end of a run and records difficulty
/// wins.
///
/// Settled exactly once per run, from whichever comes first: the run ending
/// (death or win), the results screen asking for the numbers, or the run scene
/// being left early (main menu / quit from pause), so an abandoned run still
/// pays for what it did. The win itself is recorded the moment the last boss
/// dies (<see cref="FinalBossDefeated"/>), not at the end, because after that
/// the Final Swarm only ends in death and the player may leave through the
/// pause menu instead.
///
/// Payout, before the difficulty multiplier:
///   1 per 20 kills before the swarm, 1 per elite,
///   10 per phase cleared, 15 per phase boss, 60 for the win,
///   Final Swarm: 1 per 4 s survived + 1 per 10 swarm kills.
/// </summary>
public sealed partial class RunRewards : Node
{
    public const int KillsPerPearl = 20;
    public const int PerElite = 1;
    public const int PerPhaseCleared = 10;
    public const int PerBoss = 15;
    public const int WinBonus = 60;
    public const float SwarmSecondsPerPearl = 4f;
    public const int SwarmKillsPerPearl = 10;

    public sealed record Line(string Label, long Amount);

    public sealed record Payout(IReadOnlyList<Line> Lines, long Subtotal, DifficultyTier Tier, long Total, bool Won)
    {
        /// <summary>"+123 Pearls (Hard x1.5)".</summary>
        public string Headline => Tier.Reward > 1f
            ? $"+{Total} {Pearls.Name}  ({Tier.Name} x{Tier.Reward:0.##})"
            : $"+{Total} {Pearls.Name}";
    }

    /// <summary>The live run's settler, or null outside a run.</summary>
    public static RunRewards? Current { get; private set; }

    private Main _main = null!;
    private Payout? _settled;
    private bool _ended;

    public void Bind(Main main) => _main = main;

    public override void _Ready()
    {
        Name = "RunRewards";
        Current = this;
        EventBus.Subscribe<RunEnded>(OnRunEnded);
        EventBus.Subscribe<FinalBossDefeated>(OnFinalBoss);
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<RunEnded>(OnRunEnded);
        EventBus.Unsubscribe<FinalBossDefeated>(OnFinalBoss);
        // Leaving mid-run still pays. A won run left from the pause menu during
        // the swarm never publishes RunEnded, so its win is counted here.
        if (_settled is null && Valid && _main.Run.Seconds >= 1f)
        {
            if (!_ended && _main.Stages is { Won: true }) Profile.Add(Profile.Wins, 1);
            Settle();
        }
        if (Current == this) Current = null;
    }

    private bool Valid => _main is not null && IsInstanceValid(_main) && _main.Run is not null;

    private void OnRunEnded(RunEnded e)
    {
        _ended = true;
        Settle();
    }

    private void OnFinalBoss(FinalBossDefeated e)
    {
        DifficultyTier tier = DifficultyTiers.Active;
        DifficultyTier? opened = DifficultyTiers.RecordWin();
        Profile.Save();
        GD.Print($"META difficulty win tier={tier.Id} unlocked={opened?.Id ?? "-"}");
        if (opened is not null && Player.Instance is { } p && IsInstanceValid(p))
            EventBus.Publish(new PopupRequested(p.GlobalPosition + new Vector3(0f, 4.4f, 0f),
                $"{opened.Name.ToUpperInvariant()} UNLOCKED", new Color(1f, 0.84f, 0.38f), 1.3f));
    }

    /// <summary>
    /// The current run's payout, credited and saved on the first call; later
    /// calls return the same numbers without paying again. Null outside a run.
    /// </summary>
    public static Payout? Settle()
    {
        RunRewards? r = Current;
        if (r is null || !r.Valid) return null;
        if (r._settled is not null) return r._settled;

        Payout payout = r.Compute();
        r._settled = payout;
        Pearls.Earn(payout.Total);
        Profile.Save();
        GD.Print($"META pearls +{payout.Total} ({string.Join(", ", payout.Lines.Select(l => $"{l.Label}={l.Amount}"))}; " +
                 $"tier={payout.Tier.Id} x{payout.Tier.Reward:0.##}) balance={Pearls.Balance}");
        return payout;
    }

    /// <summary>What the run would pay if it ended now. Pure: credits nothing.</summary>
    public Payout Compute()
    {
        RunState run = _main.Run;
        StageDirector? stages = _main.Stages;
        FinalSwarm? swarm = stages?.Swarm;
        bool won = stages is { Won: true };
        int bosses = stages?.BossesDefeated ?? 0;
        int swarmKills = swarm?.Kills ?? 0;
        int kills = Math.Max(0, run.Kills - swarmKills);

        var lines = new List<Line>
        {
            new($"Kills ({kills})", kills / KillsPerPearl),
            new($"Elites ({run.Elites})", (long)run.Elites * PerElite),
            new($"Phases cleared ({bosses})", (long)bosses * PerPhaseCleared),
            new($"Bosses ({bosses})", (long)bosses * PerBoss),
        };
        if (won) lines.Add(new Line("Victory", WinBonus));
        if (swarm is not null)
            lines.Add(new Line($"Final Swarm ({RunState.Format(swarm.Seconds)}, {swarmKills} kills)",
                               (long)(swarm.Seconds / SwarmSecondsPerPearl) + swarmKills / SwarmKillsPerPearl));

        long subtotal = lines.Sum(l => l.Amount);
        DifficultyTier tier = DifficultyTiers.Active;
        long total = (long)MathF.Round(subtotal * tier.Reward);
        return new Payout(lines, subtotal, tier, total, won);
    }

    /// <summary>Fields appended to Main's SUMMARY line.</summary>
    public static string SummaryFields
    {
        get
        {
            RunRewards? r = Current;
            if (r is null || !r.Valid) return $"difficulty={DifficultyTiers.Active.Id}";
            Payout p = r._settled ?? r.Compute();
            float swarm = r._main.Stages?.Swarm?.Seconds ?? 0f;
            return $"difficulty={p.Tier.Id} won={(p.Won ? 1 : 0)} swarm={swarm:0} pearls={p.Total}";
        }
    }
}
