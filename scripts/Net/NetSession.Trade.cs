using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Game.Trade;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// The swap meet's network side (Game/Trade, Actors/Trader, UI/TradePanel).
/// Host-authoritative, like everything that two players must agree on.
///
/// <b>Asking.</b> A player at the stall picks a teammate: TradeAsk goes to the
/// host, which checks both are free and in the rest room and sends that
/// teammate a TradeInvite. Their TradeAnswer, if yes, makes the host open a
/// session and tell both (TradeOpen). Unanswered invites expire after
/// <see cref="TradeAskSeconds"/>.
///
/// <b>Haggling.</b> Each side sends its offer with its current holdings
/// (TradeSet; the holdings again whenever they change). The host keeps both,
/// and every change of an offer bumps the session's version and clears both
/// READY and both CONFIRM flags (the classic guard against a last-second
/// swap). READY and CONFIRM carry the version the player saw, so a click on
/// a table that has since changed is ignored. READY is refused while the
/// shared rules (<see cref="TradeRules.Validate"/>) find a problem. Every
/// change goes to both sides as TradeState.
///
/// <b>Executing.</b> Each machine owns its own inventory, so the host cannot
/// simply rewrite both. When both have confirmed, the host asks each side to
/// escrow its offer (TradeEscrow): that side re-checks against its live
/// inventory, removes the goods and reports exactly what it removed
/// (TradeEscrowed). Only when both escrows are in does the host send each side
/// the other's escrow (TradeGrant). A failed check, a timeout, a player
/// leaving, the rest room ending or the host going away returns every
/// outstanding escrow to its owner (TradeRollback / TradeClosed / the local
/// cleanup). The goods are always in one inventory or one escrow, never two
/// places, so a disconnect cannot duplicate anything.
///
/// The host's own player goes through the same steps with local calls
/// instead of RPCs (<see cref="ToParty"/>).
/// </summary>
public sealed partial class NetSession
{
    public const float TradeAskSeconds = 20f;
    public const float TradeEscrowTimeout = 10f;

    private sealed class TradeSession
    {
        public int Id;
        public readonly long[] P = new long[2];
        public readonly TradeOffer[] Offer = { new(), new() };
        public readonly TradeHoldings[] Hold = { new(), new() };
        public readonly bool[] Ready = new bool[2];
        public readonly bool[] Confirm = new bool[2];
        public readonly TradeOffer?[] Escrow = new TradeOffer?[2];
        public int Version = 1;
        public bool Committing;
        public double CommitAt;
        public string Notice = "", NoticeArg = "";

        public int SideOf(long peer) => P[0] == peer ? 0 : P[1] == peer ? 1 : -1;
        public string[] Names => new[] { Coop.NameOf(P[0]), Coop.NameOf(P[1]) };

        public void ResetFlags()
        {
            Ready[0] = Ready[1] = false;
            Confirm[0] = Confirm[1] = false;
        }
    }

    private readonly Dictionary<int, TradeSession> _trades = new();
    private readonly Dictionary<int, (long From, long To, double Expires)> _tradeAsks = new();
    private int _nextTradeId = 1;
    private string _tradeHoldSent = "";
    private float _tradeHoldTimer;

    /// <summary>Host: swaps executed / sessions closed without a swap.</summary>
    public int TradesDone { get; private set; }
    public int TradesClosed { get; private set; }

    /// <summary>My offer as I am editing it. The host's echo of an older
    /// offer must not overwrite clicks it has not seen yet, so the window
    /// shows this, and it is replaced by the host's copy only once the host
    /// has caught up with it.</summary>
    public TradeOffer TradeDraft { get; private set; } = new();
    private bool _tradeDraftDirty;

    /// <summary>The draft has been sent but the host has not echoed it yet.</summary>
    public bool TradeDraftPending => _tradeDraftDirty;

    private bool TradeRoomOpen => Coop.InRun && _go && _main is not null && IsInstanceValid(_main) && _main.Stages.InRestRoom;

    private bool TradeBusy(long peer)
    {
        foreach (TradeSession s in _trades.Values)
            if (s.SideOf(peer) >= 0) return true;
        return false;
    }

    /// <summary>Teammates who could be asked to trade: every connected player
    /// but me. (The whole team is in the rest room together.)</summary>
    public List<CoopPeer> TradePartners()
    {
        var list = new List<CoopPeer>();
        foreach (CoopPeer p in Coop.Roster)
            if (p.Id != Coop.LocalId && _remotes.ContainsKey(p.Id)) list.Add(p);
        return list;
    }

    /// <summary>Deliver to a party: a local call for the host's own player,
    /// an RPC for anyone else.</summary>
    private void ToParty(long peer, StringName rpc, Action local, params Variant[] args)
    {
        if (peer == Coop.LocalId) local();
        else RpcId(peer, rpc, args);
    }

    private long TradeSender()
    {
        long id = Multiplayer.GetRemoteSenderId();
        return id == 0 ? Coop.LocalId : id;
    }

    // ================================================================ UI side

    public void TradeRequest(long target)
    {
        if (Coop.IsHost) HostTradeAsk(Coop.LocalId, target);
        else RpcId(1, MethodName.TradeAskRpc, target);
    }

    public void TradeRespond(bool yes)
    {
        int id = TradeView.InviteId;
        if (id == 0) return;
        TradeView.InviteId = 0;
        if (Coop.IsHost) HostTradeAnswer(Coop.LocalId, id, yes);
        else RpcId(1, MethodName.TradeAnswerRpc, id, yes);
    }

    /// <summary>Replace my offer (clamped to what I hold) and send it with my holdings.</summary>
    public void TradeSetOffer(TradeOffer offer)
    {
        if (!TradeView.Open || TradeView.Committing) return;
        TradeHoldings mine = TradeLedger.Snapshot();
        TradeOffer clean = Sanitize(offer, mine);
        TradeDraft = clean;
        _tradeDraftDirty = true;
        SendTradeSet(clean, mine);
        TradeView.RaiseChanged();
    }

    private void SendTradeSet(TradeOffer offer, TradeHoldings hold)
    {
        int id = TradeView.Id;
        string o = offer.Pack(), h = hold.Pack();
        _tradeHoldSent = h;
        if (Coop.IsHost) HostTradeSet(Coop.LocalId, id, o, h);
        else RpcId(1, MethodName.TradeSetRpc, id, o, h);
    }

    public void TradeSetReady(bool ready)
    {
        if (!TradeView.Open || _tradeDraftDirty) return;
        if (Coop.IsHost) HostTradeReady(Coop.LocalId, TradeView.Id, TradeView.Version, ready);
        else RpcId(1, MethodName.TradeReadyRpc, TradeView.Id, TradeView.Version, ready);
    }

    public void TradeConfirm()
    {
        if (!TradeView.Open || _tradeDraftDirty) return;
        if (Coop.IsHost) HostTradeConfirm(Coop.LocalId, TradeView.Id, TradeView.Version);
        else RpcId(1, MethodName.TradeConfirmRpc, TradeView.Id, TradeView.Version);
    }

    public void TradeCancel()
    {
        if (!TradeView.Open) return;
        int id = TradeView.Id;
        if (TradeView.Committing) return;   // the swap is already executing; it finishes or rolls back on its own
        if (Coop.IsHost) HostTradeCancel(Coop.LocalId, id);
        else RpcId(1, MethodName.TradeCancelRpc, id);
    }

    /// <summary>Only what I hold, tomes at their real level.</summary>
    private static TradeOffer Sanitize(TradeOffer offer, TradeHoldings mine)
    {
        var o = new TradeOffer { Gold = Math.Clamp(offer.Gold, 0, mine.Gold) };
        foreach (var (id, n) in offer.Items)
        {
            int c = Math.Min(n, mine.ItemCount(id));
            if (c > 0) o.Items[id] = c;
        }
        foreach (string id in offer.Tomes.Keys)
            if (mine.TomeLevel(id) > 0) o.Tomes[id] = mine.TomeLevel(id);
        return o;
    }

    // ================================================================ host

    private void HostTradeAsk(long from, long to)
    {
        if (!Coop.IsHost) return;
        if (!TradeRoomOpen) { TradeNoteTo(from, "Trading is only possible in the rest room.", ""); return; }
        if (from == to || Coop.Peer(to) is null) { TradeNoteTo(from, "That player is not here.", ""); return; }
        if (TradeBusy(from)) { TradeNoteTo(from, "Finish your current trade first.", ""); return; }
        if (TradeBusy(to)) { TradeNoteTo(from, "{0} is busy trading.", Coop.NameOf(to)); return; }
        foreach (var (askId, a) in _tradeAsks)
            if (a.From == from && a.To == to) return;   // already asked
        int id = _nextTradeId++;
        _tradeAsks[id] = (from, to, Now + TradeAskSeconds);
        string fromName = Coop.NameOf(from);
        GD.Print($"[trade] {fromName} asks {Coop.NameOf(to)} to trade (#{id})");
        ToParty(to, MethodName.TradeInviteRpc, () => OnTradeInvite(id, fromName), id, fromName);
        TradeNoteTo(from, "Asked {0} to trade. Waiting...", Coop.NameOf(to));
    }

    private void HostTradeAnswer(long from, int askId, bool yes)
    {
        if (!Coop.IsHost || !_tradeAsks.Remove(askId, out var ask) || ask.To != from) return;
        string toName = Coop.NameOf(ask.To);
        if (!yes)
        {
            GD.Print($"[trade] {toName} declined #{askId}");
            TradeNoteTo(ask.From, "{0} declined the trade.", toName);
            return;
        }
        if (!TradeRoomOpen || Coop.Peer(ask.From) is null) { TradeNoteTo(from, "The trade is no longer possible.", ""); return; }
        if (TradeBusy(ask.From) || TradeBusy(ask.To)) { TradeNoteTo(from, "{0} is busy trading.", Coop.NameOf(ask.From)); return; }

        var s = new TradeSession { Id = askId };
        s.P[0] = ask.From;
        s.P[1] = ask.To;
        _trades[askId] = s;
        GD.Print($"[trade] open #{askId}: {Coop.NameOf(ask.From)} <-> {toName}");
        // Remote side first: the host's own window sends its first offer at
        // once, and that state must not reach the other side before its open.
        long pa = s.P[0], pb = s.P[1];
        foreach (long party in s.P[0] == Coop.LocalId ? new[] { pb, pa } : new[] { pa, pb })
            ToParty(party, MethodName.TradeOpenRpc, () => OnTradeOpen(askId, pa, pb), askId, pa, pb);
        BroadcastTrade(s);
    }

    private void HostTradeSet(long from, int id, string offerPacked, string holdPacked)
    {
        if (!Coop.IsHost || !_trades.TryGetValue(id, out TradeSession? s)) return;
        int side = s.SideOf(from);
        if (side < 0) return;
        s.Hold[side] = TradeHoldings.Parse(holdPacked);
        if (s.Committing) { BroadcastTrade(s); return; }
        TradeOffer offer = Sanitize(TradeOffer.Parse(offerPacked), s.Hold[side]);
        if (!offer.SameAs(s.Offer[side]))
        {
            bool wasReady = s.Ready[0] || s.Ready[1] || s.Confirm[0] || s.Confirm[1];
            s.Offer[side] = offer;
            s.ResetFlags();
            s.Version++;
            if (wasReady) { s.Notice = "The offer changed, so READY was reset."; s.NoticeArg = ""; }
            else s.Notice = s.NoticeArg = "";
            GD.Print($"[trade] #{id} {Coop.NameOf(from)} offers {offer.Pack()} (v{s.Version}{(wasReady ? ", READY reset" : "")})");
        }
        BroadcastTrade(s);
    }

    private void HostTradeReady(long from, int id, int version, bool ready)
    {
        if (!Coop.IsHost || !_trades.TryGetValue(id, out TradeSession? s)) return;
        int side = s.SideOf(from);
        if (side < 0) return;
        if (version != s.Version || s.Committing) { BroadcastTrade(s); return; }
        if (ready)
        {
            List<TradeProblem> problems = TradeRules.Validate(s.Offer, s.Hold, s.Names);
            if (problems.Count > 0)
            {
                s.Notice = problems[0].Format;
                s.NoticeArg = string.Join("\u001f", Array.ConvertAll(problems[0].Args, a => Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture) ?? ""));
                BroadcastTrade(s);
                return;
            }
        }
        s.Ready[side] = ready;
        if (!ready) s.Confirm[0] = s.Confirm[1] = false;
        s.Notice = s.NoticeArg = "";
        BroadcastTrade(s);
    }

    private void HostTradeConfirm(long from, int id, int version)
    {
        if (!Coop.IsHost || !_trades.TryGetValue(id, out TradeSession? s)) return;
        int side = s.SideOf(from);
        if (side < 0 || version != s.Version || s.Committing || !s.Ready[0] || !s.Ready[1]) { if (side >= 0) BroadcastTrade(s); return; }
        s.Confirm[side] = true;
        if (s.Confirm[0] && s.Confirm[1]) CommitTrade(s);
        else BroadcastTrade(s);
    }

    private void HostTradeCancel(long from, int id)
    {
        if (!Coop.IsHost || !_trades.TryGetValue(id, out TradeSession? s) || s.SideOf(from) < 0) return;
        if (s.Committing) return;
        CloseTrade(s, "{0} cancelled the trade.", Coop.NameOf(from));
    }

    private void CommitTrade(TradeSession s)
    {
        List<TradeProblem> problems = TradeRules.Validate(s.Offer, s.Hold, s.Names);
        if (problems.Count > 0)
        {
            s.ResetFlags();
            s.Version++;
            s.Notice = problems[0].Format;
            s.NoticeArg = string.Join("\u001f", Array.ConvertAll(problems[0].Args, a => Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture) ?? ""));
            BroadcastTrade(s);
            return;
        }
        s.Committing = true;
        s.CommitAt = Now;
        s.Escrow[0] = s.Escrow[1] = null;
        GD.Print($"[trade] #{s.Id} both confirmed v{s.Version}: escrow {s.Offer[0].Pack()} <-> {s.Offer[1].Pack()}");
        BroadcastTrade(s);
        int id = s.Id, v = s.Version;
        // Remote side first, then the host's own (a local call that may
        // answer, and even roll back, at once); stop if it already did.
        int[] order = s.P[0] == Coop.LocalId ? new[] { 1, 0 } : new[] { 0, 1 };
        foreach (int side in order)
        {
            if (!s.Committing || s.Version != v || !_trades.ContainsKey(id)) break;
            string give = s.Offer[side].Pack(), get = s.Offer[1 - side].Pack();
            ToParty(s.P[side], MethodName.TradeEscrowRpc, () => OnTradeEscrow(id, v, give, get), id, v, give, get);
        }
    }

    private void HostTradeEscrowed(long from, int id, int version, bool ok, string data)
    {
        if (!Coop.IsHost || !_trades.TryGetValue(id, out TradeSession? s)) return;
        int side = s.SideOf(from);
        if (side < 0 || !s.Committing || version != s.Version) return;   // a stale answer: that side already got its rollback
        if (!ok)
        {
            GD.Print($"[trade] #{id} {Coop.NameOf(from)} could not escrow: {data}");
            RollbackTrade(s, data, "");
            return;
        }
        s.Escrow[side] = TradeOffer.Parse(data);
        if (s.Escrow[0] is null || s.Escrow[1] is null) return;

        // Both sides have removed their goods: hand each the other's.
        _trades.Remove(id);
        TradesDone++;
        GD.Print($"[trade] #{id} done: {Coop.NameOf(s.P[0])} gave {s.Escrow[0]!.Pack()}, {Coop.NameOf(s.P[1])} gave {s.Escrow[1]!.Pack()}");
        for (int k = 0; k < 2; k++)
        {
            string incoming = s.Escrow[1 - k]!.Pack();
            ToParty(s.P[k], MethodName.TradeGrantRpc, () => OnTradeGrant(id, incoming), id, incoming);
        }
    }

    private void RollbackTrade(TradeSession s, string fmt, string arg)
    {
        int id = s.Id;
        for (int k = 0; k < 2; k++)
            ToParty(s.P[k], MethodName.TradeRollbackRpc, () => OnTradeRollback(id), id);
        s.Committing = false;
        s.Escrow[0] = s.Escrow[1] = null;
        s.ResetFlags();
        s.Version++;
        s.Notice = fmt;
        s.NoticeArg = arg;
        BroadcastTrade(s);
    }

    private void CloseTrade(TradeSession s, string fmt, string arg)
    {
        _trades.Remove(s.Id);
        TradesClosed++;
        GD.Print($"[trade] #{s.Id} closed: {string.Format(fmt, arg)}");
        int id = s.Id;
        for (int k = 0; k < 2; k++)
        {
            if (Coop.Peer(s.P[k]) is null && s.P[k] != Coop.LocalId) continue;   // gone
            ToParty(s.P[k], MethodName.TradeClosedRpc, () => OnTradeClosed(id, fmt, arg), id, fmt, arg);
        }
    }

    private void BroadcastTrade(TradeSession s)
    {
        string packed = string.Join("\n",
            s.Offer[0].Pack(), s.Hold[0].Pack(), s.Offer[1].Pack(), s.Hold[1].Pack(),
            $"{(s.Ready[0] ? 1 : 0)}{(s.Ready[1] ? 1 : 0)}{(s.Confirm[0] ? 1 : 0)}{(s.Confirm[1] ? 1 : 0)}{(s.Committing ? 1 : 0)}",
            s.Notice, s.NoticeArg);
        int id = s.Id, v = s.Version;
        for (int k = 0; k < 2; k++)
            ToParty(s.P[k], MethodName.TradeStateRpc, () => OnTradeState(id, v, packed), id, v, packed);
    }

    private void TradeNoteTo(long peer, string fmt, string arg)
    {
        if (Coop.Peer(peer) is null && peer != Coop.LocalId) return;
        ToParty(peer, MethodName.TradeNoteRpc, () => OnTradeNote(fmt, arg), fmt, arg);
    }

    /// <summary>Host: a player left. Their invites and trades end; the
    /// partner gets any escrow back.</summary>
    private void TradePeerLeft(long id, string name)
    {
        if (!Coop.IsHost) return;
        var gone = new List<int>();
        foreach (var (askId, a) in _tradeAsks)
            if (a.From == id || a.To == id) gone.Add(askId);
        foreach (int askId in gone)
        {
            var a = _tradeAsks[askId];
            _tradeAsks.Remove(askId);
            long other = a.From == id ? a.To : a.From;
            if (other == a.To) ToParty(other, MethodName.TradeInviteGoneRpc, () => OnTradeInviteGone(askId), askId);
        }
        foreach (TradeSession s in new List<TradeSession>(_trades.Values))
            if (s.SideOf(id) >= 0) CloseTrade(s, "{0} left the game.", name);
    }

    /// <summary>Host: the rest room is ending (everyone in the portal, or
    /// its countdown ran out). Sent before the Phase message, so every
    /// window closes and every escrow is back before the next map.</summary>
    public void TradeRoomEnding()
    {
        if (!Coop.IsHost) return;
        foreach (var (askId, a) in _tradeAsks)
            ToParty(a.To, MethodName.TradeInviteGoneRpc, () => OnTradeInviteGone(askId), askId);
        _tradeAsks.Clear();
        foreach (TradeSession s in new List<TradeSession>(_trades.Values))
            CloseTrade(s, "The rest room ended. The trade was cancelled.", "");
    }

    /// <summary>Every frame: host timeouts; every machine re-sends its
    /// holdings when they change while a window is open.</summary>
    private void StepTrade(float dt)
    {
        if (Coop.IsHost)
        {
            double now = Now;
            List<int>? expired = null;
            foreach (var (askId, a) in _tradeAsks)
                if (now >= a.Expires) (expired ??= new List<int>()).Add(askId);
            if (expired is not null)
                foreach (int askId in expired)
                {
                    var a = _tradeAsks[askId];
                    _tradeAsks.Remove(askId);
                    TradeNoteTo(a.From, "{0} did not answer.", Coop.NameOf(a.To));
                    if (Coop.Peer(a.To) is not null) ToParty(a.To, MethodName.TradeInviteGoneRpc, () => OnTradeInviteGone(askId), askId);
                }
            foreach (TradeSession s in new List<TradeSession>(_trades.Values))
                if (s.Committing && now - s.CommitAt > TradeEscrowTimeout)
                    RollbackTrade(s, "The swap timed out. Try again.", "");
        }

        if (!TradeView.Open || TradeView.Committing) return;
        _tradeHoldTimer -= dt;
        if (_tradeHoldTimer > 0f) return;
        _tradeHoldTimer = 0.5f;
        TradeHoldings mine = TradeLedger.Snapshot();
        if (mine.Pack() != _tradeHoldSent) SendTradeSet(Sanitize(TradeDraft, mine), mine);
    }

    // ================================================================ party side

    private void OnTradeInvite(int askId, string fromName)
    {
        TradeView.InviteId = askId;
        TradeView.InviteFrom = fromName;
        GD.Print($"[trade] {fromName} wants to trade (#{askId})");
        TradeView.RaiseInvited(askId, fromName);
        TestTradeInvited(askId);
    }

    private void OnTradeInviteGone(int askId)
    {
        if (TradeView.InviteId != askId) return;
        TradeView.InviteId = 0;
        TradeView.RaiseChanged();
    }

    private void OnTradeOpen(int id, long a, long b)
    {
        TradeView.Begin(id, a, b, Coop.NameOf(a), Coop.NameOf(b), Coop.LocalId);
        TradeDraft = new TradeOffer();
        _tradeDraftDirty = false;
        _tradeHoldSent = "";
        _tradeHoldTimer = 0f;
        GD.Print($"[trade] window open #{id} with {TradeView.PartnerName}");
        SendTradeSet(TradeDraft, TradeLedger.Snapshot());
    }

    private void OnTradeState(int id, int version, string packed)
    {
        if (TradeView.Id != id) return;
        string[] p = packed.Split('\n');
        if (p.Length < 7) return;
        bool[] before = { TradeView.Ready[0], TradeView.Ready[1] };
        for (int k = 0; k < 2; k++)
        {
            TradeView.Offers[k] = TradeOffer.Parse(p[k * 2]);
            TradeView.Holds[k] = TradeHoldings.Parse(p[k * 2 + 1]);
        }
        string f = p[4];
        if (f.Length >= 5)
        {
            TradeView.Ready[0] = f[0] == '1';
            TradeView.Ready[1] = f[1] == '1';
            TradeView.Confirm[0] = f[2] == '1';
            TradeView.Confirm[1] = f[3] == '1';
            TradeView.Committing = f[4] == '1';
        }
        if (version > TradeView.Version && ((before[0] && !TradeView.Ready[0]) || (before[1] && !TradeView.Ready[1])))
            TradeView.ReadyResets++;
        TradeView.Version = version;
        TradeView.Notice = p[5];
        TradeView.NoticeArg = p[6];

        // The host has caught up with my clicks: its copy is the truth again.
        if (TradeDraft.SameAs(TradeView.Mine)) _tradeDraftDirty = false;
        if (!_tradeDraftDirty) TradeDraft = TradeView.Mine.Clone();
        TradeView.RaiseChanged();
    }

    private void OnTradeEscrow(int id, int version, string give, string get)
    {
        bool ok;
        string data;
        if (TradeView.Id != id)
        {
            ok = false;
            data = "The trade window was closed.";
        }
        else
        {
            TradeOffer? taken = TradeLedger.TakeEscrow(id, TradeOffer.Parse(give), TradeOffer.Parse(get),
                                                       TradeView.Names[TradeView.Side], TradeView.PartnerName, out string? why);
            ok = taken is not null;
            data = taken?.Pack() ?? why ?? "?";
        }
        if (Coop.IsHost) HostTradeEscrowed(Coop.LocalId, id, version, ok, data);
        else RpcId(1, MethodName.TradeEscrowedRpc, id, version, ok, data);
    }

    private void OnTradeGrant(int id, string incoming)
    {
        TradeOffer got = TradeOffer.Parse(incoming);
        TestTradeSettled(got, true);
        TradeLedger.Settle(id, got);
        TestTradeSettled(got, false);
        if (TradeView.Id == id) TradeView.Finish(true, "Trade complete!", "");
    }

    private void OnTradeRollback(int id)
    {
        TradeLedger.Refund(id);
    }

    private void OnTradeClosed(int id, string fmt, string arg)
    {
        TradeLedger.Refund(id);
        if (TradeView.InviteId == id) TradeView.InviteId = 0;
        if (TradeView.Id == id)
        {
            GD.Print($"[trade] window closed #{id}: {string.Format(fmt, arg)}");
            TradeView.Finish(false, fmt, arg);
        }
    }

    private void OnTradeNote(string fmt, string arg)
    {
        GD.Print($"[trade] note: {string.Format(fmt, arg)}");
        TradeView.RaiseNote(fmt, arg);
        Popup(Loc.F(fmt, arg), new Color(1f, 0.86f, 0.55f));
    }

    /// <summary>This machine lost the trade from under it (the host went
    /// away, the session closed, the room ended locally): any escrow goes
    /// back to this player, the window closes.</summary>
    public void TradeLocalEnd(string fmt)
    {
        TradeView.InviteId = 0;
        if (TradeLedger.EscrowId != 0) TradeLedger.Refund(TradeLedger.EscrowId);
        if (TradeView.Open) TradeView.Finish(false, fmt, "");
        if (Coop.IsHost)
        {
            _trades.Clear();
            _tradeAsks.Clear();
        }
    }

    // ================================================================ RPCs

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeAskRpc(long target) => HostTradeAsk(TradeSender(), target);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeAnswerRpc(int askId, bool yes) => HostTradeAnswer(TradeSender(), askId, yes);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeSetRpc(int id, string offer, string hold) => HostTradeSet(TradeSender(), id, offer, hold);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeReadyRpc(int id, int version, bool ready) => HostTradeReady(TradeSender(), id, version, ready);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeConfirmRpc(int id, int version) => HostTradeConfirm(TradeSender(), id, version);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeCancelRpc(int id) => HostTradeCancel(TradeSender(), id);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeEscrowedRpc(int id, int version, bool ok, string data) => HostTradeEscrowed(TradeSender(), id, version, ok, data);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeInviteRpc(int askId, string fromName) => OnTradeInvite(askId, fromName);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeInviteGoneRpc(int askId) => OnTradeInviteGone(askId);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeOpenRpc(int id, long a, long b) => OnTradeOpen(id, a, b);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeStateRpc(int id, int version, string packed) => OnTradeState(id, version, packed);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeEscrowRpc(int id, int version, string give, string get) => OnTradeEscrow(id, version, give, get);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeGrantRpc(int id, string incoming) => OnTradeGrant(id, incoming);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeRollbackRpc(int id) => OnTradeRollback(id);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeClosedRpc(int id, string fmt, string arg) => OnTradeClosed(id, fmt, arg);

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void TradeNoteRpc(string fmt, string arg) => OnTradeNote(fmt, arg);
}
