using System.Linq;
using System.Text;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Stats;

namespace Wavebreak.UI;

/// <summary>Info tab: frame rate, counts, seed and clock, and the player's
/// full stat list in the character sheet's own groups and formats.</summary>
public sealed partial class DevPanel
{
    private Control BuildInfoTab()
    {
        var split = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 24);

        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        left.AddChild(DevUi.Heading("RUNTIME"));
        Label runtime = DevUi.Lbl("", 15);
        runtime.VerticalAlignment = VerticalAlignment.Top;
        left.AddChild(runtime);
        split.AddChild(left);

        var stats = new Label[3];
        for (int i = 0; i < stats.Length; i++)
        {
            var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            col.AddChild(DevUi.Heading(i == 0 ? "PLAYER STATS" : " "));
            stats[i] = DevUi.Lbl("", 14);
            stats[i].VerticalAlignment = VerticalAlignment.Top;
            col.AddChild(stats[i]);
            split.AddChild(col);
        }

        OnRefresh(() =>
        {
            runtime.Text = RuntimeText();
            if (Main.Instance is not { } m || !IsInstanceValid(m))
            {
                stats[0].Text = Loc.T("Start a run to see the player's stats.");
                stats[1].Text = stats[2].Text = "";
                return;
            }
            StatSheet s = m.Player.Stats;
            var cols = new StringBuilder[3] { new(), new(), new() };
            int g = 0;
            foreach ((string title, Stat[] group) in StatsPanel.Groups)
            {
                StringBuilder sb = cols[System.Math.Min(2, g * 3 / StatsPanel.Groups.Length)];
                sb.AppendLine(Loc.T(title));
                foreach (Stat stat in group)
                    sb.AppendLine($"   {Loc.T(Catalog.StatName(stat))}:  {Loc.Ltr(StatsPanel.Format(s, stat, s.Get(stat)))}");
                sb.AppendLine();
                g++;
            }
            cols[2].AppendLine(Loc.T("CHARGES"));
            cols[2].AppendLine("   " + Loc.F("Rerolls {0:0}  ·  Banishes {1:0}  ·  Skips {2:0}", s.Get(Stat.Rerolls), s.Get(Stat.Banishes), s.Get(Stat.Skips)));
            for (int i = 0; i < 3; i++) stats[i].Text = cols[i].ToString().Replace("\r", "");
        });
        return split;
    }

    /// <summary>Active challenge names in the player's language, or "none".</summary>
    private static string ChallengeNames()
        => Loc.IsArabic
            ? (Challenges.Active.Any() ? string.Join("، ", Challenges.Active.Select(c => Loc.T(c.Name))) : Loc.T("none"))
            : Challenges.Describe(Challenges.Active);

    private static string RuntimeText()
    {
        var sb = new StringBuilder();
        // Each line is a table pattern, so Arabic gets its own words and
        // left-to-right numbers; English reads exactly as before.
        sb.AppendLine(Loc.F("FPS  {0:0}", Engine.GetFramesPerSecond()));
        sb.AppendLine(Loc.F("Time scale  x{0:0.##}", Engine.TimeScale));
        if (Main.Instance is { } m && IsInstanceValid(m) && m.Run is not null)
        {
            sb.AppendLine(Loc.F("Run time  {0}   (phase {1})", m.Run.Clock, m.Run.PhaseClock));
            sb.AppendLine(Loc.F("Seed  {0}", m.Run.Seed));
            sb.AppendLine(Loc.F("Phase  {0}/{1}  {2}  ·  {3}", m.Run.Phase, m.Run.PhaseCount, m.Run.StageName, m.Stages.State.ToString()));
            sb.AppendLine(Loc.F("Enemies alive  {0}  (cap {1})", m.Enemies.AliveCount, m.Enemies.MaxAlive));
            sb.AppendLine(Loc.F("Player shots  {0}", Projectile.ActiveCount));
            sb.AppendLine(Loc.F("Enemy shots  {0}   hazards {1}", EnemyAttacks.ActiveShots, EnemyAttacks.ActiveHazards));
            sb.AppendLine(Loc.F("Pickups  {0}", Pickup.ActiveCount));
            sb.AppendLine(Loc.F("Kills  {0}   elites {1}", m.Run.Kills, m.Run.Elites));
            sb.AppendLine(Loc.F("Level  {0}   gold {1}", m.Inventory.Level, m.Inventory.Gold));
            sb.AppendLine(Loc.F("Health  {0:0}/{1:0}", m.Player.Health.Current, m.Player.Health.Max));
            sb.AppendLine(Loc.F("Position  {0}", $"{m.Player.GlobalPosition.X:0.0}, {m.Player.GlobalPosition.Y:0.0}, {m.Player.GlobalPosition.Z:0.0}"));
            sb.AppendLine(Loc.F("Difficulty  {0}", DifficultyTiers.Active.Name));
            sb.AppendLine(Loc.F("Challenges  {0}", ChallengeNames()));
            sb.AppendLine(Loc.F("Counts for quests  {0}", UsedThisRun ? "no (developer panel used)" : "yes"));
        }
        else
        {
            sb.AppendLine(Loc.T("On the title screen."));
        }
        sb.AppendLine(Loc.F("Profile  {0}", Profile.Path ?? "(memory only)"));
        sb.AppendLine(Loc.F("Unlocks  {0}/{1}   {2} {3:N0}", Profile.UnlockedIds.Count, Unlocks.All.Count, Pearls.Name, Pearls.Balance));
        return sb.ToString().Replace("\r", "");
    }
}
