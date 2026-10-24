using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Game.Trade;

/// <summary>
/// <c>--check-trade</c>: the swap meet's rules and this machine's escrow on
/// the real run objects, without a network. Prints TRADE lines ending in
/// TRADE PASS/FAIL and quits.
///
///  * pack / parse round trips for offers and holdings;
///  * the rules: gold the giver lacks, more copies than held, a stack cap
///    on the receiver (counting what the receiver gives of the same item),
///    a tome the receiver already has (and the same tome swapped both ways),
///    a full tome book, an empty table;
///  * escrow: gold, two copies of a stat item and a levelled tome leave the
///    inventory, the book and the stat sheet; a refund restores holdings and
///    every stat exactly; escrow then settle with incoming goods adds them
///    (tome straight at its level), and a second escrow cannot take more
///    than is held.
/// </summary>
public static class TradeCheck
{
    public static bool Requested() => Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-trade") >= 0;

    public static async Task Run(Main main)
    {
        SceneTree tree = main.GetTree();
        await tree.ToSignal(tree.CreateTimer(1.0, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        bool ok = true;
        void Expect(string what, bool cond)
        {
            GD.Print($"TRADE {(cond ? "ok  " : "FAIL")} {what}");
            ok &= cond;
        }

        // ---- packing
        var o = new TradeOffer { Gold = 50 };
        o.Items["item_a"] = 2;
        o.Tomes["tome_b"] = 3;
        Expect("offer round trip", TradeOffer.Parse(o.Pack()).SameAs(o) && o.Pack() == "g=50;i=item_a*2;t=tome_b*3");
        var h = new TradeHoldings { Gold = 7, TomeSlots = 5 };
        h.Items["x"] = 4;
        h.Tomes["y"] = 2;
        Expect("holdings round trip", TradeHoldings.Parse(h.Pack()).Pack() == h.Pack());
        Expect("garbage parses to empty", TradeOffer.Parse("zz;i=;g=-4;t").IsEmpty);

        // ---- rules
        ItemDef capped = Catalog.Items.First(i => i.MaxStacks >= 2 && i.Effects.Count > 0);
        TomeDef[] tomes = Data.Tomes.All.Take(6).ToArray();
        string[] names = { "Ana", "Bo" };
        TradeHoldings HoldA() { var x = new TradeHoldings { Gold = 100, TomeSlots = 4 }; x.Items[capped.Id] = 2; x.Tomes[tomes[0].Id] = 3; return x; }
        TradeHoldings HoldB() { var x = new TradeHoldings { Gold = 5, TomeSlots = 4 }; x.Items[capped.Id] = capped.MaxStacks - 1; x.Tomes[tomes[1].Id] = 1; return x; }
        List<TradeProblem> V(TradeOffer a, TradeOffer b, TradeHoldings? ha = null, TradeHoldings? hb = null)
            => TradeRules.Validate(new[] { a, b }, new[] { ha ?? HoldA(), hb ?? HoldB() }, names);
        TradeOffer Of(int gold = 0, string? item = null, int n = 0, string? tome = null)
        {
            var x = new TradeOffer { Gold = gold };
            if (item is not null) x.Items[item] = n;
            if (tome is not null) x.Tomes[tome] = 1;
            return x;
        }
        Expect("empty table refused", V(Of(), Of()).Count == 1 && V(Of(), Of())[0].Side < 0);
        Expect("fair swap allowed", V(Of(50, null, 0, tomes[0].Id), Of(5)).Count == 0);
        Expect("gold the giver lacks", V(Of(101), Of()).Any(p => p.Side == 0 && p.Format.Contains("gold")));
        Expect("more copies than held", V(Of(0, capped.Id, 3), Of()).Any(p => p.Side == 0 && p.Format.Contains("holds only")));
        Expect("receiver's stack cap", V(Of(0, capped.Id, 2), Of()).Any(p => p.Side == 0 && p.Format.Contains("more")));
        Expect("cap counts what the receiver gives back", V(Of(0, capped.Id, 2), Of(0, capped.Id, 1)).Count == 0);
        Expect("receiver already has the tome", V(Of(), Of(0, null, 0, tomes[0].Id)).Any(p => p.Side == 1 && p.Format.Contains("already has")));
        TradeHoldings hbSame = HoldB(); hbSame.Tomes[tomes[0].Id] = 2;
        Expect("same tome swapped both ways", V(Of(0, null, 0, tomes[0].Id), Of(0, null, 0, tomes[0].Id), null, hbSame).Count == 0);
        TradeHoldings hbFull = HoldB();
        for (int i = 2; i < 5; i++) hbFull.Tomes[tomes[i].Id] = 1;
        Expect("full tome book", V(Of(0, null, 0, tomes[0].Id), Of(), null, hbFull).Any(p => p.Format.Contains("tome slot")));
        Expect("full book that gives one away has room", V(Of(0, null, 0, tomes[0].Id), Of(0, null, 0, tomes[1].Id), null, hbFull).Count == 0);
        Expect("problems translate", V(Of(101), Of())[0].Text.Length > 0);

        // ---- escrow on the real inventory, book and sheet
        Inventory inv = main.Inventory;
        TomeBook book = TomeBook.Current!;
        StatSheet stats = Player.Instance!.Stats;
        TomeDef tome = Data.Tomes.All.First(t => book.LevelOf(t) == 0 && t.MaxLevel >= 3 && t.Stat != capped.Effects[0].Stat);
        inv.TradeGold(120 - inv.Gold);
        while (inv.StacksOf(capped) < 2) inv.Add(capped);
        book.Add(tome); book.Add(tome); book.Add(tome);
        Stat[] watch = Enum.GetValues<Stat>();
        float[] Sheet() => watch.Select(s => stats.Get(s)).ToArray();
        float[] before = Sheet();
        string held = TradeLedger.Snapshot().Pack();
        int capBefore = inv.StacksOf(capped);

        var give = new TradeOffer { Gold = 50 };
        give.Items[capped.Id] = 2;
        give.Tomes[tome.Id] = 3;
        TradeOffer? taken = TradeLedger.TakeEscrow(901, give, new TradeOffer(), "me", "you", out string? why);
        Expect($"escrow taken ({why ?? "ok"})", taken is not null && taken.Tomes.TryGetValue(tome.Id, out int lv) && lv == 3 && taken.Gold == 50);
        Expect("escrow left the inventory", inv.Gold == 70 && inv.StacksOf(capped) == capBefore - 2 && book.LevelOf(tome) == 0);
        float[] mid = Sheet();
        Expect("escrow left the stat sheet", Math.Abs(stats.Get(tome.Stat) - before[Array.IndexOf(watch, tome.Stat)]) > 1e-4f
                                            || Math.Abs(stats.Get(capped.Effects[0].Stat) - before[Array.IndexOf(watch, capped.Effects[0].Stat)]) > 1e-4f);
        TradeLedger.Refund(901);
        float[] back = Sheet();
        Expect("refund restores the holdings", TradeLedger.Snapshot().Pack() == held && TradeLedger.EscrowId == 0);
        Expect("refund restores every stat", before.Zip(back).All(p => Math.Abs(p.First - p.Second) < 1e-3f));

        var tooMuch = new TradeOffer { Gold = 500 };
        Expect("cannot escrow more gold than held", TradeLedger.TakeEscrow(902, tooMuch, new TradeOffer(), "me", "you", out _) is null && inv.Gold == 120);

        // Escrow, then settle with what the partner gave: a different tome at Lv 2 and 40 gold.
        TomeDef incomingTome = Data.Tomes.All.First(t => book.LevelOf(t) == 0 && t != tome && t.MaxLevel >= 2);
        TradeLedger.TakeEscrow(903, give, new TradeOffer(), "me", "you", out _);
        var incoming = new TradeOffer { Gold = 40 };
        incoming.Tomes[incomingTome.Id] = 2;
        TradeLedger.Settle(903, incoming);
        Expect("settle adds the partner's goods", inv.Gold == 110 && book.LevelOf(incomingTome) == 2 && book.LevelOf(tome) == 0
                                                   && TradeLedger.EscrowId == 0);
        Expect("tome arrives with its modifiers", Math.Abs(stats.Get(incomingTome.Stat) - back[Array.IndexOf(watch, incomingTome.Stat)]) > 1e-4f
                                                  || incomingTome.Stat == tome.Stat);
        main.Hud.ResyncTomes(book);

        GD.Print($"TRADE {(ok ? "PASS" : "FAIL")} item={capped.Id} tome={tome.Id} incoming={incomingTome.Id} mid_diff={mid.Zip(before).Count(p => Math.Abs(p.First - p.Second) > 1e-4f)}");
        tree.Quit();
    }
}
