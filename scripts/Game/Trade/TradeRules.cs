using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Wavebreak.Data;

namespace Wavebreak.Game.Trade;

/// <summary>
/// What one side puts on the table at the swap meet: gold, item copies
/// (id -> count) and whole tomes (id -> level). Packed as one short string
/// for the network ("g=50;i=item_a*2;t=tome_x*3"); ids never contain the
/// separators.
/// </summary>
public sealed class TradeOffer
{
    public int Gold;
    public readonly SortedDictionary<string, int> Items = new(StringComparer.Ordinal);
    /// <summary>Tome id -> level it is given at.</summary>
    public readonly SortedDictionary<string, int> Tomes = new(StringComparer.Ordinal);

    public bool IsEmpty => Gold <= 0 && Items.Count == 0 && Tomes.Count == 0;

    public int ItemCount(string id) => Items.TryGetValue(id, out int n) ? n : 0;

    public bool HasTome(string id) => Tomes.ContainsKey(id);

    public TradeOffer Clone() => Parse(Pack());

    public bool SameAs(TradeOffer? other) => other is not null && Pack() == other.Pack();

    public string Pack()
    {
        var sb = new StringBuilder();
        sb.Append("g=").Append(Gold.ToString(CultureInfo.InvariantCulture));
        foreach (var (id, n) in Items) if (n > 0) sb.Append(";i=").Append(id).Append('*').Append(n.ToString(CultureInfo.InvariantCulture));
        foreach (var (id, l) in Tomes) sb.Append(";t=").Append(id).Append('*').Append(l.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public static TradeOffer Parse(string? packed)
    {
        var o = new TradeOffer();
        if (string.IsNullOrEmpty(packed)) return o;
        foreach (string part in packed.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 3 || part[1] != '=') continue;
            string v = part[2..];
            switch (part[0])
            {
                case 'g': o.Gold = Math.Max(0, ToInt(v)); break;
                case 'i': if (Split(v, out string iid, out int n) && n > 0) o.Items[iid] = n; break;
                case 't': if (Split(v, out string tid, out int l)) o.Tomes[tid] = Math.Max(0, l); break;
            }
        }
        return o;
    }

    internal static bool Split(string v, out string id, out int n)
    {
        int star = v.LastIndexOf('*');
        id = star > 0 ? v[..star] : v;
        n = star > 0 ? ToInt(v[(star + 1)..]) : 1;
        return id.Length > 0;
    }

    internal static int ToInt(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    public override string ToString() => Pack();
}

/// <summary>What a player holds that a trade can touch, as their own machine
/// reported it: gold, item stacks, tomes with levels, tome slots. The host
/// validates against it; the window greys out what the partner cannot take.</summary>
public sealed class TradeHoldings
{
    public int Gold;
    public int TomeSlots = Data.Tomes.MaxSlots;
    public readonly SortedDictionary<string, int> Items = new(StringComparer.Ordinal);
    public readonly SortedDictionary<string, int> Tomes = new(StringComparer.Ordinal);

    public int ItemCount(string id) => Items.TryGetValue(id, out int n) ? n : 0;
    public int TomeLevel(string id) => Tomes.TryGetValue(id, out int l) ? l : 0;

    public string Pack()
    {
        var sb = new StringBuilder();
        sb.Append("g=").Append(Gold.ToString(CultureInfo.InvariantCulture));
        sb.Append(";s=").Append(TomeSlots.ToString(CultureInfo.InvariantCulture));
        foreach (var (id, n) in Items) if (n > 0) sb.Append(";i=").Append(id).Append('*').Append(n.ToString(CultureInfo.InvariantCulture));
        foreach (var (id, l) in Tomes) sb.Append(";t=").Append(id).Append('*').Append(l.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public static TradeHoldings Parse(string? packed)
    {
        var h = new TradeHoldings();
        if (string.IsNullOrEmpty(packed)) return h;
        foreach (string part in packed.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 3 || part[1] != '=') continue;
            string v = part[2..];
            switch (part[0])
            {
                case 'g': h.Gold = Math.Max(0, TradeOffer.ToInt(v)); break;
                case 's': h.TomeSlots = Math.Max(0, TradeOffer.ToInt(v)); break;
                case 'i': if (TradeOffer.Split(v, out string iid, out int n) && n > 0) h.Items[iid] = n; break;
                case 't': if (TradeOffer.Split(v, out string tid, out int l)) h.Tomes[tid] = l; break;
            }
        }
        return h;
    }

    public override string ToString() => Pack();
}

/// <summary>One reason a trade cannot go ahead, as an English format string
/// and its arguments (translated where it is shown, with Loc.F). Side is the
/// side whose offer it is about (0 = the player who asked, 1 = the other).</summary>
public readonly struct TradeProblem
{
    public readonly int Side;
    public readonly string Format;
    public readonly object[] Args;

    public TradeProblem(int side, string format, params object[] args)
    {
        Side = side;
        Format = format;
        Args = args;
    }

    public string Text => Meta.Loc.F(Format, Args);

    public override string ToString() => string.Format(CultureInfo.InvariantCulture, Format, Args);
}

/// <summary>
/// The rules of a swap, as pure functions so the host (the authority), each
/// window (hints, greyed tiles) and the self-check all agree.
///
/// A side may only give what it holds. The receiver must be able to hold the
/// result *after* the swap: an item stack cap counts what the receiver gives
/// away of the same item; a tome must be new to the receiver (a tome comes
/// with its level and is never merged into another copy) unless the receiver
/// is giving theirs away in the same trade, and the receiver's tome count
/// after the swap must fit its slots. Weapons are not traded (see
/// ARCHITECTURE.md, "Swap meet").
/// </summary>
public static class TradeRules
{
    /// <summary>Everything wrong with offers[0] for players 0 -> 1 and
    /// offers[1] for 1 -> 0. Empty means the swap can execute.</summary>
    public static List<TradeProblem> Validate(TradeOffer[] offers, TradeHoldings[] holds, string[] names)
    {
        var problems = new List<TradeProblem>();
        if (offers[0].IsEmpty && offers[1].IsEmpty)
        {
            problems.Add(new TradeProblem(-1, "Put something on the table first."));
            return problems;
        }
        for (int s = 0; s < 2; s++)
        {
            int r = 1 - s;
            TradeOffer give = offers[s], back = offers[r];
            TradeHoldings mine = holds[s], theirs = holds[r];

            // The giver holds it all.
            if (give.Gold < 0 || give.Gold > mine.Gold)
                problems.Add(new TradeProblem(s, "{0} does not have {1} gold.", names[s], give.Gold));
            foreach (var (id, n) in give.Items)
            {
                ItemDef? def = Catalog.ItemById(id);
                if (def is null) { problems.Add(new TradeProblem(s, "Unknown item {0}.", id)); continue; }
                if (n > mine.ItemCount(id))
                    problems.Add(new TradeProblem(s, "{0} holds only {1} {2}.", names[s], mine.ItemCount(id), def.DisplayName));
            }
            foreach (string id in give.Tomes.Keys)
            {
                TomeDef? def = Data.Tomes.ById(id);
                if (def is null) { problems.Add(new TradeProblem(s, "Unknown tome {0}.", id)); continue; }
                if (mine.TomeLevel(id) <= 0)
                    problems.Add(new TradeProblem(s, "{0} does not hold {1}.", names[s], def.DisplayName));
            }

            // The receiver can hold it after the swap.
            foreach (var (id, n) in give.Items)
            {
                ItemDef? def = Catalog.ItemById(id);
                if (def is null || def.MaxStacks <= 0) continue;
                int room = def.MaxStacks - (theirs.ItemCount(id) - back.ItemCount(id));
                if (n > room)
                    problems.Add(room <= 0
                        ? new TradeProblem(s, "{0} already has the most {1} anyone can hold.", names[r], def.DisplayName)
                        : new TradeProblem(s, "{0} can hold only {1} more {2}.", names[r], room, def.DisplayName));
            }
            foreach (string id in give.Tomes.Keys)
            {
                TomeDef? def = Data.Tomes.ById(id);
                if (def is null) continue;
                if (theirs.TomeLevel(id) > 0 && !back.HasTome(id))
                    problems.Add(new TradeProblem(s, "{0} already has {1}.", names[r], def.DisplayName));
            }
            int tomesAfter = theirs.Tomes.Count - back.Tomes.Count + give.Tomes.Count;
            if (give.Tomes.Count > 0 && tomesAfter > theirs.TomeSlots)
                problems.Add(new TradeProblem(s, "{0} has no free tome slot ({1}/{2}).", names[r], theirs.Tomes.Count, theirs.TomeSlots));
        }
        return problems;
    }

    /// <summary>How many more copies of <paramref name="def"/> the receiver
    /// could take on top of <paramref name="incoming"/>; int.MaxValue when
    /// uncapped.</summary>
    public static int RoomFor(ItemDef def, TradeHoldings receiver, TradeOffer receiverGives, TradeOffer incoming)
    {
        if (def.MaxStacks <= 0) return int.MaxValue;
        return Math.Max(0, def.MaxStacks - (receiver.ItemCount(def.Id) - receiverGives.ItemCount(def.Id)) - incoming.ItemCount(def.Id));
    }

    /// <summary>Why the receiver cannot take <paramref name="def"/> as well
    /// as <paramref name="incoming"/>, as an English format with {0} the
    /// receiver's name; null when they can.</summary>
    public static string? TomeBlock(TomeDef def, TradeHoldings receiver, TradeOffer receiverGives, TradeOffer incoming)
    {
        if (receiver.TomeLevel(def.Id) > 0 && !receiverGives.HasTome(def.Id)) return "{0} already has this tome.";
        int after = receiver.Tomes.Count - receiverGives.Tomes.Count + incoming.Tomes.Count + (incoming.HasTome(def.Id) ? 0 : 1);
        if (after > receiver.TomeSlots) return "{0} has no free tome slot.";
        return null;
    }
}
