using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Combat.Weapons;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// <c>--check-distinct</c> (no <c>--auto</c>): the look-alike weapons made
/// distinct (docs/weapon-audit.md), checked twice.
///
///  * On paper: every member of every look-alike group
///    (<see cref="DistinctShowcase.Groups"/>) has a row in
///    <see cref="WeaponLooks"/>, and within a group no two members share a
///    visual (what you see: mesh or effect), a twist (what it does), or a voice
///    (sound and pitch). A group may keep one member as the plain reference.
///  * In the game: every member is fired for 30 s at the evolution check's
///    pack, and its twist must actually happen (a counter the twist bumps);
///    the effect budget must hold. The pack and lone-target output is printed
///    beside it, the same numbers as <c>--distinct-dps</c>.
///
/// Prints DISTINCT lines and DISTINCT PASS/FAIL, then quits.
/// </summary>
public static class DistinctCheck
{
    public static bool Requested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-distinct") >= 0;

    /// <summary>member -> (what you see, what it does). A null twist marks the
    /// group's plain reference, which keeps its old behaviour.</summary>
    private static readonly Dictionary<string, (string Visual, string? Twist)> Table = new()
    {
        ["splinter_rail"] = ("glass shard", "shard_split"),
        ["sparkfan"] = ("crackling star", "spark_close"),
        ["brinespike"] = ("rolling barb", "barb_ramp"),
        ["glasswire_needles"] = ("needle stream", "needle_stream"),
        ["searing_longshot"] = ("tracer + smoke line", "longshot_toughest"),
        ["reef_shotgun"] = ("pellet cloud + bubbles", null),
        ["wisp_seeker"] = ("bobbing comet", "wisp_drift"),
        ["judgement_lance"] = ("spear + pillar", "lance_straight"),
        ["blightwasp_swarm"] = ("striped wasps", "wasp_spread"),
        ["tempest_spindle"] = ("corkscrew spindle + thread", "spindle_snap"),
        ["mortar_shell"] = ("iron shell, landing ring", "lob_landed"),
        ["bloomcap_spores"] = ("spotted pod + seedlings", "pod_split"),
        ["hailstone_cluster"] = ("ice chunk, frost star", "hail_skip"),
        ["ember_halo"] = ("flame ring", "ember_ramp"),
        ["static_field"] = ("zigzag pulse + arcs", "static_pulse"),
        ["rimeheart"] = ("snowflake", "rime_pull"),
        ["cleaver_arc"] = ("heavy crescent", "cleaver_sweep"),
        ["riptide_blades"] = ("crossed thrust", "riptide_thrust"),
        ["forgefall_maul"] = ("crater slam", "maul_slam"),
        ["skyline_swords"] = ("two thin crossing arcs", "skyline_cross"),
        ["sleetbreath_horn"] = ("sleet puffs", "horn_breath"),
        ["gullwing_chakram"] = ("spinning disc", null),
        ["skimmer_kite"] = ("fang blades zigzag", "fang_tack"),
        ["evo_glass_gale"] = ("needle fans looping", "gale_loop"),
        ["circling_shoal"] = ("glass fins", null),
        ["evo_cyclone_eye"] = ("breathing spindles", "cyclone_breathe"),
        ["lighthouse_beam"] = ("sweeping lamp", "lighthouse_sweep"),
        ["evo_truthline_rail"] = ("hair line + glints", "truthline_glint"),
        ["spyglass_glint"] = ("sight beam + glint", null),
        ["forked_squall"] = ("jagged chain", "squall_jag"),
        ["thunderhead_censer"] = ("hailstones fall", null),
        ["evo_broadside_barrage"] = ("cannonballs in a row", "broadside_line"),
        ["evo_sunken_sun"] = ("slanting embers", "meteor_slant"),
        ["evo_hundred_forks"] = ("bolt from the sky", "bolt_strike"),
        ["evo_worldforge_maul"] = ("magma spikes rise", "erupt_spike"),
        ["breaker_swell"] = ("crest ring", null),
        ["evo_winters_keep"] = ("marching ice spikes", "keep_march"),
        ["evo_breakwater_cleaver"] = ("forward wave", "wave_front"),
        ["maelstrom_seed"] = ("whirlpool", null),
        ["evo_frozen_gyre"] = ("drifting shard wheel", "gyre_drift"),
        ["skimstone_sling"] = ("hopping pebble", "skim_hop"),
        ["evo_deepwater_harpoon"] = ("harpoon + rope", "harpoon_pull"),
        ["driftwood_sentry"] = ("crossbow post", null),
        ["evo_plague_hive"] = ("hive + wasps", "hive_wasp"),
        ["brine_wake"] = ("brine pools", null),
        ["evo_bloomtide"] = ("growing mushroom patch", "bloom_grow"),
        ["evo_stormcage"] = ("forks from you", "stormcage_fork"),
        ["evo_aurora_choir"] = ("spiralling motes", "aurora_spiral"),
        ["evo_stormwing"] = ("splinter fan", "stormwing_fan"),
        ["evo_knife_reef"] = ("homing glass", "knife_home"),
        ["evo_reef_of_teeth"] = ("lobbed shell bits", "teeth_lob"),
        ["evo_sunlance"] = ("flame ring", "sunlance_flare"),
        ["evo_breaching_gun"] = ("cone through the body", "breach_cone"),
        ["evo_stormfan"] = ("jagged forks", "stormfan_fork"),
        ["evo_final_verdict"] = ("pillars from above", "verdict_pillar"),
        ["evo_undertow_blades"] = ("dark thrust drag", "undertow_drag"),
        ["evo_twin_horizons"] = ("horizon line", "horizon_echo"),
    };

    private static async Task Wait(SceneTree tree, double s)
        => await tree.ToSignal(tree.CreateTimer(s, processAlways: false), SceneTreeTimer.SignalName.Timeout);

    public static async Task Run(Main main)
    {
        SceneTree tree = main.GetTree();
        await Wait(tree, 1.5);
        bool ok = true;
        void Expect(string what, bool cond)
        {
            GD.Print($"DISTINCT {(cond ? "ok  " : "FAIL")} {what}");
            ok &= cond;
        }

        // ---- on paper
        foreach ((string group, string[] members) in DistinctShowcase.Groups)
        {
            foreach (string m in members)
            {
                Expect($"{group}/{m} is a real weapon or evolution",
                       Catalog.WeaponById(m) is not null || Evolutions.ById(m) is not null);
                Expect($"{group}/{m} has a look row and a table row", WeaponLooks.TryGet(m, out _) && Table.ContainsKey(m));
            }
            var rows = members.Where(Table.ContainsKey).ToList();
            Expect($"{group}: every member looks different",
                   rows.Select(m => Table[m].Visual).Distinct().Count() == rows.Count);
            var twists = rows.Select(m => Table[m].Twist).ToList();
            Expect($"{group}: every member does something different (at most one plain reference)",
                   twists.Count(t => t is null) <= 1 && twists.Where(t => t is not null).Distinct().Count() == twists.Count(t => t is not null));
            var voices = rows.Select(m => WeaponLooks.TryGet(m, out Look l) ? $"{l.Voice}@{l.Pitch:0.00}" : "?").ToList();
            Expect($"{group}: every member sounds different ({string.Join(", ", voices)})",
                   voices.Distinct().Count() == voices.Count);
        }

        // ---- in the game
        Screenshot.Invulnerable = true;
        Main.CatchUpPicks = 100000;
        main.Player.MoveOverride = () => Vector3.Zero;
        EnemyManager.DevFrozen = true;
        main.Enemies.ClearField(0f);
        DistinctShowcase.ClearWeapons(main);
        bool hits = WeaponSystem.PublishHitEvents, numbers = WeaponSystem.PublishDamageNumbers;
        WeaponSystem.PublishHitEvents = false;
        WeaponSystem.PublishDamageNumbers = false;
        DistinctFx.Peak = 0;
        foreach ((string group, string[] members) in DistinctShowcase.Groups)
            foreach (string m in members)
            {
                DistinctStats.Reset();
                (float pack, float lone) = DistinctShowcase.Measure(main.Weapons, m);
                string? twist = Table.GetValueOrDefault(m).Twist;
                bool happened = twist is null || DistinctStats.Of(twist) > 0;
                Expect($"{group}/{m,-24} {(twist ?? "(reference)"),-18} x{(twist is null ? 0 : DistinctStats.Of(twist))}   pack {pack,7:0.0} lone {lone,7:0.0}",
                       happened && pack > 0f);
                SignatureFx.Clear();
            }
        Expect($"effect budget held (peak {DistinctFx.Peak} of 40)", DistinctFx.Peak is > 0 and <= 40);
        WeaponSystem.PublishHitEvents = hits;
        WeaponSystem.PublishDamageNumbers = numbers;
        EnemyManager.DevFrozen = false;

        GD.Print(ok ? "DISTINCT PASS" : "DISTINCT FAIL");
        tree.Quit();
    }
}
