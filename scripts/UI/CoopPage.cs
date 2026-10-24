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
/// plus the lobby itself: Steam avatar, name, character and skin, READY, and
/// START for the host. Invites accepted in the overlay, "Join Game" in Steam
/// and <c>+connect_lobby</c> all land here (<see cref="SteamLobby.PendingJoin"/>).
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
    private readonly List<Button> _arrows = new();
    private Label _status = null!;
    private VBoxContainer _lobby = null!;
    private bool _readyOn;

    public CoopPage(Action back)
    {
        _back = back;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        LoadSettings();

        var column = new VBoxContainer();
        MainMenu.Place(column, 60f, 186f, 420f, 520f);
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        column.AddChild(MainMenu.MakeLabel("CO-OP", 22, InkDim));
        _hostSteam = MainMenu.MenuButton("HOST (Steam)", HostSteam);
        column.AddChild(_hostSteam);
        _invite = MainMenu.MenuButton("INVITE FRIENDS", Invite);
        column.AddChild(_invite);

        // Who you play as, chosen here (the PLAY page starts a solo run on a
        // click). Only unlocked characters and skins; the choice is saved and
        // the lobby updates at once.
        _heroName = Picker(column, "Character", () => CycleCharacter(-1), () => CycleCharacter(+1));
        _skinName = Picker(column, "Skin", () => CycleSkin(-1), () => CycleSkin(+1));

        // Friends in a Wavebreak lobby, each with JOIN.
        _friendsTitle = MainMenu.MakeLabel("FRIENDS IN A GAME", 15, InkDim);
        column.AddChild(_friendsTitle);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 96f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        _friends = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _friends.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_friends);
        _friendsScroll = scroll;
        column.AddChild(scroll);

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

        column.AddChild(MainMenu.MenuButton("BACK", () => { LeaveSession(); _readyOn = false; _back(); }));

        // Right: the lobby and the session buttons.
        var panel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 16, ContentMarginBottom = 16 };
        style.SetCornerRadiusAll(6);
        panel.AddThemeStyleboxOverride("panel", style);
        MainMenu.Place(panel, 510f, 186f, 680f, 520f);
        AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        _status = MainMenu.MakeLabel("", 17, Ink);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.AutoTranslateMode = AutoTranslateModeEnum.Disabled;   // already translated, may hold a name
        box.AddChild(_status);
        _retry = MainMenu.MenuButton("RETRY", Retry);
        _retry.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _retry.CustomMinimumSize = new Vector2(180f, 44f);
        box.AddChild(_retry);

        box.AddChild(MainMenu.MakeLabel("PLAYERS (up to 4)", 15, InkDim));
        _lobby = new VBoxContainer();
        _lobby.AddThemeConstantOverride("separation", 4);
        _lobby.CustomMinimumSize = new Vector2(0f, 136f);
        box.AddChild(_lobby);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        _ready = MainMenu.MenuButton("READY", ToggleReady);
        _start = MainMenu.MenuButton("START", () => NetSession.Instance?.StartGame());
        _leave = MainMenu.MenuButton("LEAVE", () => { LeaveSession(); _readyOn = false; Refresh(); });
        foreach (Button b in new[] { _ready, _start, _leave })
        {
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            buttons.AddChild(b);
        }
        box.AddChild(buttons);

        Label note = MainMenu.MakeLabel(
            "Play with your Steam friends: HOST (Steam) opens a friends-only lobby and INVITE FRIENDS opens the Steam overlay. " +
            "Friends join from the invite, with \"Join Game\" on your name in Steam, or with JOIN in their list. No addresses or ports. " +
            "Steam shows the game as \"Spacewar\" (Valve's test app) until Wavebreak has its own Steam page. " +
            "Each player keeps their own character, weapons, level-ups and items; experience is shared. " +
            "A downed player is revived by a teammate standing next to them.", 14, InkDim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(note);

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
        if (!IsVisibleInTree() || !SteamApp.Ready) return;
        _friendsAt += delta;
        if (_friendsAt >= FriendsEvery) RefreshFriends();
    }

    private void OnSteamChanged()
    {
        RefreshFriends();
        Refresh();
    }

    // ------------------------------------------------------------ actions

    private void Retry()
    {
        _hint = null;
        if (SteamApp.TryInit(GetTree()))
        {
            Coop.Notice = null;
            SteamLobby.ConsumePendingJoin();
            _hostSteam.GrabFocus();
        }
        RefreshFriends();
        Refresh();
    }

    private void HostSteam()
    {
        if (!SteamApp.TryInit(GetTree())) { Refresh(); return; }
        Coop.Notice = null;
        _hint = null;
        _readyOn = false;
        SteamLobby.Host();
        Refresh();
    }

    private void Invite()
    {
        // The overlay may not hook a game Steam did not launch, so the
        // friends-list route is always shown alongside.
        SteamLobby.OpenInviteDialog();
        _hint = Loc.T("No Steam window? Invite from your Steam friends list: right-click a friend, Invite to Lobby.");
        Refresh();
    }

    private void JoinFriend(SteamLobby.FriendGame f)
    {
        _hint = null;
        _readyOn = false;
        SteamLobby.Join(f.Lobby, f.Name);
        Refresh();
    }

    private void HostDirect()
    {
        if (!int.TryParse(_port.Text.Trim(), out int port) || port is < 1 or > 65535)
        {
            Coop.Notice = Loc.T("The port must be a number from 1 to 65535.");
            Refresh();
            return;
        }
        Coop.Notice = null;
        SaveSettings();
        NetSession.Ensure(GetTree()).Host(port);
    }

    private void JoinDirect()
    {
        if (!NetSession.ParseAddress(_address.Text, out _, out _))
        {
            Coop.Notice = Loc.T("Enter an address like 192.168.1.20:7777.");
            Refresh();
            return;
        }
        Coop.Notice = null;
        _readyOn = false;
        SaveSettings();
        NetSession.Ensure(GetTree()).Join(_address.Text.Trim());
    }

    private void ToggleReady()
    {
        if (!Coop.IsClient) return;
        _readyOn = !_readyOn;
        NetSession.Instance?.SetReady(_readyOn);
        Refresh();
    }

    // ------------------------------------------------------------ view

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
        // The look can change in the lobby, not once the run has started.
        foreach (Button arrow in _arrows) arrow.Disabled = Coop.InRun || Launch.Automated;

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

        foreach (Node n in _lobby.GetChildren()) n.QueueFree();
        bool viaSteam = NetSession.Instance?.ViaSteam == true;
        foreach (CoopPeer p in Coop.Roster)
        {
            CharacterDef pc = Characters.ById(p.CharacterId) ?? Characters.Default;
            string state = p.Id == 1 ? Loc.T("host") : p.Ready ? Loc.T("ready") : Loc.T("not ready");
            string line = Loc.F("{0}  ·  {1} ({2})  ·  {3}", p.Name, pc.DisplayName, SkinName(pc, p.SkinId), state);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            if (viaSteam) row.AddChild(AvatarRect(SteamLobby.SteamIdForPeer(p.Id), Coop.SlotColor(p.Slot)));
            Label l = MainMenu.MakeLabel(line, 17, Coop.SlotColor(p.Slot));
            l.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
            l.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(l);
            _lobby.AddChild(row);
        }

        _hostSteam.Disabled = active || busy || !steam;
        _invite.Disabled = !(steam && viaSteam && SteamLobby.LobbyId != 0 && !Coop.InRun);
        _retry.Visible = !steam && !active;
        bool showFriends = steam && !active && !busy;
        _friendsTitle.Visible = showFriends;
        _friendsScroll.Visible = showFriends;

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
    private static Control AvatarRect(ulong steamId, Color fallback)
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

    // ------------------------------------------------------------ character and skin

    private Label Picker(VBoxContainer column, string name, Action prev, Action next)
    {
        var value = MainMenu.MakeLabel("", 18, Accent);
        value.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        value.ClipText = true;
        // Right to left, "previous" sits on the right and points that way.
        Button a = Arrow(Loc.IsRtl ? ">" : "<", prev);
        Button b = Arrow(Loc.IsRtl ? "<" : ">", next);
        column.AddChild(Row(name, a, value, b));
        return value;
    }

    private Button Arrow(string text, Action pressed)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(44f, 40f) };
        b.AddThemeFontSizeOverride("font_size", 20);
        b.Pressed += pressed;
        _arrows.Add(b);
        return b;
    }

    private static CharacterDef Current()
        => Characters.ById(Profile.SelectedCharacter) is { IsAvailable: true } c ? c : Characters.Default;

    private void CycleCharacter(int step)
    {
        var open = new List<CharacterDef>();
        foreach (CharacterDef c in Characters.All) if (c.IsAvailable) open.Add(c);
        if (open.Count == 0) return;
        int i = Math.Max(0, open.IndexOf(Current()));
        Profile.SelectedCharacter = open[((i + step) % open.Count + open.Count) % open.Count].Id;
        LookChanged();
    }

    private void CycleSkin(int step)
    {
        CharacterDef c = Current();
        var open = new List<SkinDef>();
        foreach (SkinDef s in c.Skins) if (s.IsAvailable) open.Add(s);
        if (open.Count == 0) return;
        int i = Math.Max(0, open.IndexOf(c.SelectedSkin));
        Profile.SelectSkin(c.Id, open[((i + step) % open.Count + open.Count) % open.Count].Id);
        LookChanged();
    }

    private void LookChanged()
    {
        Profile.Save();
        NetSession.Instance?.SendLook();
        Refresh();
    }

    private static string SkinName(CharacterDef c, string skinId)
    {
        foreach (SkinDef s in c.Skins) if (s.Id == skinId) return Loc.T(s.Name);
        return Loc.T(c.Skins[0].Name);
    }

    private static LineEdit Field(string text, int maxLength)
    {
        var f = new LineEdit { Text = text, MaxLength = maxLength, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(150f, 40f) };
        f.AddThemeFontSizeOverride("font_size", 18);
        f.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return f;
    }

    private static Control Row(string name, params Control[] controls)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        Label l = MainMenu.MakeLabel(name, 17, Ink);
        l.CustomMinimumSize = new Vector2(110f, 0f);
        l.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(l);
        foreach (Control c in controls)
        {
            if (c is Button b && b.CustomMinimumSize.X < 1f) b.CustomMinimumSize = new Vector2(110f, 44f);
            row.AddChild(c);
        }
        return row;
    }

    // ------------------------------------------------------------ settings

    private string _savedPort = Coop.DefaultPort.ToString();
    private string _savedAddress = "127.0.0.1:" + Coop.DefaultPort;

    private void LoadSettings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) return;
        string name = cfg.GetValue("coop", "name", "").AsString();
        if (name.Length > 0) NetSession.PlayerName = NetSession.CleanName(name);
        _savedPort = cfg.GetValue("coop", "port", _savedPort).AsString();
        _savedAddress = cfg.GetValue("coop", "address", _savedAddress).AsString();
    }

    private void SaveSettings()
    {
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);   // keep the other sections
        cfg.SetValue("coop", "name", NetSession.PlayerName);
        cfg.SetValue("coop", "port", _port?.Text.Trim() ?? _savedPort);
        cfg.SetValue("coop", "address", _address?.Text.Trim() ?? _savedAddress);
        cfg.Save(SettingsPath);
    }
}
