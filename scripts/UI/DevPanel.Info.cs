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
                stats[0].Text = "Start a run to see the player's stats.";
                stats[1].Text = stats[2].Text = "";
                return;
            }
            StatSheet s = m.Player.Stats;
            var cols = new StringBuilder[3] { new(), new(), new() };
            int g = 0;
            foreach ((string title, Stat[] group) in StatsPanel.Groups)
            {
                StringBuilder sb = cols[System.Math.Min(2, g * 3 / StatsPanel.Groups.Length)];
                sb.AppendLine(title);
                foreach (Stat stat in group)
                    sb.AppendLine($"   {Catalog.StatName(stat)}:  {StatsPanel.Format(s, stat, s.Get(stat))}");
                sb.AppendLine();
                g++;
            }
            cols[2].AppendLine("CHARGES");
            cols[2].AppendLine($"   Rerolls {s.Get(Stat.Rerolls):0}  ·  Banishes {s.Get(Stat.Banishes):0}  ·  Skips {s.Get(Stat.Skips):0}");
            for (int i = 0; i < 3; i++) stats[i].Text = cols[i].ToString().Replace("\r", "");
        });
        return split;
    }

    private static string RuntimeText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"FPS  {Engine.GetFramesPerSecond():0}");
        sb.AppendLine($"Time scale  x{Engine.TimeScale:0.##}");
        if (Main.Instance is { } m && IsInstanceValid(m) && m.Run is not null)
        {
            sb.AppendLine($"Run time  {m.Run.Clock}   (phase {m.Run.PhaseClock})");
            sb.AppendLine($"Seed  {m.Run.Seed}");
            sb.AppendLine($"Phase  {m.Run.Phase}/{m.Run.PhaseCount}  {m.Run.StageName}  ·  {m.Stages.State}");
            sb.AppendLine($"Enemies alive  {m.Enemies.AliveCount}  (cap {m.Enemies.MaxAlive})");
            sb.AppendLine($"Player shots  {Projectile.ActiveCount}");
            sb.AppendLine($"Enemy shots  {EnemyAttacks.ActiveShots}   hazards {EnemyAttacks.ActiveHazards}");
            sb.AppendLine($"Pickups  {Pickup.ActiveCount}");
            sb.AppendLine($"Kills  {m.Run.Kills}   elites {m.Run.Elites}");
            sb.AppendLine($"Level  {m.Inventory.Level}   gold {m.Inventory.Gold}");
            sb.AppendLine($"Health  {m.Player.Health.Current:0}/{m.Player.Health.Max:0}");
            sb.AppendLine($"Position  {m.Player.GlobalPosition.X:0.0}, {m.Player.GlobalPosition.Y:0.0}, {m.Player.GlobalPosition.Z:0.0}");
            sb.AppendLine($"Difficulty  {DifficultyTiers.Active.Name}");
            sb.AppendLine($"Challenges  {Challenges.Describe(Challenges.Active)}");
            sb.AppendLine($"Counts for quests  {(UsedThisRun ? "no (developer panel used)" : "yes")}");
        }
        else
        {
            sb.AppendLine("On the title screen.");
        }
        sb.AppendLine($"Profile  {Profile.Path ?? "(memory only)"}");
        sb.AppendLine($"Unlocks  {Profile.UnlockedIds.Count}/{Unlocks.All.Count}   {Pearls.Name} {Pearls.Balance:N0}");
        return sb.ToString().Replace("\r", "");
    }
}
