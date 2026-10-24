using System;
using System.Collections.Generic;

namespace Wavebreak.Game.Trade;

/// <summary>
/// The trade this machine's player is in, as the host last described it:
/// both offers and holdings, both READY and CONFIRM flags, the version every
/// change bumps, and whether the swap is executing. Written only by
/// Net/NetSession.Trade.cs; read by the trade window. Side 0 is the player
/// who asked, side 1 the one who accepted.
/// </summary>
public static class TradeView
{
    public static int Id { get; internal set; }
    public static int Side { get; internal set; }
    public static long[] Peers { get; } = new long[2];
    public static string[] Names { get; } = { "", "" };
    public static TradeOffer[] Offers { get; } = { new(), new() };
    public static TradeHoldings[] Holds { get; } = { new(), new() };
    public static bool[] Ready { get; } = new bool[2];
    public static bool[] Confirm { get; } = new bool[2];
    public static int Version { get; internal set; }
    public static bool Committing { get; internal set; }
    /// <summary>The host's last word on this trade (English format), e.g.
    /// "Offers changed: READY again." Empty when there is none.</summary>
    public static string Notice { get; internal set; } = "";
    public static string NoticeArg { get; internal set; } = "";

    /// <summary>How many times a change reset a READY that was set (tests).</summary>
    public static int ReadyResets { get; internal set; }

    public static bool Open => Id > 0;
    public static int Other => 1 - Side;
    public static TradeOffer Mine => Offers[Side];
    public static TradeOffer Theirs => Offers[Other];
    public static TradeHoldings MyHold => Holds[Side];
    public static TradeHoldings TheirHold => Holds[Other];
    public static string PartnerName => Names[Other];

    /// <summary>Problems with the current offers, by the shared rules.</summary>
    public static List<TradeProblem> Problems() => TradeRules.Validate(Offers, Holds, Names);

    /// <summary>The window redraws.</summary>
    public static event Action? Changed;

    /// <summary>The trade ended: (completed?, English reason format, argument).</summary>
    public static event Action<bool, string, string>? Ended;

    /// <summary>A teammate asked to trade: (ask id, their name).</summary>
    public static event Action<int, string>? Invited;

    /// <summary>A short line for this player only ("Bob is busy").</summary>
    public static event Action<string, string>? Note;

    public static int InviteId { get; internal set; }
    public static string InviteFrom { get; internal set; } = "";

    // Totals for the tests and the log.
    public static int Completed { get; internal set; }
    public static int Cancelled { get; internal set; }
    public static string LastEnd { get; internal set; } = "";

    internal static void RaiseChanged() => Changed?.Invoke();
    internal static void RaiseInvited(int askId, string from) => Invited?.Invoke(askId, from);
    internal static void RaiseNote(string fmt, string arg) => Note?.Invoke(fmt, arg);

    internal static void Begin(int id, long a, long b, string nameA, string nameB, long me)
    {
        Id = id;
        Peers[0] = a;
        Peers[1] = b;
        Names[0] = nameA;
        Names[1] = nameB;
        Side = me == b ? 1 : 0;
        for (int i = 0; i < 2; i++)
        {
            Offers[i] = new TradeOffer();
            Holds[i] = new TradeHoldings();
            Ready[i] = Confirm[i] = false;
        }
        Version = 0;
        Committing = false;
        Notice = NoticeArg = "";
        InviteId = 0;
        Changed?.Invoke();
    }

    internal static void Finish(bool completed, string reason, string arg)
    {
        if (!Open) return;
        Id = 0;
        Committing = false;
        if (completed) Completed++; else Cancelled++;
        LastEnd = completed ? "done" : reason;
        Ended?.Invoke(completed, reason, arg);
        Changed?.Invoke();
    }

    /// <summary>A new run or a closed session.</summary>
    public static void Reset()
    {
        Id = 0;
        InviteId = 0;
        Committing = false;
        Completed = Cancelled = ReadyResets = 0;
        LastEnd = "";
    }
}
