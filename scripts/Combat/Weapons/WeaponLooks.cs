using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Combat.Weapons;

/// <summary>What a styled shot does in flight beyond its <see cref="ProjectileMode"/>
/// (Combat/Weapons/Projectile.Style.cs). None is the old plain shot.</summary>
internal enum ShotStyle : byte
{
    None,
    /// <summary>Splinter Rail: when it breaks on a body it bursts into two small splinters.</summary>
    Shard,
    /// <summary>Fan of Sparks: crackles side to side; hits harder close, weaker far.</summary>
    Spark,
    /// <summary>Brinespike: spins, drags what it pierces along the line, harder per body.</summary>
    Barb,
    /// <summary>Glasswire Needles: a crit leaves a glint.</summary>
    Needle,
    /// <summary>Searing Longshot: leaves a smoke line.</summary>
    Tracer,
    /// <summary>Plain look, no rule (shotgun pellets, turret bolts).</summary>
    Plain,
    /// <summary>Wisp Seeker: drifts and bobs before it commits, then curves in slowly.</summary>
    Wisp,
    /// <summary>Judgement Lance: steers to its first target, then flies dead straight.</summary>
    Lance,
    /// <summary>Blightwasp Swarm: jittery, each wasp takes a different target.</summary>
    Wasp,
    /// <summary>Tempest Spindle: corkscrews, and snaps to an unhit body after each pierce.</summary>
    Spindle,
    /// <summary>Mortar Shell: a high lob to a marked spot; blows up on landing only.</summary>
    Lob,
    /// <summary>Bloomcap Spores: bursts and splits into two hopping seedlings.</summary>
    Pod,
    /// <summary>A seedling's short hop and pop.</summary>
    Seedling,
    /// <summary>Hailstone Cluster: a low throw that skips once and shatters where it lands.</summary>
    Hail,
    /// <summary>Aurora Choir: a mote that spirals outward.</summary>
    Mote,
    /// <summary>Knife Reef: a glass shard that turns toward the nearest body.</summary>
    Homeshard,
    /// <summary>Reef of Teeth: a shell bit lobbed off a corpse that bursts where it lands.</summary>
    ShellBit,
    /// <summary>Plague Hive: a homing wasp from the hive.</summary>
    HiveWasp,
    // ---- styles of the expansion deliveries an evolution borrows
    /// <summary>Glass Gale: the boomerang flies a loop instead of out-and-back.</summary>
    Loop,
    /// <summary>Eye of the Cyclone: the orbit breathes in and out.</summary>
    Breathe,
    /// <summary>Winter's Keep: the ring marches (advance, pause, advance).</summary>
    March,
    /// <summary>Breakwater Cleaver: the ring is a forward third, not all round.</summary>
    Front,
    /// <summary>Frozen Gyre: the vortex drifts toward the nearest body.</summary>
    Drift,
    /// <summary>Deepwater Harpoon: hits pull toward the thrower, with a rope.</summary>
    Harpoon,
    /// <summary>Skimstone Sling: hops along the ground as it skips.</summary>
    Skip,
    /// <summary>Plague Hive: the turret's shots are homing wasps.</summary>
    Hive,
    /// <summary>Bloomtide: the pool grows while it lasts.</summary>
    Grow,
    /// <summary>Broadside Barrage: shells come in at a slant from one side.</summary>
    Cannon,
    /// <summary>Sunken Sun: a burning chunk falls at a slant, trailing flame.</summary>
    Meteor,
    /// <summary>Hundred Forks: no stone; a bolt comes straight down at the end.</summary>
    Bolt,
    /// <summary>Worldforge Maul: a spike rises out of the ground instead of falling.</summary>
    Erupt,
}

/// <summary>What a hit or a burst looks like where it lands.</summary>
internal enum ImpactKind : byte { Blast, Glass, Spark, Fire, Frost, Spore, Thunder, Debris }

/// <summary>The weapon's firing sound (Audio/AudioDirector.cs, Audio/SfxSynth.Voices.cs).
/// Shape keeps the old per-shape sound.</summary>
public enum WeaponVoice : byte { Shape, Glass, Zap, Thunk, Crack, Chime, Buzz, Pop, Whoosh, Hiss, Hum, Clang, Twang }

/// <summary>A weapon's own look: shot mesh and size, flight style, impact,
/// and voice. <see cref="Mesh"/> null keeps the mode's default mesh.</summary>
internal readonly record struct Look(Mesh? Mesh, float Scale, ShotStyle Style, ImpactKind Impact, WeaponVoice Voice, float Pitch = 1f);

/// <summary>
/// The table that keeps look-alike weapons apart (docs/weapon-audit.md). One
/// row per weapon id, and per evolution id where the evolution borrows another
/// weapon's delivery, so a borrowed delivery wears its own shape and twist.
/// Every behaviour twist reads its row through <see cref="For"/>; the content
/// audit and <c>--check-distinct</c> read the same rows.
/// </summary>
internal static class WeaponLooks
{
    private static Dictionary<string, Look>? _rows;

    private static Dictionary<string, Look> Rows => _rows ??= new Dictionary<string, Look>
    {
        // ---- straight shots
        ["splinter_rail"] = new(LookMeshes.Shard, 0.26f, ShotStyle.Shard, ImpactKind.Glass, WeaponVoice.Glass, 1f),
        ["sparkfan"] = new(LookMeshes.Spark, 0.26f, ShotStyle.Spark, ImpactKind.Spark, WeaponVoice.Zap, 1.15f),
        ["brinespike"] = new(LookMeshes.Barb, 0.34f, ShotStyle.Barb, ImpactKind.Spore, WeaponVoice.Thunk, 1.1f),
        ["glasswire_needles"] = new(LookMeshes.Needle, 0.24f, ShotStyle.Needle, ImpactKind.Glass, WeaponVoice.Glass, 1.5f),
        ["searing_longshot"] = new(LookMeshes.Tracer, 0.3f, ShotStyle.Tracer, ImpactKind.Fire, WeaponVoice.Crack, 1f),
        ["reef_shotgun"] = new(LookMeshes.Pellet, 0.12f, ShotStyle.Plain, ImpactKind.Blast, WeaponVoice.Shape, 1f),
        // ---- seekers
        ["wisp_seeker"] = new(LookMeshes.Wisp, 0.24f, ShotStyle.Wisp, ImpactKind.Frost, WeaponVoice.Chime, 1f),
        ["judgement_lance"] = new(LookMeshes.Lance, 0.42f, ShotStyle.Lance, ImpactKind.Thunder, WeaponVoice.Crack, 0.7f),
        ["blightwasp_swarm"] = new(LookMeshes.Wasp, 0.22f, ShotStyle.Wasp, ImpactKind.Spore, WeaponVoice.Buzz, 1f),
        ["tempest_spindle"] = new(LookMeshes.Spindle, 0.3f, ShotStyle.Spindle, ImpactKind.Spark, WeaponVoice.Zap, 0.75f),
        // ---- explosives
        ["mortar_shell"] = new(LookMeshes.MortarShell, 0.42f, ShotStyle.Lob, ImpactKind.Debris, WeaponVoice.Thunk, 0.7f),
        ["bloomcap_spores"] = new(LookMeshes.Pod, 0.36f, ShotStyle.Pod, ImpactKind.Spore, WeaponVoice.Pop, 1f),
        ["hailstone_cluster"] = new(LookMeshes.Hailstone, 0.3f, ShotStyle.Hail, ImpactKind.Frost, WeaponVoice.Clang, 1.3f),
        // ---- fields and swings (their meshes live in WeaponRuntime.Looks.cs)
        ["ember_halo"] = new(null, 1f, ShotStyle.None, ImpactKind.Fire, WeaponVoice.Hum, 0.8f),
        ["static_field"] = new(null, 1f, ShotStyle.None, ImpactKind.Spark, WeaponVoice.Zap, 0.6f),
        ["rimeheart"] = new(null, 1f, ShotStyle.None, ImpactKind.Frost, WeaponVoice.Chime, 0.75f),
        ["cleaver_arc"] = new(null, 1f, ShotStyle.None, ImpactKind.Blast, WeaponVoice.Whoosh, 0.85f),
        ["riptide_blades"] = new(null, 1f, ShotStyle.None, ImpactKind.Frost, WeaponVoice.Whoosh, 1.45f),
        ["forgefall_maul"] = new(null, 1f, ShotStyle.None, ImpactKind.Debris, WeaponVoice.Thunk, 0.55f),
        ["skyline_swords"] = new(null, 1f, ShotStyle.None, ImpactKind.Blast, WeaponVoice.Clang, 1f),
        ["sleetbreath_horn"] = new(null, 1f, ShotStyle.None, ImpactKind.Frost, WeaponVoice.Hiss, 1f),
        // ---- expansion weapons that already had their own delivery
        ["gullwing_chakram"] = new(null, 1f, ShotStyle.None, ImpactKind.Blast, WeaponVoice.Whoosh, 1.2f),
        ["skimmer_kite"] = new(LookMeshes.Fang, 1f, ShotStyle.None, ImpactKind.Spark, WeaponVoice.Shape, 1f),
        ["circling_shoal"] = new(null, 1f, ShotStyle.None, ImpactKind.Frost, WeaponVoice.Hum, 1.2f),
        ["lighthouse_beam"] = new(null, 1f, ShotStyle.None, ImpactKind.Fire, WeaponVoice.Hum, 1.4f),
        ["spyglass_glint"] = new(null, 1f, ShotStyle.None, ImpactKind.Glass, WeaponVoice.Shape, 1f),
        ["forked_squall"] = new(null, 1f, ShotStyle.None, ImpactKind.Spark, WeaponVoice.Zap, 1f),
        ["thunderhead_censer"] = new(null, 1f, ShotStyle.None, ImpactKind.Blast, WeaponVoice.Thunk, 1.2f),
        ["breaker_swell"] = new(null, 1f, ShotStyle.None, ImpactKind.Blast, WeaponVoice.Hum, 0.6f),
        ["maelstrom_seed"] = new(null, 1f, ShotStyle.None, ImpactKind.Blast, WeaponVoice.Hum, 0.9f),
        ["skimstone_sling"] = new(null, 1f, ShotStyle.Skip, ImpactKind.Blast, WeaponVoice.Twang, 1.2f),
        ["driftwood_sentry"] = new(null, 1f, ShotStyle.None, ImpactKind.Blast, WeaponVoice.Twang, 0.9f),
        ["brine_wake"] = new(null, 1f, ShotStyle.None, ImpactKind.Spore, WeaponVoice.Hiss, 0.7f),
        ["barnacle_mines"] = new(null, 1f, ShotStyle.None, ImpactKind.Debris, WeaponVoice.Pop, 0.7f),
        // ---- evolutions that borrow a delivery: their own shape and twist
        ["evo_glass_gale"] = new(LookMeshes.NeedleFan, 1f, ShotStyle.Loop, ImpactKind.Glass, WeaponVoice.Glass, 1.3f),
        ["evo_cyclone_eye"] = new(LookMeshes.Spindle, 1f, ShotStyle.Breathe, ImpactKind.Spark, WeaponVoice.Zap, 0.8f),
        ["evo_truthline_rail"] = new(null, 1f, ShotStyle.None, ImpactKind.Glass, WeaponVoice.Glass, 0.8f),
        ["evo_broadside_barrage"] = new(LookMeshes.Cannonball, 0.42f, ShotStyle.Cannon, ImpactKind.Debris, WeaponVoice.Thunk, 0.6f),
        ["evo_winters_keep"] = new(LookMeshes.IceRing, 1f, ShotStyle.March, ImpactKind.Frost, WeaponVoice.Chime, 0.6f),
        ["evo_frozen_gyre"] = new(LookMeshes.ShardWheel, 1f, ShotStyle.Drift, ImpactKind.Frost, WeaponVoice.Clang, 1.1f),
        ["evo_deepwater_harpoon"] = new(LookMeshes.Harpoon, 0.6f, ShotStyle.Harpoon, ImpactKind.Spore, WeaponVoice.Twang, 0.7f),
        ["evo_plague_hive"] = new(LookMeshes.Hive, 0.55f, ShotStyle.Hive, ImpactKind.Spore, WeaponVoice.Buzz, 0.8f),
        // ---- evolutions whose rider borrows a shared effect
        ["evo_sunken_sun"] = new(LookMeshes.Ember, 0.5f, ShotStyle.Meteor, ImpactKind.Fire, WeaponVoice.Hum, 0.8f),
        ["evo_hundred_forks"] = new(LookMeshes.Star, 0.6f, ShotStyle.Bolt, ImpactKind.Thunder, WeaponVoice.Zap, 1f),
        ["evo_worldforge_maul"] = new(LookMeshes.MagmaSpike, 0.7f, ShotStyle.Erupt, ImpactKind.Fire, WeaponVoice.Thunk, 0.55f),
        ["evo_breakwater_cleaver"] = new(LookMeshes.CrestArc, 1f, ShotStyle.Front, ImpactKind.Blast, WeaponVoice.Whoosh, 0.85f),
        ["evo_bloomtide"] = new(LookMeshes.SporePatch, 1f, ShotStyle.Grow, ImpactKind.Spore, WeaponVoice.Pop, 1f),
        ["evo_aurora_choir"] = new(LookMeshes.Wisp, 0.2f, ShotStyle.Mote, ImpactKind.Frost, WeaponVoice.Chime, 1f),
        ["evo_stormcage"] = new(null, 1f, ShotStyle.None, ImpactKind.Spark, WeaponVoice.Zap, 0.6f),
        ["evo_stormwing"] = new(LookMeshes.Shard, 0.2f, ShotStyle.Plain, ImpactKind.Glass, WeaponVoice.Whoosh, 1.2f),
        ["evo_knife_reef"] = new(LookMeshes.Shard, 0.18f, ShotStyle.Homeshard, ImpactKind.Glass, WeaponVoice.Hum, 1.2f),
        ["evo_reef_of_teeth"] = new(LookMeshes.Seedling, 0.18f, ShotStyle.ShellBit, ImpactKind.Debris, WeaponVoice.Pop, 0.7f),
        ["evo_sunlance"] = new(null, 1f, ShotStyle.None, ImpactKind.Fire, WeaponVoice.Crack, 1f),
        ["evo_breaching_gun"] = new(null, 1f, ShotStyle.None, ImpactKind.Debris, WeaponVoice.Shape, 1f),
        ["evo_stormfan"] = new(null, 1f, ShotStyle.None, ImpactKind.Spark, WeaponVoice.Zap, 1.15f),
        ["evo_final_verdict"] = new(null, 1f, ShotStyle.None, ImpactKind.Thunder, WeaponVoice.Crack, 0.7f),
        ["evo_undertow_blades"] = new(null, 1f, ShotStyle.None, ImpactKind.Frost, WeaponVoice.Whoosh, 1.45f),
        ["evo_twin_horizons"] = new(null, 1f, ShotStyle.None, ImpactKind.Blast, WeaponVoice.Clang, 1f),
    };

    /// <summary>Ids with a row (the check and the audit walk these).</summary>
    public static IEnumerable<string> Ids => Rows.Keys;

    public static bool TryGet(string id, out Look look) => Rows.TryGetValue(id, out look);

    /// <summary>The row an evolved weapon wears: the evolution's own, else its weapon's.</summary>
    public static Look For(string weaponId, EvolutionDef? evolution)
    {
        if (evolution is not null && Rows.TryGetValue(evolution.Id, out Look e)) return e;
        return Rows.TryGetValue(weaponId, out Look w) ? w : default;
    }

    public static Look For(WeaponRuntime w) => For(w.Def.Id, w.Evolution);

    /// <summary>The evolution's own row only (null when it has none).</summary>
    public static Look? OfEvolution(EvolutionDef? e) => e is not null && Rows.TryGetValue(e.Id, out Look l) ? l : null;

    /// <summary>Put a row's mesh and style on a shot. A row without a mesh
    /// leaves the shot as it was.</summary>
    public static void Dress(ref ProjectileSpec spec, in Look look, int index = 0)
    {
        if (look.Mesh is null && look.Style == ShotStyle.None) return;
        spec.Mesh = look.Mesh;
        spec.MeshScale = look.Scale;
        spec.Style = look.Style;
        spec.Impact = look.Impact;
        spec.StyleIndex = index;
    }

    /// <summary>The voice a weapon fires with, for the audio director.</summary>
    public static (WeaponVoice Voice, float Pitch) VoiceOf(string? weaponId, string? evolutionId)
    {
        if (evolutionId is not null && Rows.TryGetValue(evolutionId, out Look e) && e.Voice != WeaponVoice.Shape) return (e.Voice, e.Pitch);
        if (weaponId is not null && Rows.TryGetValue(weaponId, out Look w)) return (w.Voice, w.Pitch);
        return (WeaponVoice.Shape, 1f);
    }

    /// <summary>Only the shapes are cut on the Low preset: trails, puffs and
    /// ornament particles go, every silhouette and every twist stays.</summary>
    public static bool Extras => Meta.GraphicsSettings.Current.Preset != Meta.GraphicsSettings.Low;
}

/// <summary>How often each twist actually happened this process: the
/// <c>--check-distinct</c> run fires each weapon and reads these back.</summary>
internal static class DistinctStats
{
    private static readonly Dictionary<string, int> Counts = new();

    public static void Note(string twist) => Counts[twist] = Counts.GetValueOrDefault(twist) + 1;

    public static int Of(string twist) => Counts.GetValueOrDefault(twist);

    public static void Reset() => Counts.Clear();
}
