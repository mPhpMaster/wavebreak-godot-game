using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Core;
using Wavebreak.Meta;
using Wavebreak.Net;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class ScoreboardCheck : Node
{
    public override void _Ready() => _ = Run();
    private async Task Wait(float time) => await ToSignal(GetTree().CreateTimer(time), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool value, string label)
    {
        GD.Print($"SCOREBOARD {(value ? "OK" : "FAIL")} {label}");
        if (!value) throw new InvalidOperationException(label);
    }
    private async Task Run()
    {
        try
        {
            await Wait(.7f); Main.Instance!.SetProcess(false); EnemyManagerStop();
            var meter = new DamageWindow(0); meter.Add(0, 100);
            Require(meter.Rate(5) == 20 && meter.Rate(10) == 0, "DPS uses a bounded ten-second window and expires old damage");
            RunScoreboard.Begin(); RunScoreboard.NoteHit(Coop.LocalId, 250);
            EventBus.Publish(new PlayerHealed(25)); RunScoreboard.RefreshLocal();
            RunScoreboard.ApplySelf(99, 2, 4, 80, 15, 50); RunScoreboard.NoteHit(99, 150); RunScoreboard.NoteKill(99, false);
            RunScoreboard.Line(99).Name = "Test teammate"; RunScoreboard.Line(99).Deaths = 1;
            long[] packed = RunScoreboard.Pack();
            Require(packed.Length == 24 && RunScoreboard.Line(Coop.LocalId).HealingRestored == 25, "actual restored health and complete player rows are recorded");
            RunScoreboard.Apply(packed);
            Require(RunScoreboard.Line(99).HealingRestored == 50 && RunScoreboard.Line(99).Dps > 0
                && RunScoreboard.Line(99).Downs == 2 && RunScoreboard.Line(99).Deaths == 1,
                "network payload preserves DPS, healing and distinct deaths/downs");
            var ordered = new List<ScoreLine>(); ScoreboardOrdering.Fill(RunScoreboard.All, ordered, ScoreMetric.Healing);
            Require(ordered[0].Id == 99 && ScoreboardOrdering.Leads(ordered[0], ScoreMetric.Healing, ordered), "sorting and leader selection use the selected statistic");
            Require(Keybinds.Get("scoreboard", 0) is InputEventKey { PhysicalKeycode: Key.Tab }, "Tab opens the scoreboard by default");
            Input.ActionPress("scoreboard"); await Wait(.1f);
            Require(ScoreboardPanel.Instance?.Visible == true && !GetTree().Paused, "real scoreboard opens while gameplay remains unpaused");
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--scoreboard-shot=")) await Screenshot.Capture(GetTree(), this, arg["--scoreboard-shot=".Length..]);
            Input.ActionRelease("scoreboard"); await Wait(.1f);
            Require(ScoreboardPanel.Instance?.Visible == false, "releasing Tab closes the live table");
            GD.Print("SCOREBOARD PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("SCOREBOARD FAIL " + error); GetTree().Quit(1); }
        finally { Input.ActionRelease("scoreboard"); }
    }
    private static void EnemyManagerStop()
    { if (Actors.EnemyManager.Instance is { } enemies) { enemies.ClearField(999); enemies.Quiet = true; } }
}
