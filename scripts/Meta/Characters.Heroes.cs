using System.Collections.Generic;
using Godot;
using Wavebreak.Combat.Weapons;
using Wavebreak.Stats;
using Wavebreak.Visuals;

namespace Wavebreak.Meta;

/// <summary>
/// The heroes trio: three harbour folk with hand-built bodies of their own
/// (<see cref="HeroModels"/>: a parrot captain, a sailor dog, a kite fox), each
/// with a signature weapon only they can hold (<see cref="Signatures"/>), a
/// passive (<see cref="CharacterPassives"/>, Heroes partial), a stat trade, six
/// skins and an unlock quest (<see cref="HeroQuests"/>). They are built to play
/// differently from each other:
///
///   * Sunny the Lookout (parrot): precise. Look at what you want hit.
///   * Biscuit the Sea Dog: control. Pin a crowd in a ring and haul it.
///   * Flick the Kite Fox: movement. The weapon is steered by running.
///
/// No static fields: <see cref="Characters.All"/> is built from the original
/// file's initialiser (see Characters.Cute.cs).
/// </summary>
public static partial class Characters
{
    /// <summary>Ids of the trio, in roster order.</summary>
    public static readonly string[] HeroIds = { "sunny", "biscuit", "flick" };

    public static bool IsHero(string id) => System.Array.IndexOf(HeroIds, id) >= 0;

    /// <summary>A soft hue rotation of the fur or plumage; brass, eyes and iron keep theirs.</summary>
    private static SkinDef HeroShift(string id, string name, string look, Color tint, float turns, float saturation = 1f, float value = 1f)
        => new() { Id = id, Name = name, LookId = look, Tint = tint, Recolor = new SkinRecolor(turns, saturation, value, Colors.White, 0f, 0.10f) };

    private static List<CharacterDef> HeroCrew() => new()
    {
        // Precise: a lookout who never misses what she is looking at.
        new()
        {
            Id = "sunny",
            DisplayName = "Sunny the Lookout",
            Blurb = "Keeps watch from the crow's nest with a brass spyglass and a very loud voice. Never misses a thing, and rarely misses a shot.",
            StartingWeapon = Signatures.Spyglass,
            LookId = HeroModels.Sunny,
            Tint = new Color(0.95f, 0.35f, 0.30f),
            Passive = PassiveKind.CalmLookout,
            PassiveName = "Calm Lookout",
            PassiveText = "After 3 seconds without being hit, gain +15% critical hit chance and +30% critical damage until you are hit.",
            Stats = new[]
            {
                new CharacterStat(Stat.CritChance, ModifierOp.Flat, 0.05f),
                new CharacterStat(Stat.CritDamage, ModifierOp.Additive, 0.15f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -10f),
            },
            ExtraSkins = new[]
            {
                CuteSkin("sunny.macaw", "Golden Macaw", HeroModels.Sunny, new Color(1.00f, 0.82f, 0.25f), 0.85f, 1.10f, 0.95f),
                CuteSkin("sunny.lagoon", "Blue Lagoon", HeroModels.Sunny, new Color(0.30f, 0.62f, 1.00f), 0.85f, 1.05f, 0.95f),
                HeroShift("sunny.parakeet", "Parakeet", HeroModels.Sunny, new Color(0.45f, 0.90f, 0.40f), 0.30f, 0.80f, 1.00f),
                CuteSkin("sunny.petrel", "Storm Petrel", HeroModels.Sunny, new Color(0.42f, 0.46f, 0.58f), 0.85f, 0.85f, 0.55f),
                CuteSkin("sunny.cockatoo", "Coral Cockatoo", HeroModels.Sunny, new Color(1.00f, 0.78f, 0.82f), 0.80f, 1.30f, 0.55f),
            },
        },
        // Control: the bosun who moors anything that comes too close.
        new()
        {
            Id = "biscuit",
            DisplayName = "Biscuit the Sea Dog",
            Blurb = "The harbour's bosun: knows every knot, every bollard and every rope. Once he has moored something, it stays moored.",
            StartingWeapon = Signatures.Anchor,
            LookId = HeroModels.Biscuit,
            Tint = new Color(0.82f, 0.62f, 0.40f),
            Passive = PassiveKind.OldSalt,
            PassiveName = "Old Salt",
            PassiveText = "+1 armour for every enemy within 5 m, up to +12. The tighter the crowd, the harder he is to shift.",
            Stats = new[]
            {
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, 20f),
                new CharacterStat(Stat.Armor, ModifierOp.Flat, 3f),
                new CharacterStat(Stat.AreaSize, ModifierOp.Additive, 0.10f),
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, -0.08f),
            },
            ExtraSkins = new[]
            {
                CuteSkin("biscuit.sailor", "Salty Sailor", HeroModels.Biscuit, new Color(0.22f, 0.45f, 0.85f), 0.88f, 1.00f, 0.95f),
                CuteSkin("biscuit.rust", "Rusty Chain", HeroModels.Biscuit, new Color(0.62f, 0.20f, 0.12f), 0.92f, 0.72f, 1.10f),
                CuteSkin("biscuit.seafoam", "Seafoam Pup", HeroModels.Biscuit, new Color(0.55f, 0.95f, 0.82f), 0.85f, 1.15f, 0.80f),
                CuteSkin("biscuit.midnight", "Midnight Watch", HeroModels.Biscuit, new Color(0.20f, 0.24f, 0.45f), 0.85f, 0.75f, 0.85f),
                CuteSkin("biscuit.sandy", "Sandy Paws", HeroModels.Biscuit, new Color(1.00f, 0.90f, 0.66f), 0.80f, 1.25f, 0.65f),
            },
        },
        // Movement: a fox and a kite, both always going somewhere.
        new()
        {
            Id = "flick",
            DisplayName = "Flick the Kite Fox",
            Blurb = "Chases the sea breeze with twin returning storm blades. Quick on his feet, dangerous on both passes.",
            StartingWeapon = Signatures.Kite,
            LookId = HeroModels.Flick,
            Tint = new Color(1.00f, 0.55f, 0.25f),
            Passive = PassiveKind.SeaBreeze,
            PassiveName = "Sea Breeze",
            PassiveText = "Every 40 m you travel adds a Breeze stack, up to 5: +4% attack speed and +3% move speed each. Standing still loses one every 2 s.",
            Stats = new[]
            {
                new CharacterStat(Stat.MoveSpeed, ModifierOp.Additive, 0.12f),
                new CharacterStat(Stat.JumpHeight, ModifierOp.Additive, 0.10f),
                new CharacterStat(Stat.MaxHealth, ModifierOp.Flat, -15f),
            },
            ExtraSkins = new[]
            {
                CuteSkin("flick.arctic", "Arctic Fox", HeroModels.Flick, new Color(0.85f, 0.93f, 1.00f), 0.85f, 1.35f, 0.40f),
                CuteSkin("flick.ember", "Ember Tail", HeroModels.Flick, new Color(0.95f, 0.22f, 0.18f), 0.85f, 0.95f, 1.00f),
                CuteSkin("flick.lavender", "Lavender Dusk", HeroModels.Flick, new Color(0.70f, 0.55f, 0.98f), 0.85f, 1.05f, 0.85f),
                HeroShift("flick.seaglass", "Seaglass", HeroModels.Flick, new Color(0.35f, 0.85f, 0.80f), 0.47f, 0.85f, 1.05f),
                CuteSkin("flick.nightkite", "Night Kite", HeroModels.Flick, new Color(0.18f, 0.22f, 0.40f), 0.85f, 0.70f, 0.90f),
            },
        },
    };
}
