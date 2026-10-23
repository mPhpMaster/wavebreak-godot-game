using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>Challenge cards: what each modifier does, its Pearls bonus, and
/// the maps it has been cleared on (and wins) from the profile.</summary>
public sealed partial class Codex
{
    private static readonly Color ChallengeInk = new(0.40f, 0.82f, 1.00f);

    private static Button ChallengeCard(ChallengeDef c)
    {
        List<string> maps = Challenges.ClearedMaps(c);
        long wins = Challenges.Wins(c);
        bool done = wins > 0;
        string cleared = maps.Count == 0 ? "No map cleared yet" : "Cleared: " + string.Join(", ", maps);
        string status = done ? $"WON x{wins}" : $"CHALLENGE  ·  {maps.Count}/{Data.Stages.All.Count} MAPS";
        return Card(c.Name, c.Blurb, $"{c.RewardText}  ·  {cleared}", status, done ? Earned : ChallengeInk, done);
    }
}
