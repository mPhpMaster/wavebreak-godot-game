using System;
using System.Linq;
using Godot;
using Wavebreak.Game.Puzzle;
using Wavebreak.Meta;

namespace Wavebreak.Data;

/// <summary>
/// The puzzle route (Game/Puzzle): every piece type has a title, an objective
/// (both twin variants), a hint and Arabic for all of them; every string the
/// route shows has Arabic; each piece type, forced into a map, generates and
/// is solvable by its solver; every prize kind is paid by some medal; the
/// route sits last in the list (saved choices are indices) and plays Classic's
/// maps at every length; the guards exist; the quests are in the book; and the
/// hint has a key and a pad button.
/// </summary>
public static partial class ContentAudit
{
    private static void AuditPuzzles()
    {
        foreach (string s in PuzzleText.All())
            if (s.Length > 0) Check(Loc.ArabicFor(s) is not null, $"puzzle: no Arabic for \"{s}\"");

        foreach (RoomKind kind in Enum.GetValues<RoomKind>())
        {
            Check(PuzzleText.Title(kind).Length > 0 && PuzzleText.Objective(kind, false).Length > 0
                  && PuzzleText.Objective(kind, true).Length > 0 && PuzzleText.Hint(kind).Length > 0, $"puzzle: text for {kind}");
            if (kind is RoomKind.Start or RoomKind.Reward) continue;
            PuzzlePlan plan = PuzzleGen.Build(42, 1, 2, 1, kind.ToString());
            RoomPlan? room = plan.Rooms.FirstOrDefault(r => r.Kind == kind);
            Check(room is not null, $"puzzle: {kind} can be built");
            if (room is null) continue;
            Check(PuzzleSolver.DoorsConnected(room), $"puzzle: {kind} keeps its doors connected");
            bool solvable = kind switch
            {
                RoomKind.Crates => PuzzleSolver.SolveCrates(room, room.EntryCell, room.CrateStart, out _) is not null,
                RoomKind.Mirrors => PuzzleSolver.SolveMirrors(room, PuzzleGen.MirrorStartBits(room)) >= 0,
                RoomKind.Wind => PuzzleSolver.WindCrossable(room),
                RoomKind.Runes => room.Sequence.Length >= 3 && room.Sequence.All(b => b >= 0 && b < room.Bells.Length)
                                  && room.BellSymbols.Distinct().Count() == room.Bells.Length,
                RoomKind.Levers => PuzzleSolver.SolveLevers(room, 0) > 0,
                RoomKind.Twin => PuzzleSolver.TwinSoloSolvable(room),
                _ => false,
            };
            Check(solvable, $"puzzle: {kind} solvable");
        }

        var paid = Enum.GetValues<RewardTier>().SelectMany(PuzzleRewards.KindsFor).ToHashSet();
        foreach (PuzzleRewardKind k in Enum.GetValues<PuzzleRewardKind>())
            Check(paid.Contains(k), $"puzzle: prize {k} is paid by some medal");
        Check(PuzzleRewards.KindsFor(RewardTier.Gold).Length > PuzzleRewards.KindsFor(RewardTier.Bronze).Length, "puzzle: gold offers more than bronze");
        Check(PuzzleRewards.BonusRerolls(RewardTier.Gold) > PuzzleRewards.BonusRerolls(RewardTier.Bronze), "puzzle: gold pays more rerolls than bronze");

        RouteDef? route = Routes.ById(Routes.PuzzleId);
        Check(route is not null && Routes.All[^1].Id == Routes.PuzzleId, "puzzle: route appended last (saved route indices preserved)");
        if (route is not null)
            foreach (int count in new[] { 3, 4, 5, 6 })
                Check(Routes.Build(route, 7, count).Select(s => s.Id).SequenceEqual(Routes.Build(Routes.All[0], 7, count).Select(s => s.Id)),
                      $"puzzle: route plays Classic's maps at {count}");
        Check(PuzzleMap.WeakEnemies().Count > 0, "puzzle: guards to send");
        foreach (string id in new[] { "qm_puzzle_3", "qm_puzzle_12", "qm_puzzle_gold" })
            Check(Unlocks.All.Any(u => u.Id == id), $"puzzle: quest {id}");
        Check(Keybinds.Primary("help_toggle") is InputEventKey && Keybinds.PadEvent("help_toggle") is not null, "puzzle: hint has a key and a pad button");
        Line($"puzzle kinds={Enum.GetValues<RoomKind>().Length - 2} strings={PuzzleText.All().Count()} prizes={paid.Count}");
    }
}
