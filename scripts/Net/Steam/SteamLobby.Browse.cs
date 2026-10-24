using System.Collections.Generic;
using Godot;
using Steamworks;

namespace Wavebreak.Net;

/// <summary>
/// Public lobbies and host migration on Steam.
///
/// <b>Browse.</b> <see cref="RequestPublicList"/> asks Steam for lobbies of
/// this App ID whose data says game=wavebreak, our protocol and vis=public,
/// worldwide; the answer is read into <see cref="PublicGames.Entry"/> records
/// and filtered and formatted by <see cref="PublicGames"/>.
///
/// <b>State.</b> The host keeps players, difficulty and phase in the lobby
/// data (<see cref="PublishState"/>, once a second in a run).
///
/// <b>Migration.</b> Steam passes lobby ownership on by itself when the owner
/// leaves, but not necessarily to the player the survivors elected; the
/// member who has it hands it on (<see cref="HandOwnershipTo"/>), so lobby
/// data, joinability and invites follow the new host.
/// </summary>
public static partial class SteamLobby
{
    private static CallResult<LobbyMatchList_t>? _listed;
    private static readonly List<PublicGames.Entry> _public = new();

    /// <summary>Public Wavebreak lobbies from the last search (unfiltered).</summary>
    public static IReadOnlyList<PublicGames.Entry> PublicList => _public;

    /// <summary>A public search is on its way.</summary>
    public static bool Searching { get; private set; }

    /// <summary>Ask Steam for public Wavebreak lobbies; <see cref="Changed"/> fires with the answer.</summary>
    public static void RequestPublicList()
    {
        if (!SteamApp.Ready || Searching) return;
        _listed ??= CallResult<LobbyMatchList_t>.Create(OnListed);
        SteamMatchmaking.AddRequestLobbyListStringFilter("game", GameTag, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter("protocol", Coop.Protocol, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(PublicGames.VisibilityKey, PublicGames.PublicValue, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
        Searching = true;
        _listed.Set(SteamMatchmaking.RequestLobbyList());
    }

    private static void OnListed(LobbyMatchList_t r, bool ioFailure)
    {
        Searching = false;
        _public.Clear();
        if (!ioFailure)
            for (int i = 0; i < (int)r.m_nLobbiesMatching; i++)
            {
                CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(i);
                _public.Add(new PublicGames.Entry(
                    lobby.m_SteamID,
                    SteamMatchmaking.GetLobbyData(lobby, "game"),
                    SteamMatchmaking.GetLobbyData(lobby, "protocol"),
                    SteamMatchmaking.GetLobbyData(lobby, PublicGames.VisibilityKey),
                    SteamMatchmaking.GetLobbyData(lobby, "host"),
                    SteamMatchmaking.GetNumLobbyMembers(lobby),
                    SteamMatchmaking.GetLobbyMemberLimit(lobby),
                    SteamMatchmaking.GetLobbyData(lobby, "diff"),
                    SteamMatchmaking.GetLobbyData(lobby, "state")));
            }
        GD.Print($"[steam] public lobbies: {_public.Count}{(ioFailure ? " (IO failure)" : "")}");
        Changed?.Invoke();
    }

    /// <summary>Host: players, difficulty and phase for the browse list.</summary>
    public static void PublishState(int players, string difficulty, string state)
    {
        if (!SteamApp.Ready || LobbyId == 0) return;
        var lobby = new CSteamID(LobbyId);
        IsOwner = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID == SteamApp.LocalSteamId;
        if (!IsOwner) return;
        SteamMatchmaking.SetLobbyData(lobby, "host", SteamApp.PersonaName);
        SteamMatchmaking.SetLobbyData(lobby, "players", players.ToString());
        SteamMatchmaking.SetLobbyData(lobby, "diff", difficulty);
        SteamMatchmaking.SetLobbyData(lobby, "state", state);
    }

    /// <summary>Host migration: if this machine owns the lobby and the elected
    /// host is someone else, give it to them. Safe to call on every survivor.</summary>
    public static void HandOwnershipTo(ulong steamId)
    {
        if (!SteamApp.Ready || LobbyId == 0 || steamId == 0) return;
        var lobby = new CSteamID(LobbyId);
        ulong owner = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID;
        if (owner == SteamApp.LocalSteamId && steamId != owner)
        {
            bool ok = SteamMatchmaking.SetLobbyOwner(lobby, new CSteamID(steamId));
            GD.Print($"[steam] lobby ownership to {steamId}: {(ok ? "ok" : "refused")}");
        }
        IsOwner = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID == SteamApp.LocalSteamId;
        if (IsOwner) SetPresence();
    }
}
