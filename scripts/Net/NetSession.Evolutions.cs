using System.Collections.Generic;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// What this machine knows about its teammates' evolved weapons, by peer id.
/// Reset at run start; single-player never writes it.
/// </summary>
public static class EvolutionNet
{
    private static readonly Dictionary<long, HashSet<string>> Evolved = new();

    /// <summary>Evolved-list messages received this run (the co-op test reads it).</summary>
    public static int Received { get; private set; }

    /// <summary>Teammates' rider flashes mirrored here.</summary>
    public static int FxReceived { get; internal set; }

    /// <summary>Most evolutions any teammate reported this run (the test reads
    /// it after the teammate has left).</summary>
    public static int MaxSeen { get; private set; }

    public static void Reset()
    {
        Evolved.Clear();
        Received = FxReceived = MaxSeen = 0;
    }

    public static int CountOf(long peer) => Evolved.TryGetValue(peer, out HashSet<string>? s) ? s.Count : 0;

    /// <summary>Store a peer's list. Returns what is new since its last list; a
    /// peer's first list (a late joiner, or anyone after a migration) announces
    /// nothing, so old news is not toasted as new.</summary>
    internal static List<EvolutionDef> Set(long peer, string packed)
    {
        Received++;
        var fresh = new List<EvolutionDef>();
        bool known = Evolved.TryGetValue(peer, out HashSet<string>? have);
        have ??= new HashSet<string>();
        foreach (string id in packed.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
            if (Evolutions.ById(id) is { } e && have.Add(id) && known) fresh.Add(e);
        Evolved[peer] = have;
        if (have.Count > MaxSeen) MaxSeen = have.Count;
        return fresh;
    }
}

/// <summary>
/// Evolution sync. Weapons belong to their owner's machine (see "Who owns
/// what"): evolved hits reach the host through the ordinary hit batch, so a late
/// joiner and a migrated session lose nothing by construction. What teammates
/// need is to know about it: each player sends its evolved list to everyone
/// (reliable; tiny, at once on a change and every few seconds, the same shape as
/// <see cref="NetSession.SendPerks"/>), which marks that player's puppet, and
/// each rider sends an unreliable flash where it landed.
/// </summary>
public sealed partial class NetSession
{
    private const float EvolutionSyncSeconds = 3f;
    private const double EvolutionFxInterval = 0.15;

    private bool _testCheckEvolve;   // --coop-check-evolve
    private string _evolvedSent = "";
    private float _evolvedTimer;
    private double _evolutionFxAt;

    private static string PackEvolved(WeaponSystem? weapons)
    {
        if (weapons is null) return "";
        var ids = new List<string>();
        foreach (WeaponRuntime w in weapons.Weapons) if (w.Evolution is { } e) ids.Add(e.Id);
        return string.Join(";", ids);
    }

    /// <summary>Per frame while in a run. Sends the list when it changed, and
    /// every few seconds so a late joiner or a new host hears it too.</summary>
    private void StepEvolutions(float dt)
    {
        if (!Coop.InRun || !_go || _migrating || _peer is null || _main is null || !IsInstanceValid(_main)) return;
        string packed = PackEvolved(_main.Weapons);
        _evolvedTimer -= dt;
        if (packed == _evolvedSent && _evolvedTimer > 0f) return;
        _evolvedSent = packed;
        _evolvedTimer = EvolutionSyncSeconds;
        Rpc(MethodName.EvolvedList, packed);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void EvolvedList(string packed)
    {
        long from = Multiplayer.GetRemoteSenderId();
        if (from == 0 || from == Coop.LocalId) return;
        foreach (EvolutionDef e in EvolutionNet.Set(from, packed))
        {
            GD.Print($"[coop] {Coop.NameOf(from)} evolved {e.Id}");
            UI.CoopToast.Show(Loc.F("{0} evolved {1}", Coop.NameOf(from), Loc.T(e.Name)));
        }
        if (_remotes.TryGetValue(from, out RemotePlayer? r) && IsInstanceValid(r)) r.ShowEvolved(EvolutionNet.CountOf(from));
    }

    /// <summary>A rider landed: show teammates a flash there. Rate-limited,
    /// and cosmetic, so a dropped one changes nothing.</summary>
    internal void SendEvolutionFx(Vector3 at, float radius, Color tint)
    {
        if (!_go || Now - _evolutionFxAt < EvolutionFxInterval) return;
        _evolutionFxAt = Now;
        Rpc(MethodName.EvolvedFx, at, radius, tint);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void EvolvedFx(Vector3 at, float radius, Color tint)
    {
        if (!_go || !at.IsFinite()) return;
        EvolutionNet.FxReceived++;
        ExplosionFx.Play(at, Mathf.Clamp(radius, 0.3f, 8f), tint);
    }

    /// <summary>The co-op test's evolution checks (with --coop-check-evolve;
    /// every player runs with an evolved weapon from --give): this player's
    /// weapon is still evolved, and a teammate's list arrived. Riders and their
    /// flashes depend on what the bots meet, so those are only reported.</summary>
    private string EvolutionTestCheck(List<string> problems)
    {
        if (!_testCheckEvolve) return "";
        int mine = _main is not null && IsInstanceValid(_main) ? _main.Weapons.EvolvedCount : 0;
        if (mine == 0) problems.Add("this player's weapon is not evolved");
        if (EvolutionNet.Received == 0) problems.Add("no evolved list arrived from a teammate");
        else if (EvolutionNet.MaxSeen == 0) problems.Add("the teammate's evolved list arrived empty");
        return $"evolved={mine} evolved_msgs={EvolutionNet.Received} evolved_seen={EvolutionNet.MaxSeen} " +
               $"rider_fired={Combat.Weapons.EvolutionRiders.Fired} rider_fx={EvolutionNet.FxReceived}";
    }
}
