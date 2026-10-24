using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// Host migration: when the host leaves mid-run, a remaining player becomes
/// the host and the run goes on.
///
/// <b>What the clients already have.</b> Every client holds the whole run the
/// host simulates, as puppets: every enemy (id, def, transform, health), every
/// orb, every chest, the map, the boss, the portal and the rest room (the
/// phase flow is replayed on every machine). What only the host knew, it
/// streams in a small reliable <c>RunSnap</c> once a second: the next free
/// enemy, orb and chest ids, the enemy director's schedule (credits, the elite
/// wave clock, the spawn timer), the phase machine's private timers (boss
/// arrival, rest-room countdown, Final Swarm clock, endless loop), and, on
/// ENet, each peer's address.
///
/// <b>Election.</b> Deterministic, with no messages: the remaining player with
/// the lowest lobby slot (slots are unique and every survivor has the same
/// roster). On Steam the lobby owner hands ownership to it
/// (<see cref="SteamLobby.HandOwnershipTo"/>), so lobby data and invites stay
/// with the host.
///
/// <b>The new host</b> opens a listen socket (ENet: the session's port + 1, the
/// pre-agreed fallback port; Steam: a <see cref="SteamPeer"/> host on the same
/// lobby), takes peer id 1, turns its puppets into real AI enemies with the
/// current difficulty, adopts the director and phase timers from the last
/// snapshot, and keeps a slot open for each other survivor for
/// <see cref="RejoinWindow"/> seconds.
///
/// <b>The others</b> wait a moment, then connect to the new host (ENet: its
/// address as the old host saw it, on port + 1; Steam: its Steam id) and say
/// "I was slot N". The new host re-keys their roster entry, avatar and
/// scoreboard line to the new peer id and sends them a full world snapshot
/// (<see cref="SendWorldSnapshot"/>), against which they line their puppets
/// up. Until then their own player keeps moving; only the puppets stand still.
///
/// <b>Fallback.</b> If the new host cannot open its socket, or a survivor
/// cannot reach the new host within <see cref="RejoinWindow"/>, that machine
/// ends its run with the results screen (rewards and records saved as on any
/// run end) and the message; never a crash, never a silent return to the menu.
/// </summary>
public sealed partial class NetSession
{
    /// <summary>Seconds a survivor has to reach the new host.</summary>
    public const double RejoinWindow = 20.0;

    /// <summary>Delay before a survivor first tries the new host, so its
    /// socket is open.</summary>
    private const double ReconnectDelay = 1.0;

    private const float RunSnapInterval = 1f;

    // Run snapshot layout (host -> clients, 1 Hz).
    private const int SnapNextEnemy = 0, SnapNextPickup = 1, SnapNextChest = 2, SnapDirector = 3;
    private const int SnapStage = SnapDirector + EnemyManager.DirectorStateLength;
    private const int SnapLoop = SnapStage + StageDirector.HostTimersLength;
    private const int SnapLength = SnapLoop + 1;

    private float _runSnapTimer;
    private float[] _hostSnap = Array.Empty<float>();
    private readonly Dictionary<long, string> _addresses = new();

    // ENet: the session's port (host: listening; client: joined) and the host's address.
    private int _enetPort = Coop.DefaultPort;
    private string _enetHost = "127.0.0.1";

    // A survivor reconnecting to the new host.
    private bool _migrating;
    private double _migrateDeadline, _reconnectAt;
    private string _migrateAddress = "";
    private ulong _migrateSteamId;
    private int _mySlotBefore = -1;

    // New host: slots kept for survivors on their way (slot -> deadline).
    private readonly Dictionary<int, double> _awaiting = new();
    private double _migrationStartedAt;

    /// <summary>Wall seconds from losing the host to being back in sync (-1: not yet).</summary>
    public double ResyncSeconds { get; private set; } = -1;

    /// <summary>Host migrations this machine lived through.</summary>
    public int Migrations { get; private set; }
    public string LastOldHost { get; private set; } = "";
    public string LastNewHost { get; private set; } = "";
    public bool BecameHost { get; private set; }
    /// <summary>Run clock when the host was lost (-1: never).</summary>
    public float MigratedAt { get; private set; } = -1f;
    /// <summary>Survivors who reached the new host (counted on the new host).</summary>
    public int Rejoins { get; private set; }
    /// <summary>Enemies turned from puppets into real enemies (new host).</summary>
    public int PromotedEnemies { get; private set; }
    /// <summary>Set when migration was impossible and the run was ended.</summary>
    public string MigrationFallback { get; private set; } = "";

    /// <summary>A survivor is between hosts.</summary>
    public bool Migrating => _migrating;

    private static double Now => Time.GetTicksMsec() / 1000.0;

    // ------------------------------------------------------------ run snapshot

    private void StepRunSnapshot(float dt)
    {
        _runSnapTimer -= dt;
        if (_runSnapTimer > 0f || _main is null) return;
        _runSnapTimer = RunSnapInterval;
        if (Multiplayer.GetPeers().Length == 0) return;
        var d = new List<float>(SnapLength) { _nextEnemyId, _nextPickupId, _nextChestId };
        if (EnemyManager.Instance is { } em) em.WriteDirectorState(d);
        else for (int i = 0; i < EnemyManager.DirectorStateLength; i++) d.Add(0f);
        _main.Stages.WriteHostTimers(d);
        d.Add(EndlessMode.Loop);
        Rpc(MethodName.RunSnap, d.ToArray(), PackAddresses());
        PublishLobbyState();
    }

    /// <summary>ENet: "id=ip;..." for every client, as this host sees them.</summary>
    private string PackAddresses()
    {
        if (_peer is not ENetMultiplayerPeer enet) return "";
        var sb = new StringBuilder();
        foreach (int id in Multiplayer.GetPeers())
        {
            ENetPacketPeer? p = enet.GetPeer(id);
            if (p is null) continue;
            sb.Append(id).Append('=').Append(p.GetRemoteAddress()).Append(';');
        }
        return sb.ToString();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RunSnap(float[] d, string addresses)
    {
        if (!Coop.IsClient || d.Length < SnapLength) return;
        _hostSnap = d;
        EnemyManager.Instance?.ReadDirectorState(d, SnapDirector);
        _addresses.Clear();
        foreach (string pair in addresses.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && long.TryParse(pair[..eq], out long id)) _addresses[id] = pair[(eq + 1)..];
        }
    }

    // ------------------------------------------------------------ losing the host

    /// <summary>The host is gone. True when a migration started (or this
    /// machine was moved to the fallback); false leaves the old behaviour to
    /// the caller (lobby: back to the CO-OP page).</summary>
    private bool TryMigrate()
    {
        if (!Coop.IsClient || !Coop.InRun || _main is null || !IsInstanceValid(_main)) return false;
        if (_main.Run.Over) return false;   // the results screen stays
        if (!_go)
        {
            Fallback(Loc.T("The host left the game."));
            return true;
        }
        CoopPeer? oldHost = Coop.Peer(1);
        string oldName = oldHost?.Name ?? Loc.T("The host");
        var survivors = new List<CoopPeer>();
        foreach (CoopPeer p in Coop.Roster) if (p.Id != 1) survivors.Add(p);
        if (_testNoMigrate || Coop.LocalPeer is null || survivors.Count == 0)
        {
            Fallback(Loc.T("The host left the game."));
            return true;
        }

        CoopPeer elected = Elect(survivors);
        bool me = elected.Id == Coop.LocalId;
        ulong electedSteam = ViaSteam && !me ? SteamLobby.SteamIdForPeer(elected.Id) : 0;
        string electedAddress = _addresses.TryGetValue(elected.Id, out string? a) && a.Length > 0 ? a : _enetHost;
        _mySlotBefore = Coop.LocalPeer.Slot;

        CloseSocket();
        DepartOldHost(oldHost);
        Migrations++;
        _migrationStartedAt = Now;
        LastOldHost = oldName;
        LastNewHost = elected.Name;
        MigratedAt = _main.Run.Seconds;
        GD.Print($"[coop] host {oldName} left at t={_main.Run.Clock}; {elected.Name} (slot {elected.Slot}) takes over{(me ? " (this machine)" : "")}");
        UI.CoopToast.Show(me ? Loc.F("{0} left — you are now the host", oldName)
                             : Loc.F("{0} left — {1} is now the host", oldName, elected.Name));

        if (me) return PromoteToHost(survivors);

        if (ViaSteam)
        {
            SteamLobby.HandOwnershipTo(electedSteam);
            if (electedSteam == 0)
            {
                Fallback(Loc.T("Could not reach the new host."));
                return true;
            }
        }
        _migrating = true;
        _migrateSteamId = electedSteam;
        _migrateAddress = electedAddress;
        _migrateDeadline = Now + RejoinWindow;
        _reconnectAt = Now + ReconnectDelay;
        _hits.Clear();
        Status = Loc.F("Reconnecting to {0}...", elected.Name);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Lowest slot first (slot 0 was the host). Every survivor holds
    /// the same roster, so every survivor picks the same player.</summary>
    internal static CoopPeer Elect(List<CoopPeer> survivors)
    {
        CoopPeer best = survivors[0];
        foreach (CoopPeer p in survivors)
            if (p.Slot < best.Slot || (p.Slot == best.Slot && p.Id < best.Id)) best = p;
        return best;
    }

    /// <summary>The old host leaves the roster and the field; its scoreboard
    /// line stays under a departed id (<see cref="Coop.DepartedId"/>).</summary>
    private void DepartOldHost(CoopPeer? oldHost)
    {
        if (oldHost is not null)
        {
            Coop.Roster.Remove(oldHost);
            long departed = Coop.DepartedId(oldHost.Slot);
            Coop.Departed[departed] = oldHost.Name;
            RunScoreboard.Rekey(1, departed);
        }
        _loaded.Remove(1);
        if (_remotes.Remove(1, out RemotePlayer? r) && IsInstanceValid(r)) r.QueueFree();
    }

    // ------------------------------------------------------------ the new host

    private bool PromoteToHost(List<CoopPeer> survivors)
    {
        long oldId = Coop.LocalId;
        if (ViaSteam)
        {
            SteamPeer sp = SteamPeer.CreateHost(new SteamMessagesLink());
            sp.AllowJoin = SteamLobby.IsMember;
            _peer = sp;
        }
        else
        {
            var enet = new ENetMultiplayerPeer();
            int port = _enetPort + 1;
            Error err = enet.CreateServer(port, Coop.MaxPlayers - 1);
            if (err != Error.Ok)
            {
                GD.Print($"[coop] could not open port {port} as the new host ({err})");
                Fallback(Loc.F("Could not take over as the host ({0}).", err.ToString()));
                return true;
            }
            _enetPort = port;
            _peer = enet;
        }
        Multiplayer.MultiplayerPeer = _peer;
        Coop.Mode = CoopMode.Host;
        Coop.LocalId = 1;
        if (Coop.Peer(oldId) is { } self)
        {
            self.Id = 1;
            self.Ready = true;
        }
        RunScoreboard.Rekey(oldId, 1);
        BecameHost = true;
        if (ViaSteam)
        {
            SteamLobby.PublishPeerId(1);
            SteamLobby.HandOwnershipTo(SteamApp.LocalSteamId);
        }

        _awaiting.Clear();
        foreach (CoopPeer p in survivors)
            if (p.Id != 1) _awaiting[p.Slot] = Now + RejoinWindow;
        _loaded.Clear();
        _loaded.Add(1);
        _hits.Clear();

        // The world: puppets become enemies, orbs and chests come under this
        // machine's rule, the director and the phase machine carry on.
        var live = new Dictionary<int, Enemy>();
        int maxEnemy = 0;
        foreach (KeyValuePair<int, Enemy> kv in _enemies)
        {
            if (!IsInstanceValid(kv.Value) || kv.Value.IsDead || kv.Value.NetId != kv.Key) continue;
            live[kv.Key] = kv.Value;
            maxEnemy = Math.Max(maxEnemy, kv.Key);
        }
        _enemies.Clear();
        foreach (KeyValuePair<int, Enemy> kv in live) _enemies[kv.Key] = kv.Value;
        float snapEnemy = _hostSnap.Length >= SnapLength ? _hostSnap[SnapNextEnemy] : 0f;
        _nextEnemyId = Math.Max(maxEnemy + 1, (int)snapEnemy);
        PromotedEnemies = EnemyManager.Instance?.PromotePuppets(_main!.Run.Seconds) ?? 0;

        int maxPickup = Pickup.PromoteToHost();
        float snapPickup = _hostSnap.Length >= SnapLength ? _hostSnap[SnapNextPickup] : 0f;
        _nextPickupId = Math.Max(maxPickup + 1, (int)snapPickup);

        int maxChest = 0;
        _claimedChests.Clear();
        foreach (KeyValuePair<int, Chest> kv in _chests)
        {
            maxChest = Math.Max(maxChest, kv.Key);
            if (IsInstanceValid(kv.Value) && kv.Value.IsOpen) _claimedChests.Add(kv.Key);
        }
        float snapChest = _hostSnap.Length >= SnapLength ? _hostSnap[SnapNextChest] : 0f;
        _nextChestId = Math.Max(maxChest + 1, (int)snapChest);
        ChestSpawner.Instance?.PromoteToHost();

        _main!.Stages.PromoteToHost(_hostSnap, _hostSnap.Length >= SnapLength ? SnapStage : -1);
        _clockTimer = 0f;
        _runSnapTimer = 0f;

        Status = Loc.T("You are the host now.");
        GD.Print($"[coop] migrated: this machine is the host now ({(ViaSteam ? "Steam" : $"port {_enetPort}")}), " +
                 $"{PromotedEnemies} enemies promoted, waiting for {_awaiting.Count} player(s)");
        Changed?.Invoke();
        return true;
    }

    /// <summary>New host: survivors who have not arrived in time are let go.</summary>
    private void StepAwaiting()
    {
        if (_awaiting.Count == 0) return;
        double now = Now;
        var late = new List<int>();
        foreach (KeyValuePair<int, double> kv in _awaiting) if (now > kv.Value) late.Add(kv.Key);
        foreach (int slot in late)
        {
            _awaiting.Remove(slot);
            CoopPeer? p = Coop.Roster.Find(x => x.Slot == slot);
            if (p is null) continue;
            GD.Print($"[coop] {p.Name} did not reconnect in time");
            Coop.Roster.Remove(p);
            PeerLeftRun(p.Id, p.Name);
            BroadcastRoster();
            Changed?.Invoke();
        }
    }

    /// <summary>New host: a survivor (who was slot <paramref name="slot"/>)
    /// reached us as peer <paramref name="id"/>. False when that slot was not
    /// being kept.</summary>
    private bool AcceptRejoin(long id, int slot)
    {
        if (!_awaiting.Remove(slot)) return false;
        CoopPeer? p = Coop.Roster.Find(x => x.Slot == slot);
        if (p is null) return false;
        long oldId = p.Id;
        p.Id = id;
        p.Ready = true;
        if (_remotes.Remove(oldId, out RemotePlayer? r) && IsInstanceValid(r))
        {
            r.PeerId = id;
            _remotes[id] = r;
        }
        RunScoreboard.Rekey(oldId, id);
        _loaded.Add(id);
        Rejoins++;
        GD.Print($"[coop] {p.Name} reconnected as peer {id} (was {oldId})");
        return true;
    }

    // ------------------------------------------------------------ the others

    /// <summary>Per frame: a survivor tries the new host until it answers or
    /// the window closes.</summary>
    private void StepMigration()
    {
        if (Coop.IsHost) StepAwaiting();
        if (!_migrating) return;
        double now = Now;
        if (now > _migrateDeadline)
        {
            _migrating = false;
            Fallback(Loc.T("Could not reach the new host."));
            return;
        }
        if (_peer is not null || now < _reconnectAt) return;
        _reconnectAt = double.MaxValue;   // until this attempt fails
        if (ViaSteam)
        {
            _peer = SteamPeer.CreateClient(new SteamMessagesLink(), _migrateSteamId);
            GD.Print($"[coop] reconnecting to the new host {_migrateSteamId} through Steam");
        }
        else
        {
            var enet = new ENetMultiplayerPeer();
            int port = _enetPort + 1;
            Error err = enet.CreateClient(_migrateAddress, port);
            if (err != Error.Ok)
            {
                _reconnectAt = now + 1.0;
                return;
            }
            _peer = enet;
            GD.Print($"[coop] reconnecting to the new host at {_migrateAddress}:{port}");
        }
        Multiplayer.MultiplayerPeer = _peer;
    }

    /// <summary>A reconnect attempt failed; try again shortly (until the window closes).</summary>
    private void RetryReconnect()
    {
        if (_peer is not null) { _peer.Close(); _peer = null; }
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        _reconnectAt = Now + 1.0;
    }

    /// <summary>Connected to the new host: from here it is a normal session again.</summary>
    private void ReconnectedToNewHost()
    {
        if (!ViaSteam) _enetPort += 1;
        _enetHost = _migrateAddress;
        GD.Print($"[coop] reached the new host as peer {Coop.LocalId}");
    }

    // ------------------------------------------------------------ fallback

    /// <summary>Migration is impossible here: end this machine's run with the
    /// results screen (what was earned is kept, as on any run end) and the
    /// message, and close the session.</summary>
    private void Fallback(string message)
    {
        MigrationFallback = message;
        GD.Print($"[coop] host migration not possible: {message}; ending the run");
        _migrating = false;
        TestMigrationFallback();
        Main? main = _main is not null && IsInstanceValid(_main) ? _main : null;
        // The scoreboard on the results screen: the host's line under its
        // departed id, this player's under 1 (single-player's id from here on).
        if (Coop.IsClient && Coop.Peer(1) is { } host) DepartOldHost(host);
        if (Coop.LocalId != 1) RunScoreboard.Rekey(Coop.LocalId, 1);
        CloseSocket();
        if (ViaSteam) SteamLobby.Leave();
        Coop.Notice = message;
        Status = message;
        EndRunState();
        Coop.Reset();
        Changed?.Invoke();
        if (main is not null && !main.Run.Over)
        {
            GetTree().Paused = false;
            UI.CoopToast.Show(message, low: true);
            main.Run.Finish(survived: false);
        }
        QueueFree();
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    /// <summary>Host: keep the Steam lobby's browse data current (phase, players).</summary>
    private void PublishLobbyState()
    {
        if (!ViaSteam || !Coop.IsHost) return;
        bool inRun = Coop.InRun && _main is not null && IsInstanceValid(_main);
        SteamLobby.PublishState(Coop.Roster.Count, Coop.DifficultyId,
            PublicGames.FormatState(inRun, inRun && _main!.Stages.InRestRoom, inRun ? _main!.Run.Phase : 0, inRun ? _main!.Run.PhaseCount : 0));
    }
}
