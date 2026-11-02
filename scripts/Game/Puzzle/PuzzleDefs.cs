using System;
using System.Collections.Generic;

namespace Wavebreak.Game.Puzzle;

/// <summary>What a room of a puzzle map holds. <see cref="Start"/> and
/// <see cref="Reward"/> bracket the puzzle rooms; the rest are the piece types.</summary>
public enum RoomKind
{
    Start,
    /// <summary>Pressure plates held down by pushable crates (and one player).</summary>
    Crates,
    /// <summary>A light beam turned by rotating mirrors onto a crystal.</summary>
    Mirrors,
    /// <summary>Plates inside rings of gusting wind: cross in the calm.</summary>
    Wind,
    /// <summary>Rune bells struck in the order they were shown (memory).</summary>
    Runes,
    /// <summary>Levers that each flip several lamps; light them all.</summary>
    Levers,
    /// <summary>Two plates at once: two players, or one with the plate's latch.</summary>
    Twin,
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

/// <summary>The shapes carved on rune bells. Every piece that has to be told
/// apart says so with a shape, never with colour alone.</summary>
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
/// Names and words for the puzzle route, in one place: the HUD, the world
/// labels, the content audit and the Arabic table all read from here, so a new
/// piece type cannot ship without its text.
/// </summary>
public static class PuzzleText
{
    public const string RouteName = "Puzzle Route";

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

    /// <summary>Room titles, per kind.</summary>
    public static string Title(RoomKind kind) => kind switch
    {
        RoomKind.Start => "Antechamber",
        RoomKind.Crates => "Weighing Room",
        RoomKind.Mirrors => "Mirror Room",
        RoomKind.Wind => "Gust Room",
        RoomKind.Runes => "Bell Room",
        RoomKind.Levers => "Lamp Room",
        RoomKind.Twin => "Twin Plates",
        RoomKind.Reward => "Reward Vault",
        _ => "",
    };

    /// <summary>What to do, one line, for the HUD.</summary>
    public static string Objective(RoomKind kind, bool assist) => kind switch
    {
        RoomKind.Start => "Head through the door. Solve each room to open the next.",
        RoomKind.Crates => "Push crates onto the plates, then stand on the last one.",
        RoomKind.Mirrors => "Turn the mirrors until the beam reaches the crystal.",
        RoomKind.Wind => "Cross the gust rings while the wind is calm and touch every plate.",
        RoomKind.Runes => "Strike the bells in the order they rang.",
        RoomKind.Levers => "Pull levers until every lamp stands tall and lit.",
        RoomKind.Twin => assist ? "Step on one plate, then reach the other before its latch runs out."
                                : "Two players: stand on both plates at the same time.",
        RoomKind.Reward => "Claim one reward, then step into the exit portal.",
        _ => "",
    };

    /// <summary>The hint for a kind, shown with whatever the room highlights.</summary>
    public static string Hint(RoomKind kind) => kind switch
    {
        RoomKind.Crates => "Hint: the marked crate moves next, in the arrow's direction. The bell resets the crates.",
        RoomKind.Mirrors => "Hint: the marked mirror is turned the wrong way.",
        RoomKind.Wind => "Hint: wait at the ring's edge until the arrows drop, then run straight in.",
        RoomKind.Runes => "Hint: the next bell is marked. Strike the centre bell to hear the order again.",
        RoomKind.Levers => "Hint: the marked lever is part of the answer.",
        RoomKind.Twin => "Hint: the plate keeps its latch for a few seconds after you step off.",
        _ => "Hint: solve the room to open its door.",
    };

    /// <summary>The verb on the interact prompt, per piece.</summary>
    public const string VerbMirror = "Turn the mirror";
    public const string VerbLever = "Pull the lever";
    public const string VerbBell = "Strike the bell";
    public const string VerbEcho = "Ring the order again";
    public const string VerbReset = "Reset the crates";
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

    /// <summary>Every string above, for the content audit and the Arabic table check.</summary>
    public static IEnumerable<string> All()
    {
        yield return RouteName;
        foreach (string n in MapNames) yield return n;
        foreach (RoomKind k in Enum.GetValues<RoomKind>())
        {
            yield return Title(k);
            yield return Objective(k, false);
            yield return Objective(k, true);
            yield return Hint(k);
        }
        yield return VerbMirror;
        yield return VerbLever;
        yield return VerbBell;
        yield return VerbEcho;
        yield return VerbReset;
        yield return VerbClaim;
        foreach (RuneSymbol s in Enum.GetValues<RuneSymbol>()) yield return SymbolName(s);
        foreach (RewardTier t in Enum.GetValues<RewardTier>()) if (t != RewardTier.None) yield return TierName(t);
        foreach (PuzzleRewardKind r in Enum.GetValues<PuzzleRewardKind>())
        {
            yield return RewardName(r);
            yield return RewardBlurb(r);
        }
    }
}
