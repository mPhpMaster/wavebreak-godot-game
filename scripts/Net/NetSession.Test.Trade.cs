using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Game.Trade;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Net;

/// <summary>
/// Automated swap-meet checks (tests/coop.ps1, scenarios coop-trade,
/// coop-trade-ar, coop-trade-timeout). Both bots stand at the stall instead
/// of shopping while the trade runs.
///
///   --coop-trade-test      the host asks the client to trade. The host
///                          offers 40 gold and one stat item, the client one
///                          of its tomes; both press READY; then the host
///                          raises the gold to 50, which must reset both
///                          READY flags; both READY again, the windows are
///                          photographed (--coop-trade-shot=PNG), both
///                          CONFIRM. Each side checks its own inventory,
///                          gold, tome book and stat sheet afterwards, and
///                          prints its gold before and after so the script
///                          can check that the team's total did not change.
///   --coop-trade-timeout   the client asks the host; both offer and READY;
///                          the host's bot stands in the CONTINUE portal, so
///                          the rest room's countdown (--rest-exit-seconds)
///                          ends the room with the trade open. Both windows
///                          must close with "The rest room ended", nothing
///                          may change hands, and the run goes on to map 2.
///   --coop-trade-invite-shot=PNG  the invite, on the side that gets it.
/// </summary>
public sealed partial class NetSession
{
    private bool _ttArgs, _ttMain, _ttTimeout;
    private string? _ttShot, _ttInviteShot;
    private double _ttRoomAt = -1, _ttOpenAt = -1, _ttAcceptAt = -1, _ttReadyAt, _ttConfirmAt = -1, _ttBothReadyAt = -1;
    private bool _ttAsked, _ttOffered, _ttChanged, _ttShotTaken, _ttInviteShotTaken, _ttConfirmed, _ttEnded;
    private TradeHoldings? _ttBefore, _ttAfter;
    private string _ttItem = "", _ttTome = "";
    private int _ttTomeLevel;
    private float _ttItemStatBefore, _ttItemStatAfter, _ttTomeStatBefore, _ttTomeStatAfter;
    private Stat _ttItemStat, _ttTomeStat;
    private TradeOffer? _ttGot;
    private string _ttEndReason = "";
    private int _ttTotal;

    private bool TradeTesting => _ttMain || _ttTimeout;

    private void ParseTradeTest()
    {
        if (_ttArgs) return;
        _ttArgs = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--coop-trade-test") _ttMain = true;
            else if (arg == "--coop-trade-timeout") _ttTimeout = true;
            else if (arg.StartsWith("--coop-trade-shot=")) _ttShot = arg["--coop-trade-shot=".Length..];
            else if (arg.StartsWith("--coop-trade-invite-shot=")) _ttInviteShot = arg["--coop-trade-invite-shot=".Length..];
        }
        if (TradeTesting) TradeView.Ended += OnTestTradeEnded;
    }

    private void OnTestTradeEnded(bool completed, string reason, string arg)
    {
        _ttEnded = true;
        _ttEndReason = completed ? "done" : reason;
        _ttAfter = TradeLedger.Snapshot();
        if (Player.Instance is { } p)
        {
            _ttItemStatAfter = p.Stats.Get(_ttItemStat);
            _ttTomeStatAfter = p.Stats.Get(_ttTomeStat);
        }
        GD.Print($"[trade-test] ended completed={completed} reason=\"{reason}\" holdings {_ttAfter.Pack()}");
    }

    /// <summary>An invite arrived: accept it shortly (the invite is photographed first).</summary>
    private void TestTradeInvited(int askId)
    {
        if (!_testMode || !TradeTesting) return;
        _ttAcceptAt = Now + 1.2;
    }

    private Stat _ttGotStat;
    private float _ttGotStatBefore, _ttGotStatAfter;

    /// <summary>Around <see cref="TradeLedger.Settle"/>: the stat of what
    /// arrived, before and after, so the check can see the sheet move.</summary>
    private void TestTradeSettled(TradeOffer got, bool before)
    {
        if (!_testMode || !TradeTesting || Player.Instance is not { } p) return;
        if (before)
        {
            _ttGot = got;
            foreach (string id in got.Tomes.Keys) if (Data.Tomes.ById(id) is { } t) _ttGotStat = t.Stat;
            foreach (string id in got.Items.Keys) if (Catalog.ItemById(id) is { Effects.Count: > 0 } d && d.Effects[0] is { } e) _ttGotStat = e.Stat;
            _ttGotStatBefore = p.Stats.Get(_ttGotStat);
        }
        else _ttGotStatAfter = p.Stats.Get(_ttGotStat);
    }

    /// <summary>Per frame in a test run (from ProcessTest).</summary>
    private void StepTradeTest()
    {
        ParseTradeTest();
        if (!TradeTesting || _main is null || !IsInstanceValid(_main)) return;
        if (_main.Stages is not { InRestRoom: true, Rest: { } room })
        {
            Autopilot.RestSteer = null;
            return;
        }
        double now = Now;
        if (_ttRoomAt < 0) _ttRoomAt = now;

        // Steering: everyone at the stall while the trade runs; in the
        // timeout check the host waits in the portal and the client stays put.
        Vector3 stall = room.Trader?.GlobalPosition + new Vector3(0f, 0f, 2.4f) ?? RestRoom.SpawnPoint;
        Vector3 portal = room.ExitPosition;
        bool done = _ttEnded && _ttMain;
        Autopilot.RestSteer = done ? null : me =>
        {
            Vector3 goal = _ttTimeout && Coop.IsHost && _ttOpenAt > 0 ? portal : stall + Coop.SlotOffset(Coop.LocalPeer?.Slot ?? 0) * 0.6f;
            Vector3 d = goal - me;
            d.Y = 0f;
            return d.Length() > 0.5f ? d.Normalized() : Vector3.Zero;
        };

        // The invite: photograph it, then accept.
        if (TradeView.InviteId != 0 && _ttInviteShot is not null && !_ttInviteShotTaken && _ttAcceptAt > 0 && now >= _ttAcceptAt - 0.6)
        {
            _ttInviteShotTaken = true;
            _ = Screenshot.Capture(GetTree(), this, _ttInviteShot);
        }
        if (TradeView.InviteId != 0 && _ttAcceptAt > 0 && now >= _ttAcceptAt)
        {
            _ttAcceptAt = -1;
            GD.Print("[trade-test] accepting the invite");
            TradeRespond(true);
        }

        // Ask: the host in the main check, the client in the timeout check.
        bool asker = _ttMain ? Coop.IsHost : Coop.IsClient;
        if (asker && !_ttAsked && now - _ttRoomAt > 2.0 && TradePartners().Count > 0)
        {
            _ttAsked = true;
            GD.Print($"[trade-test] asking {TradePartners()[0].Name} to trade");
            TradeRequest(TradePartners()[0].Id);
        }

        if (!TradeView.Open) return;
        if (_ttOpenAt < 0) _ttOpenAt = now;
        if (now - _ttOpenAt < 1.0) return;   // let both holdings arrive

        if (!_ttOffered) { TestMakeOffer(); return; }
        if (TradeView.Committing || TradeDraftPending) return;

        int me2 = TradeView.Side;
        bool both = !TradeView.Mine.IsEmpty && !TradeView.Theirs.IsEmpty;
        if (both && !TradeView.Ready[me2] && now >= _ttReadyAt && TradeView.Problems().Count == 0)
        {
            _ttReadyAt = now + 0.5;
            GD.Print($"[trade-test] READY (v{TradeView.Version})");
            TradeSetReady(true);
            return;
        }
        if (!(TradeView.Ready[0] && TradeView.Ready[1])) { _ttBothReadyAt = -1; return; }
        if (_ttBothReadyAt < 0) _ttBothReadyAt = now;

        // The mid-trade change: the host raises its gold once both are READY.
        if (_ttMain && Coop.IsHost && !_ttChanged && now - _ttBothReadyAt > 0.6)
        {
            _ttChanged = true;
            TradeOffer o = TradeDraft.Clone();
            o.Gold = 50;
            GD.Print("[trade-test] changing the offer while both are READY: 40 -> 50 gold");
            TradeSetOffer(o);
            return;
        }
        bool settledOffers = _ttMain && (Coop.IsHost ? _ttChanged : TradeView.ReadyResets > 0);
        if (!settledOffers) return;   // the timeout check never confirms

        if (_ttShot is not null && !_ttShotTaken && now - _ttBothReadyAt > 0.8)
        {
            _ttShotTaken = true;
            _ttConfirmAt = now + 1.0;
            _ = Screenshot.Capture(GetTree(), this, _ttShot);
            return;
        }
        if (!_ttConfirmed && (_ttShot is null || _ttConfirmAt > 0) && now >= _ttConfirmAt && now - _ttBothReadyAt > 0.8)
        {
            _ttConfirmed = true;
            GD.Print($"[trade-test] CONFIRM (v{TradeView.Version})");
            TradeConfirm();
        }
    }

    /// <summary>Make sure there is something to give, note the before state,
    /// and offer it: the host 40 gold and a stat item the client can hold,
    /// the client a tome the host does not have (learned now if needed).</summary>
    private void TestMakeOffer()
    {
        _ttOffered = true;
        Inventory? inv = Inventory.Current;
        TomeBook? book = TomeBook.Current;
        Player? p = Player.Instance;
        if (inv is null || book is null || p is null) return;
        TradeHoldings theirs = TradeView.TheirHold;
        var offer = new TradeOffer();
        if (Coop.IsHost)   // the host gives the item in both checks
        {
            if (inv.Gold < 60) inv.TradeGold(60 - inv.Gold);
            ItemDef? pick = null;
            foreach (ItemStack s in inv.Items)
                if (s.Def.Effects.Count > 0 && TradeRules.RoomFor(s.Def, theirs, TradeView.Theirs, offer) > 0) { pick = s.Def; break; }
            if (pick is null)
                foreach (ItemDef d in Catalog.Items)
                    if (d.Effects.Count > 0 && d.Effects[0] is not null && TradeRules.RoomFor(d, theirs, TradeView.Theirs, offer) > 0 && inv.Add(d)) { pick = d; break; }
            if (pick is not null)
            {
                _ttItem = pick.Id;
                _ttItemStat = pick.Effects[0].Stat;
                offer.Items[pick.Id] = 1;
            }
            offer.Gold = _ttMain ? 40 : 0;
        }
        else
        {
            if (_ttMain)
            {
                TomeDef? pick = null;
                foreach (TomeDef t in book.Held)
                    if (TradeRules.TomeBlock(t, theirs, TradeView.Theirs, offer) is null) { pick = t; break; }
                if (pick is null && !book.IsFull)
                    foreach (TomeDef t in Data.Tomes.All)
                        if (book.LevelOf(t) == 0 && TradeRules.TomeBlock(t, theirs, TradeView.Theirs, offer) is null && book.Add(t))
                        {
                            book.Add(t);   // level 2, so the level travels too
                            pick = t;
                            break;
                        }
                if (pick is not null)
                {
                    _ttTome = pick.Id;
                    _ttTomeLevel = book.LevelOf(pick);
                    _ttTomeStat = pick.Stat;
                    offer.Tomes[pick.Id] = _ttTomeLevel;
                }
            }
            else
            {
                if (inv.Gold < 10) inv.TradeGold(10 - inv.Gold);
                offer.Gold = 10;
            }
        }
        // The other side's goods decide the stats to watch on this side too.
        _ttBefore = TradeLedger.Snapshot();
        _ttTotal = _ttBefore.Gold;
        _ttItemStatBefore = p.Stats.Get(_ttItemStat);
        _ttTomeStatBefore = p.Stats.Get(_ttTomeStat);
        GD.Print($"[trade-test] offering {offer.Pack()}  holdings {_ttBefore.Pack()}");
        TradeSetOffer(offer);
    }

    /// <summary>The COOP line for a trade check, or null when this is not one.</summary>
    private string? TradeTestLine(string role, List<string> problems, int phase)
    {
        ParseTradeTest();
        if (!TradeTesting) return null;
        TradeHoldings b = _ttBefore ?? new TradeHoldings(), a = _ttAfter ?? new TradeHoldings();
        if (phase < 2) problems.Add($"only reached phase {phase}");
        if (TradeLedger.EscrowId != 0) problems.Add("an escrow is still outstanding");
        if (_ttBefore is null) problems.Add("the trade window never opened");
        if (!_ttEnded) problems.Add("the trade never ended");
        string fields;
        if (_ttMain)
        {
            if (TradeView.Completed != 1) problems.Add($"completed={TradeView.Completed}");
            if (TradeView.ReadyResets < 1) problems.Add("changing the offer did not reset READY");
            if (_ttShot is not null && !_ttShotTaken) problems.Add("no trade window screenshot");
            if (Coop.IsHost)
            {
                if (TradesDone != 1) problems.Add($"host executed {TradesDone} swaps");
                if (a.Gold != b.Gold - 50) problems.Add($"gold {b.Gold} -> {a.Gold}, wanted -50");
                if (_ttItem.Length == 0) problems.Add("no item to give");
                else if (a.ItemCount(_ttItem) != b.ItemCount(_ttItem) - 1) problems.Add($"{_ttItem} {b.ItemCount(_ttItem)} -> {a.ItemCount(_ttItem)}");
                if (_ttGot is null || _ttGot.Tomes.Count != 1) problems.Add("no tome received");
                else
                {
                    foreach (var (id, level) in _ttGot.Tomes)
                    {
                        if (a.TomeLevel(id) != level || b.TomeLevel(id) != 0) problems.Add($"tome {id}: Lv{b.TomeLevel(id)} -> Lv{a.TomeLevel(id)}, sent Lv{level}");
                        if (Mathf.IsEqualApprox(_ttGotStatAfter, _ttGotStatBefore)) problems.Add($"received {id} but {_ttGotStat} did not change");
                        _ttTome = $"{id}:{level}";
                    }
                }
                if (_ttItemStat != _ttGotStat && Mathf.IsEqualApprox(_ttItemStatAfter, _ttItemStatBefore)) problems.Add($"gave {_ttItem} but {_ttItemStat} did not change");
            }
            else
            {
                if (a.Gold != b.Gold + 50) problems.Add($"gold {b.Gold} -> {a.Gold}, wanted +50");
                if (_ttGot is null || _ttGot.Items.Count != 1) problems.Add("no item received");
                else foreach (var (id, n) in _ttGot.Items)
                {
                    if (a.ItemCount(id) != b.ItemCount(id) + n) problems.Add($"item {id}: {b.ItemCount(id)} -> {a.ItemCount(id)}, got {n}");
                    _ttItem = id;
                }
                if (_ttGot is not null && Mathf.IsEqualApprox(_ttGotStatAfter, _ttGotStatBefore)) problems.Add($"received {_ttItem} but {_ttGotStat} did not change");
                if (_ttTome.Length == 0) problems.Add("no tome to give");
                else if (a.TomeLevel(_ttTome) != 0) problems.Add($"still holds {_ttTome} after giving it");
                if (_ttTomeStat != _ttGotStat && Mathf.IsEqualApprox(_ttTomeStatAfter, _ttTomeStatBefore)) problems.Add($"gave {_ttTome} but {_ttTomeStat} did not change");
            }
            fields = $"trade=done item={_ttItem} tome={_ttTome} ready_resets={TradeView.ReadyResets} swaps={TradesDone}";
        }
        else
        {
            if (TradeView.Completed != 0) problems.Add("a trade completed");
            if (TradeView.Cancelled < 1) problems.Add("the open trade was not cancelled");
            if (!_ttEndReason.StartsWith("The rest room ended")) problems.Add($"ended by '{_ttEndReason}'");
            if (_ttBefore is not null && _ttAfter is not null && a.Pack() != b.Pack()) problems.Add($"holdings changed: {b.Pack()} -> {a.Pack()}");
            if (Coop.IsHost && _main?.Stages.RestExitReason != "timeout") problems.Add($"rest room left by '{_main?.Stages.RestExitReason}'");
            fields = $"trade=cancelled reason=\"{_ttEndReason}\" closed={TradesClosed} rest_exit={_main?.Stages.RestExitReason ?? "-"}";
        }
        return $"role={role} {fields} gold_before={b.Gold} gold_after={a.Gold} escrow={TradeLedger.EscrowId} refunds={TradeLedger.Refunds} " +
               $"phase={phase} players={System.Math.Max(PeakPlayers, Coop.Roster.Count)} t={_main?.Run.Clock ?? "-"}";
    }
}
