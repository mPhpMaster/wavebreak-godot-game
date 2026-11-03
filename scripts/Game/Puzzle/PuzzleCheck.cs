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
/// <b>On paper.</b> Every registered kind, for many seeds and every
/// difficulty: it needs one player, generates the same room twice, keeps its
/// doors connected, is not solved before anyone touches it, points a hint at
/// something in the room, and its own solution script, driven with ONE body
/// through the real rules (<see cref="PaperDriver"/>), solves it. Then whole
/// maps for many seeds, tiers and puzzle numbers: generated the same twice,
/// the right number of rooms, no kind twice in a map, every room solved alone
/// the same way, medals and the SKIP portal; and the run's shuffle bag shows
/// every kind once before any repeats.
///
/// <b>In the engine</b>, on this run: every kind is entered through the stage
/// director (two maps share them) and each room is solved through the real
/// map by the same scripts (<see cref="LiveDriver"/>: the player's body
/// teleported onto plates and tiles, asks through <see cref="PuzzleMap.Ask"/>);
/// every door must open, the vault must offer prizes and a claim must change
/// the build; the exit leads into the rest room. A last map waits out its
/// clock, its SKIP portal must open, and walking into it must leave with the
/// skip's rerolls paid.
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
    /// vault with every room solved; --puzzle-shot-kind=id puts that kind in
    /// room 1; --puzzle-shot-hint shows a hint; --puzzle-shot-door solves
    /// room N and photographs its door opening).</summary>
    public static string? ShotPath => Arg("--puzzle-shot");

    public static async Task Shot(Main main, string path)
    {
        SceneTree tree = main.GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        await Wait(1.2);
        StageDirector stages = main.Stages;
        string? kindId = Arg("--puzzle-shot-kind");
        if (kindId is not null) stages.DevEnterPuzzle(0, kindId);
        else if (!stages.InPuzzle) stages.DevEnterPuzzle(0);
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
            // On the grid's edge by the entrance, looking down across the hall.
            main.Player.Teleport(r.Centre - toCentre * 8.6f + new Vector3(toCentre.Z, 0f, toCentre.X) * 1.2f + Vector3.Up * 0.3f);
            PlayerCamera.Instance?.FaceDirection(toCentre);
            PlayerCamera.Instance?.SetPitchDegrees(-38f);
        }
        PlayerCamera.Instance?.SnapToTarget();
        await Wait(1.0);
        if (door)
        {
            map.State.MarkSolved(room, map.Elapsed);
            map.Flush();
            await Wait(0.9);
            await Screenshot.Capture(tree, main, path);
            GD.Print($"[puzzle] shot {path}: room {room} {r.KindId} door {map.DoorOpenness(room):0.00} open");
            tree.Quit();
            return;
        }
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--puzzle-shot-hint") >= 0) map.Ask(PuzzleAction.Hint, room, 0);
        await Wait(room == map.Plan.Rooms.Count - 1 ? 2.9 : 2.0);
        await Screenshot.Capture(tree, main, path);
        GD.Print($"[puzzle] shot {path}: room {room} {r.KindId} hint={map.HintShown}");
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

        Kinds();
        Paper();
        await Live(main, Wait);

        GD.Print(_ok ? "PUZZLE PASS" : "PUZZLE FAIL");
        tree.Quit();
    }

    // ----------------------------------------------------------- drivers

    /// <summary>The rules alone, one body, instant: a solution script runs to
    /// the end in one call.</summary>
    public sealed class PaperDriver : IPuzzleDriver
    {
        public PuzzleState State { get; }
        public float Now { get; private set; }
        private Vector3? _at;
        private readonly int _room;

        public PaperDriver(PuzzleState state, int room, float now)
        {
            State = state;
            _room = room;
            Now = now;
        }

        private void Tick()
        {
            var bodies = new List<Vector3>();
            if (_at is { } p) bodies.Add(p);
            State.Step(bodies, Now, 1);
        }

        public Task Stand(Vector3 world)
        {
            _at = world;
            Now += 0.05f;
            Tick();
            return Task.CompletedTask;
        }

        public Task Ask(PuzzleAction action, int a, int b = 0)
        {
            Now += 0.05f;
            Tick();
            State.Act(_room, action, a, b, Now);
            return Task.CompletedTask;
        }

        public Task Wait(float seconds)
        {
            // Step through the wait, so time-driven rules see every moment.
            for (float left = seconds; left > 0f; left -= 0.1f)
            {
                Now += Mathf.Min(0.1f, left);
                Tick();
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>The live map: the real player teleported, real asks, real time.</summary>
    private sealed class LiveDriver : IPuzzleDriver
    {
        private readonly PuzzleMap _map;
        private readonly Player _player;
        private readonly Func<double, Task> _wait;
        private readonly int _room;

        public LiveDriver(PuzzleMap map, Player player, Func<double, Task> wait, int room)
        {
            _map = map;
            _player = player;
            _wait = wait;
            _room = room;
        }

        public PuzzleState State => _map.State;
        public float Now => _map.Elapsed;

        public async Task Stand(Vector3 world)
        {
            _player.Teleport(world + Vector3.Up * 0.3f);
            await _wait(0.15);
        }

        public async Task Ask(PuzzleAction action, int a, int b = 0)
        {
            _map.Ask(action, _room, a, b);
            await _wait(0.05);
        }

        public Task Wait(float seconds) => _wait(seconds);
    }

    /// <summary>Run a kind's script on paper from the room's current state.</summary>
    public static bool SolveOnPaper(PuzzlePlan plan, int r, out string why)
    {
        var st = new PuzzleState(plan);
        RoomPlan room = plan.Rooms[r];
        // Earlier rooms are taken as solved, as the doors would require.
        for (int i = 1; i < r; i++) st.MarkSolved(i, 1f);
        why = "";
        if (room.Def is not { } kind) { why = "no kind"; return false; }
        if (st.Solved(r)) { why = "solved before anyone touched it"; return false; }
        if (!PuzzleSolver.DoorsConnected(room)) { why = "doors cut off"; return false; }
        var d = new PaperDriver(st, r, 10f);
        Task<string?> run = kind.Solve(d, room, r);
        if (!run.IsCompleted) { why = "the script waited for something on paper"; return false; }
        if (run.Result is { } failed) { why = failed; return false; }
        if (!st.Solved(r)) { why = "the answer did not solve it"; return false; }
        if (!st.DoorOpen(r)) { why = "door stayed shut"; return false; }
        return true;
    }

    private static bool Fail(string why)
    {
        GD.Print($"PUZZLE why {why}");
        return false;
    }

    // ------------------------------------------------------------- kinds

    private static void Kinds()
    {
        ulong started = Time.GetTicksMsec();
        IReadOnlyList<PuzzleKind> all = PuzzleKinds.All;
        Expect($"at least ten kinds ({all.Count})", all.Count >= 10);
        Expect("ids are unique", all.Select(k => k.Id).Distinct().Count() == all.Count);
        Expect("no twin room (it needed two players)", PuzzleKinds.ById("twin") is null);
        int rooms = 0;
        foreach (PuzzleKind kind in all)
        {
            Expect($"{kind.Id}: needs one player", kind.MinPlayers == 1);
            Expect($"{kind.Id}: text", kind.Title.Length > 0 && kind.Objective.Length > 0 && kind.Hint.Length > 0);
            for (ulong seed = 1; seed <= 40; seed++)
                for (int d = 0; d <= PuzzleGen.MaxDifficulty; d++)
                {
                    PuzzlePlan plan = PuzzleGen.Build(seed * 7919, (int)(seed % 6), d, 1, kind.Id);
                    PuzzlePlan again = PuzzleGen.Build(seed * 7919, (int)(seed % 6), d, 1, kind.Id);
                    string tag = $"{kind.Id} seed={seed} d={d}";
                    RoomPlan room = plan.Rooms[1];
                    Expect($"{tag}: built", ReferenceEquals(room.Def, kind));
                    Expect($"{tag}: same room twice", new PuzzleState(plan).Hash() == new PuzzleState(again).Hash()
                           && room.Pillar.SequenceEqual(again.Rooms[1].Pillar) && room.Fixed.SequenceEqual(again.Rooms[1].Fixed));
                    var st = new PuzzleState(plan);
                    PuzzleHint? hint = kind.HintAt(st, room, room.World(room.EntryCell), 5f);
                    Expect($"{tag}: a hint in the room", hint is { } h && room.Contains(h.At, -1f));
                    Expect($"{tag}: solved alone", SolveOnPaper(plan, 1, out string why) || Fail($"{tag}: {why}"));
                    rooms++;
                }
        }
        GD.Print($"PUZZLE kinds={all.Count} [{string.Join(",", all.Select(k => k.Id))}] rooms={rooms} ms={Time.GetTicksMsec() - started}");
    }

    // ------------------------------------------------------------- paper

    private static readonly ulong[] Seeds = { 1, 7, 42, 99, 1234, 4242, 9001, 31337, 777777, 20261102 };

    private static void Paper()
    {
        var seen = new HashSet<PuzzleKind>();
        int maps = 0, rooms = 0;
        ulong started = Time.GetTicksMsec();
        int bag = PuzzleKinds.Bag.Count;
        foreach (ulong seed in Seeds)
        {
            // The bag: every kind once before any comes back, across the run.
            List<PuzzleKind> seq = PuzzleKinds.Sequence(seed, bag * 3);
            for (int b = 0; b < 3; b++)
                Expect($"seed={seed}: bag {b} holds every kind once", seq.Skip(b * bag).Take(bag).Distinct().Count() == bag);
            for (int i = 1; i < seq.Count; i++)
                Expect($"seed={seed}: room {i} repeats the room before", !ReferenceEquals(seq[i], seq[i - 1]));
            foreach (int tier in new[] { 0, 1, 3 })
            {
                var runKinds = new List<PuzzleKind>();
                for (int index = 0; index < 6; index++)
                {
                    int d = PuzzleGen.DifficultyFor(index, tier);
                    PuzzlePlan plan = PuzzleGen.ForRun(seed, index, tier, 1);
                    PuzzlePlan again = PuzzleGen.ForRun(seed, index, tier, 2);
                    string tag = $"seed={seed} tier={tier} puzzle={index + 1}";
                    Expect($"{tag}: same map twice (and for any team size)", plan.Describe() == again.Describe()
                           && new PuzzleState(plan).Hash() == new PuzzleState(again).Hash()
                           && plan.Rooms.Select(r => (r.MacroX, r.MacroZ)).SequenceEqual(again.Rooms.Select(r => (r.MacroX, r.MacroZ))));
                    Expect($"{tag}: {PuzzleGen.RoomsFor(d)} puzzle rooms", plan.PuzzleRooms == PuzzleGen.RoomsFor(d));
                    Expect($"{tag}: rooms do not overlap", plan.Rooms.Select(r => (r.MacroX, r.MacroZ)).Distinct().Count() == plan.Rooms.Count);
                    var kinds = plan.Rooms.Where(r => r.Def is not null).Select(r => r.Def!).ToList();
                    Expect($"{tag}: no kind twice in a map ({plan.Describe()})", kinds.Distinct().Count() == kinds.Count);
                    runKinds.AddRange(kinds);
                    for (int i = 1; i < plan.Rooms.Count - 1; i++)
                    {
                        seen.Add(plan.Rooms[i].Def!);
                        Expect($"{tag} room {i} {plan.Rooms[i].KindId}: solved alone", SolveOnPaper(plan, i, out string why) || Fail($"{tag} room {i}: {why}"));
                        rooms++;
                    }
                    Expect($"{tag}: medals", Medals(plan));
                    Expect($"{tag}: skip", SkipOnPaper(plan));
                    maps++;
                }
                Expect($"seed={seed} tier={tier}: the run's first {bag} rooms are every kind", runKinds.Take(bag).Distinct().Count() == Math.Min(bag, runKinds.Count));
            }
        }
        foreach (PuzzleKind k in PuzzleKinds.Bag) Expect($"kind {k.Id} appears in generated maps", seen.Contains(k));
        GD.Print($"PUZZLE paper maps={maps} rooms={rooms} kinds={seen.Count} ms={Time.GetTicksMsec() - started}");
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
        List<string> ids = PuzzleKinds.Bag.Select(k => k.Id).ToList();
        int half = (ids.Count + 1) / 2;
        var passes = new List<string?> { string.Join(",", ids.Take(half)), string.Join(",", ids.Skip(half)), null };

        for (int pass = 0; pass < passes.Count; pass++)
        {
            int index = Math.Min(pass, 1);
            if (!stages.DevEnterPuzzle(index, passes[pass])) { Expect("enter a puzzle map", false); return; }
            await wait(1.0);
            if (stages.Puzzle is not { } map) { Expect("puzzle map in the tree", false); return; }
            GD.Print($"PUZZLE live pass={pass} puzzle={index + 1} name='{map.Plan.Name}' rooms={map.Plan.Describe()}");
            // No guards: their kills would level the player up, and the level-up
            // panel pauses the game in a check that has no bot to pick a card.
            foreach (RoomPlan room in map.Plan.Rooms) room.Enemies = 0;
            Expect("in the puzzle state", stages.InPuzzle);
            float clock = main.Run.Seconds;

            if (passes[pass] is null)
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
                RoomPlan room = map.Plan.Rooms[r];
                player.Teleport(room.World(room.EntryCell) + Vector3.Up * 0.3f);
                await wait(0.3);
                // A hint must mark something in the room.
                map.Ask(PuzzleAction.Hint, r, 0);
                await wait(0.2);
                Expect($"live room {r} {room.KindId}: hint shown", map.HintShown);
                string? why = await room.Def!.Solve(new LiveDriver(map, player, wait, r), room, r);
                bool solved = await Until(() => map.State.Solved(r), 4.0, wait);
                Expect($"live room {r} {room.KindId} solved{(why is null ? "" : " (" + why + ")")}", why is null && solved);
                await wait(0.3);
                Expect($"live door {r} opened", map.DoorIsOpen(r));
                GD.Print($"PUZZLE live room={r} kind={room.KindId} solved={solved} door={map.DoorIsOpen(r)} t={map.Elapsed:0.0}");
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
}
