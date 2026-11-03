using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// <c>--check-gauntlet[=results.png]</c> with <c>--route=puzzles --auto</c>:
/// lets the bot play the whole Puzzle Gauntlet, then checks the run ended on
/// its own after the last puzzle with every medal on the list (none skipped
/// unless <c>--puzzle-skip</c>), the stage director never left the puzzle
/// maps for an action map, and the results screen is up; photographs it and
/// prints GAUNTLET PASS or GAUNTLET FAIL. Gives up after 20 minutes.
/// </summary>
public static class GauntletCheck
{
    private static string? Value()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == "--check-gauntlet") return "";
            if (a.StartsWith("--check-gauntlet=")) return a["--check-gauntlet=".Length..];
        }
        return null;
    }

    public static bool Requested => Value() is not null;

    public static async Task Run(Main main)
    {
        SceneTree tree = main.GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        string shot = Value() ?? "";
        bool ok = true;
        void Expect(string what, bool cond)
        {
            if (!cond) GD.Print($"GAUNTLET FAIL {what}");
            ok &= cond;
        }
        StageDirector stages = main.Stages;
        bool actionMap = false;
        int seen = -1;
        double start = Time.GetTicksMsec() / 1000.0;
        while (!main.Run.Over && Time.GetTicksMsec() / 1000.0 - start < 1200)
        {
            if (stages.State is not (StageDirector.PhaseState.Puzzle)) actionMap = true;
            if (stages.Puzzle is { } map && map.Plan.PuzzleIndex != seen)
            {
                seen = map.Plan.PuzzleIndex;
                GD.Print($"GAUNTLET puzzle {seen + 1}/{Meta.Routes.GauntletPuzzles} d={map.Plan.Difficulty} rooms={map.Plan.Describe()} guards={(map.Plan.Rooms.Exists(r => r.Enemies > 0) ? "yes" : "no")}");
                Expect($"puzzle {seen + 1} has no guards", !map.Plan.Rooms.Exists(r => r.Enemies > 0));
            }
            await Wait(0.5);
        }
        await Wait(1.5);
        List<RewardTier> medals = stages.PuzzleMedals;
        bool skipping = Array.IndexOf(OS.GetCmdlineUserArgs(), "--puzzle-skip") >= 0;
        GD.Print($"GAUNTLET end over={main.Run.Over} done={stages.GauntletDone} medals={string.Join(",", medals)} best={Meta.GauntletRecord.Best}");
        Expect("the run ended", main.Run.Over);
        Expect("the gauntlet was finished", stages.GauntletDone);
        Expect($"a medal for each of {Meta.Routes.GauntletPuzzles} puzzles ({medals.Count})", medals.Count == Meta.Routes.GauntletPuzzles);
        Expect("no action map was entered", !actionMap);
        if (!skipping) Expect("every puzzle solved, none skipped", !medals.Contains(RewardTier.Skipped) && !medals.Contains(RewardTier.None));
        Expect("the difficulty climbs", seen == Meta.Routes.GauntletPuzzles - 1);
        Expect("the results screen is up", tree.Paused);
        if (shot.Length > 0) await Screenshot.Capture(tree, main, shot);
        GD.Print(ok ? "GAUNTLET PASS" : "GAUNTLET FAIL");
        tree.Quit();
    }
}
