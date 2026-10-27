using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// The co-op session: one node at <c>/root/NetSession</c> on every peer, so
/// every RPC has the same path everywhere. It outlives scene changes (title ->
/// run -> title) and exists only while a session does — single-player never
/// creates it.
///
/// Built on Godot's high-level multiplayer (RPCs over a MultiplayerPeer). The
/// peer is a <see cref="SteamPeer"/> for players (Steam lobby, Valve relay) or
/// an ENetMultiplayerPeer for the developer direct-IP path and the automated
/// test; nothing above the peer differs. This file is the connection and the
/// lobby; the run's traffic is in the other partials:
///   NetSession.Run.cs    players, down/revive, start/end, clock
///   NetSession.World.cs  enemies, hits, enemy attacks, pickups, chests, stage
///   NetSession.Test.cs   the automated two-process test
/// </summary>
public sealed partial class NetSession : Node
{
    public static NetSession? Instance { get; private set; }

    /// <summary>Lobby or session state changed; the CO-OP page redraws.</summary>
    public static event Action? Changed;

    /// <summary>Human-readable state line for the CO-OP page ("Hosting on port
    /// 7777", "Connecting...").</summary>
    public string Status { get; private set; } = "";

    private MultiplayerPeer? _peer;

    /// <summary>The session runs over Steam (lobby + <see cref="SteamPeer"/>), not ENet.</summary>
    public bool ViaSteam { get; private set; }

    /// <summary>Steam id behind a peer id, as far as the transport knows it.</summary>
    public ulong SteamIdOfPeer(long peerId) => _peer is SteamPeer sp ? sp.SteamIdOf(peerId) : 0;
    private double _connectStarted = -1;
    private const double ConnectTimeout = 8.0;

    // ------------------------------------------------------------ lifecycle

    /// <summary>The live session node, created on demand under the tree root.
    /// Added deferred: the caller is usually a scene's _Ready, when the root
    /// refuses new children.</summary>
    public static NetSession Ensure(SceneTree tree)
    {
        if (Instance is not null && IsInstanceValid(Instance)) return Instance;
        var s = new NetSession { Name = "NetSession" };
        Instance = s;
        tree.Root.CallDeferred(Node.MethodName.AddChild, s);
        return s;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnected;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        _pendingStart?.Invoke();
        _pendingStart = null;
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnected;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    private Action? _pendingStart;

    /// <summary>Run <paramref name="a"/> once the node is in the tree (now, if it is).</summary>
    private void WhenReady(Action a)
    {
        if (IsInsideTree()) a();
        else _pendingStart += a;
    }

    // ------------------------------------------------------------ host / join

    /// <summary>Developer path: open a lobby on <paramref name="port"/> over
    /// ENet (direct IP). The host is player 1.</summary>
    public void Host(int port)
    {
        WhenReady(() =>
        {
            CloseSocket();
            var enet = new ENetMultiplayerPeer();
            Error err = enet.CreateServer(port, Coop.MaxPlayers - 1);
            if (err != Error.Ok)
            {
                Fail(Loc.F("Could not host on port {0} ({1}).", port, err.ToString()));
                return;
            }
            ViaSteam = false;
            _enetPort = port;
            BecomeHost(enet, Loc.F("Hosting on port {0}. Waiting for players...", port));
            GD.Print($"[coop] hosting on port {port}");
        });
    }

    /// <summary>Host in the Steam lobby just created (<see cref="SteamLobby"/>):
    /// lobby members connect through Valve's relay.</summary>
    public void HostSteam()
    {
        WhenReady(() =>
        {
            CloseSocket();
            ViaSteam = true;
            PlayerName = CleanName(SteamApp.PersonaName);
            SteamPeer sp = SteamPeer.CreateHost(new SteamMessagesLink());
            sp.AllowJoin = SteamLobby.IsMember;
            BecomeHost(sp, Loc.T("Lobby open. Invite friends with INVITE FRIENDS or from your Steam friends list."));
            SteamLobby.PublishPeerId(1);
            GD.Print($"[coop] hosting Steam lobby {SteamLobby.LobbyId}");
        });
    }

    private void BecomeHost(MultiplayerPeer peer, string status)
    {
        _peer = peer;
        Multiplayer.MultiplayerPeer = peer;
        Coop.Mode = CoopMode.Host;
        Coop.LocalId = 1;
        Coop.Roster.Clear();
        CoopPeer me = LocalProfile();
        me.Id = 1;
        me.Slot = 0;
        me.Ready = true;
        Coop.Roster.Add(me);
        // The lobby's difficulty starts as the host's own choice (PLAY page / CO-OP page).
        Coop.DifficultyId = _testDifficulty ?? DifficultyTiers.Selected.Id;
        Status = status;
        Changed?.Invoke();
    }

    /// <summary>Connect to the owner of the Steam lobby just joined. The
    /// transport has its own connect timeout (the relay can take a few seconds).</summary>
    public void JoinSteam(ulong hostSteamId, string hostName)
    {
        WhenReady(() =>
        {
            CloseSocket();
            ViaSteam = true;
            PlayerName = CleanName(SteamApp.PersonaName);
            _peer = SteamPeer.CreateClient(new SteamMessagesLink(), hostSteamId);
            Multiplayer.MultiplayerPeer = _peer;
            Coop.Mode = CoopMode.Client;
            Coop.Roster.Clear();
            Status = Loc.F("Connecting to {0} through Steam...", hostName.Length > 0 ? hostName : hostSteamId.ToString());
            GD.Print($"[coop] joining {hostSteamId} through Steam");
            Changed?.Invoke();
        });
    }

    /// <summary>Connect to a host at "ip:port" (port defaults to 7777).</summary>
    public void Join(string address)
    {
        WhenReady(() =>
        {
            if (!ParseAddress(address, out string host, out int port))
            {
                Fail(Loc.T("Enter an address like 192.168.1.20:7777."));
                return;
            }
            CloseSocket();
            ViaSteam = false;
            var enet = new ENetMultiplayerPeer();
            Error err = enet.CreateClient(host, port);
            if (err != Error.Ok)
            {
                Fail(Loc.F("Could not connect to {0} ({1}).", $"{host}:{port}", err.ToString()));
                return;
            }
            _peer = enet;
            _enetPort = port;
            _enetHost = host;
            Multiplayer.MultiplayerPeer = _peer;
            Coop.Mode = CoopMode.Client;
            Coop.Roster.Clear();
            _connectStarted = Time.GetTicksMsec() / 1000.0;
            Status = Loc.F("Connecting to {0}...", $"{host}:{port}");
            GD.Print($"[coop] joining {host}:{port}");
            Changed?.Invoke();
        });
    }

    public static bool ParseAddress(string text, out string host, out int port)
    {
        host = "";
        port = Coop.DefaultPort;
        text = (text ?? "").Trim();
        if (text.Length == 0) return false;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon)
        {
            if (!int.TryParse(text[(colon + 1)..], out port) || port is < 1 or > 65535) return false;
            text = text[..colon];
        }
        host = text.Trim();
        return host.Length > 0;
    }

    /// <summary>Leave the session (both roles) and go back to single-player
    /// state. Safe to call twice.</summary>
    public void Leave()
    {
        bool wasActive = Coop.Active;
        TradeLocalEnd("The trade was cancelled.");   // NetSession.Trade.cs
        EndRunState();
        CloseSocket();
        if (ViaSteam) SteamLobby.Leave();
        Coop.Reset();
        Status = "";
        if (wasActive) GD.Print("[coop] session closed");
        Changed?.Invoke();
        QueueFree();
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    private void CloseSocket()
    {
        if (_peer is not null)
        {
            _peer.Close();
            _peer = null;
        }
        if (IsInsideTree()) Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        _connectStarted = -1;
    }

    private void Fail(string message)
    {
        GD.Print($"[coop] {message}");
        Coop.Notice = message;
        CloseSocket();
        if (ViaSteam) SteamLobby.Leave();
        Coop.Reset();
        Status = message;
        Changed?.Invoke();
        TestFail(message);
    }

    public override void _Process(double delta)
    {
        if (_connectStarted > 0 && Time.GetTicksMsec() / 1000.0 - _connectStarted > ConnectTimeout)
        {
            _connectStarted = -1;
            Fail(Loc.T("No answer from the host. Check the address, the port and the firewall."));
        }
        StepMigration();   // NetSession.Migration.cs
        ProcessRun((float)delta);
        StepTrade((float)delta);   // NetSession.Trade.cs
        ProcessTest((float)delta);
    }

    // ------------------------------------------------------------ connection events

    private void OnPeerConnected(long id)
    {
        GD.Print($"[coop] peer {id} connected");
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"[coop] peer {id} disconnected");
        if (!Coop.IsHost) return;
        string name = Coop.NameOf(id);
        Coop.Roster.RemoveAll(p => p.Id == id);
        BroadcastRoster();
        TradePeerLeft(id, name);   // NetSession.Trade.cs: their trades end, escrows go home
        PeerLeftRun(id, name);
        Changed?.Invoke();
    }

    private void OnConnected()
    {
        _connectStarted = -1;
        Coop.LocalId = Multiplayer.GetUniqueId();
        // After a host migration: "I was slot N" (NetSession.Migration.cs).
        int prevSlot = -1;
        if (_migrating)
        {
            ReconnectedToNewHost();
            prevSlot = _mySlotBefore;
        }
        CoopPeer me = LocalProfile();
        if (!Coop.InRun) Status = Loc.T("Connected. Waiting for the host...");
        GD.Print($"[coop] connected as {Coop.LocalId}");
        if (ViaSteam) SteamLobby.PublishPeerId(Coop.LocalId);
        RpcId(1, MethodName.Hello, Coop.Protocol, me.Name, me.CharacterId, me.SkinId, me.HatId, prevSlot);
        Changed?.Invoke();
    }

    private void OnConnectionFailed()
    {
        if (_migrating) { RetryReconnect(); return; }
        _connectStarted = -1;
        if (_peer is SteamPeer sp)
            Fail(sp.EndReason == "refused" ? Loc.T("That game is full.") : Loc.T("Could not reach the host through Steam."));
        else
            Fail(Loc.T("Could not reach the host."));
    }

    private void OnServerDisconnected()
    {
        GD.Print("[coop] host disconnected");
        TradeLocalEnd("The host left. The trade was cancelled.");   // NetSession.Trade.cs
        if (_migrating) { RetryReconnect(); return; }   // the new host went too: keep trying until the window closes
        // Mid-run: a remaining player takes over (NetSession.Migration.cs).
        if (TryMigrate()) return;
        bool inRun = Coop.InRun;
        bool runOver = Game.Main.Instance?.Run is { Over: true };
        string message = Loc.T("The host left the game.");
        TestHostLeft();
        CloseSocket();
        if (ViaSteam) SteamLobby.Leave();
        Coop.Reset();
        Coop.Notice = message;
        Status = message;
        Changed?.Invoke();
        if (inRun && !runOver && IsInsideTree())
        {
            UI.CoopPage.OpenOnTitle = true;
            GetTree().Paused = false;
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, UI.MainMenu.TitleScene);
        }
        QueueFree();
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    // ------------------------------------------------------------ lobby

    /// <summary>This machine's player as the lobby shows it: the name typed on
    /// the CO-OP page and the character and skin chosen on the PLAY page.</summary>
    public static CoopPeer LocalProfile()
    {
        Profile.EnsureLoaded();
        CharacterDef c = Characters.ForThisRun();
        SkinDef skin = Launch.Automated ? c.Skins[0] : c.SelectedSkin;
        string hat = Launch.Automated ? TestHat : Hats.SelectedFor(c.Id).Id;
        return new CoopPeer
        {
            Id = Coop.LocalId,
            Name = CleanName(PlayerName),
            CharacterId = c.Id,
            SkinId = skin.Id,
            HatId = hat,
        };
    }

    /// <summary>The name this machine plays under.</summary>
    public static string PlayerName { get; set; } = DefaultName();

    private static string DefaultName()
    {
        string n = OS.GetEnvironment("USERNAME");
        if (string.IsNullOrWhiteSpace(n)) n = OS.GetEnvironment("USER");
        return CleanName(string.IsNullOrWhiteSpace(n) ? "Player" : n);
    }

    public static string CleanName(string? name)
    {
        var sb = new StringBuilder();
        foreach (char ch in (name ?? "").Trim())
            if (!char.IsControl(ch) && ch != '|' && ch != '\n') sb.Append(ch);
        string s = sb.ToString().Trim();
        if (s.Length > 16) s = s[..16];
        return s.Length == 0 ? "Player" : s;
    }

    /// <summary>A player introduces themselves. <paramref name="prevSlot"/> is
    /// the slot they held before a host migration (-1 otherwise); mid-run, a
    /// new player is a late joiner (NetSession.Snapshot.cs).</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(string protocol, string name, string characterId, string skinId, string hatId, int prevSlot)
    {
        if (!Coop.IsHost) return;
        long id = Multiplayer.GetRemoteSenderId();
        if (protocol != Coop.Protocol)
        {
            RefusePeer(id, Loc.T("Different game version."));
            return;
        }
        CoopPeer? p = Coop.Peer(id);
        bool rejoin = false, lateJoin = false;
        if (p is null)
        {
            if (Coop.InRun && prevSlot >= 0 && AcceptRejoin(id, prevSlot))
            {
                p = Coop.Peer(id);
                rejoin = true;
            }
            else if (Coop.InRun && !CanLateJoin(out string why))
            {
                RefusePeer(id, why);
                return;
            }
            else if (Coop.Roster.Count >= Coop.MaxPlayers)
            {
                RefusePeer(id, Loc.T("That game is full."));
                return;
            }
            if (p is null)
            {
                p = new CoopPeer { Id = id, Slot = FreeSlot(), Ready = Coop.InRun };
                Coop.Roster.Add(p);
                GD.Print($"[coop] {CleanName(name)} joined as slot {p.Slot}{(Coop.InRun ? " (run in progress)" : "")}");
                // Already in the run scene (it missed its rejoin window): the world snapshot will do.
                if (Coop.InRun && prevSlot >= 0) { rejoin = true; _loaded.Add(id); AddRemote(p); }
                else lateJoin = Coop.InRun;
            }
        }
        p!.Name = UniqueName(CleanName(name), id);
        p.CharacterId = characterId;
        p.SkinId = skinId;
        p.HatId = hatId;
        BroadcastRoster();
        if (rejoin) SendWorldSnapshot(id, lateJoin: false);
        else if (lateJoin) StartLateJoin(p);
        Changed?.Invoke();
    }

    private static string UniqueName(string name, long id)
    {
        foreach (CoopPeer o in Coop.Roster)
            if (o.Id != id && o.Name == name) return $"{name} {id % 100}";
        return name;
    }

    private static int FreeSlot()
    {
        for (int s = 0; s < Coop.MaxPlayers; s++)
            if (!Coop.Roster.Exists(p => p.Slot == s)) return s;
        return Coop.Roster.Count;
    }

    private void RefusePeer(long id, string reason)
    {
        GD.Print($"[coop] refused peer {id}: {reason}");
        RpcId(id, MethodName.Refused, reason);
        // Let the message leave before the socket does.
        GetTree().CreateTimer(0.5, processAlways: true).Timeout += () =>
        {
            if (_peer is not null && IsInstanceValid(_peer)) _peer.DisconnectPeer((int)id);
        };
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Refused(string reason)
    {
        Fail(reason);
    }

    /// <summary>The character or skin changed on the CO-OP page: tell the lobby.</summary>
    public void SendLook()
    {
        if (Coop.InRun) return;
        CoopPeer me = LocalProfile();
        if (Coop.IsClient && Coop.LocalPeer is not null)
            RpcId(1, MethodName.Hello, Coop.Protocol, me.Name, me.CharacterId, me.SkinId, me.HatId, -1);
        else if (Coop.IsHost && Coop.LocalPeer is { } host)
        {
            host.CharacterId = me.CharacterId;
            host.SkinId = me.SkinId;
            host.HatId = me.HatId;
            BroadcastRoster();
            Changed?.Invoke();
        }
    }

    /// <summary>Ready toggle from the CO-OP page (clients only; the host is
    /// always ready and starts the game).</summary>
    public void SetReady(bool ready)
    {
        if (Coop.IsClient) RpcId(1, MethodName.ReadyChanged, ready);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReadyChanged(bool ready)
    {
        if (!Coop.IsHost) return;
        if (Coop.Peer(Multiplayer.GetRemoteSenderId()) is not { } p) return;
        p.Ready = ready;
        BroadcastRoster();
        Changed?.Invoke();
    }

    /// <summary>Host: the difficulty picked on the CO-OP page. Clients get it
    /// with the roster and cannot change it.</summary>
    /// <summary>Host: the route changed in the lobby; tell everyone.</summary>
    public void SetLobbyRoute()
    {
        if (!Coop.IsHost || Coop.InRun) return;
        Coop.RouteId = Routes.Selected.Id;
        BroadcastRoster();
        Changed?.Invoke();
    }

    public void SetLobbyDifficulty(string tierId)
    {
        if (!Coop.IsHost || Coop.InRun || DifficultyTiers.ById(tierId) is null) return;
        Coop.DifficultyId = tierId;
        BroadcastRoster();
        Changed?.Invoke();
    }

    /// <summary>The host's Start button: enabled with 2-4 players, all ready.</summary>
    public bool CanStart => Coop.IsHost && !Coop.InRun && Coop.Roster.Count >= 2
                            && Coop.Roster.TrueForAll(p => p.Ready);

    private void BroadcastRoster()
    {
        if (!Coop.IsHost) return;
        Rpc(MethodName.RosterChanged, PackRoster());
        PublishLobbyState();   // the browse list's players / difficulty (NetSession.Migration.cs)
    }

    private static string PackRoster()
    {
        var sb = new StringBuilder();
        // Lobby-wide facts first, on a line of their own: the host's difficulty.
        sb.Append("@|").Append(Coop.DifficultyId).Append('|').Append(Routes.Selected.Id).Append('\n');
        // Hosts who left mid-run, for their scoreboard lines (host migration).
        foreach (KeyValuePair<long, string> d in Coop.Departed)
            sb.Append("~|").Append(d.Key).Append('|').Append(d.Value).Append('\n');
        foreach (CoopPeer p in Coop.Roster)
            sb.Append(p.Id).Append('|').Append(p.Slot).Append('|').Append(p.Ready ? 1 : 0).Append('|')
              .Append(p.CharacterId).Append('|').Append(p.SkinId).Append('|').Append(p.Name).Append('|')
              .Append(p.HatId).Append('\n');
        return sb.ToString();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RosterChanged(string packed)
    {
        if (!Coop.IsClient) return;
        Coop.Roster.Clear();
        foreach (string line in packed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = line.Split('|');
            if (f[0] == "@")
            {
                if (f.Length > 1) Coop.DifficultyId = f[1];
                if (f.Length > 2) Coop.RouteId = f[2];
                continue;
            }
            if (f[0] == "~")
            {
                if (f.Length > 2 && long.TryParse(f[1], out long gone)) Coop.Departed[gone] = f[2];
                continue;
            }
            if (f.Length < 6) continue;
            Coop.Roster.Add(new CoopPeer
            {
                Id = long.Parse(f[0]),
                Slot = int.Parse(f[1]),
                Ready = f[2] == "1",
                CharacterId = f[3],
                SkinId = f[4],
                Name = f[5],
                HatId = f.Length > 6 ? f[6] : "",
            });
        }
        if (Coop.LocalPeer is not null && !Coop.InRun) Status = Loc.T("In the lobby. Press READY when you are set.");
        SyncRemotes();
        Changed?.Invoke();
    }

    /// <summary>Host: start the run for everyone.</summary>
    public void StartGame()
    {
        if (!CanStart && !_testMode) return;
        ulong seed = ((ulong)GD.Randi() << 16) ^ GD.Randi();
        // The tier picked in the lobby (Coop.DifficultyId); the host's own
        // selection if the lobby never set one.
        string difficulty = DifficultyTiers.ById(Coop.DifficultyId) is { } lobbyTier ? lobbyTier.Id : DifficultyTiers.Selected.Id;
        difficulty = Coop.PackStart(difficulty, EndlessMode.Selected, Routes.Selected.Id);
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--seed=")) seed = (ulong)arg["--seed=".Length..].ToInt();
        GD.Print($"[coop] starting run seed={seed} players={Coop.Roster.Count}");
        Rpc(MethodName.StartRun, (long)seed, difficulty);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartRun(long seed, string difficultyId)
    {
        Coop.Seed = (ulong)seed;
        Coop.UnpackStart(difficultyId);
        Coop.InRun = true;
        Coop.ClearHold();
        // The lobby stays joinable: players can join a run in progress
        // (NetSession.Snapshot.cs), up to the lobby's 4 slots.
        BeginLoading();
        GetTree().Paused = false;
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, UI.MainMenu.RunScene);
    }
}
