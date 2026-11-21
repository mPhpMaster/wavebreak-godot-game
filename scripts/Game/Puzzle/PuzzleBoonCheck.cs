using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Stats;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// <c>--check-puzzle-boons[=shot.png]</c>: the puzzle boons
/// (<see cref="PuzzleBoons"/>). On paper: every boon string has Arabic, the
/// line of boons is the same for every machine and team size, a stretch's
/// first six are all different, and no stretch passes a boon's cap. In the
/// engine: a puzzle map's rooms are solved one by one and each must give
/// this player its boon (stacks, the stat it touches, the banner and the
/// list); leaving the map must take every boon off again. Then the Puzzle
/// Gauntlet's rule on paper: boons carry from one gauntlet map to the next
/// and end with the last, while on the route each map starts clean. Prints
/// PUZZLE BOONS PASS or PUZZLE BOONS FAIL.
/// </summary>
public static class PuzzleBoonCheck
{
    private static string? Value()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == "--check-puzzle-boons") return "";
            if (a.StartsWith("--check-puzzle-boons=")) return a["--check-puzzle-boons=".Length..];
        }
        return null;
    }

    public static bool Requested => Value() is not null;

    private static bool _ok = true;

    private static void Expect(string what, bool cond)
    {
        if (!cond) GD.Print($"PUZZLE BOONS FAIL {what}");
        _ok &= cond;
    }

    private readonly record struct Snap(float Speed, float Jump, float Dash, float Health, float Armour, int Air);

    private static Snap Take(Player p) => new(p.Stats.Get(Stat.MoveSpeed), p.Stats.Get(Stat.JumpHeight), p.Stats.Get(Stat.DashCooldown),
                                              p.Stats.Get(Stat.MaxHealth), p.Stats.Get(Stat.Armor), p.ExtraAirJumps);

    public static async Task Run(Main main)
    {
        SceneTree tree = main.GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        string shot = Value() ?? "";
        await Wait(1.0);

        Paper();

        StageDirector stages = main.Stages;
        Player player = main.Player;
        Snap before = Take(player);
        if (!stages.DevEnterPuzzle(1, "runes,scales,trail,levers,crates")) { Expect("enter a puzzle map", false); Finish(tree); return; }
        await Wait(1.0);
        if (stages.Puzzle is not { } map) { Expect("puzzle map in the tree", false); Finish(tree); return; }
        foreach (RoomPlan room in map.Plan.Rooms) room.Enemies = 0;
        Expect($"no boon before a room is solved ({PuzzleBoons.Total})", PuzzleBoons.Total == 0);
        int shown = UI.PuzzleBoonHud.Shown;
        var expected = new int[PuzzleBoons.All.Length];
        int rooms = map.Plan.PuzzleRooms;
        for (int r = 1; r <= rooms; r++)
        {
            PuzzleBoon b = PuzzleBoons.For(map.Plan, r);
            expected[(int)b]++;
            map.State.MarkSolved(r, map.Elapsed);
            map.Flush();
            await Wait(0.3);
            Expect($"room {r}: one more boon ({PuzzleBoons.Total})", PuzzleBoons.Total == r);
            Expect($"room {r}: {b} x{expected[(int)b]}", PuzzleBoons.Stacks(b) == expected[(int)b]);
            Expect($"room {r}: the banner is up", map.BoonHud is { BannerVisible: true });
            GD.Print($"PUZZLE BOONS room {r} gave {b} -> held {PuzzleBoons.Total} banner='{UI.PuzzleBoonHud.LastText}'");
            if (r == rooms && shot.Length > 0) await Screenshot.Capture(tree, main, shot);
            await Wait(UI.PuzzleBoonHud.ShowSeconds);
        }
        Expect($"a banner per boon ({UI.PuzzleBoonHud.Shown - shown}/{rooms})", UI.PuzzleBoonHud.Shown - shown == rooms);
        Snap with = Take(player);
        int Held(PuzzleBoon b) => expected[(int)b];
        Expect($"swift: speed {before.Speed:0.00} -> {with.Speed:0.00}", Held(PuzzleBoon.Swift) == 0 ? Mathf.IsEqualApprox(with.Speed, before.Speed) : with.Speed > before.Speed);
        Expect($"spring: air jumps {before.Air} -> {with.Air}", with.Air == before.Air + Held(PuzzleBoon.Spring));
        Expect($"leap: jump {before.Jump:0.00} -> {with.Jump:0.00}", Held(PuzzleBoon.Leap) == 0 ? Mathf.IsEqualApprox(with.Jump, before.Jump) : with.Jump > before.Jump);
        Expect($"dash: cooldown {before.Dash:0.00} -> {with.Dash:0.00}", Held(PuzzleBoon.Dash) == 0 ? Mathf.IsEqualApprox(with.Dash, before.Dash) : with.Dash < before.Dash);
        Expect($"vigor: health {before.Health:0} -> {with.Health:0}", Mathf.IsEqualApprox(with.Health, before.Health + PuzzleBoons.VigorHealth * Held(PuzzleBoon.Vigor)));
        Expect($"ward: armour {before.Armour:0} -> {with.Armour:0}", Mathf.IsEqualApprox(with.Armour, before.Armour + PuzzleBoons.WardArmour * Held(PuzzleBoon.Ward)));
        Expect("the map is finished", map.State.Finished);

        // Leave by the vault's exit: every boon ends with the map.
        await Wait(1.5);
        if (map.ExitTarget is { } exit) player.Teleport(exit + Vector3.Up * 0.3f);
        double start = Time.GetTicksMsec() / 1000.0;
        while (!stages.InRestRoom && Time.GetTicksMsec() / 1000.0 - start < 6.0) await Wait(0.1);
        Expect("the exit leads to the rest room", stages.InRestRoom);
        Snap after = Take(player);
        Expect($"boons end with the map ({PuzzleBoons.Total})", PuzzleBoons.Total == 0);
        // (The vault's prize, taken on the way out, may change the build too.)
        Expect($"no boon left on the player ({PuzzleBoons.ModifiersOn(player)} modifiers, air jumps {after.Air} vs {before.Air})",
               PuzzleBoons.ModifiersOn(player) == 0 && after.Air == before.Air);

        Gauntlet(player, Take(player));
        Finish(tree);
    }

    private static void Finish(SceneTree tree)
    {
        GD.Print(_ok ? "PUZZLE BOONS PASS" : "PUZZLE BOONS FAIL");
        tree.Quit();
    }

    private static void Paper()
    {
        foreach (string s in PuzzleBoons.Strings())
            Expect($"Arabic for \"{s}\"", Meta.Loc.ArabicFor(s) is not null);
        foreach (ulong seed in new ulong[] { 1, 7, 42, 99, 31337 })
        {
            var held = new int[PuzzleBoons.All.Length];
            var first = new HashSet<PuzzleBoon>();
            for (int j = 0; j < 30; j++)
            {
                PuzzleBoon b = PuzzleBoons.Line(seed, 0, j);
                Expect($"seed {seed}: boon {j} the same twice", b == PuzzleBoons.Line(seed, 0, j));
                held[(int)b]++;
                Expect($"seed {seed}: {b} within its cap", held[(int)b] <= PuzzleBoons.Cap(b));
                if (j < PuzzleBoons.All.Length) first.Add(b);
            }
            Expect($"seed {seed}: the first six are all different", first.Count == PuzzleBoons.All.Length);
            var later = new HashSet<PuzzleBoon>();
            for (int j = 7; j < 13; j++) later.Add(PuzzleBoons.Line(seed, 7, j));
            Expect($"seed {seed}: a stretch from boon 7 starts with six different", later.Count == PuzzleBoons.All.Length);
            for (int index = 0; index < 4; index++)
            {
                PuzzlePlan a = PuzzleGen.ForRun(seed, index, 0, 1), b = PuzzleGen.ForRun(seed, index, 0, 3);
                for (int r = 1; r <= a.PuzzleRooms; r++)
                    Expect($"seed {seed} puzzle {index + 1} room {r}: the same boon for any team", PuzzleBoons.For(a, r) == PuzzleBoons.For(b, r));
            }
        }
        GD.Print($"PUZZLE BOONS paper boons={PuzzleBoons.All.Length} strings={PuzzleBoons.Strings().Count()}");
    }

    /// <summary>The gauntlet keeps boons across its maps; the route does not.</summary>
    private static void Gauntlet(Player player, Snap clean)
    {
        const ulong seed = 4242;
        int last = Meta.Routes.GauntletPuzzles - 1;
        PuzzlePlan g0 = PuzzleGen.Build(seed, 0, 0, 1, null, 0, gauntlet: true);
        PuzzlePlan g1 = PuzzleGen.Build(seed, 1, 2, 1, null, g0.PuzzleRooms, gauntlet: true);
        PuzzlePlan gLast = PuzzleGen.Build(seed, last, 5, 1, null, 20, gauntlet: true);
        PuzzleBoons.Track(g0);
        PuzzleBoons.OnSolved(g0, 1);
        PuzzleBoons.OnSolved(g0, 1);
        Expect($"gauntlet: a room gives its boon once ({PuzzleBoons.Total})", PuzzleBoons.Total == 1);
        PuzzleBoons.EndMap(g0);
        PuzzleBoons.Track(g1);
        Expect($"gauntlet: boons carry to the next map ({PuzzleBoons.Total})", PuzzleBoons.Total == 1);
        PuzzleBoons.OnSolved(g1, 1);
        PuzzleBoons.OnSolved(g1, 2);
        PuzzleBoons.EndMap(g1);
        PuzzleBoons.Track(gLast);
        PuzzleBoons.OnSolved(gLast, 1);
        Expect($"gauntlet: stacking through ({PuzzleBoons.Total})", PuzzleBoons.Total == 4);
        Expect("gauntlet: a boon changed the player", Take(player) != clean);
        PuzzleBoons.EndMap(gLast);
        Expect($"gauntlet: boons end with the last map ({PuzzleBoons.Total})", PuzzleBoons.Total == 0 && Take(player) == clean);

        PuzzlePlan r0 = PuzzleGen.ForRun(seed, 0, 0, 1), r1 = PuzzleGen.ForRun(seed, 1, 0, 1);
        PuzzleBoons.Track(r0);
        PuzzleBoons.OnSolved(r0, 1);
        PuzzleBoons.OnSolved(r0, 2);
        Expect($"route: two rooms, two boons ({PuzzleBoons.Total})", PuzzleBoons.Total == 2);
        PuzzleBoons.Track(r1);
        Expect($"route: the next map starts clean ({PuzzleBoons.Total})", PuzzleBoons.Total == 0 && Take(player) == clean);
        PuzzleBoons.EndMap(r1);
        GD.Print("PUZZLE BOONS gauntlet and route rules checked");
    }
}
