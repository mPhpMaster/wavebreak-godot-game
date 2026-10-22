using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// Where the imported (CC0, see assets/CREDITS.md) models live and how their
/// animation clips map onto <see cref="AnimSlot"/>s.
///
/// Imported models are the primary look; the procedural <see cref="ModelBuilder"/>
/// is the fallback for anything without a mapping and for when a file is missing
/// (a stripped build, a renamed asset) — the game must never lose a body because
/// an art file moved. <see cref="UseImported"/> switches every lookup back to the
/// procedural models at once, for comparison and for perf triage.
/// </summary>
public static class ArtAssets
{
    public const string Root = "res://assets/";

    /// <summary>Global switch: false = procedural models everywhere.</summary>
    public static bool UseImported { get; set; } = true;

    /// <summary>False = enemies keep their old primitive bodies (--art-off). Only
    /// for A/B performance measurement on the same machine state.</summary>
    public static bool ModelsEnabled { get; set; } = true;

    /// <summary>Kenney's humanoid kits (mini characters, graveyard characters)
    /// share one clip vocabulary.</summary>
    public static readonly Dictionary<AnimSlot, string> HumanoidClips = new()
    {
        [AnimSlot.Idle] = "idle",
        [AnimSlot.Walk] = "walk",
        [AnimSlot.Run] = "sprint",
        [AnimSlot.Dash] = "sprint",
        [AnimSlot.Jump] = "jump",
        [AnimSlot.Fall] = "fall",
        [AnimSlot.Slide] = "crouch",
        [AnimSlot.Slam] = "fall",
        [AnimSlot.Death] = "die",
        [AnimSlot.Attack] = "attack-melee-right",
        [AnimSlot.Windup] = "holding-right",
    };

    /// <summary>Blocky characters: the same names, without jump/fall/crouch.</summary>
    public static readonly Dictionary<AnimSlot, string> BlockyClips = new()
    {
        [AnimSlot.Idle] = "idle",
        [AnimSlot.Walk] = "walk",
        [AnimSlot.Run] = "sprint",
        [AnimSlot.Death] = "die",
        [AnimSlot.Attack] = "attack-melee-right",
        [AnimSlot.Windup] = "holding-both",
    };

    /// <summary>Cube pets have no attack or death: a bite reads as "eat", the
    /// windup as the negative gesture, and death falls back to the procedural
    /// tip-over-and-sink.</summary>
    public static readonly Dictionary<AnimSlot, string> PetClips = new()
    {
        [AnimSlot.Idle] = "idle",
        [AnimSlot.Walk] = "walk",
        [AnimSlot.Run] = "run",
        [AnimSlot.Attack] = "eat",
        [AnimSlot.Windup] = "gesture-negative",
    };

    /// <summary>Quaternius "Ultimate Monsters", Blob set (the spelling of
    /// HitRecieve is the pack's).</summary>
    public static readonly Dictionary<AnimSlot, string> QuaterniusBlobClips = new()
    {
        [AnimSlot.Idle] = "Idle",
        [AnimSlot.Walk] = "Walk",
        [AnimSlot.Attack] = "Bite_Front",
        [AnimSlot.Hit] = "HitRecieve",
        [AnimSlot.Death] = "Death",
        [AnimSlot.Jump] = "Jump",
    };

    /// <summary>Quaternius Flying set: they never touch the ground, so walking is
    /// fast flight and idle is hovering.</summary>
    public static readonly Dictionary<AnimSlot, string> QuaterniusFlyingClips = new()
    {
        [AnimSlot.Idle] = "Flying_Idle",
        [AnimSlot.Walk] = "Fast_Flying",
        [AnimSlot.Windup] = "Headbutt",
        [AnimSlot.Attack] = "Punch",
        [AnimSlot.Hit] = "HitReact",
        [AnimSlot.Death] = "Death",
    };

    /// <summary>Quaternius Big set: the richest rigs, used for brutes, elites
    /// and bosses. Weapon is the wind-up, Punch the strike, Run the charge.</summary>
    public static readonly Dictionary<AnimSlot, string> QuaterniusBigClips = new()
    {
        [AnimSlot.Idle] = "Idle",
        [AnimSlot.Walk] = "Walk",
        [AnimSlot.Run] = "Run",
        [AnimSlot.Windup] = "Weapon",
        [AnimSlot.Attack] = "Punch",
        [AnimSlot.Hit] = "HitReact",
        [AnimSlot.Death] = "Death",
        [AnimSlot.Jump] = "Jump",
        [AnimSlot.Fall] = "Jump_Idle",
    };

    /// <summary>
    /// An imported, animated model scaled to <paramref name="height"/>, or null
    /// if the file is absent or imports are switched off. Kenney models face +Z;
    /// the rig wants -Z, hence the half turn.
    /// </summary>
    public static ModelSpec? Model(string relativePath, float height, Dictionary<AnimSlot, string> clips, string? id = null)
    {
        if (!UseImported) return null;
        string path = Root + relativePath;
        ModelSpec? s = ModelSpec.FromScene(path, height, clips, yaw: Mathf.Pi, id: id ?? path);
        return s;
    }

    public static Dictionary<AnimSlot, string> ClipsFor(string relativePath)
        => relativePath.StartsWith("kenney/cube-pets/") ? PetClips
         : relativePath.StartsWith("quaternius/blob/") ? QuaterniusBlobClips
         : relativePath.StartsWith("quaternius/flying/") ? QuaterniusFlyingClips
         : relativePath.StartsWith("quaternius/big/") ? QuaterniusBigClips
         : relativePath.StartsWith("kenney/blocky-characters/") ? BlockyClips
         : HumanoidClips;
}
