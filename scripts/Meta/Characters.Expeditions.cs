using System.Collections.Generic;
using Godot;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

public static partial class Characters
{
    public static bool IsExpedition(string id) => id is "nori" or "vesper";

    private static List<CharacterDef> ExpeditionCrew() => new()
    {
        new()
        {
            Id = "nori", DisplayName = "Nori the Reef Runner", LookId = "expedition-nori",
            Blurb = "A bright reef fish with powerful fins and a diver's belt. At home in Lumenreef; ready for every shore.",
            StartingWeapon = "reef_shotgun", Tint = new Color(0.12f, 0.82f, 0.78f),
            Passive = PassiveKind.Momentum, PassiveName = "Momentum",
            PassiveText = "Moving faster than a walk builds up to +30% damage. The bonus fades slowly once you stop, so keep sliding and dashing.",
            Stats = new[] { new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, 0.08f) },
        },
        new()
        {
            Id = "vesper", DisplayName = "Vesper the Skyline Scout", LookId = "expedition-vesper",
            Blurb = "A city scout in a cropped jacket, crossed harness and storm scarf. Carries twin hook reels. Recommended for Bellspire; playable everywhere.",
            StartingWeapon = "skyline_swords", Tint = new Color(0.72f, 0.48f, 0.28f),
            Passive = PassiveKind.Tailwind, PassiveName = "Tailwind",
            PassiveText = "One extra air jump. Every kill takes 0.3 s off the dash cooldown.",
            Stats = new[] { new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, 0.06f) },
        },
    };
}
