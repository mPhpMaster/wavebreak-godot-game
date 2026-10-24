using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Net;

/// <summary>
/// What this machine knows about its teammates' team perks: their Crew Beacon
/// and Lifeline levels, by peer id. Filled by <c>NetSession.PerkLevels</c>,
/// reset at run start. Single-player never writes it, so every lookup is 0.
/// </summary>
public static class PerkNet
{
    private static readonly Dictionary<long, (int Beacon, int Lifeline)> Levels = new();

    /// <summary>Perk-level messages received this run (the co-op test reads it).</summary>
    public static int Received { get; private set; }

    /// <summary>Revives this player got that a teammate's Lifeline sped up.</summary>
    public static int BoostedRevives { get; internal set; }

    /// <summary>Highest levels any teammate reported this run (the test reads
    /// them after the teammate has left).</summary>
    public static int MaxBeaconSeen { get; private set; }
    public static int MaxLifelineSeen { get; private set; }

    public static void Reset()
    {
        Levels.Clear();
        MaxBeaconSeen = MaxLifelineSeen = 0;
        Received = 0;
        BoostedRevives = 0;
    }

    internal static void Set(long peer, int beacon, int lifeline)
    {
        Levels[peer] = (beacon, lifeline);
        Received++;
        if (beacon > MaxBeaconSeen) MaxBeaconSeen = beacon;
        if (lifeline > MaxLifelineSeen) MaxLifelineSeen = lifeline;
    }

    public static int BeaconOf(long peer) => Levels.TryGetValue(peer, out var l) ? l.Beacon : 0;

    public static int LifelineOf(long peer) => Levels.TryGetValue(peer, out var l) ? l.Lifeline : 0;

    /// <summary>Revive progress multiplier when <paramref name="helper"/> stands over you.</summary>
    public static float ReviveRate(long helper)
    {
        int lv = LifelineOf(helper);
        return lv > 0 && Perks.ById(Perks.Lifeline) is { } d ? 1f + d.V(0, lv) / 100f : 1f;
    }

    /// <summary>Extra revive reach in metres for <paramref name="helper"/>.</summary>
    public static float ReviveReach(long helper)
    {
        int lv = LifelineOf(helper);
        return lv > 0 && Perks.ById(Perks.Lifeline) is { } d ? d.V(1, lv) : 0f;
    }
}

/// <summary>
/// Team-perk sync: each player sends its own Crew Beacon and Lifeline levels
/// to everyone (reliable; tiny, every few seconds and on a change). The host
/// relays like any other client-to-client RPC. See Combat/Weapons/PerkProcs.Coop.cs.
/// </summary>
public sealed partial class NetSession
{
    private bool _testCheckPerks;   // --coop-check-perks

    public void SendPerks(int beacon, int lifeline)
    {
        if (!Coop.InRun || Multiplayer.MultiplayerPeer is null) return;
        Rpc(MethodName.PerkLevels, beacon, lifeline);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PerkLevels(int beacon, int lifeline)
    {
        long from = Multiplayer.GetRemoteSenderId();
        if (from == 0 || from == Coop.LocalId) return;
        PerkNet.Set(from, Mathf.Clamp(beacon, 0, 10), Mathf.Clamp(lifeline, 0, 10));
    }

    /// <summary>The co-op test's perk checks (with --coop-check-perks; the host
    /// holds Crew Beacon and Lifeline, the client Lifeline): the teammate's
    /// levels arrived; a player whose teammate has a stronger beacon than its
    /// own stood in it; and (client) the host's Lifeline sped up the revive.</summary>
    private string PerkTestCheck(string role, List<string> problems)
    {
        if (!_testCheckPerks) return "";
        int remoteBeacon = PerkNet.MaxBeaconSeen, remoteLifeline = PerkNet.MaxLifelineSeen;
        int ownBeacon = Progression.TomeBook.Current?.LevelOf(Perks.Beacon) ?? 0;
        float teamBeacon = Combat.Weapons.PerkProcs.TeamBeaconSeconds;
        if (PerkNet.Received == 0) problems.Add("no perk levels arrived from the teammate");
        else if (remoteBeacon == 0 && remoteLifeline == 0) problems.Add("teammate's perk levels arrived as 0");
        if (remoteBeacon > ownBeacon && teamBeacon < 0.5f) problems.Add($"never stood in the teammate's beacon ({teamBeacon:0.0}s)");
        if (remoteBeacon <= ownBeacon && teamBeacon > 0f) problems.Add("a weaker or equal team beacon was applied over this player's own");
        if (role == "client" && _testDowned && PerkNet.BoostedRevives == 0) problems.Add("the revive was not sped up by the host's Lifeline");
        return $"perk_msgs={PerkNet.Received} remote_beacon={remoteBeacon} remote_lifeline={remoteLifeline} " +
               $"team_beacon_s={teamBeacon:0.0} boosted_revives={PerkNet.BoostedRevives}";
    }
}
