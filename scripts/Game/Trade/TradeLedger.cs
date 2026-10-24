using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Progression;

namespace Wavebreak.Game.Trade;

/// <summary>
/// This machine's side of a swap: its own inventory, gold and tome book,
/// which only this machine may change (see "Who owns what").
///
/// The swap is a two-step escrow so nothing can be duplicated:
///   1. <see cref="TakeEscrow"/>: when the host says go, each side checks it
///      still holds its offer and can hold what it gets, then *removes* its
///      offer and keeps it here, reporting exactly what it removed.
///   2. the host, once both sides have escrowed, sends each the other's
///      escrow: <see cref="Settle"/> adds it and forgets the escrow. If
///      anything goes wrong first (a side failed its check, left, the room
///      ended, the host went away) <see cref="Refund"/> puts the escrow back.
/// Goods therefore exist in exactly one place at every moment: an
/// inventory, or one escrow that is either settled into the other inventory
/// or refunded into its owner's.
/// </summary>
public static class TradeLedger
{
    /// <summary>Trade id of the escrow held, or 0.</summary>
    public static int EscrowId { get; private set; }

    /// <summary>What was removed for <see cref="EscrowId"/>, tome levels included.</summary>
    public static TradeOffer? Escrow { get; private set; }

    public static int Settled { get; private set; }
    public static int Refunds { get; private set; }

    /// <summary>Everything this machine's player holds that a trade can touch.</summary>
    public static TradeHoldings Snapshot()
    {
        var h = new TradeHoldings();
        if (Inventory.Current is { } inv)
        {
            h.Gold = inv.Gold;
            foreach (ItemStack s in inv.Items) if (s.Count > 0) h.Items[s.Id] = s.Count;
        }
        if (TomeBook.Current is { } book)
        {
            h.TomeSlots = book.Slots;
            foreach (TomeDef t in book.Held) h.Tomes[t.Id] = book.LevelOf(t);
        }
        return h;
    }

    /// <summary>Why <paramref name="give"/> for <paramref name="receive"/>
    /// cannot happen here and now (English), or null. The same rules the
    /// host ran, against the live inventory rather than the last report.</summary>
    public static TradeProblem? CheckHere(TradeOffer give, TradeOffer receive, string me, string partner)
    {
        TradeHoldings here = Snapshot();
        // The partner's capacity is their machine's business; only this
        // side's holdings and room are checked here (the partner's are
        // "infinite" for this call).
        var other = new TradeHoldings { Gold = int.MaxValue, TomeSlots = 1000 };
        foreach (var (id, n) in receive.Items) other.Items[id] = n;
        foreach (var (id, l) in receive.Tomes) other.Tomes[id] = System.Math.Max(1, l);
        List<TradeProblem> problems = TradeRules.Validate(new[] { give, receive }, new[] { here, other }, new[] { me, partner });
        foreach (TradeProblem p in problems)
            if (p.Side >= 0) return p;   // "empty table" is the host's call
        return null;
    }

    /// <summary>Step 1: remove <paramref name="give"/> into escrow for trade
    /// <paramref name="tradeId"/>. Returns what was actually removed (tome
    /// levels filled in), or null and nothing changed when a check fails.</summary>
    public static TradeOffer? TakeEscrow(int tradeId, TradeOffer give, TradeOffer receive, string me, string partner, out string? why)
    {
        why = null;
        if (EscrowId != 0 && EscrowId != tradeId)
        {
            // A stale escrow from a trade that never answered: put it back first.
            Refund(EscrowId);
        }
        if (EscrowId == tradeId && Escrow is not null)
        {
            if (SameGoods(Escrow, give)) return Escrow;   // a repeated request
            Refund(tradeId);                               // a new round with other goods
        }

        if (CheckHere(give, receive, me, partner) is { } problem) { why = problem.ToString(); return null; }
        Inventory? inv = Inventory.Current;
        TomeBook? book = TomeBook.Current;
        if (inv is null || book is null) { why = "no inventory"; return null; }

        var taken = new TradeOffer();
        if (give.Gold > 0 && inv.TradeGold(-give.Gold)) taken.Gold = give.Gold;
        foreach (var (id, n) in give.Items)
        {
            int got = inv.TradeRemove(Catalog.ItemById(id), n);
            if (got > 0) taken.Items[id] = got;
        }
        bool tomes = false;
        foreach (string id in give.Tomes.Keys)
        {
            int level = book.TradeRemove(Data.Tomes.ById(id));
            if (level > 0) { taken.Tomes[id] = level; tomes = true; }
        }
        if (tomes) Main.Instance?.Hud.ResyncTomes(book);
        EscrowId = tradeId;
        Escrow = taken;
        GD.Print($"[trade] escrow #{tradeId}: {taken.Pack()}  (gold now {inv.Gold})");
        return taken;
    }

    /// <summary>Put the escrow for <paramref name="tradeId"/> back. No-op when
    /// none is held for it.</summary>
    public static void Refund(int tradeId)
    {
        if (EscrowId == 0 || EscrowId != tradeId || Escrow is null) return;
        TradeOffer back = Escrow;
        EscrowId = 0;
        Escrow = null;
        Apply(back);
        Refunds++;
        GD.Print($"[trade] refunded #{tradeId}: {back.Pack()}");
    }

    /// <summary>Step 2: the swap happened. Forget the escrow and add what the
    /// partner gave.</summary>
    public static void Settle(int tradeId, TradeOffer incoming)
    {
        if (EscrowId == tradeId)
        {
            EscrowId = 0;
            Escrow = null;
        }
        Apply(incoming);
        Settled++;
        GD.Print($"[trade] settled #{tradeId}: received {incoming.Pack()}  holdings {Snapshot().Pack()}");
    }

    /// <summary>Adds goods to this player. Each item copy goes through
    /// <see cref="Inventory.Add"/> (stack caps, stat sheet); each tome is
    /// learned at its level.</summary>
    private static void Apply(TradeOffer goods)
    {
        Inventory? inv = Inventory.Current;
        TomeBook? book = TomeBook.Current;
        if (inv is null) return;
        if (goods.Gold > 0) inv.TradeGold(goods.Gold);
        foreach (var (id, n) in goods.Items)
        {
            ItemDef? def = Catalog.ItemById(id);
            for (int i = 0; i < n; i++)
                if (!inv.Add(def)) { GD.PrintErr($"[trade] could not add {id} copy {i + 1}/{n} (cap)"); break; }
        }
        if (book is null) return;
        foreach (var (id, level) in goods.Tomes)
            if (!book.TradeGrant(Data.Tomes.ById(id), level)) GD.PrintErr($"[trade] could not learn {id} Lv{level}");
    }

    private static bool SameGoods(TradeOffer held, TradeOffer give)
    {
        if (held.Gold != give.Gold || held.Items.Count != give.Items.Count || held.Tomes.Count != give.Tomes.Count) return false;
        foreach (var (id, n) in give.Items) if (held.ItemCount(id) != n) return false;
        foreach (string id in give.Tomes.Keys) if (!held.HasTome(id)) return false;
        return true;
    }

    /// <summary>A new run: nothing is held in escrow.</summary>
    public static void Reset()
    {
        EscrowId = 0;
        Escrow = null;
        Settled = 0;
        Refunds = 0;
    }
}
