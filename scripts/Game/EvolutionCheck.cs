using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Combat.Weapons;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Meta;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Game;

/// <summary>
/// <c>--check-evolutions</c> (no <c>--auto</c>): one evolution end to end in the
/// running game, then the whole table measured. Prints EVOLVE lines ending in
/// EVOLVE PASS/FAIL and quits. Add <c>--check-evolutions-shot=x.png</c> for a
/// frame of the hand with the gold card (<c>--lang=ar</c> for Arabic).
///
///  * a maxed weapon offers nothing until its catalyst is held; then the level-up
///    hand gains a gold card, a reroll keeps it, banish refuses it, and taking
///    another card leaves the weapon as it was and offers the card again;
///  * the card is taken with a real key press on the real panel: the runtime
///    evolves, keeps its id, level and upgrades, the event, sound, toast and
///    profile all happen, and real shots hit a real enemy as the evolved form;
///  * trading the catalyst away afterwards changes nothing; before evolving it
///    takes the offer away;
///  * a real chest offers the card with a "Not now", and it comes back;
///  * every row of the table is fired against the same pack as the max-level
///    weapon. One measurement swings by about +-0.4 from run to run (frame
///    timing, crits, spread), so a row must be clearly stronger and not absurd
///    (x1.3 to x3.0), and the table's mean must sit at x1.7 to x2.3.
/// </summary>
public static class EvolutionCheck
{
    public const float MinRatio = 1.3f, MaxRatio = 3.0f, MinMean = 1.7f, MaxMean = 2.3f;

    public static bool Requested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-evolutions") >= 0;

    private static async Task Wait(SceneTree tree, double s)
        => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);

    private static void Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private static readonly object CheckSource = new();

    public static async Task Run(Main main)
    {
        SceneTree tree = main.GetTree();
        await Wait(tree, 1.5);
        bool ok = true;
        void Expect(string what, bool cond)
        {
            GD.Print($"EVOLVE {(cond ? "ok  " : "FAIL")} {what}");
            ok &= cond;
        }

        Player p = main.Player;
        WeaponSystem weapons = main.Weapons;
        Inventory inv = main.Inventory;
        TomeBook book = TomeBook.Current!;
        LevelUpChoices choices = LevelUpChoices.Current!;
        Screenshot.Invulnerable = true;
        p.MoveOverride = () => Vector3.Zero;
        // Real experience from real kills must not stop the clock with a hand.
        Main.CatchUpPicks = 100000;
        p.Stats.Add(Stat.Rerolls, ModifierOp.Flat, 3f, CheckSource);
        p.Stats.Add(Stat.Banishes, ModifierOp.Flat, 3f, CheckSource);

        var ready = new List<string>();
        var evolved = new List<string>();
        int evolvedHits = 0;
        EventBus.Subscribe<EvolutionReady>(e => ready.Add(e.EvolutionId));
        EventBus.Subscribe<WeaponEvolved>(e => evolved.Add(e.EvolutionId));
        EventBus.Subscribe<EnemyDamaged>(e => { if (e.Info?.Evolution is not null) evolvedHits++; });

        // ---- table sanity
        EvolutionDef e1 = Evolutions.ForWeapon("splinter_rail")!;
        EvolutionDef e2 = Evolutions.ForWeapon("sparkfan")!;
        GD.Print($"EVOLVE table={Evolutions.All.Count}");
        Expect("the table has at least 18 evolutions", Evolutions.All.Count >= 18);

        // ---- the requirement
        WeaponDef def1 = Catalog.WeaponById(e1.WeaponId)!;
        void MaxOut(WeaponDef d) { for (int i = 0; i < 12 && weapons.LevelOf(d.Id) < d.MaxLevel; i++) weapons.Add(d); }
        weapons.Add(def1);
        Expect("a weapon below max level offers nothing, catalyst or not",
               EvolutionOffers.NextReady(weapons, inv, book) is null);
        MaxOut(def1);
        WeaponRuntime rail = weapons.Get(e1.WeaponId)!;
        int upgrades = rail.Upgrades.Count;
        float bonus = rail.Bonus.Damage;
        Expect($"max level without the catalyst offers nothing (Lv {rail.Level})",
               rail.Level == def1.MaxLevel && EvolutionOffers.NextReady(weapons, inv, book) is null);
        book.Add(Tomes.ById(e1.CatalystId));
        await Wait(tree, 0.1);
        Expect("the catalyst makes it ready, and says so", EvolutionOffers.NextReady(weapons, inv, book) == e1 && ready.Contains(e1.Id));

        // ---- the hand
        List<Offer> hand = choices.Hand();
        Offer gold = hand[^1];
        Expect($"the level-up hand gains a gold card ({hand.Count} cards)", hand.Count == LevelUpChoices.Cards + 1 && gold.Evolution == e1);
        Expect("the card is not a supply payout and has no pool source", !gold.IsBonus && gold.Source is null);
        Expect("banish refuses it", !LevelUpChoices.CanBanish(gold) && !choices.TryBanish(gold, hand, out _));
        Expect("a reroll keeps it", choices.TryReroll(hand, out List<Offer> rerolled)
               && rerolled.Count == hand.Count && rerolled.Count(o => o.IsEvolution) == 1);

        // ---- never forced
        Offer other = rerolled.First(o => !o.IsEvolution);
        main.ChooseForTest(other);
        Expect("taking another card leaves the weapon as it was", rail.Evolution is null);
        Expect("and the next hand offers it again", choices.Hand().Any(o => o.Evolution == e1));

        // ---- the real panel, a real key
        string? shot = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--check-evolutions-shot="))?["--check-evolutions-shot=".Length..];
        main.LevelUp.Show(choices.Hand(), main.ChooseForTest);
        await Wait(tree, 0.7);
        if (shot is not null) await Screenshot.Capture(tree, main, shot);
        Expect("the panel holds the hand", main.LevelUp.IsOpen);
        Press(Key.Key4);
        await Wait(tree, 0.2);
        Expect("key 4 takes the gold card", !main.LevelUp.IsOpen && rail.Evolution == e1 && evolved.Contains(e1.Id));
        Expect($"evolved in place: id {rail.Def.Id}, Lv {rail.Level}, {rail.Upgrades.Count} upgrades, name \"{rail.Def.DisplayName}\"",
               rail.Def.Id == e1.WeaponId && rail.Level == def1.MaxLevel && rail.Upgrades.Count == upgrades
               && MathF.Abs(rail.Bonus.Damage - bonus) < 1e-5f && rail.Def.DisplayName == e1.Name && rail.Def.Tint == e1.Tint);
        Expect("the catalogue's weapon is untouched", def1.DisplayName != e1.Name);
        Expect("it stops being offered", EvolutionOffers.NextReady(weapons, inv, book) is null && !choices.Hand().Any(o => o.IsEvolution));
        Expect("saved as discovered", Profile.HasEvolved(e1.Id));
        Expect("the sting played", main.AudioForTest.EvolutionsHeard >= 1);

        // ---- it fires, for real
        EnemyDef grunt = Catalog.Enemies.First(x => !x.IsBoss && !x.IsElite && x.DirectorSpawns);
        Vector3 ahead = p.GlobalPosition + new Vector3(0f, 0f, -6f);
        foreach (int offset in new[] { -2, 0, 2 })
            if (main.Enemies.SpawnAt(grunt, ahead + new Vector3(offset, 0f, 0f)) is { } enemy)
            {
                enemy.Configure(grunt, 12f, 0f, 1f);
                enemy.Spawn(ahead + new Vector3(offset, 0f, 0f));
            }
        for (int i = 0; i < 40 && evolvedHits == 0; i++) await Wait(tree, 0.1);
        Expect($"real evolved shots hit real enemies ({evolvedHits} hits)", evolvedHits > 0);

        // ---- trades
        book.TradeRemove(Tomes.ById(e1.CatalystId));
        Expect("trading the catalyst away afterwards keeps the evolution", rail.Evolution == e1);
        WeaponDef def2 = Catalog.WeaponById(e2.WeaponId)!;
        ItemDef item2 = Catalog.ItemById(e2.CatalystId)!;
        weapons.Add(def2);
        MaxOut(def2);
        inv.Add(item2);
        Expect("second pair ready", EvolutionOffers.NextReady(weapons, inv, book) == e2);
        inv.TradeRemove(item2, inv.StacksOf(item2));
        Expect("trading the catalyst away before evolving takes the offer away", EvolutionOffers.NextReady(weapons, inv, book) is null);
        inv.Add(item2);

        // ---- a real chest, declined then taken
        ChestSpawner spawner = ChestSpawner.Instance!;
        async Task<bool> OpenChest(Key key)
        {
            if (spawner.FirstUsableForTest() is not { } chest) return false;
            inv.GrantGold(5000);
            if (!spawner.TryOpen(chest)) return false;
            await Wait(tree, 0.7);
            bool open = main.LevelUp.IsOpen;
            Press(key);
            await Wait(tree, 0.2);
            return open && !main.LevelUp.IsOpen;
        }
        Expect("a chest offers it, and Not now is honoured", await OpenChest(Key.Key2) && weapons.Get(e2.WeaponId)!.Evolution is null);
        Expect("the next chest offers it again and it can be taken", await OpenChest(Key.Key1) && weapons.Get(e2.WeaponId)!.Evolution == e2);
        Expect("the codex count moved", Profile.FoundEvolutions.Count >= 2);
        Expect("the run counts two evolved weapons", weapons.EvolvedCount == 2);

        // ---- the table, measured
        Main.CatchUpPicks = 100000;
        ok &= MeasureAll(weapons);

        p.Stats.RemoveAllFrom(CheckSource);
        GD.Print(ok ? "EVOLVE PASS" : "EVOLVE FAIL");
        tree.Quit();
    }

    // ----------------------------------------------------------- measurement

    /// <summary>A body that takes a few volleys to fall and is replaced half a
    /// second later, like a pack walking in: enough for the kill riders to fire,
    /// and a shard flung off a corpse does not hit the corpse.</summary>
    private sealed class Dummy : IDamageable
    {
        private readonly float _hp;
        private float _left;
        public Dummy(Vector3 at, float hp) { Position = at; _hp = _left = hp; }
        public Vector3 Position { get; }
        public bool IsAlive { get; private set; } = true;
        public float Damage;
        private int _down;

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) { info.Nullified = true; return; }
            Damage += MathF.Max(0f, info.Amount);
            if ((_left -= info.Amount) > 0f) return;
            IsAlive = false;
            _down = 30;
        }

        public void StandUp()
        {
            if (IsAlive || --_down > 0) return;
            IsAlive = true;
            _left = _hp;
        }
    }

    private static float Output(WeaponSystem system, WeaponDef def, EvolutionDef? evolution, List<Vector3> at, float hp)
    {
        var dummies = at.Select(v => new Dummy(system.MuzzlePosition + v, hp)).ToList();
        Targeting.Source = into =>
        {
            foreach (Dummy d in dummies)
            {
                d.StandUp();
                if (d.IsAlive) into.Add(d);
            }
            return into.Count;
        };

        var runtime = new WeaponRuntime(def);
        while (runtime.LevelUp()) { }
        if (evolution is not null) runtime.Evolve(evolution);

        const float dt = 1f / 60f;
        const int frames = 1800;
        system.Rng = new Rng(0xBA1A_4CEUL);   // the same crit rolls for both forms
        for (int frame = 0; frame < frames; frame++)
        {
            Targeting.Refresh();
            runtime.Tick(dt, system);
            Projectile.UpdateAll(dt);
        }
        // What is in the air when the window closes is not counted: both forms
        // pay the same cut-off, and a long-fused delivery is not paid in advance.
        float total = dummies.Sum(d => d.Damage);
        for (int frame = 0; frame < 900 && Projectile.ActiveCount > 0; frame++)
        {
            Targeting.Refresh();
            Projectile.UpdateAll(dt);
        }
        runtime.Dispose();
        Targeting.Source = null;
        Targeting.Refresh();
        return total / (frames * dt);
    }

    private static readonly List<Vector3> Pack = new()
    {
        new(2.0f, 0f, 0.6f), new(-1.6f, 0f, 1.4f), new(0.4f, 0f, -2.4f), new(3.4f, 0f, -1.6f),
        new(5.5f, 0f, 1.2f), new(-5.0f, 0f, -2.4f), new(2.6f, 0f, 5.2f), new(8.0f, 0f, 0.4f),
    };

    private static readonly List<Vector3> Single = new() { new(1.6f, 0f, 0f) };

    /// <summary>Every row, pack and lone target, against the max-level weapon
    /// firing at the same bodies with the same stats. The pack is what must land
    /// in the band; the lone target is printed because a rider's share shrinks
    /// when it has nobody to reach.</summary>
    private static bool MeasureAll(WeaponSystem system)
    {
        bool hits = WeaponSystem.PublishHitEvents, numbers = WeaponSystem.PublishDamageNumbers;
        WeaponSystem.PublishHitEvents = false;
        WeaponSystem.PublishDamageNumbers = false;
        Rng runRng = system.Rng;
        bool ok = true;
        var ratios = new List<float>();
        foreach (EvolutionDef e in Evolutions.All)
        {
            WeaponDef def = Catalog.WeaponById(e.WeaponId)!;
            // A pack member falls to a handful of max-level hits; the lone target
            // is a boss and does not fall at all.
            float hp = def.Damage * 30f;
            float basePack = Output(system, def, null, Pack, hp), evoPack = Output(system, def, e, Pack, hp);
            float baseOne = Output(system, def, null, Single, float.MaxValue), evoOne = Output(system, def, e, Single, float.MaxValue);
            float ratio = basePack > 0f ? evoPack / basePack : 0f;
            bool inBand = ratio >= MinRatio && ratio <= MaxRatio;
            ok &= inBand;
            ratios.Add(ratio);
            GD.Print($"EVOLVE {(inBand ? "ok  " : "FAIL")} dps {e.Id,-24} pack {basePack,7:0} -> {evoPack,7:0} x{ratio:0.00}" +
                     $"   lone {baseOne,6:0} -> {evoOne,6:0} x{(baseOne > 0f ? evoOne / baseOne : 0f):0.00}");
        }
        float mean = ratios.Average();
        bool meanOk = mean >= MinMean && mean <= MaxMean;
        ok &= meanOk;
        GD.Print($"EVOLVE {(meanOk ? "ok  " : "FAIL")} dps band x{ratios.Min():0.00} to x{ratios.Max():0.00}, mean x{mean:0.00} over {ratios.Count}");
        system.Rng = runRng;
        WeaponSystem.PublishHitEvents = hits;
        WeaponSystem.PublishDamageNumbers = numbers;
        return ok;
    }
}
