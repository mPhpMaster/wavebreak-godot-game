using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Combat.Weapons;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Game;

/// <summary>
/// Self-checks for the perks (Data/Tomes.Perks.cs, Combat/Weapons/PerkProcs).
///
/// <c>--check-perks</c> (no <c>--auto</c>): gives every perk at its top level
/// (the slot limit is lifted for the check only), then makes each one's
/// trigger happen for real in the running game — real enemies, real hits
/// through the weapon system's hit report, real dashes, falls, gold and
/// experience — and checks that the effect fired. One line per perk, then
/// <c>PERKS PASS</c> or <c>PERKS FAIL</c>, and the game quits. Add
/// <c>--check-perks-shot=x.png</c> for a frame with every perk active.
///
/// <c>--perk-levelup-shot=x.png</c>: a level-up hand of perk cards (one new,
/// two upgrades), photographed, then quit. Add <c>--lang=ar</c> for Arabic.
/// </summary>
public static class PerksCheck
{
    public static void MaybeStart(Main main, PerkProcs procs)
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--check-perks") >= 0)
        {
            string? shot = Array.Find(args, a => a.StartsWith("--check-perks-shot="))?["--check-perks-shot=".Length..];
            _ = Run(main, procs, shot);
        }
        else if (Array.Find(args, a => a.StartsWith("--perk-levelup-shot=")) is { } arg)
            _ = LevelUpShot(main, arg["--perk-levelup-shot=".Length..]);
    }

    private static async Task Wait(SceneTree tree, double s)
        => await tree.ToSignal(tree.CreateTimer(s, processAlways: false), SceneTreeTimer.SignalName.Timeout);

    // ------------------------------------------------------------ check

    private static async Task Run(Main main, PerkProcs procs, string? shot)
    {
        SceneTree tree = main.GetTree();
        await Wait(tree, 1.5);
        Player p = main.Player;
        TomeBook book = TomeBook.Current!;
        EnemyManager em = main.Enemies;
        bool wasInvulnerable = Screenshot.Invulnerable;
        Screenshot.Invulnerable = true;   // the check's own lethal blow turns this off for a moment
        p.MoveOverride = () => Vector3.Zero;
        // Kills and the check's own experience level the player up: pick those
        // cards automatically (a pausing panel would stop the check's clock).
        Main.CatchUpPicks = 100000;
        PerkProcs.Trace = true;

        var lines = new List<string>();
        var failed = new List<string>();
        void Report(PerkDef d, bool ok, string detail)
        {
            string line = $"PERK {d.Id,-20} L{book.LevelOf(d)}/{d.MaxLevel} {(ok ? "ok" : "FAIL")} fired={procs.FiredCount(d.Id)} {detail}";
            GD.Print(line);
            if (!ok) failed.Add(d.Id);
        }

        book.Slots = 99;
        foreach (PerkDef d in Perks.All) while (book.Add(d)) { }
        main.Hud.DevResync(main.Weapons, book);
        bool allMax = Perks.All.All(d => book.LevelOf(d) == d.MaxLevel);
        GD.Print($"PERKS given={Perks.All.Count} all_max={allMax}");
        if (!allMax) failed.Add("levels");

        EnemyDef grunt = Catalog.Enemies.First(e => !e.IsBoss && !e.IsElite && e.DirectorSpawns);
        EnemyDef elite = Catalog.Enemies.FirstOrDefault(e => e.IsElite && !e.IsBoss) ?? grunt;
        Vector3 Ahead(float m) => p.GlobalPosition + new Vector3(0f, 0f, -m);
        Enemy? Spawn(EnemyDef def, Vector3 at) => em.SpawnAt(def, at);
        PerkDef D(string id) => Perks.ById(id)!;

        // A real weapon hit, reported the way weapons report theirs.
        void WeaponHit(IDamageable t, float amount, bool crit = false, bool mega = false)
        {
            var info = new DamageInfo { Amount = amount, Crit = crit, Megacrit = mega, ProcCoefficient = 1f, Source = "check" };
            t.TakeDamage(info);
            main.Weapons.ReportHit(t, info);
        }

        // 1. Stormcount: enough hits on one enemy with others around.
        {
            PerkDef d = D(Perks.Stormcount);
            var near = new List<Enemy?>();
            for (int i = 0; i < 5; i++) near.Add(Spawn(grunt, Ahead(3f + i * 1.2f)));
            await Wait(tree, 0.2);
            int before = procs.FiredCount(d.Id);
            Enemy? target = null;
            // Tiny hits (the starting weapon is firing too, and grunts are frail):
            // the count is what matters, not the damage.
            for (int i = 0; i < 3 * (int)d.V(0, d.MaxLevel) && procs.FiredCount(d.Id) == before; i++)
            {
                target = near.FirstOrDefault(e => e is { IsAlive: true });
                if (target is null) break;
                WeaponHit(target, 0.01f);
            }
            int alive = near.Count(e => e is { IsAlive: true });
            int around = target is null ? 0 : Targeting.Overlap(target.Position, 7f, new List<IDamageable>());
            Report(d, procs.FiredCount(d.Id) > before, $"every={(int)d.V(0, d.MaxLevel)} spawned_alive={alive} around={around}");
        }

        // 2. Red Tide: crits heal while hurt.
        {
            PerkDef d = D(Perks.RedTide);
            await Wait(tree, 1.0);   // let the heal budget refill
            p.Health.CheckSetFraction(0.5f);
            float before = p.Health.Current;
            // An elite: a grunt can die to the rest of the maxed build before
            // the crits land, which made this flaky across seeds.
            Enemy? t = Spawn(elite, Ahead(3f));
            await Wait(tree, 0.1);
            for (int i = 0; i < 4 && t is not null && t.IsAlive; i++) WeaponHit(t, 0.01f, crit: true);
            await Wait(tree, 0.1);
            Report(d, p.Health.Current > before + 0.5f, $"health {before:0.#} -> {p.Health.Current:0.#}");
        }

        // 3. Bubble Net: an elite dies, its bubble is walked into.
        {
            PerkDef d = D(Perks.BubbleNet);
            Enemy? e = Spawn(elite, Ahead(4f));
            await Wait(tree, 0.2);
            if (e is not null) WeaponHit(e, 1e7f);
            int waiting = procs.BubblesWaiting;
            await Wait(tree, 0.2);
            if (e is not null) p.Teleport(e.GlobalPosition + Vector3.Up * 0.2f);
            await Wait(tree, 0.4);
            Report(d, waiting > 0 && procs.FiredCount(d.Id) > 0 && p.Health.Shield > 1f,
                   $"elite={elite.Id} dropped={waiting} shield={p.Health.Shield:0}");
        }

        // 4. Brine Wake: dash through a pack.
        {
            PerkDef d = D(Perks.BrineWake);
            for (int i = 0; i < 3; i++) Spawn(elite, Ahead(1.5f + i * 1.2f));   // elites: they outlive the build
            await Wait(tree, 0.2);
            p.MoveOverride = () => Vector3.Forward;
            p.RequestDash();
            await Wait(tree, 1.2);
            p.MoveOverride = () => Vector3.Zero;
            Report(d, procs.FiredCount(d.Id) > 0, $"dashes={p.Dashes}");
        }

        // 5. Crab Stomp: drop onto an enemy.
        {
            PerkDef d = D(Perks.CrabStomp);
            Enemy? e = Spawn(grunt, Ahead(3f));
            await Wait(tree, 0.3);
            for (int tries = 0; tries < 3 && e is not null && procs.FiredCount(d.Id) == 0; tries++)
            {
                if (!e.IsAlive) e = Spawn(grunt, Ahead(3f));
                await Wait(tree, 0.1);
                if (e is null) break;
                p.Teleport(e.GlobalPosition + Vector3.Up * 3.2f);
                await Wait(tree, 1.0);
            }
            Report(d, procs.FiredCount(d.Id) > 0, "");
        }

        // 6. Flotsam Orbit: experience in, a mote out, and it hits.
        {
            PerkDef d = D(Perks.Flotsam);
            for (int i = 0; i < 4; i++)
            {
                main.Inventory.GrantXp(2);
                await Wait(tree, 0.35);
            }
            int motes = procs.Motes;
            for (int i = 0; i < 4; i++)
                Spawn(grunt, p.GlobalPosition + new Vector3(Mathf.Cos(i * 1.6f), 0f, Mathf.Sin(i * 1.6f)) * 1.8f);
            for (int i = 0; i < 30 && procs.FiredCount(d.Id) == 0; i++) await Wait(tree, 0.1);
            Report(d, motes > 0 && procs.FiredCount(d.Id) > 0, $"motes={motes}");
        }

        // 7. Golden Ballast: gold to full stacks, then a burst.
        {
            PerkDef d = D(Perks.Ballast);
            Spawn(grunt, Ahead(2f));
            await Wait(tree, 0.2);
            int max = (int)d.V(1, d.MaxLevel);
            for (int i = 0; i < max + 3; i++) main.Inventory.GrantGold(1);
            await Wait(tree, 0.1);
            float bonus = procs.ModValue(d.Id, Stat.Damage);
            Report(d, procs.BallastStacks == max && bonus > 0f && procs.FiredCount(d.Id) >= 2,
                   $"stacks={procs.BallastStacks}/{max} damage=+{bonus * 100f:0}%");
        }

        // 8. Last Light: a lethal blow is refused.
        {
            PerkDef d = D(Perks.LastLight);
            int charges = procs.LastLightCharges;
            p.Health.CheckSetFraction(0.1f);
            await Wait(tree, 0.6);   // past any i-frames
            Screenshot.Invulnerable = false;
            for (int i = 0; i < 12 && procs.FiredCount(d.Id) == 0 && p.Health.IsAlive; i++)
            {
                p.Health.TakeDamage(new DamageInfo { Amount = 1e6f, Source = "check" });   // may be dodged: try again
                await Wait(tree, 0.05);
            }
            Screenshot.Invulnerable = true;
            Report(d, p.Health.IsAlive && procs.FiredCount(d.Id) > 0 && procs.LastLightCharges == charges - 1,
                   $"alive={p.Health.IsAlive} charges {charges} -> {procs.LastLightCharges} health={p.Health.Fraction:0.00}");
        }

        // 9. Lighthouse Keeper: a beam on the nearest closed chest.
        {
            PerkDef d = D(Perks.Lighthouse);
            for (int i = 0; i < 80 && procs.FiredCount(d.Id) == 0; i++) await Wait(tree, 0.1);
            Report(d, procs.FiredCount(d.Id) > 0 && procs.MarkedChest is not null,
                   $"marked={(procs.MarkedChest is { } c ? $"{c.GlobalPosition.DistanceTo(p.GlobalPosition):0} m away" : "none")}");
        }

        // 10. Fair Winds: running raises attack speed.
        {
            PerkDef d = D(Perks.FairWinds);
            // Run one way, and another if a rock or wall stops the first.
            float bonus = 0f;
            foreach (Vector3 dir in new[] { Vector3.Right, Vector3.Left, Vector3.Back, Vector3.Forward })
            {
                p.MoveOverride = () => dir;
                await Wait(tree, 0.8);
                bonus = procs.ModValue(d.Id, Stat.AttackSpeed);
                if (bonus > 0.03f) break;
            }
            p.MoveOverride = () => Vector3.Zero;
            Report(d, bonus > 0.03f, $"attack speed +{bonus * 100f:0.#}% at {p.PlanarVelocity.Length():0.0} m/s");
        }

        // 11. Barnacle Hide: stand still, armour grows.
        {
            PerkDef d = D(Perks.Barnacle);
            await Wait(tree, 2.0);
            float armour = procs.ModValue(d.Id, Stat.Armor);
            Report(d, armour >= d.V(0, d.MaxLevel) * 1.5f, $"armour +{armour:0.#}");
        }

        // 12. Cornered Eel: hurt, stronger.
        {
            PerkDef d = D(Perks.Cornered);
            p.Health.CheckSetFraction(0.15f);
            await Wait(tree, 0.15);
            float dmg = procs.ModValue(d.Id, Stat.Damage), eva = procs.ModValue(d.Id, Stat.Evasion);
            Report(d, dmg > 0f && eva > 0f, $"damage +{dmg * 100f:0}% evasion +{eva:0}");
        }

        // 13. Lifeline: below 35% the surge heals (it was primed by step 12).
        {
            PerkDef d = D(Perks.Lifeline);
            float before = p.Health.Current;
            await Wait(tree, 1.5);
            Report(d, procs.FiredCount(d.Id) > 0 && p.Health.Current > before + 1f,
                   $"health {before:0} -> {p.Health.Current:0}");
        }

        // 14. Crew Beacon: alone, it buffs you.
        {
            PerkDef d = D(Perks.Beacon);
            float want = d.V(1, d.MaxLevel) / 100f;
            float got = procs.ModValue(d.Id, Stat.AttackSpeed);
            Report(d, Mathf.Abs(got - want) < 1e-3f && procs.ModValue(d.Id, Stat.Armor) > 0f, $"attack speed +{got * 100f:0}%");
        }

        // 15. Tidal Pulse: enemies near when it goes off.
        {
            PerkDef d = D(Perks.TidalPulse);
            int before = procs.FiredCount(d.Id);
            for (int i = 0; i < 3; i++)
                Spawn(grunt, p.GlobalPosition + new Vector3(Mathf.Cos(i * 2.1f), 0f, Mathf.Sin(i * 2.1f)) * 2.5f);
            // Fire now: with every perk at max, the rest of the build kills the
            // grunts long before a 5 s pulse would come round on its own.
            procs.PrimePulseForCheck();
            for (int i = 0; i < 80 && procs.FiredCount(d.Id) == before; i++) await Wait(tree, 0.1);
            Report(d, procs.FiredCount(d.Id) > before, "");
        }

        // Screen stays clean: the effects in flight are capped, and sounds played.
        int fx = procs.LiveFx;
        bool sounds = Audio.AudioDirector.PerkSoundsPlayed > 0;
        GD.Print($"PERKS live_fx={fx} perk_sounds={Audio.AudioDirector.PerkSoundsPlayed}");
        if (fx > 60) failed.Add("fx-cap");
        if (!sounds) failed.Add("sounds");

        if (shot is not null)
        {
            for (int i = 0; i < 5; i++) Spawn(grunt, Ahead(3f + i));
            p.MoveOverride = () => new Vector3(0.6f, 0f, -0.8f);
            main.Inventory.GrantXp(3);
            await Wait(tree, 1.6);
            await Screenshot.Capture(tree, main, shot);
        }

        // Clearing the build removes every perk modifier.
        book.Clear();
        await Wait(tree, 0.2);
        var residue = new List<string>();
        foreach (PerkDef d in Perks.All)
            foreach (Stat s in new[] { Stat.Damage, Stat.AttackSpeed, Stat.Armor, Stat.Thorns, Stat.Evasion, Stat.Lifesteal, Stat.ProjectileSpeed })
                if (MathF.Abs(procs.ModValue(d.Id, s)) > 1e-4f) residue.Add($"{d.Id}:{s}");
        GD.Print($"PERKS cleared residue={(residue.Count == 0 ? "none" : string.Join(",", residue))}");
        if (residue.Count > 0) failed.Add("residue");

        Screenshot.Invulnerable = wasInvulnerable;
        Main.CatchUpPicks = 0;
        GD.Print(failed.Count == 0 ? $"PERKS PASS perks={Perks.All.Count}" : $"PERKS FAIL failed={string.Join(",", failed)}");
        tree.Quit();
    }

    // ---------------------------------------------------- level-up capture

    private static async Task LevelUpShot(Main main, string path)
    {
        SceneTree tree = main.GetTree();
        await Wait(tree, 1.0);
        TomeBook book = TomeBook.Current!;
        // Two perks already read (an upgrade card shows "what this level adds,
        // and the total"), and one new one.
        PerkDef a = Perks.ById(Perks.Stormcount)!, b = Perks.ById(Perks.BubbleNet)!, c = Perks.ById(Perks.Beacon)!;
        book.Add(a); book.Add(a);
        book.Add(b);
        main.Hud.DevResync(main.Weapons, book);
        var hand = new List<Offer> { Offer.ForTome(a, book.LevelOf(a)), Offer.ForTome(c, 0), Offer.ForTome(b, book.LevelOf(b)) };
        main.LevelUp.Show(hand, o => { if (o.Tome is not null) book.Add(o.Tome); });
        await tree.ToSignal(tree.CreateTimer(0.8, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        foreach (Offer o in hand) GD.Print($"PERK CARD {o.Title} | {o.DetailText} | {o.Effect.Replace("\n", " / ")}");
        await Screenshot.Capture(tree, main, path);
        tree.Quit();
    }
}
