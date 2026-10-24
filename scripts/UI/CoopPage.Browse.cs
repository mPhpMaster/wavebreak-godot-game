using System.Collections.Generic;
using System.Text;
using Godot;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// The CO-OP page's public games: the host's Public / Invite only choice
/// (kept in settings.cfg, <see cref="LobbyVisibility"/>) and the BROWSE PUBLIC
/// GAMES list, one row per joinable public Wavebreak lobby: host, players x/4,
/// difficulty, and the map it is on or "In the lobby", with JOIN. Anyone may
/// join a public game, also a run in progress (they arrive at the team's
/// level; Net/NetSession.Snapshot.cs).
///
/// The list comes from a Steam lobby search (<see cref="SteamLobby.RequestPublicList"/>),
/// refreshed when the page opens, with REFRESH, and every
/// <see cref="BrowseEvery"/> seconds; filtering and formatting are
/// <see cref="PublicGames"/>'s. <c>--fake-browse</c> fills it with made-up
/// games so the list can be photographed on a machine without Steam.
/// </summary>
public sealed partial class CoopPage
{
    private const double BrowseEvery = 15.0;

    private Control _lobbyTypeRow = null!;
    private Label _lobbyType = null!;
    private readonly List<Button> _lobbyTypeArrows = new();
    private Control _browseHead = null!;
    private Control _browseScroll = null!;
    private VBoxContainer _browse = null!;
    private Button _browseRefresh = null!;
    private string _browseKey = "";
    private double _browseAt;

    private static readonly bool FakeBrowse = PublicGames.FakeRequested;

    private void BuildLobbyType(VBoxContainer box)
    {
        _lobbyType = MainMenu.MakeLabel("", 17, Accent);
        _lobbyType.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        _lobbyType.HorizontalAlignment = HorizontalAlignment.Center;
        _lobbyType.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Button a = Arrow(Loc.IsRtl ? ">" : "<", ToggleLobbyType, _lobbyTypeArrows);
        Button b = Arrow(Loc.IsRtl ? "<" : ">", ToggleLobbyType, _lobbyTypeArrows);
        _lobbyTypeRow = Row("Lobby", a, _lobbyType, b);
        box.AddChild(_lobbyTypeRow);
    }

    private void BuildBrowse(VBoxContainer box)
    {
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        Label title = MainMenu.MakeLabel("BROWSE PUBLIC GAMES", 15, InkDim);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.VerticalAlignment = VerticalAlignment.Center;
        head.AddChild(title);
        _browseRefresh = MainMenu.MenuButton("REFRESH", () => RequestBrowse(force: true));
        _browseRefresh.CustomMinimumSize = new Vector2(130f, 30f);
        _browseRefresh.AddThemeFontSizeOverride("font_size", 15);
        head.AddChild(_browseRefresh);
        _browseHead = head;
        box.AddChild(head);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 128f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        _browse = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _browse.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_browse);
        _browseScroll = scroll;
        box.AddChild(scroll);
    }

    private void ToggleLobbyType()
    {
        if (Coop.Active || SteamLobby.Busy) return;
        LobbyVisibility.Public = !LobbyVisibility.Public;
        Refresh();
    }

    /// <summary>Ask Steam again (on open, REFRESH, and every so often).</summary>
    private void RequestBrowse(bool force = false)
    {
        _browseAt = 0;
        if (!force && Coop.Active) return;
        if (SteamApp.Ready) SteamLobby.RequestPublicList();
        RefreshBrowseList();
    }

    private void StepBrowse(double delta)
    {
        if (!IsVisibleInTree() || Coop.Active || !SteamApp.Ready) return;
        _browseAt += delta;
        if (_browseAt >= BrowseEvery) RequestBrowse();
    }

    /// <summary>Show or hide the section; called from <see cref="Refresh"/>.</summary>
    private void RefreshBrowseView(bool active, bool busy, bool steam)
    {
        if (_lobbyType is null) return;
        bool viaSteam = NetSession.Instance?.ViaSteam == true;
        _lobbyTypeRow.Visible = (steam || FakeBrowse) && (!active || (Coop.IsHost && viaSteam));
        _lobbyType.Text = Loc.T(LobbyVisibility.Public ? "Public" : "Invite only");
        _lobbyType.TooltipText = Loc.T(LobbyVisibility.Public
            ? "Anyone can find this game under BROWSE PUBLIC GAMES and join, also during the run."
            : "Only friends you invite can join, also during the run.");
        foreach (Button arrow in _lobbyTypeArrows) arrow.Disabled = active || busy || Launch.Automated && !FakeBrowse;

        bool show = (steam || FakeBrowse) && !active && !busy;
        _browseHead.Visible = show;
        _browseScroll.Visible = show;
        _browseRefresh.Disabled = SteamLobby.Searching || !steam;
        // The explanation gives way to the list when there is one to show.
        if (show) _note.Visible = false;
        RefreshBrowseList();
    }

    /// <summary>Rebuild the rows, only when they changed (keeps keyboard focus).</summary>
    private void RefreshBrowseList()
    {
        if (_browse is null) return;
        List<PublicGames.Entry> all = FakeBrowse ? PublicGames.FakeEntries() : new List<PublicGames.Entry>(SteamLobby.PublicList);
        List<PublicGames.Entry> list = PublicGames.Filter(all, SteamLobby.LobbyId);
        var key = new StringBuilder();
        foreach (PublicGames.Entry e in list) key.Append(e.Lobby).Append(':').Append(e.Players).Append(':').Append(e.State).Append(':').Append(e.Host).Append(';');
        key.Append(SteamLobby.Searching ? "?" : "").Append(SteamApp.Ready ? "+" : "-");
        if (key.ToString() == _browseKey) return;
        _browseKey = key.ToString();

        foreach (Node n in _browse.GetChildren()) n.QueueFree();
        if (list.Count == 0)
        {
            _browse.AddChild(MainMenu.MakeLabel(SteamLobby.Searching ? "Looking for public games..." : "No public games right now. Host one!", 15, InkDim));
            return;
        }
        foreach (PublicGames.Entry e in list)
        {
            PublicGames.Row r = PublicGames.Format(e);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(Cell(r.Host, 17, Ink, expand: true));
            row.AddChild(Cell(r.Players, 16, Accent, width: 46f));
            row.AddChild(Cell(r.Difficulty, 15, Gold, width: 104f));
            row.AddChild(Cell(r.State, 15, InkDim, width: 170f));
            ulong lobby = e.Lobby;
            string host = r.Host;
            Button join = MainMenu.MenuButton("JOIN", () => JoinPublic(lobby, host));
            join.CustomMinimumSize = new Vector2(88f, 30f);
            join.AddThemeFontSizeOverride("font_size", 16);
            row.AddChild(join);
            _browse.AddChild(row);
        }
    }

    private static Label Cell(string text, int size, Color color, bool expand = false, float width = 0f)
    {
        Label l = MainMenu.MakeLabel(text, size, color);
        l.AutoTranslateMode = AutoTranslateModeEnum.Disabled;   // already translated, may hold a name
        l.VerticalAlignment = VerticalAlignment.Center;
        l.ClipText = true;
        if (expand) l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (width > 0f) l.CustomMinimumSize = new Vector2(width, 0f);
        return l;
    }

    private void JoinPublic(ulong lobby, string host)
    {
        if (FakeBrowse && !SteamApp.Ready) return;
        _hint = null;
        _readyOn = false;
        SteamLobby.Join(lobby, host);
        Refresh();
    }
}
