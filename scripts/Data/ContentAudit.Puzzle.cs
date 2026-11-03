using System;
using System.Linq;
using Godot;
using Wavebreak.Game.Puzzle;
using Wavebreak.Meta;

namespace Wavebreak.Data;

/// <summary>
/// The puzzle route (Game/Puzzle): every registered kind has a unique id, a
/// title, an objective, a hint and Arabic for all of its strings, needs one
/// player, and, forced into a map, generates a room its own script solves
/// alone; every string the route shows has Arabic; every prize kind is paid
/// by some medal; the puzzle route and the Puzzle Gauntlet sit last in the
/// route list (saved choices are indices) and play Classic's maps; the guards
/// exist; the quests are in the book; and the hint has a key and a pad button.
/// </summary>
public static partial class ContentAudit
{
    private static void AuditPuzzles()
    {
        foreach (string s in PuzzleText.All())
            if (s.Length > 0) Check(Loc.ArabicFor(s) is not null, $"puzzle: no Arabic for \"{s}\"");

        Check(PuzzleKinds.All.Select(k => k.Id).Distinct().Count() == PuzzleKinds.All.Count, "puzzle: kind ids are unique");
        foreach (PuzzleKind kind in PuzzleKinds.All)
        {
            Check(kind.Title.Length > 0 && kind.Objective.Length > 0 && kind.Hint.Length > 0, $"puzzle: text for {kind.Id}");
            Check(kind.MinPlayers == 1, $"puzzle: {kind.Id} needs one player");
            PuzzlePlan plan = PuzzleGen.Build(42, 1, 2, 1, kind.Id);
            RoomPlan? room = plan.Rooms.FirstOrDefault(r => ReferenceEquals(r.Def, kind));
            Check(room is not null, $"puzzle: {kind.Id} can be built");
            if (room is null) continue;
            Check(PuzzleSolver.DoorsConnected(room), $"puzzle: {kind.Id} keeps its doors connected");
            Check(PuzzleCheck.SolveOnPaper(plan, room.Index, out string why), $"puzzle: {kind.Id} solvable alone ({why})");
        }
        Check(PuzzleKinds.Bag.Count >= 9, $"puzzle: at least nine kinds in the bag ({PuzzleKinds.Bag.Count})");

        var paid = Enum.GetValues<RewardTier>().SelectMany(PuzzleRewards.KindsFor).ToHashSet();
        foreach (PuzzleRewardKind k in Enum.GetValues<PuzzleRewardKind>())
            Check(paid.Contains(k), $"puzzle: prize {k} is paid by some medal");
        Check(PuzzleRewards.KindsFor(RewardTier.Gold).Length > PuzzleRewards.KindsFor(RewardTier.Bronze).Length, "puzzle: gold offers more than bronze");
        Check(PuzzleRewards.BonusRerolls(RewardTier.Gold) > PuzzleRewards.BonusRerolls(RewardTier.Bronze), "puzzle: gold pays more rerolls than bronze");

        RouteDef? route = Routes.ById(Routes.PuzzleId);
        Check(route is not null && Routes.All[^2].Id == Routes.PuzzleId && Routes.All[^1].Id == Routes.GauntletId,
              "puzzle: puzzle route then Puzzle Gauntlet appended last (saved route indices preserved)");
        if (route is not null)
            foreach (int count in new[] { 3, 4, 5, 6 })
                Check(Routes.Build(route, 7, count).Select(s => s.Id).SequenceEqual(Routes.Build(Routes.All[0], 7, count).Select(s => s.Id)),
                      $"puzzle: route plays Classic's maps at {count}");
        if (Routes.ById(Routes.GauntletId) is { } gauntlet)
            Check(Routes.Build(gauntlet, 7, Routes.GauntletPuzzles).Count == Routes.GauntletPuzzles, "puzzle: the gauntlet has a stage per puzzle");
        Check(PuzzleMap.WeakEnemies().Count > 0, "puzzle: guards to send");
        foreach (string id in new[] { "qm_puzzle_3", "qm_puzzle_12", "qm_puzzle_gold" })
            Check(Unlocks.All.Any(u => u.Id == id), $"puzzle: quest {id}");
        Check(Keybinds.Primary("help_toggle") is InputEventKey && Keybinds.PadEvent("help_toggle") is not null, "puzzle: hint has a key and a pad button");
        Line($"puzzle kinds={PuzzleKinds.All.Count} [{string.Join(",", PuzzleKinds.All.Select(k => k.Id))}] strings={PuzzleText.All().Count()} prizes={paid.Count}");
    }
}
