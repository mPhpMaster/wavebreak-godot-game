using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// <c>--check-puzzles</c> (add <c>--invulnerable</c>): proves the puzzle
/// route cannot trap a player, in two halves, printing PUZZLE lines and then
/// PUZZLE PASS or PUZZLE FAIL.
///
/// <b>On paper</b>, for many seeds, every puzzle number, three tiers and one
/// and two players: the map is generated twice (it must come out the same),
/// every room is solved from scratch by <see cref="PuzzleSolver"/> and that
/// answer is played through the real rules in <see cref="PuzzleState"/> (the
/// room must end solved and its door open), the way between each room's doors
/// is open, timing rooms leave their margin, every piece type turns up, the
/// medal thresholds hold, and the SKIP portal opens on time and pays the lowest
/// tier.
///
/// <b>In the engine</b>, on this run: two puzzle maps are entered through the
/// stage director; each room is solved through the real map (crates pushed and
/// mirrors, levers and bells used through <see cref="PuzzleMap.Ask"/>, plates
/// stood on by the player's real body), every door must open, the vault must
/// offer prizes and a claim must change the build; then the exit leads into the
/// rest room. A second map waits out its clock, its SKIP portal must open, and
/// walking into it must leave with the skip's rerolls paid.
/// </summary>
public static class PuzzleCheck
{
    public static bool Requested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-puzzles") >= 0;

    private static string? Arg(string name)
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith(name + "=")) return a[(name.Length + 1)..];
        return null;
    }

    /// <summary>--puzzle-shot=x.png: photograph a puzzle map (with
    /// --puzzle-shot-room=N, default 1; N past the last puzzle room is the
    /// vault with every room solved; --puzzle-shot-hint shows a hint;
    /// --puzzle-shot-door solves room N and photographs its door opening).</summary>
    public static string? ShotPath => Arg("--puzzle-shot");

    public static async Task Shot(Main main, string path)
    {
        SceneTree tree = main.GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        await Wait(1.2);
        StageDirector stages = main.Stages;
        if (!stages.InPuzzle) stages.DevEnterPuzzle(0);
        await Wait(0.5);
        if (stages.Puzzle is not { } map) { GD.Print("[puzzle] no puzzle map to photograph"); tree.Quit(); return; }
        int room = Math.Clamp((Arg("--puzzle-shot-room") ?? "1").ToInt(), 1, map.Plan.Rooms.Count - 1);
        // A believable clock for the card (the rooms before are taken as solved).
        map.SetElapsed(16f * room + 4f);
        for (int i = 1; i < room && i < map.Plan.Rooms.Count - 1; i++) map.State.MarkSolved(i, map.Elapsed);
        map.Flush();
        RoomPlan r = map.Plan.Rooms[room];
        bool door = Array.IndexOf(OS.GetCmdlineUserArgs(), "--puzzle-shot-door") >= 0 && r.ExitSide >= 0;
        var toCentre = -new Vector3(RoomPlan.Dx[r.EntrySide], 0f, RoomPlan.Dz[r.EntrySide]);
        if (door)
        {
            // Facing the way out from the middle of the room.
            var toDoor = new Vector3(RoomPlan.Dx[r.ExitSide], 0f, RoomPlan.Dz[r.ExitSide]);
            main.Player.Teleport(r.Centre + toDoor * 3.5f + new Vector3(toDoor.Z, 0f, toDoor.X) * 1.0f + Vector3.Up * 0.3f);
            PlayerCamera.Instance?.FaceDirection(toDoor);
        }
        else
        {
            // A few steps in from the entrance, looking across the hall.
            main.Player.Teleport(r.Centre - toCentre * 5.5f + new Vector3(toCentre.Z, 0f, toCentre.X) * 1.2f + Vector3.Up * 0.3f);
            PlayerCamera.Instance?.FaceDirection(toCentre);
        }
        PlayerCamera.Instance?.SnapToTarget();
        await Wait(1.0);
        if (door)
        {
            map.State.MarkSolved(room, map.Elapsed);
            map.Flush();
            await Wait(0.9);
            await Screenshot.Capture(tree, main, path);
            GD.Print($"[puzzle] shot {path}: room {room} {r.Kind} door {map.DoorOpenness(room):0.00} open");
            tree.Quit();
            return;
        }
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--puzzle-shot-hint") >= 0) map.Ask(PuzzleAction.Hint, room, 0);
        await Wait(room == map.Plan.Rooms.Count - 1 ? 2.9 : 2.0);
        await Screenshot.Capture(tree, main, path);
        GD.Print($"[puzzle] shot {path}: room {room} {r.Kind}");
        tree.Quit();
    }

    private static bool _ok = true;

    private static void Expect(string what, bool condition)
    {
        if (!condition) GD.Print($"PUZZLE FAIL {what}");
        _ok &= condition;
    }

    public static async Task Run(Main main)
    {
        SceneTree tree = main.GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        await Wait(1.0);

        Paper();
        await Live(main, Wait);

        GD.Print(_ok ? "PUZZLE PASS" : "PUZZLE FAIL");
        tree.Quit();
    }

    // ------------------------------------------------------------- paper

    private static readonly ulong[] Seeds = { 1, 7, 42, 99, 1234, 4242, 9001, 31337, 777777, 20261102 };

    private static void Paper()
    {
        var seen = new HashSet<RoomKind>();
        int maps = 0, rooms = 0;
        ulong started = Time.GetTicksMsec();
        foreach (ulong seed in Seeds)
            foreach (int tier in new[] { 0, 1, 3 })
                for (int index = 0; index < 6; index++)
                    foreach (int players in new[] { 1, 2 })
                    {
                        int d = PuzzleGen.DifficultyFor(index, tier);
                        PuzzlePlan plan = PuzzleGen.Build(seed, index, d, players);
                        PuzzlePlan again = PuzzleGen.Build(seed, index, d, players);
                        string tag = $"seed={seed} tier={tier} puzzle={index + 1} players={players}";
                        Expect($"{tag}: same map twice", plan.Describe() == again.Describe()
                               && new PuzzleState(plan).Hash() == new PuzzleState(again).Hash()
                               && plan.Rooms.Select(r => (r.MacroX, r.MacroZ)).SequenceEqual(again.Rooms.Select(r => (r.MacroX, r.MacroZ))));
                        Expect($"{tag}: {PuzzleGen.RoomsFor(d)} puzzle rooms", plan.PuzzleRooms == PuzzleGen.RoomsFor(d));
                        Expect($"{tag}: a twin room with two players", players < 2 || plan.Rooms.Any(r => r.Kind == RoomKind.Twin));
                        Expect($"{tag}: rooms do not overlap", plan.Rooms.Select(r => (r.MacroX, r.MacroZ)).Distinct().Count() == plan.Rooms.Count);
                        for (int i = 1; i < plan.Rooms.Count - 1; i++)
                        {
                            seen.Add(plan.Rooms[i].Kind);
                            Expect($"{tag} room {i} {plan.Rooms[i].Kind}: solvable", SolveOnPaper(plan, i, players, out string why) || Fail(why));
                            rooms++;
                        }
                        Expect($"{tag}: medals", Medals(plan));
                        Expect($"{tag}: skip", SkipOnPaper(plan));
                        maps++;
                    }
        foreach (RoomKind k in Enum.GetValues<RoomKind>())
            if (k is not (RoomKind.Start or RoomKind.Reward))
                Expect($"piece type {k} appears", seen.Contains(k));
        GD.Print($"PUZZLE paper maps={maps} rooms={rooms} kinds={seen.Count} ms={Time.GetTicksMsec() - started}");
    }

    private static bool Fail(string why)
    {
        GD.Print($"PUZZLE why {why}");
        return false;
    }

    /// <summary>Solve room <paramref name="r"/> from its start with the solver
    /// alone and play the answer through the rules.</summary>
    private static bool SolveOnPaper(PuzzlePlan plan, int r, int players, out string why)
    {
        why = "";
        var st = new PuzzleState(plan);
        RoomPlan room = plan.Rooms[r];
        // Earlier rooms are taken as solved, as the doors would require.
        for (int i = 1; i < r; i++) st.MarkSolved(i, 1f);
        if (st.Solved(r)) { why = "solved before anyone touched it"; return false; }
        if (!PuzzleSolver.DoorsConnected(room)) { why = "doors cut off"; return false; }
        float t = 10f;
        var at = new List<Vector3>();
        switch (room.Kind)
        {
            case RoomKind.Crates:
            {
                List<int>? moves = PuzzleSolver.SolveCrates(room, room.EntryCell, room.CrateStart, out int pushes);
                if (moves is null) { why = "no crate solution"; return false; }
                int p = room.EntryCell;
                foreach (int side in moves)
                {
                    int next = PuzzleSolver.Step(p, side);
                    int crate = Array.IndexOf(st.Crates(room), next);
                    if (crate >= 0 && !st.Push(r, crate, side, t)) { why = "a push the solver made was refused"; return false; }
                    p = next;
                }
                at.Add(room.World(p));
                st.Step(at, t, players);
                if (pushes < 1) { why = "no push needed"; return false; }
                break;
            }
            case RoomKind.Mirrors:
            {
                int start = st.Get(room, PuzzleState.RA);
                if (PuzzleSolver.Beam(room, start).Hit) { why = "beam lit at the start"; return false; }
                int answer = PuzzleSolver.SolveMirrors(room, start);
                if (answer < 0) { why = "no mirror solution"; return false; }
                for (int i = 0; i < room.MirrorCells.Length; i++)
                    if (((answer ^ start) >> i & 1) != 0) st.TurnMirror(r, i, t);
                break;
            }
            case RoomKind.Levers:
            {
                int answer = PuzzleSolver.SolveLevers(room, 0);
                if (answer <= 0) { why = "no lever solution"; return false; }
                for (int i = 0; i < room.Levers.Length; i++)
                    if ((answer >> i & 1) != 0) st.PullLever(r, i, t);
                break;
            }
            case RoomKind.Runes:
            {
                at.Add(room.Centre);
                st.Step(at, t, players);
                t += 1f + room.Sequence.Length * PuzzleGen.RuneBeat;
                if (st.StrikeBell(r, (room.Sequence[0] + 1) % room.Bells.Length, t) != StrikeResult.Wrong) { why = "a wrong bell was not wrong"; return false; }
                t += 1f + room.Sequence.Length * PuzzleGen.RuneBeat;
                StrikeResult last = StrikeResult.Ignored;
                foreach (int b in room.Sequence) last = st.StrikeBell(r, b, t += 0.5f);
                if (last != StrikeResult.Done) { why = $"the shown order did not finish ({last})"; return false; }
                break;
            }
            case RoomKind.Wind:
            {
                if (!PuzzleSolver.WindCrossable(room)) { why = "calm too short"; return false; }
                foreach (Vector2 c in room.WindCentres)
                {
                    at.Clear();
                    at.Add(room.World(c));
                    st.Step(at, t += 1f, players);
                }
                break;
            }
            case RoomKind.Twin:
            {
                if (!PuzzleSolver.TwinSoloSolvable(room)) { why = "latch too short for one player"; return false; }
                if (players >= 2)
                {
                    at.Add(room.World(room.PlateA));
                    st.Step(at, t, players);
                    if (st.Solved(r)) { why = "one plate solved a two-player room"; return false; }
                    at.Add(room.World(room.PlateB));
                    st.Step(at, t + 0.1f, players);
                    if (st.Get(room, PuzzleState.RAssist) != 0) { why = "assist on with two players"; return false; }
                }
                else
                {
                    at.Add(room.World(room.PlateA));
                    st.Step(at, t, 1);
                    at.Clear();
                    st.Step(at, t += 0.1f, 1);
                    at.Add(room.World(room.PlateB));
                    st.Step(at, t + PuzzleSolver.TwinWalkSeconds(room), 1);
                }
                break;
            }
        }
        if (!st.Solved(r)) { why = "the answer did not solve it"; return false; }
        if (!st.DoorOpen(r)) { why = "door stayed shut"; return false; }
        return true;
    }

    private static bool Medals(PuzzlePlan plan)
    {
        var st = new PuzzleState(plan);
        return st.TierFor(plan.GoldSeconds - 1f, 0) == RewardTier.Gold
               && st.TierFor(plan.GoldSeconds - 1f, 1) == RewardTier.Silver
               && st.TierFor(plan.SilverSeconds + 1f, 0) == RewardTier.Bronze
               && st.TierFor(plan.SilverSeconds - 1f, 2) == RewardTier.Bronze
               && plan.GoldSeconds < plan.SilverSeconds;
    }

    private static bool SkipOnPaper(PuzzlePlan plan)
    {
        var st = new PuzzleState(plan);
        st.Step(new List<Vector3>(), plan.SkipSeconds - 1f, 1);
        if (st.SkipOpen) return false;
        st.Step(new List<Vector3>(), plan.SkipSeconds + 0.1f, 1);
        if (!st.SkipOpen) return false;
        st.Skip(plan.SkipSeconds + 2f);
        return st.Skipped && st.Tier == RewardTier.Skipped && !st.Finished;
    }

    // -------------------------------------------------------------- live

    private static async Task Live(Main main, Func<double, Task> wait)
    {
        StageDirector stages = main.Stages;
        Player player = main.Player;

        for (int pass = 0; pass < 2; pass++)
        {
            int index = pass;
            if (!stages.DevEnterPuzzle(index)) { Expect("enter a puzzle map", false); return; }
            await wait(1.0);
            if (stages.Puzzle is not { } map) { Expect("puzzle map in the tree", false); return; }
            GD.Print($"PUZZLE live puzzle={index + 1} name='{map.Plan.Name}' rooms={map.Plan.Describe()}");
            Expect("in the puzzle state", stages.InPuzzle);
            float clock = main.Run.Seconds;

            if (pass == 1)
            {
                // Wait out the clock: the SKIP portal must open and lead out.
                map.SetElapsed(map.Plan.SkipSeconds + 0.5f);
                await wait(0.5);
                Expect("skip portal opens after the timeout", map.State.SkipOpen && map.SkipPosition is not null);
                int rerollsBefore = Progression.LevelUpChoices.Current?.RerollsLeft ?? 0;
                await wait(1.3);   // the portal's opening animation
                if (map.SkipPosition is { } skip) player.Teleport(skip + Vector3.Up * 0.3f);
                bool left = await Until(() => stages.InRestRoom, 5.0, wait);
                Expect("the skip portal leads to the rest room", left);
                Expect($"skip exit reason ({stages.PuzzleExitReason})", stages.PuzzleExitReason.EndsWith(":Skipped"));
                int rerollsAfter = Progression.LevelUpChoices.Current?.RerollsLeft ?? 0;
                Expect($"skip pays a reroll ({rerollsBefore} -> {rerollsAfter})", rerollsAfter == rerollsBefore + PuzzleRewards.BonusRerolls(RewardTier.Skipped));
                Expect("run clock held through the puzzle", Mathf.IsEqualApprox(main.Run.Seconds, clock));
                continue;
            }

            for (int r = 1; r < map.Plan.Rooms.Count - 1; r++)
            {
                bool solved = await SolveLive(map, r, player, wait);
                Expect($"live room {r} {map.Plan.Rooms[r].Kind} solved", solved);
                await wait(0.3);
                Expect($"live door {r} opened", map.DoorIsOpen(r));
                GD.Print($"PUZZLE live room={r} kind={map.Plan.Rooms[r].Kind} solved={solved} door={map.DoorIsOpen(r)} t={map.Elapsed:0.0}");
            }
            Expect("puzzle finished", map.State.Finished);
            Expect($"tier given ({map.State.Tier})", map.State.Tier >= RewardTier.Bronze);
            await wait(0.4);
            Expect("vault offers prizes", map.Offers is { Count: > 0 });
            int granted = PuzzleRewards.Granted;
            string before = Build(main);
            player.Teleport(map.Plan.Vault.Centre + Vector3.Up * 0.3f);
            map.Claim(map.Offers is { Count: > 1 } ? 1 : 0);
            await wait(0.3);
            Expect("a prize is granted", PuzzleRewards.Granted == granted + 1 && map.Claimed);
            Expect($"the prize changes the build ({before} -> {Build(main)})", Build(main) != before);
            await wait(1.0);
            if (map.ExitTarget is { } exit) player.Teleport(exit + Vector3.Up * 0.3f);
            bool out1 = await Until(() => stages.InRestRoom, 5.0, wait);
            Expect("the exit leads to the rest room", out1);
            Expect($"exit reason ({stages.PuzzleExitReason})", stages.PuzzleExitReason.StartsWith("exit:"));
            Expect("run clock held through the puzzle", Mathf.IsEqualApprox(main.Run.Seconds, clock));
        }
    }

    private static string Build(Main main)
        => $"w{main.Weapons.Weapons.Sum(w => w.Level)}i{main.Inventory.TotalStacks}t{Progression.TomeBook.Current?.Held.Sum(t => Progression.TomeBook.Current.LevelOf(t)) ?? 0}r{Progression.LevelUpChoices.Current?.RerollsLeft ?? 0}g{main.Inventory.Gold}";

    private static async Task<bool> Until(Func<bool> cond, double timeout, Func<double, Task> wait)
    {
        double start = Time.GetTicksMsec() / 1000.0;
        while (!cond())
        {
            if (Time.GetTicksMsec() / 1000.0 - start > timeout) return false;
            await wait(0.1);
        }
        return true;
    }

    /// <summary>Solve one room through the live map: the same asks a player's
    /// keys make, and the player's real body on the plates.</summary>
    private static async Task<bool> SolveLive(PuzzleMap map, int r, Player player, Func<double, Task> wait)
    {
        RoomPlan room = map.Plan.Rooms[r];
        PuzzleState st = map.State;
        void Stand(Vector3 at) => player.Teleport(at + Vector3.Up * 0.3f);
        Stand(room.World(room.EntryCell));
        await wait(0.3);
        switch (room.Kind)
        {
            case RoomKind.Crates:
            {
                List<int>? moves = PuzzleSolver.SolveCrates(room, room.EntryCell, st.Crates(room), out _);
                if (moves is null) return false;
                int p = room.EntryCell;
                foreach (int side in moves)
                {
                    int next = PuzzleSolver.Step(p, side);
                    int crate = Array.IndexOf(st.Crates(room), next);
                    if (crate >= 0) map.Ask(PuzzleAction.Push, r, crate, side);
                    p = next;
                }
                await wait(0.2);
                Stand(room.World(p));
                break;
            }
            case RoomKind.Mirrors:
            {
                int bits = st.Get(room, PuzzleState.RA);
                int answer = PuzzleSolver.SolveMirrors(room, bits);
                for (int i = 0; i < room.MirrorCells.Length; i++)
                    if (((answer ^ bits) >> i & 1) != 0) map.Ask(PuzzleAction.Mirror, r, i);
                break;
            }
            case RoomKind.Levers:
            {
                int answer = PuzzleSolver.SolveLevers(room, st.Get(room, PuzzleState.RA));
                for (int i = 0; i < room.Levers.Length; i++)
                    if ((answer >> i & 1) != 0) map.Ask(PuzzleAction.Lever, r, i);
                break;
            }
            case RoomKind.Runes:
            {
                Stand(room.Centre + new Vector3(2f, 0f, 2f));
                await wait(1.2 + room.Sequence.Length * PuzzleGen.RuneBeat);
                foreach (int b in room.Sequence)
                {
                    map.Ask(PuzzleAction.Bell, r, b);
                    await wait(0.15);
                }
                break;
            }
            case RoomKind.Wind:
                foreach (Vector2 c in room.WindCentres)
                {
                    Stand(room.World(c));
                    await wait(0.3);
                }
                break;
            case RoomKind.Twin:
                Stand(room.World(room.PlateA));
                await wait(0.3);
                Stand(room.World(room.PlateB));
                await wait(0.3);
                break;
        }
        return await Until(() => st.Solved(r), 3.0, wait);
    }
}
