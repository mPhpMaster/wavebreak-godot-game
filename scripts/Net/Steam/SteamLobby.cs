using System;
using System.Collections.Generic;
using Godot;
using Steamworks;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// The Steam lobby around a co-op session, the way Bonk With Friends does it:
/// the host creates a friends-only lobby (4 slots) and invites friends from
/// the Steam overlay; a friend joins through the invite, through "Join Game"
/// in Steam (rich presence "connect"), through the CO-OP page's list of
/// friends in a game, or by launching with <c>+connect_lobby &lt;id&gt;</c>.
/// Once in the lobby the client opens a <see cref="SteamPeer"/> to the lobby
/// owner, and from there NetSession runs exactly as it does over ENet.
///
/// The lobby only finds people and says who is in; game traffic never goes
/// through it. Lobby data: game=wavebreak, protocol=<see cref="Coop.Protocol"/>.
/// Member data: peer=&lt;Godot peer id&gt;, so every player can put a Steam
/// avatar on every roster row.
/// </summary>
public static partial class SteamLobby
{
    public const string GameTag = "wavebreak";

    /// <summary>Lobby or Steam state changed (lobby joined, avatar loaded...).</summary>
    public static event Action? Changed;

    /// <summary>A join arrived from Steam (overlay invite, Join Game) while
    /// the title is up: it shows the CO-OP page, which carries it out.</summary>
    public static event Action? JoinRequested;

    public static ulong LobbyId { get; private set; }
    public static bool IsOwner { get; private set; }

    /// <summary>Busy creating or entering a lobby.</summary>
    public static bool Busy { get; private set; }

    /// <summary>Progress line for the CO-OP page while <see cref="Busy"/>.</summary>
    public static string Status { get; private set; } = "";

    /// <summary>A lobby to join as soon as the CO-OP page is shown.</summary>
    public static ulong PendingJoin { get; set; }

    private static SceneTree? _tree;
    private static bool _installed;
    private static CallResult<LobbyCreated_t>? _created;
    private static CallResult<LobbyEnter_t>? _entered;
    private static readonly List<IDisposable> _callbacks = new();
    private static readonly Dictionary<ulong, ImageTexture> _avatars = new();
    private static readonly Dictionary<ulong, double> _dataAsked = new();
    private static string _joinName = "";
    private static bool _cancelled;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        _tree = Engine.GetMainLoop() as SceneTree;
        _created = CallResult<LobbyCreated_t>.Create(OnCreated);
        _entered = CallResult<LobbyEnter_t>.Create(OnEntered);
        _callbacks.Add(Callback<GameLobbyJoinRequested_t>.Create(e => RequestJoin(e.m_steamIDLobby.m_SteamID, e.m_steamIDFriend.m_SteamID)));
        _callbacks.Add(Callback<GameRichPresenceJoinRequested_t>.Create(e =>
        {
            ulong id = ParseConnect(e.m_rgchConnect);
            if (id != 0) RequestJoin(id, e.m_steamIDFriend.m_SteamID);
        }));
        _callbacks.Add(Callback<LobbyChatUpdate_t>.Create(_ => Changed?.Invoke()));
        _callbacks.Add(Callback<LobbyDataUpdate_t>.Create(_ => Changed?.Invoke()));
        _callbacks.Add(Callback<PersonaStateChange_t>.Create(_ => Changed?.Invoke()));
        _callbacks.Add(Callback<AvatarImageLoaded_t>.Create(e =>
        {
            _avatars.Remove(e.m_steamID.m_SteamID);
            Changed?.Invoke();
        }));
    }

    // ------------------------------------------------------------ host

    /// <summary>HOST (Steam): create a lobby and host in it: public (listed
    /// under BROWSE PUBLIC GAMES, anyone may join) or invite only (friends-only,
    /// as before), as chosen on the CO-OP page (<see cref="LobbyVisibility"/>).</summary>
    public static void Host()
    {
        if (!SteamApp.Ready || Busy) return;
        Leave();
        _cancelled = false;
        Busy = true;
        Status = Loc.T("Creating a Steam lobby...");
        GD.Print("[steam] creating lobby");
        ELobbyType type = LobbyVisibility.Public ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeFriendsOnly;
        _created!.Set(SteamMatchmaking.CreateLobby(type, Coop.MaxPlayers));
        Changed?.Invoke();
    }

    private static void OnCreated(LobbyCreated_t r, bool ioFailure)
    {
        Busy = false;
        Status = "";
        if (_cancelled)
        {
            // LEAVE was pressed while Steam was still making it.
            _cancelled = false;
            if (!ioFailure && r.m_eResult == EResult.k_EResultOK) SteamMatchmaking.LeaveLobby(new CSteamID(r.m_ulSteamIDLobby));
            return;
        }
        if (ioFailure || r.m_eResult != EResult.k_EResultOK)
        {
            Fail(Loc.F("Could not create a Steam lobby ({0}).", ioFailure ? "IO" : r.m_eResult.ToString().Replace("k_EResult", "")));
            return;
        }
        LobbyId = r.m_ulSteamIDLobby;
        IsOwner = true;
        var lobby = new CSteamID(LobbyId);
        SteamMatchmaking.SetLobbyData(lobby, "game", GameTag);
        SteamMatchmaking.SetLobbyData(lobby, "protocol", Coop.Protocol);
        SteamMatchmaking.SetLobbyData(lobby, "host", SteamApp.PersonaName);
        // Browse data (SteamLobby.Browse.cs, PublicGames).
        SteamMatchmaking.SetLobbyData(lobby, PublicGames.VisibilityKey, LobbyVisibility.Public ? PublicGames.PublicValue : PublicGames.InviteValue);
        SteamMatchmaking.SetLobbyData(lobby, "diff", DifficultyTiers.Selected.Id);
        SteamMatchmaking.SetLobbyData(lobby, "state", PublicGames.FormatState(false, false, 0, 0));
        SteamMatchmaking.SetLobbyJoinable(lobby, true);
        SetPresence();
        GD.Print($"[steam] lobby {LobbyId} created");
        if (_tree is not null) NetSession.Ensure(_tree).HostSteam();
        Changed?.Invoke();
    }

    /// <summary>INVITE FRIENDS: the overlay's invite dialog for this lobby.
    /// False when the overlay is off (the page then points to the friends list).</summary>
    public static bool OpenInviteDialog()
    {
        if (!SteamApp.Ready || LobbyId == 0) return false;
        if (!SteamUtils.IsOverlayEnabled()) return false;
        SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(LobbyId));
        return true;
    }

    // ------------------------------------------------------------ join

    /// <summary>Steam asked to join a lobby (invite accepted, Join Game, +connect_lobby).</summary>
    public static void RequestJoin(ulong lobby, ulong friend)
    {
        if (lobby == 0 || lobby == LobbyId) return;
        GD.Print($"[steam] join requested: lobby {lobby} from {friend}");
        if (Coop.InRun)
        {
            GD.Print("[steam] ignored: a co-op run is in progress");
            return;
        }
        PendingJoin = lobby;
        _joinName = friend != 0 ? SteamFriends.GetFriendPersonaName(new CSteamID(friend)) ?? "" : "";
        if (JoinRequested is null)
        {
            // Mid single-player run: the CO-OP page opens on the next title visit.
            UI.CoopPage.OpenOnTitle = true;
            Coop.Notice = Loc.T("A Steam invite is waiting. It opens here when the run ends.");
            return;
        }
        JoinRequested.Invoke();
    }

    /// <summary>The CO-OP page is showing: carry out a waiting join.</summary>
    public static void ConsumePendingJoin()
    {
        if (PendingJoin == 0 || !SteamApp.Ready || Busy) return;
        ulong lobby = PendingJoin;
        PendingJoin = 0;
        Join(lobby, _joinName);
    }

    public static void Join(ulong lobby, string friendName)
    {
        if (!SteamApp.Ready || Busy || lobby == 0) return;
        if (NetSession.Instance is { } s && !Coop.InRun) s.Leave();
        Leave();
        Coop.Notice = null;
        _cancelled = false;
        Busy = true;
        _joinName = friendName;
        Status = friendName.Length > 0 ? Loc.F("Joining {0}'s game...", friendName) : Loc.T("Joining the lobby...");
        GD.Print($"[steam] joining lobby {lobby}");
        _entered!.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobby)));
        Changed?.Invoke();
    }

    private static void OnEntered(LobbyEnter_t e, bool ioFailure)
    {
        Busy = false;
        Status = "";
        var response = (EChatRoomEnterResponse)e.m_EChatRoomEnterResponse;
        if (_cancelled)
        {
            _cancelled = false;
            if (!ioFailure && response == EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                SteamMatchmaking.LeaveLobby(new CSteamID(e.m_ulSteamIDLobby));
            return;
        }
        if (ioFailure || response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            Fail(response switch
            {
                EChatRoomEnterResponse.k_EChatRoomEnterResponseFull => Loc.T("That game is full."),
                EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist => Loc.T("That game no longer exists."),
                _ => Loc.F("Could not join that lobby ({0}).", ioFailure ? "IO" : response.ToString().Replace("k_EChatRoomEnterResponse", "")),
            });
            return;
        }
        LobbyId = e.m_ulSteamIDLobby;
        IsOwner = false;
        var lobby = new CSteamID(LobbyId);
        CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
        if (SteamMatchmaking.GetLobbyData(lobby, "game") != GameTag)
        {
            Fail(Loc.T("That lobby is not a Wavebreak game."));
            return;
        }
        if (SteamMatchmaking.GetLobbyData(lobby, "protocol") != Coop.Protocol)
        {
            Fail(Loc.T("Different game version."));
            return;
        }
        if (owner.m_SteamID == SteamApp.LocalSteamId || owner.m_SteamID == 0)
        {
            Fail(Loc.T("Could not reach the host through Steam."));
            return;
        }
        SetPresence();
        string hostName = SteamFriends.GetFriendPersonaName(owner) ?? "";
        GD.Print($"[steam] in lobby {LobbyId}; host {hostName} ({owner.m_SteamID})");
        if (_tree is not null) NetSession.Ensure(_tree).JoinSteam(owner.m_SteamID, hostName);
        Changed?.Invoke();
    }

    /// <summary>Host: open or close the lobby to new members (closed once the run starts).</summary>
    public static void SetJoinable(bool joinable)
    {
        if (SteamApp.Ready && IsOwner && LobbyId != 0) SteamMatchmaking.SetLobbyJoinable(new CSteamID(LobbyId), joinable);
    }

    /// <summary>Leave the Steam lobby (the session is closed by NetSession).</summary>
    public static void Leave()
    {
        if (!SteamApp.Ready) return;
        if (Busy)
        {
            _cancelled = true;   // the pending create/join result leaves at once
            Busy = false;
            Status = "";
        }
        if (LobbyId != 0)
        {
            GD.Print($"[steam] leaving lobby {LobbyId}");
            SteamMatchmaking.LeaveLobby(new CSteamID(LobbyId));
        }
        LobbyId = 0;
        IsOwner = false;
        SteamFriends.ClearRichPresence();
        Changed?.Invoke();
    }

    private static void Fail(string message)
    {
        GD.Print($"[steam] {message}");
        Busy = false;
        Status = "";
        Leave();
        Coop.Notice = message;
        Changed?.Invoke();
    }

    private static void SetPresence()
    {
        // "connect" is what Steam's "Join Game" passes back (in-game through
        // GameRichPresenceJoinRequested, at launch as command-line arguments).
        SteamFriends.SetRichPresence("connect", $"+connect_lobby {LobbyId}");
        SteamFriends.SetRichPresence("status", "Wavebreak co-op");
    }

    // ------------------------------------------------------------ command line

    private static bool _cmdlineUsed;

    /// <summary>The lobby named by <c>+connect_lobby &lt;id&gt;</c> on the
    /// command line (Steam adds it when a friend launches the game from an
    /// invite), once per process; 0 when there is none.</summary>
    public static ulong TakeCommandLineLobby()
    {
        if (_cmdlineUsed) return 0;
        _cmdlineUsed = true;
        var args = new List<string>(OS.GetCmdlineArgs());
        args.AddRange(OS.GetCmdlineUserArgs());
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            if (a.StartsWith("+connect_lobby=") || a.StartsWith("--connect-lobby="))
                return ulong.TryParse(a[(a.IndexOf('=') + 1)..], out ulong v) ? v : 0;
            if ((a == "+connect_lobby" || a == "--connect-lobby") && i + 1 < args.Count)
                return ulong.TryParse(args[i + 1], out ulong v) ? v : 0;
        }
        return 0;
    }

    /// <summary>"+connect_lobby 1234" (a rich presence connect string) to 1234.</summary>
    public static ulong ParseConnect(string? connect)
    {
        if (string.IsNullOrWhiteSpace(connect)) return 0;
        string[] parts = connect.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i++)
            if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out ulong id)) return id;
        return 0;
    }

    // ------------------------------------------------------------ friends

    public readonly record struct FriendGame(ulong SteamId, string Name, ulong Lobby);

    /// <summary>Friends sitting in a Wavebreak lobby right now (not ours).
    /// A friend's lobby shows once its data says it is Wavebreak: under the
    /// test App ID people may be in real Spacewar lobbies too.</summary>
    public static List<FriendGame> FriendsInGames()
    {
        var list = new List<FriendGame>();
        if (!SteamApp.Ready) return list;
        double now = Time.GetTicksMsec() / 1000.0;
        int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        for (int i = 0; i < n; i++)
        {
            CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            if (!SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t info)) continue;
            if (info.m_gameID.AppID().m_AppId != SteamApp.AppId) continue;
            ulong lobby = info.m_steamIDLobby.m_SteamID;
            if (lobby == 0 || lobby == LobbyId) continue;
            string game = SteamMatchmaking.GetLobbyData(info.m_steamIDLobby, "game");
            if (string.IsNullOrEmpty(game))
            {
                if (!_dataAsked.TryGetValue(lobby, out double at) || now - at > 10.0)
                {
                    _dataAsked[lobby] = now;
                    SteamMatchmaking.RequestLobbyData(info.m_steamIDLobby);
                }
                continue;
            }
            if (game != GameTag) continue;
            list.Add(new FriendGame(friend.m_SteamID, SteamFriends.GetFriendPersonaName(friend) ?? "", lobby));
        }
        return list;
    }

    // ------------------------------------------------------------ who is who

    /// <summary>The Steam id behind a co-op peer id, or 0 when unknown (ENet
    /// sessions, or a member who has not published theirs yet).</summary>
    public static ulong SteamIdForPeer(long peerId)
    {
        if (!SteamApp.Ready || NetSession.Instance is not { ViaSteam: true } session) return 0;
        if (peerId == Coop.LocalId) return SteamApp.LocalSteamId;
        ulong known = session.SteamIdOfPeer(peerId);
        if (known != 0) return known;
        if (LobbyId == 0) return 0;
        var lobby = new CSteamID(LobbyId);
        string want = peerId.ToString();
        int n = SteamMatchmaking.GetNumLobbyMembers(lobby);
        for (int i = 0; i < n; i++)
        {
            CSteamID m = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i);
            if (SteamMatchmaking.GetLobbyMemberData(lobby, m, "peer") == want) return m.m_SteamID;
        }
        return 0;
    }

    /// <summary>Tell the lobby which co-op peer this machine is.</summary>
    public static void PublishPeerId(long peerId)
    {
        if (SteamApp.Ready && LobbyId != 0) SteamMatchmaking.SetLobbyMemberData(new CSteamID(LobbyId), "peer", peerId.ToString());
    }

    /// <summary>Is <paramref name="steamId"/> in our lobby? (The host lets only members play.)</summary>
    public static bool IsMember(ulong steamId)
    {
        if (!SteamApp.Ready || LobbyId == 0) return false;
        var lobby = new CSteamID(LobbyId);
        int n = SteamMatchmaking.GetNumLobbyMembers(lobby);
        for (int i = 0; i < n; i++)
            if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID == steamId) return true;
        return false;
    }

    /// <summary>Small (32 px) Steam avatar, or null while it is loading.</summary>
    public static Texture2D? Avatar(ulong steamId)
    {
        if (!SteamApp.Ready || steamId == 0) return null;
        if (_avatars.TryGetValue(steamId, out ImageTexture? cached)) return cached;
        var id = new CSteamID(steamId);
        int handle = SteamFriends.GetSmallFriendAvatar(id);
        if (handle == 0) SteamFriends.RequestUserInformation(id, false);   // AvatarImageLoaded follows
        if (handle <= 0) return null;
        if (!SteamUtils.GetImageSize(handle, out uint w, out uint h) || w == 0 || h == 0) return null;
        var rgba = new byte[w * h * 4];
        if (!SteamUtils.GetImageRGBA(handle, rgba, rgba.Length)) return null;
        ImageTexture tex = ImageTexture.CreateFromImage(Image.CreateFromData((int)w, (int)h, false, Image.Format.Rgba8, rgba));
        _avatars[steamId] = tex;
        return tex;
    }
}
