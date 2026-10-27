using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.Game;

/// <summary>
/// Self-checks for the run's length and the boss portal's countdown.
///
/// <c>--check-phases</c> (add <c>--difficulty=&lt;id&gt;</c>): the run in
/// progress has the active tier's number of phases, no map twice, rising enemy
/// numbers, and the final boss's health share only on the last map; then the
/// same for every tier and every route, built offline. Prints PHASES lines and
/// PHASES PASS/FAIL.
///
/// <c>--check-portal-timer</c> (add <c>--portal-seconds=N</c> to keep it short,
/// <c>--portal-shot=&lt;png&gt;</c> for a frame of the countdown): opens the
/// portal with the player held still far from it and checks the countdown runs
/// and, at zero, pulls the player through. Prints PORTALTIMER PASS/FAIL.
/// </summary>
public static class RunLengthCheck
{
    public static bool Requested()
    {
        string[] args = OS.GetCmdlineUserArgs();
        return Array.IndexOf(args, "--check-phases") >= 0 || Array.IndexOf(args, "--check-portal-timer") >= 0;
    }

    public static async Task Run(Main main)
    {
        SceneTree tree = main.GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        await Wait(1.0);
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-phases") >= 0) CheckPhases(main);
        else await CheckPortalTimer(main, Wait);
        tree.Quit();
    }

    private static void CheckPhases(Main main)
    {
        bool ok = true;
        void Expect(string what, bool c)
        {
            GD.Print($"PHASES {(c ? "ok  " : "FAIL")} {what}");
            ok &= c;
        }

        DifficultyTier active = DifficultyTiers.Active;
        IReadOnlyList<StageDef> route = main.Stages.Route;
        Expect($"tier {active.Id}: run has {route.Count} phases, tier wants {active.Phases} ({string.Join(" > ", route.Select(s => s.Id))})",
               route.Count == Routes.PhasesFor(active));
        Expect($"HUD phase count {main.Run.PhaseCount} matches", main.Run.PhaseCount == route.Count);
        CheckRoute(Expect, $"run ({active.Id})", route);

        foreach (DifficultyTier tier in DifficultyTiers.All)
            foreach (RouteDef r in Routes.All)
            {
                IReadOnlyList<StageDef> maps = Routes.Build(r, 7UL, Routes.PhasesFor(tier));
                bool countOk = maps.Count == Routes.PhasesFor(tier);
                if (!countOk) Expect($"{tier.Id}/{r.Id}: {maps.Count} maps, want {tier.Phases}", false);
                CheckRoute(Expect, $"{tier.Id}/{r.Id}", maps, quiet: true);
            }
        Expect("every tier x route builds the right number of distinct, rising maps", ok);
        Expect("Normal has 4 phases", Routes.PhasesFor(DifficultyTiers.All[0]) == 4);
        Expect("Abyss has 6 phases (every map)", Routes.PhasesFor(DifficultyTiers.ById("abyss")!) == 6);
        Expect("the long map order includes Stormwatch Harbour", System.Array.IndexOf(Stages.LongOrder, "stormwatch") >= 0);
        GD.Print(ok ? "PHASES PASS" : "PHASES FAIL");
    }

    private static void CheckRoute(Action<string, bool> expect, string label, IReadOnlyList<StageDef> maps, bool quiet = false)
    {
        void E(string what, bool c) { if (!quiet || !c) expect(what, c); }
        E($"{label}: no map twice", maps.Select(m => m.Id).Distinct().Count() == maps.Count);
        for (int i = 1; i < maps.Count; i++)
            E($"{label}: phase {i + 1} harder than phase {i} (hp x{maps[i - 1].EnemyHealthScale:0.00} -> x{maps[i].EnemyHealthScale:0.00})",
              maps[i].EnemyHealthScale > maps[i - 1].EnemyHealthScale && maps[i].EnemyDamageScale >= maps[i - 1].EnemyDamageScale);
        for (int i = 0; i < maps.Count; i++)
        {
            bool last = i == maps.Count - 1;
            E($"{label}: phase {i + 1} boss share x{maps[i].BossHealthScale:0.00} ({(last ? "final" : "gate")})",
              last ? maps[i].BossHealthScale <= maps[0].BossHealthScale : maps[i].BossHealthScale >= 0.5f);
        }
    }

    private static async Task CheckPortalTimer(Main main, Func<double, Task> wait)
    {
        bool ok = true;
        void Expect(string what, bool c)
        {
            GD.Print($"PORTALTIMER {(c ? "ok  " : "FAIL")} {what}");
            ok &= c;
        }
        StageDirector st = main.Stages;
        Actors.Player player = main.Player;
        // Stand still far from the centre, where the portal opens.
        player.Teleport(new Vector3(30f, 1f, 0f));
        player.MoveOverride = () => Vector3.Zero;
        Expect("portal opens", st.DevOpenPortal());
        float full = st.PortalSecondsLeft;
        Expect($"countdown starts at the tier's {st.PortalSeconds:0}s ({DifficultyTiers.Active.Id})", Mathf.Abs(full - st.PortalSeconds) < 0.5f);
        await wait(2.0);
        float later = st.PortalSecondsLeft;
        Expect($"countdown runs ({full:0.0} -> {later:0.0})", later < full - 1f && later > 0f);
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--portal-shot="))
            {
                // Face the portal for the frame.
                Actors.PlayerCamera.Instance?.FaceDirection(-player.GlobalPosition);
                await wait(0.6);
                await Screenshot.Capture(main.GetTree(), main, arg["--portal-shot=".Length..]);
            }
        double start = Time.GetTicksMsec() / 1000.0;
        while (st.State == StageDirector.PhaseState.PortalOpen && Time.GetTicksMsec() / 1000.0 - start < st.PortalSeconds + 20)
            await wait(0.1);
        Expect($"at zero the player is pulled through ({st.PortalExitReason}, state {st.State})",
               st.PortalExitReason == "timeout" && st.State is StageDirector.PhaseState.RestRoom or StageDirector.PhaseState.Waves);
        GD.Print(ok ? "PORTALTIMER PASS" : "PORTALTIMER FAIL");
    }
}
