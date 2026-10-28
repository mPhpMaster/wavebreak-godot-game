using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// The title screen's CO-OP page, played through Steam:
///   HOST (Steam)     a friends-only Steam lobby for up to 4 (Net/Steam/SteamLobby)
///   INVITE FRIENDS   the Steam overlay's invite dialog
///   friends list     friends sitting in a Wavebreak lobby, each with JOIN
/// plus the lobby itself, READY, and START for the host. Invites accepted in
/// the overlay, "Join Game" in Steam and <c>+connect_lobby</c> all land here
/// (<see cref="SteamLobby.PendingJoin"/>).
///
/// Layout: the left column picks who you play as (character, skin, hat) with
/// the same character panel as single-player (<see cref="CharacterInfo"/>),
/// and the difficulty, which only the host can change. The right panel holds
/// the status, the friends list and the session buttons. Below it, on the
/// beach, every joined player stands side by side in their look with their
/// name and READY state over their head (<see cref="CoopLineup"/>).
///
/// Steam starts when this page opens, never earlier. When it cannot (Steam
/// not running, not signed in) the page says so and offers RETRY; the rest of
/// the game is unaffected. Automated runs never start Steam (--steam-test).
///
/// Direct IP (ENet) is a developer path only: hidden unless switched on in
/// the developer panel (<see cref="ShowDirectIp"/>); the automated co-op test
/// uses it through --coop-host / --coop-join without this page.
/// </summary>
public sealed partial class CoopPage : Control
{
    /// <summary>Set when a co-op run ended under the player (the host left),
    /// or a Steam invite is waiting: the title opens on this page.</summary>
    public static bool OpenOnTitle { get; set; }

    /// <summary>Developer panel switch: show the direct-IP host/join rows.</summary>
    public static bool ShowDirectIp { get; set; }

    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color Accent = new(0.45f, 0.82f, 1.00f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    private static readonly Color Warn = new(1.00f, 0.72f, 0.45f);
    private static readonly Color PanelBack = new(0.05f, 0.06f, 0.10f, 0.86f);

    private const string SettingsPath = "user://settings.cfg";
    private const double FriendsEvery = 2.0;

    private readonly Action _back;

    private Button _hostSteam = null!;
    private Button _invite = null!;
    private Button _retry = null!;
    private VBoxContainer _friends = null!;
    private Label _friendsTitle = null!;
    private Control _friendsScroll = null!;
    private string _friendsKey = "";
    private double _friendsAt;
    private string? _hint;

    // developer direct-IP rows
    private VBoxContainer _direct = null!;
    private LineEdit _name = null!;
    private LineEdit _port = null!;
    private LineEdit _address = null!;
    private Button _host = null!;
    private Button _join = null!;

    private Button _ready = null!;
    private Button _start = null!;
    private Button _leave = null!;
    private Label _heroName = null!;
    private Label _skinName = null!;
    private Label _hatName = null!;
    private Label _difficulty = null!;
    private readonly List<Button> _arrows = new();
    private readonly List<Button> _difficultyArrows = new();
    private Label _route = null!;
    private readonly List<Button> _routeArrows = new();
    private Label _status = null!;
    private Label _note = null!;
    private Label _players = null!;
    private bool _readyOn;
    private Control _panel = null!;

    /// <summary>Lowest screen y of the session panel; the beach line-up keeps its name tags under it.</summary>
    public float PanelBottom => _panel is null ? 0f : _panel.GetGlobalRect().End.Y;

    // the character panel (shared text with the PLAY page: CharacterInfo)
    private Label _infoName = null!;
    private Label _infoBlurb = null!;
    private Label _infoStats = null!;
    private Label _infoPassive = null!;

    public CoopPage(Action back)
    {
        _back = back;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        LoadSettings();

        // ---- left: who you are
        var column = new VBoxContainer();
        MainMenu.Place(column, 60f, 186f, 400f, 520f);
        column.AddThemeConstantOverride("separation", 5);
        AddChild(column);

        column.AddChild(MainMenu.MakeLabel("CO-OP", 20, InkDim));
        var hostRow = new HBoxContainer();
        hostRow.AddThemeConstantOverride("separation", 6);
        _hostSteam = MainMenu.MenuButton("HOST (Steam)", HostSteam);
        _invite = MainMenu.MenuButton("INVITE FRIENDS", Invite);
        foreach (Button b in new[] { _hostSteam, _invite })
        {
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.CustomMinimumSize = new Vector2(0f, 42f);
            b.AddThemeFontSizeOverride("font_size", 17);
            b.ClipText = true;
            hostRow.AddChild(b);
        }
        column.AddChild(hostRow);

        // Who you play as, chosen here (the PLAY page starts a solo run on a
        // click). Only unlocked characters, skins and owned hats; the choice
        // is saved and the lobby updates at once.
        _heroName = Picker(column, "Character", () => CycleCharacter(-1), () => CycleCharacter(+1), _arrows);
        _skinName = Picker(column, "Skin", () => CycleSkin(-1), () => CycleSkin(+1), _arrows);
        _hatName = Picker(column, "Hat", () => CycleHat(-1), () => CycleHat(+1), _arrows);
        // The host picks the difficulty for everyone; a client sees it here.
        _difficulty = Picker(column, "Difficulty", () => StepDifficulty(-1), () => StepDifficulty(+1), _difficultyArrows);
        // ...and the route (which maps, in which order).
        _route = Picker(column, "Route", () => StepRoute(-1), () => StepRoute(+1), _routeArrows);

        // The same panel as the PLAY page: name, blurb, starting weapon and
        // stats, passive.
        var info = new PanelContainer();
        var infoStyle = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 10 };
        infoStyle.SetCornerRadiusAll(6);
        info.AddThemeStyleboxOverride("panel", infoStyle);
        var infoBox = new VBoxContainer();
        infoBox.AddThemeConstantOverride("separation", 3);
        info.AddChild(infoBox);
        _infoName = MainMenu.MakeLabel("", 21, Ink);
        _infoBlurb = MainMenu.MakeLabel("", 14, Ink);
        _infoStats = MainMenu.MakeLabel("", 14, Accent);
        _infoPassive = MainMenu.MakeLabel("", 14, Gold);
        foreach (Label l in new[] { _infoName, _infoBlurb, _infoStats, _infoPassive })
        {
            l.AutoTranslateMode = AutoTranslateModeEnum.Disabled;   // CharacterInfo translates
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            infoBox.AddChild(l);
        }
        column.AddChild(info);

        // Developer only: direct IP over ENet.
        _direct = new VBoxContainer();
        _direct.AddThemeConstantOverride("separation", 6);
        _name = Field(NetSession.PlayerName, 16);
        _name.TextChanged += t => { NetSession.PlayerName = NetSession.CleanName(t); SaveSettings(); };
        _direct.AddChild(Row("Your name", _name));
        _port = Field(_savedPort, 5);
        _host = MainMenu.MenuButton("HOST", HostDirect);
        _direct.AddChild(Row("Port", _port, _host));
        _address = Field(_savedAddress, 40);
        _address.PlaceholderText = "192.168.1.20:7777";
        _join = MainMenu.MenuButton("JOIN", JoinDirect);
        _direct.AddChild(Row("Address", _address, _join));
        column.AddChild(_direct);

        Button back = MainMenu.MenuButton("BACK", () => { LeaveSession(); _readyOn = false; _back(); });
        back.CustomMinimumSize = new Vector2(0f, 42f);
        column.AddChild(back);

        // ---- right: the session; the beach under it shows the players.
        var panel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 12, ContentMarginBottom = 12 };
        style.SetCornerRadiusAll(6);
        panel.AddThemeStyleboxOverride("panel", style);
        MainMenu.Place(panel, 490f, 186f, 730f, 0f);
        AddChild(panel);
        _panel = panel;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        _status = MainMenu.MakeLabel("", 17, Ink);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.AutoTranslateMode = AutoTranslateModeEnum.Disabled;   // already translated, may hold a name
        box.AddChild(_status);
        _retry = MainMenu.MenuButton("RETRY", Retry);
        _retry.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _retry.CustomMinimumSize = new Vector2(180f, 44f);
        box.AddChild(_retry);
        BuildLobbyType(box);   // Public / Invite only (CoopPage.Browse.cs)

        // Friends in a Wavebreak lobby, each with JOIN.
        _friendsTitle = MainMenu.MakeLabel("FRIENDS IN A GAME", 15, InkDim);
        box.AddChild(_friendsTitle);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 92f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        _friends = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _friends.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_friends);
        _friendsScroll = scroll;
        box.AddChild(scroll);
        BuildBrowse(box);   // BROWSE PUBLIC GAMES (CoopPage.Browse.cs)

        _players = MainMenu.MakeLabel("", 15, InkDim);
        _players.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        box.AddChild(_players);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        _ready = MainMenu.MenuButton("READY", ToggleReady);
        _start = MainMenu.MenuButton("START", () => NetSession.Instance?.StartGame());
        _leave = MainMenu.MenuButton("LEAVE", () => { LeaveSession(); _readyOn = false; Refresh(); });
        foreach (Button b in new[] { _ready, _start, _leave })
        {
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.CustomMinimumSize = new Vector2(0f, 44f);
            buttons.AddChild(b);
        }
        box.AddChild(buttons);

        _note = MainMenu.MakeLabel(
            "Play with your Steam friends: HOST (Steam) opens a friends-only lobby and INVITE FRIENDS opens the Steam overlay. " +
            "Friends join from the invite, with \"Join Game\" on your name in Steam, or with JOIN in their list. No addresses or ports. " +
            "Steam shows the game as \"Spacewar\" (Valve's test app) until Wavebreak has its own Steam page. " +
            "Each player keeps their own character, weapons, level-ups and items; experience is shared. " +
            "A downed player is revived by a teammate standing next to them.", 13, InkDim);
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_note);

        NetSession.Changed += Refresh;
        SteamLobby.Changed += OnSteamChanged;
        Refresh();
    }

    public override void _ExitTree()
    {
        NetSession.Changed -= Refresh;
        SteamLobby.Changed -= OnSteamChanged;
    }

    /// <summary>Called when the page is shown. Starts Steam (first time) and
    /// carries out a join that arrived from Steam.</summary>
    public void Open()
    {
        OpenOnTitle = false;
        _hint = null;
        if (!Coop.Active) SteamApp.TryInit(GetTree());
        SteamLobby.ConsumePendingJoin();
        _friendsKey = "";
        RefreshFriends();
        RequestBrowse();
        Refresh();
        if (Coop.IsHost) _start.GrabFocus();
        else if (Coop.IsClient) _ready.GrabFocus();
        else if (!SteamApp.Ready && !Coop.Active) _retry.GrabFocus();
        else _hostSteam.GrabFocus();
    }

    /// <summary>BACK, LEAVE and Escape: close the session and the Steam lobby.
    /// Not during a run (the pause menu's LEAVE GAME does that).</summary>
    public static void LeaveSession()
    {
        if (Coop.InRun) return;
        NetSession.Instance?.Leave();
        SteamLobby.Leave();
    }

    public override void _Process(double delta)
    {
        StepBrowse(delta);
        if (!IsVisibleInTree() || !SteamApp.Ready) return;
        _friendsAt += delta;
        if (_friendsAt >= FriendsEvery) RefreshFriends();
    }

    private void OnSteamChanged()
    {
        RefreshFriends();
        Refresh();
    }

    private void Refresh()
    {
        if (!IsInsideTree() || _status is null) return;
        bool active = Coop.Active;
        bool busy = SteamLobby.Busy;
        bool steam = SteamApp.Ready;

        CoopPeer me = NetSession.LocalProfile();
        CharacterDef c = Characters.ById(me.CharacterId) ?? Characters.Default;
        _heroName.Text = Loc.T(c.DisplayName);
        _skinName.Text = SkinName(c, me.SkinId);
        _hatName.Text = Hats.ById(me.HatId) is { } hat ? Loc.T(hat.Name) : Loc.T(Hats.All[0].Name);
        CharacterInfo.Of(c).Into(_infoName, _infoBlurb, _infoStats, _infoPassive);
        // The look can change in the lobby, not once the run has started.
        foreach (Button arrow in _arrows) arrow.Disabled = Coop.InRun || Launch.Automated;

        // Difficulty: the host's (or, out of a session, your own) choice.
        // Only the host and a player not yet in a session can change it.
        DifficultyTier tier = active && DifficultyTiers.ById(Coop.DifficultyId) is { } lobbyTier ? lobbyTier : DifficultyTiers.Selected;
        string tierName = Loc.T(tier.Name);
        _difficulty.Text = Coop.IsClient ? Loc.F("{0} (the host picks)", tierName) : tierName;
        _difficulty.TooltipText = tier.Summary;
        _difficulty.MouseFilter = MouseFilterEnum.Pass;
        _difficulty.AddThemeColorOverride("font_color", DifficultyTiers.IndexOf(tier) switch { 0 => Accent, 1 => Gold, _ => Warn });
        foreach (Button arrow in _difficultyArrows)
        {
            arrow.Visible = !Coop.IsClient;
            arrow.Disabled = Coop.InRun || Launch.Automated;
        }

        RouteDef route = Coop.IsClient && Routes.ById(Coop.RouteId) is { } hostRoute ? hostRoute : Routes.Selected;
        string routeName = Loc.T(route.Name);
        _route.Text = Coop.IsClient ? Loc.F("{0} (the host picks)", routeName) : routeName;
        _route.TooltipText = Loc.T(route.Blurb);
        _route.MouseFilter = MouseFilterEnum.Pass;
        foreach (Button arrow in _routeArrows)
        {
            arrow.Visible = !Coop.IsClient;
            arrow.Disabled = Coop.InRun || Launch.Automated;
        }

        string status;
        bool warn = false;
        if (active)
        {
            status = NetSession.Instance?.Status ?? "";
            if (Coop.IsHost && Coop.Roster.Count >= 2)
                status += "\n" + Loc.T(NetSession.Instance?.CanStart == true ? "Everyone is ready. Press START." : "Waiting for everyone to press READY...");
        }
        else if (busy) status = SteamLobby.Status;
        else if (!steam)
        {
            status = Loc.T("Steam must be running to play co-op.") + "\n" + Loc.T("Start Steam and sign in, then press RETRY.");
            if (Coop.Notice is { } n) status = n + "\n" + status;
            warn = true;
        }
        else
        {
            status = Coop.Notice ?? Loc.T("Host a game and invite your Steam friends, or join a friend's game.");
            warn = Coop.Notice is not null;
        }
        if (_hint is not null) status += "\n" + _hint;
        if (active && Coop.LocalPeer is { } self) _name.Text = self.Name;
        _status.Text = status;
        _status.AddThemeColorOverride("font_color", warn ? Warn : Ink);

        // Who is in: a count here; the players themselves stand on the beach.
        int ready = 0;
        foreach (CoopPeer p in Coop.Roster) if (p.Ready) ready++;
        _players.Visible = active;
        _players.Text = Loc.F("PLAYERS {0}/{1}   ·   ready {2}/{3}", Coop.Roster.Count, Coop.MaxPlayers, ready, Coop.Roster.Count);

        bool viaSteam = NetSession.Instance?.ViaSteam == true;
        _hostSteam.Disabled = active || busy || !steam;
        _invite.Disabled = !(steam && viaSteam && SteamLobby.LobbyId != 0 && !Coop.InRun);
        _retry.Visible = !steam && !active;
        bool showFriends = steam && !active && !busy;
        _friendsTitle.Visible = showFriends;
        _friendsScroll.Visible = showFriends;
        _note.Visible = !active;

        _direct.Visible = ShowDirectIp;
        _host.Disabled = active || busy;
        _join.Disabled = active || busy;
        _port.Editable = !active;
        _address.Editable = !active;
        _name.Editable = !active;

        _ready.Visible = Coop.IsClient;
        _ready.Text = Loc.T(_readyOn ? "NOT READY" : "READY");
        _start.Visible = Coop.IsHost;
        _start.Disabled = NetSession.Instance?.CanStart != true;
        _leave.Visible = active || busy;
        RefreshBrowseView(active, busy, steam);
    }

    /// <summary>Rebuild the friends list, only when it changed (rebuilding
    /// would steal keyboard focus from a JOIN button every two seconds).</summary>
    private void RefreshFriends()
    {
        _friendsAt = 0;
        if (_friends is null) return;
        List<SteamLobby.FriendGame> list = SteamApp.Ready ? SteamLobby.FriendsInGames() : new();
        var key = new StringBuilder();
        foreach (SteamLobby.FriendGame f in list) key.Append(f.SteamId).Append(':').Append(f.Lobby).Append(':').Append(f.Name).Append(';');
        key.Append(SteamApp.Ready ? "+" : "-");
        if (key.ToString() == _friendsKey) return;
        _friendsKey = key.ToString();

        foreach (Node n in _friends.GetChildren()) n.QueueFree();
        if (list.Count == 0)
        {
            _friends.AddChild(MainMenu.MakeLabel("No friends are hosting right now.", 15, InkDim));
            return;
        }
        foreach (SteamLobby.FriendGame f in list)
        {
            SteamLobby.FriendGame friend = f;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(AvatarRect(f.SteamId, Accent));
            Label name = MainMenu.MakeLabel(f.Name, 17, Ink);
            name.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.ClipText = true;
            row.AddChild(name);
            Button join = MainMenu.MenuButton("JOIN", () => JoinFriend(friend));
            join.CustomMinimumSize = new Vector2(100f, 40f);
            row.AddChild(join);
            _friends.AddChild(row);
        }
    }

    /// <summary>A Steam avatar square, or a slot-coloured blank while it loads.</summary>
    internal static Control AvatarRect(ulong steamId, Color fallback)
    {
        var size = new Vector2(28f, 28f);
        if (SteamLobby.Avatar(steamId) is { } tex)
            return new TextureRect
            {
                Texture = tex,
                CustomMinimumSize = size,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
        return new ColorRect { Color = fallback with { A = 0.35f }, CustomMinimumSize = size, SizeFlagsVertical = SizeFlags.ShrinkCenter };
    }

}
