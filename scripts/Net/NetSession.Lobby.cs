using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Net;

public sealed partial class NetSession
{
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
    public void SetLobbyDifficulty(string tierId)
    {
        if (!Coop.IsHost || Coop.InRun || DifficultyTiers.ById(tierId) is null) return;
        Coop.DifficultyId = tierId;
        BroadcastRoster();
        Changed?.Invoke();
    }

    public void SetLobbyRoute()
    {
        if (!Coop.IsHost || Coop.InRun) return;
        Coop.RouteId = Routes.Selected.Id;
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
                if (f.Length > 2) Coop.RouteId = Routes.ById(f[2])?.Id ?? "classic";
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
        if (EndlessMode.Selected) difficulty = "endless:" + difficulty;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--seed=")) seed = (ulong)arg["--seed=".Length..].ToInt();
        GD.Print($"[coop] starting run seed={seed} players={Coop.Roster.Count}");
        string route = Routes.Selected.Id;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg is "--route=reef-dive" or "--route=skyline-run") route = arg["--route=".Length..];
        Rpc(MethodName.StartRun, (long)seed, difficulty, route);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartRun(long seed, string difficultyId, string routeId)
    {
        Coop.Seed = (ulong)seed;
        Coop.UnpackStart(difficultyId);
        Coop.RouteId = Routes.ById(routeId)?.Id ?? "classic";
        Coop.InRun = true;
        Coop.ClearHold();
        // The lobby stays joinable: players can join a run in progress
        // (NetSession.Snapshot.cs), up to the lobby's 4 slots.
        BeginLoading();
        GetTree().Paused = false;
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, UI.MainMenu.RunScene);
    }
}
