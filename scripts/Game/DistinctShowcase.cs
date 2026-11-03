using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Combat.Weapons;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// Look-alike weapons made distinct (docs/weapon-audit.md,
/// Combat/Weapons/WeaponLooks.cs).
///
/// The look-alike groups and the tools to look at them (the self-check is
/// <see cref="DistinctCheck"/>).
///
/// <c>--distinct-dps</c>: every weapon and every evolution fired for 30 s at the
/// evolution check's pack and lone target (same crit rolls), one
/// <c>DISTINCT dps</c> line each: the before/after balance table.
///
/// <c>--distinct-shot=&lt;dir&gt;</c>: for every group, each member fired alone
/// at a frozen ring of real enemies, photographed 0.06, 0.16 and 0.36 s after it
/// first fires (<c>&lt;dir&gt;/&lt;group&gt;__&lt;member&gt;__&lt;n&gt;.png</c>). Add
/// <c>--distinct-only=&lt;group,group&gt;</c> to shoot fewer.
/// </summary>
public static class DistinctShowcase
{
    /// <summary>The audit's look-alike groups: weapon ids and evolution ids.</summary>
    public static readonly (string Group, string[] Members)[] Groups =
    {
        ("straight", new[] { "splinter_rail", "sparkfan", "brinespike", "glasswire_needles", "searing_longshot", "reef_shotgun" }),
        ("homing", new[] { "wisp_seeker", "judgement_lance", "blightwasp_swarm", "tempest_spindle" }),
        ("explosive", new[] { "mortar_shell", "bloomcap_spores", "hailstone_cluster" }),
        ("aura", new[] { "ember_halo", "static_field", "rimeheart" }),
        ("melee", new[] { "cleaver_arc", "riptide_blades", "forgefall_maul", "skyline_swords", "sleetbreath_horn" }),
        ("boomerang", new[] { "gullwing_chakram", "skimmer_kite", "evo_glass_gale" }),
        ("orbit", new[] { "circling_shoal", "evo_cyclone_eye" }),
        ("beam", new[] { "lighthouse_beam", "evo_truthline_rail", "spyglass_glint", "forked_squall" }),
        ("sky", new[] { "thunderhead_censer", "evo_broadside_barrage", "evo_sunken_sun", "evo_hundred_forks", "evo_worldforge_maul" }),
        ("ring", new[] { "breaker_swell", "evo_winters_keep", "evo_breakwater_cleaver" }),
        ("vortex", new[] { "maelstrom_seed", "evo_frozen_gyre" }),
        ("ricochet", new[] { "skimstone_sling", "evo_deepwater_harpoon" }),
        ("sentry", new[] { "driftwood_sentry", "evo_plague_hive" }),
        ("pool", new[] { "brine_wake", "evo_bloomtide" }),
        ("nova", new[] { "evo_stormcage", "evo_aurora_choir" }),
        ("shards", new[] { "evo_stormwing", "evo_knife_reef", "evo_reef_of_teeth" }),
        ("blast", new[] { "evo_sunlance", "evo_breaching_gun" }),
        ("arc", new[] { "evo_stormfan", "evo_final_verdict" }),
        ("echo", new[] { "evo_undertow_blades", "evo_twin_horizons" }),
    };

    public static bool DpsRequested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--distinct-dps") >= 0;

    public static string? ShotDir
        => Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--distinct-shot="))?["--distinct-shot=".Length..];

    private static async Task Wait(SceneTree tree, double s)
        => await tree.ToSignal(tree.CreateTimer(s, processAlways: false), SceneTreeTimer.SignalName.Timeout);

    private static readonly double[] Frames = { 0.06, 0.16, 0.36 };
    private static readonly double[] SkyFrames = { 0.3, 0.55, 0.8 };

    // ------------------------------------------------------------- stage

    /// <summary>A frozen ring of real enemies around the player, mostly in
    /// front of the camera, that do not fall.</summary>
    /// <param name="close">A tight ring (fields, swings, orbits); otherwise the
    /// nearest body stands 5.5 m ahead, so a shot is seen in flight.</param>
    internal static void Stage(Main main, bool close = true)
    {
        EnemyManager.DevFrozen = true;
        main.Enemies.ClearField(0f);
        EnemyDef grunt = Catalog.Enemies.First(x => !x.IsBoss && !x.IsElite && x.DirectorSpawns);
        Vector3 c = main.Player.GlobalPosition;
        Vector3[] at = !close ? new Vector3[]
        {
            new(0f, 0f, -5.5f), new(-2.4f, 0f, -7f), new(2.6f, 0f, -7.5f), new(0.4f, 0f, -10f),
            new(-1.5f, 0f, -12f), new(3.5f, 0f, -11.5f), new(-4f, 0f, -9f),
        } : new Vector3[]
        {
            new(0f, 0f, -3.5f), new(-2.4f, 0f, -5.5f), new(2.6f, 0f, -6f), new(0.4f, 0f, -8.5f),
            new(-4.5f, 0f, -2f), new(4.8f, 0f, -2.6f), new(-1.5f, 0f, -11f), new(3.5f, 0f, -10f),
            new(-6f, 0f, 1.5f), new(5.5f, 0f, 2.5f), new(0f, 0f, 3.5f), new(0.3f, 0f, -2.0f),
        };
        foreach (Vector3 v in at)
            if (main.Enemies.SpawnAt(grunt, c + v) is { } enemy)
            {
                enemy.Configure(grunt, 400f, 0f, 1f);
                enemy.Spawn(c + v);
            }
    }

    internal static void ClearWeapons(Main main)
    {
        foreach (string id in main.Weapons.Weapons.Select(w => w.Def.Id).ToList()) main.Weapons.DevRemove(id);
        Projectile.Shutdown();
        Projectile.Initialize(main.Weapons, WeaponSystem.ProjectileCapacity);
    }

    /// <summary>Hold one weapon (or an evolution) at max level.</summary>
    internal static WeaponRuntime? Give(Main main, string member)
    {
        if (Evolutions.ById(member) is { } evo)
        {
            if (Catalog.WeaponById(evo.WeaponId) is not { } wd) return null;
            main.Weapons.DevMax(wd);
            WeaponRuntime? r = main.Weapons.Get(wd.Id);
            r?.Evolve(evo);
            return r;
        }
        if (Catalog.WeaponById(member) is not { } def) return null;
        main.Weapons.DevMax(def);
        return main.Weapons.Get(def.Id);
    }

    // ------------------------------------------------------------- dps

    /// <summary>Pack and lone-target output of one weapon or evolution (30 s).</summary>
    internal static (float Pack, float Lone) Measure(WeaponSystem system, string member)
    {
        EvolutionDef? evo = Evolutions.ById(member);
        WeaponDef def = Catalog.WeaponById(evo?.WeaponId ?? member)!;
        float hp = def.Damage * 30f;
        float pack = EvolutionCheck.Output(system, def, evo, EvolutionCheck.Pack, hp);
        SignatureFx.Clear();   // nothing lingering may score on the next measurement
        float lone = EvolutionCheck.Output(system, def, evo, EvolutionCheck.Single, float.MaxValue);
        SignatureFx.Clear();
        return (pack, lone);
    }

    public static async Task Dps(Main main)
    {
        SceneTree tree = main.GetTree();
        await Wait(tree, 1.5);
        Screenshot.Invulnerable = true;
        Main.CatchUpPicks = 100000;
        main.Player.MoveOverride = () => Vector3.Zero;
        EnemyManager.DevFrozen = true;
        main.Enemies.ClearField(0f);
        ClearWeapons(main);
        bool hits = WeaponSystem.PublishHitEvents, numbers = WeaponSystem.PublishDamageNumbers;
        WeaponSystem.PublishHitEvents = false;
        WeaponSystem.PublishDamageNumbers = false;
        var ids = Catalog.Weapons.Select(w => w.Id).Concat(Evolutions.All.Select(e => e.Id));
        foreach (string id in ids)
        {
            (float pack, float lone) = Measure(main.Weapons, id);
            GD.Print($"DISTINCT dps {id,-24} pack {pack,8:0.0} lone {lone,8:0.0}");
        }
        WeaponSystem.PublishHitEvents = hits;
        WeaponSystem.PublishDamageNumbers = numbers;
        GD.Print("DISTINCT dps done");
        tree.Quit();
    }

    // ------------------------------------------------------------- shots

    public static async Task Shots(Main main, string dir)
    {
        SceneTree tree = main.GetTree();
        await Wait(tree, 4.0);   // past the map's title banner
        Screenshot.Invulnerable = true;
        Main.CatchUpPicks = 100000;
        main.Player.MoveOverride = () => Vector3.Zero;
        // Closer than play distance, so a shot's shape reads in a cropped frame.
        if (PlayerCamera.Instance is { } cam) { cam.Distance = 6.5f; cam.SetPitchDegrees(-48f); }
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(dir));
        string? only = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--distinct-only="))?["--distinct-only=".Length..];
        HashSet<string>? pick = only?.Split(',').ToHashSet();
        foreach ((string group, string[] members) in Groups)
        {
            if (pick is not null && !pick.Contains(group)) continue;
            foreach (string member in members)
            {
                ClearWeapons(main);
                Stage(main, group is "aura" or "melee" or "orbit" or "pool" or "echo" or "ring" or "sky");
                await Wait(tree, 0.35);
                if (Give(main, member) is null) { GD.Print($"DISTINCT shot skip {member}"); continue; }
                double t = 0;
                // Things that fall from the sky take longer to arrive.
                double[] frames = group == "sky" ? SkyFrames : Frames;
                for (int i = 0; i < frames.Length; i++)
                {
                    await Wait(tree, frames[i] - t);
                    t = frames[i];
                    await Screenshot.Capture(tree, main, $"{dir}/{group}__{member}__{i}.png");
                }
            }
        }
        ClearWeapons(main);
        EnemyManager.DevFrozen = false;
        GD.Print("DISTINCT shots done");
        tree.Quit();
    }
}
