using System;
using System.Collections.Generic;

namespace Wavebreak.Game.Puzzle;

/// <summary>What part a room plays in a puzzle map. <see cref="Start"/> and
/// <see cref="Reward"/> bracket the puzzle rooms; what a puzzle room holds is
/// its <see cref="RoomPlan.Def"/>, one of the registered
/// <see cref="PuzzleKind"/>s (Game/Puzzle/Kinds).</summary>
public enum RoomKind
{
    Start,
    Puzzle,
    Reward,
}

/// <summary>How well a puzzle map went. Ordered: higher is better.</summary>
public enum RewardTier
{
    None,
    Skipped,
    Bronze,
    Silver,
    Gold,
}

/// <summary>What a reward pedestal gives.</summary>
public enum PuzzleRewardKind
{
    EpicChest,
    RareChest,
    Chest,
    Catalyst,
    WeaponPick,
    Rerolls,
}

/// <summary>The shapes carved on rune bells, scale weights and memory pieces.
/// Every piece that has to be told apart says so with a shape, never with
/// colour alone.</summary>
public enum RuneSymbol
{
    Triangle,
    Circle,
    Square,
    Diamond,
    Star,
    Cross,
}

/// <summary>
/// Names and words for the puzzle route shared by every room kind, in one
/// place: the HUD, the world labels, the content audit and the Arabic table
/// all read from here. Each kind's own words (title, objective, hint, verbs,
/// HUD lines) live on its <see cref="PuzzleKind"/> and are listed by
/// <see cref="All"/> too, so a new kind cannot ship without its text.
/// </summary>
public static class PuzzleText
{
    public const string RouteName = "Puzzle Route";
    public const string GauntletName = "Puzzle Gauntlet";

    /// <summary>Puzzle maps get a name from the run's seed, so the HUD and the
    /// rest room can say where you were.</summary>
    public static readonly IReadOnlyList<string> MapNames = new[]
    {
        "Tidelock Vaults",
        "Mirrorwater Grotto",
        "Bellstone Halls",
        "Windward Locks",
        "Lantern Works",
        "Saltgear Cloister",
    };

    public const string StartTitle = "Antechamber";
    public const string RewardTitle = "Reward Vault";
    public const string StartObjective = "Head through the door. Solve each room to open the next.";
    public const string RewardObjective = "Claim one reward, then step into the exit portal.";
    public const string GauntletRewardObjective = "Step into the exit portal for the next puzzle.";
    public const string DefaultHint = "Hint: solve the room to open its door.";

    /// <summary>Room title for the HUD and the sign over its door.</summary>
    public static string Title(RoomPlan room) => room.Kind switch
    {
        RoomKind.Start => StartTitle,
        RoomKind.Reward => RewardTitle,
        _ => room.Def?.Title ?? "",
    };

    /// <summary>What to do, one line, for the HUD.</summary>
    public static string Objective(RoomPlan room) => room.Kind switch
    {
        RoomKind.Start => StartObjective,
        RoomKind.Reward => RewardObjective,
        _ => room.Def?.Objective ?? "",
    };

    /// <summary>The verb on the claim prompt.</summary>
    public const string VerbClaim = "Claim: {0}";

    public static string SymbolName(RuneSymbol s) => s switch
    {
        RuneSymbol.Triangle => "Triangle",
        RuneSymbol.Circle => "Circle",
        RuneSymbol.Square => "Square",
        RuneSymbol.Diamond => "Diamond",
        RuneSymbol.Star => "Star",
        RuneSymbol.Cross => "Cross",
        _ => "",
    };

    public static string TierName(RewardTier t) => t switch
    {
        RewardTier.Gold => "GOLD MEDAL",
        RewardTier.Silver => "SILVER MEDAL",
        RewardTier.Bronze => "BRONZE MEDAL",
        RewardTier.Skipped => "SKIPPED",
        _ => "",
    };

    public static string RewardName(PuzzleRewardKind k) => k switch
    {
        PuzzleRewardKind.EpicChest => "Epic chest",
        PuzzleRewardKind.RareChest => "Rare chest",
        PuzzleRewardKind.Chest => "Chest",
        PuzzleRewardKind.Catalyst => "Evolution catalyst",
        PuzzleRewardKind.WeaponPick => "Weapon pick",
        PuzzleRewardKind.Rerolls => "Rerolls",
        _ => "",
    };

    public static string RewardBlurb(PuzzleRewardKind k) => k switch
    {
        PuzzleRewardKind.EpicChest => "An item of epic rarity or better.",
        PuzzleRewardKind.RareChest => "An item of rare rarity or better.",
        PuzzleRewardKind.Chest => "An item, as from a chest.",
        PuzzleRewardKind.Catalyst => "The catalyst one of your weapons evolves with.",
        PuzzleRewardKind.WeaponPick => "A new weapon, or a level for one you hold.",
        PuzzleRewardKind.Rerolls => "Extra level-up rerolls.",
        _ => "",
    };

    /// <summary>Every string above and every kind's, for the content audit
    /// and the Arabic table check.</summary>
    public static IEnumerable<string> All()
    {
        yield return RouteName;
        yield return GauntletName;
        foreach (string n in MapNames) yield return n;
        yield return StartTitle;
        yield return RewardTitle;
        yield return StartObjective;
        yield return RewardObjective;
        yield return GauntletRewardObjective;
        yield return DefaultHint;
        yield return VerbClaim;
        foreach (PuzzleKind k in PuzzleKinds.All)
        {
            yield return k.Title;
            yield return k.Objective;
            yield return k.Hint;
            foreach (string s in k.Strings) yield return s;
        }
        foreach (RuneSymbol s in Enum.GetValues<RuneSymbol>()) yield return SymbolName(s);
        foreach (RewardTier t in Enum.GetValues<RewardTier>()) if (t != RewardTier.None) yield return TierName(t);
        foreach (PuzzleRewardKind r in Enum.GetValues<PuzzleRewardKind>())
        {
            yield return RewardName(r);
            yield return RewardBlurb(r);
        }
    }
}
