using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// Public lobbies: what the CO-OP page's BROWSE PUBLIC GAMES list shows, and
/// the host's Public / Invite only choice.
///
/// A public host's Steam lobby is <c>k_ELobbyTypePublic</c> and carries, as
/// lobby data: game=wavebreak, protocol, vis=public, host (name), diff (tier
/// id) and state ("lobby", "map:2/3" or "rest:2/3", kept current once a
/// second during the run). The page asks Steam for lobbies with game, protocol
/// and vis matching (<see cref="SteamLobby.RequestPublicList"/>), and this
/// class does the rest without Steam, so it can be tested: drop anything that
/// is not ours, full, empty or our own lobby (<see cref="Filter"/>), order
/// what is left, and turn each into a row (<see cref="Format"/>).
/// </summary>
public static class PublicGames
{
    public const string VisibilityKey = "vis", PublicValue = "public", InviteValue = "invite";

    public readonly record struct Entry(ulong Lobby, string Game, string Protocol, string Visibility,
                                        string Host, int Players, int MaxPlayers, string Difficulty, string State);

    /// <summary>One row of the list, already translated.</summary>
    public readonly record struct Row(ulong Lobby, string Host, string Players, string Difficulty, string State);

    /// <summary>Ours, joinable and not our own lobby; lobbies still in the
    /// lobby first (a fresh start), then fuller games, then by host name.</summary>
    public static List<Entry> Filter(IEnumerable<Entry> all, ulong ownLobby)
    {
        var list = new List<Entry>();
        var seen = new HashSet<ulong>();
        foreach (Entry e in all)
        {
            if (e.Lobby == 0 || e.Lobby == ownLobby || !seen.Add(e.Lobby)) continue;
            if (e.Game != SteamLobby.GameTag || e.Protocol != Coop.Protocol) continue;
            if (e.Visibility != PublicValue) continue;
            int max = e.MaxPlayers > 0 ? Math.Min(e.MaxPlayers, Coop.MaxPlayers) : Coop.MaxPlayers;
            if (e.Players <= 0 || e.Players >= max) continue;
            list.Add(e with { MaxPlayers = max });
        }
        list.Sort((a, b) =>
        {
            bool la = !ParseState(a.State).InRun, lb = !ParseState(b.State).InRun;
            if (la != lb) return la ? -1 : 1;
            if (a.Players != b.Players) return b.Players.CompareTo(a.Players);
            return string.Compare(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    public static Row Format(Entry e)
    {
        string host = string.IsNullOrWhiteSpace(e.Host) ? Loc.T("Unknown host") : NetSession.CleanName(e.Host);
        return new Row(e.Lobby, host, $"{e.Players}/{(e.MaxPlayers > 0 ? e.MaxPlayers : Coop.MaxPlayers)}",
                       DifficultyText(e.Difficulty), StateText(e.State));
    }

    // ------------------------------------------------------------ the state field

    /// <summary>"lobby", "map:2/3" or "rest:2/3".</summary>
    public static string FormatState(bool inRun, bool resting, int phase, int count)
        => !inRun ? "lobby" : $"{(resting ? "rest" : "map")}:{Math.Max(1, phase)}/{Math.Max(1, count)}";

    public static (bool InRun, bool Resting, int Phase, int Count) ParseState(string? s)
    {
        if (string.IsNullOrEmpty(s) || s == "lobby") return (false, false, 0, 0);
        int colon = s.IndexOf(':'), slash = s.IndexOf('/');
        if (colon < 0 || slash < colon) return (false, false, 0, 0);
        bool resting = s[..colon] == "rest";
        if (!int.TryParse(s[(colon + 1)..slash], out int phase) || !int.TryParse(s[(slash + 1)..], out int count))
            return (false, false, 0, 0);
        return (true, resting, phase, count);
    }

    public static string StateText(string? s)
    {
        var (inRun, resting, phase, count) = ParseState(s);
        if (!inRun) return Loc.T("In the lobby");
        return resting ? Loc.F("Resting after map {0}/{1}", phase, count) : Loc.F("Map {0}/{1}", phase, count);
    }

    public static string DifficultyText(string? id)
    {
        if (string.IsNullOrEmpty(id)) return Loc.T("Normal");
        return DifficultyTiers.ById(id) is { } tier ? Loc.T(tier.Name) : id;
    }

    // ------------------------------------------------------------ fake entries (captures)

    /// <summary>--fake-browse: the list as it would look with games to join,
    /// for screenshots on a machine without Steam.</summary>
    public static bool FakeRequested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--fake-browse") >= 0;

    public static List<Entry> FakeEntries() => new()
    {
        new(1001, SteamLobby.GameTag, Coop.Protocol, PublicValue, "Nadia", 1, 4, "normal", "lobby"),
        new(1002, SteamLobby.GameTag, Coop.Protocol, PublicValue, "Kenji", 3, 4, "hard", "map:2/3"),
        new(1003, SteamLobby.GameTag, Coop.Protocol, PublicValue, "Omar", 2, 4, "nightmare", "rest:1/4"),
        new(1004, SteamLobby.GameTag, Coop.Protocol, PublicValue, "Full House", 4, 4, "normal", "lobby"),
        new(1005, "spacewar", "", "", "Not Ours", 1, 4, "", ""),
        new(1006, SteamLobby.GameTag, Coop.Protocol, PublicValue, "Lena", 2, 4, "tempest", "map:4/4"),
    };

    // ------------------------------------------------------------ self-check

    public static bool CheckRequested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-browse") >= 0;

    /// <summary>--check-browse: the filter, the order and the formatting, with
    /// no Steam. Prints BROWSE PASS/FAIL and returns whether it passed.</summary>
    public static bool RunCheck()
    {
        int checks = 0;
        var failed = new List<string>();
        void Expect(string what, bool ok)
        {
            checks++;
            GD.Print($"BROWSE {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failed.Add(what);
        }
        string tag = SteamLobby.GameTag, proto = Coop.Protocol;
        var all = new List<Entry>
        {
            new(10, tag, proto, PublicValue, "InRun", 2, 4, "hard", "map:2/3"),
            new(11, tag, proto, PublicValue, "Fresh", 1, 4, "normal", "lobby"),
            new(12, tag, proto, PublicValue, "Fuller", 3, 4, "normal", "lobby"),
            new(13, tag, proto, PublicValue, "Full", 4, 4, "normal", "lobby"),
            new(14, tag, proto, InviteValue, "Private", 1, 4, "normal", "lobby"),
            new(15, "spacewar", proto, PublicValue, "OtherGame", 1, 4, "", ""),
            new(16, tag, "wavebreak-coop-1", PublicValue, "OldBuild", 1, 4, "", "lobby"),
            new(17, tag, proto, PublicValue, "Mine", 1, 4, "", "lobby"),
            new(18, tag, proto, PublicValue, "Empty", 0, 4, "", "lobby"),
            new(11, tag, proto, PublicValue, "Fresh again", 1, 4, "normal", "lobby"),
            new(19, tag, proto, PublicValue, "", 1, 8, "", "rest:1/4"),
        };
        List<Entry> list = Filter(all, ownLobby: 17);
        var hosts = new List<string>();
        foreach (Entry e in list) hosts.Add(e.Host);
        string order = string.Join(",", hosts);
        Expect($"filter keeps only joinable public Wavebreak lobbies of this version, not ours ({order})", list.Count == 4);
        Expect("lobbies still in the lobby come first, fuller first", hosts.Count >= 2 && hosts[0] == "Fuller" && hosts[1] == "Fresh");
        Expect("games in progress follow, fuller first", hosts.Count == 4 && hosts[2] == "InRun" && hosts[3] == "");
        Expect("a lobby listed twice shows once", hosts.FindAll(h => h.StartsWith("Fresh")).Count == 1);
        Expect("more slots than co-op allows are capped at 4", list.Count == 4 && list[3].MaxPlayers == Coop.MaxPlayers);

        Row r = Format(list[2]);
        Expect($"row: host, players, difficulty, phase ({r.Host} | {r.Players} | {r.Difficulty} | {r.State})",
               r.Host == "InRun" && r.Players == "2/4" && r.Difficulty == Loc.T("Hard") && r.State == Loc.F("Map {0}/{1}", 2, 3));
        Row lobby = Format(list[0]);
        Expect($"a lobby not started reads '{Loc.T("In the lobby")}'", lobby.State == Loc.T("In the lobby") && lobby.Players == "3/4");
        Row anon = Format(list[3]);
        Expect("a nameless host and an unknown tier still format", anon.Host == Loc.T("Unknown host") && anon.Difficulty == Loc.T("Normal")
               && anon.State == Loc.F("Resting after map {0}/{1}", 1, 4));

        Expect("state round trip: lobby", FormatState(false, false, 2, 3) == "lobby" && !ParseState("lobby").InRun);
        Expect("state round trip: map", ParseState(FormatState(true, false, 2, 3)) == (true, false, 2, 3));
        Expect("state round trip: rest", ParseState(FormatState(true, true, 1, 4)) == (true, true, 1, 4));
        Expect("garbage state reads as the lobby", !ParseState("map:x/").InRun && !ParseState(null).InRun);
        Expect("the fake list (captures) filters to 4 rows", Filter(FakeEntries(), 0).Count == 4);

        GD.Print(failed.Count == 0 ? $"BROWSE PASS checks={checks}" : $"BROWSE FAIL checks={checks} failed={failed.Count}");
        return failed.Count == 0;
    }
}

/// <summary>The host's Public / Invite only choice, kept in settings.cfg
/// ([coop] lobby_public). Automated runs use the default and never write.</summary>
public static class LobbyVisibility
{
    private const string SettingsPath = "user://settings.cfg";
    private static bool _loaded;
    private static bool _public;

    public static bool Public
    {
        get
        {
            if (!_loaded) Load();
            return _public;
        }
        set
        {
            _loaded = true;
            _public = value;
            if (Launch.Automated) return;
            var cfg = new ConfigFile();
            cfg.Load(SettingsPath);   // keep the other sections
            cfg.SetValue("coop", "lobby_public", value);
            cfg.Save(SettingsPath);
        }
    }

    private static void Load()
    {
        _loaded = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--lobby="))
            {
                _public = arg["--lobby=".Length..] == "public";
                return;
            }
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) == Error.Ok) _public = cfg.GetValue("coop", "lobby_public", false).AsBool();
    }
}
