using System;
using Godot;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// The title screen's CO-OP page: host a game on a port or join one by
/// address, see who is in the lobby with their character and skin, ready up,
/// and (host) start. Built like the other title pages: plain controls, every
/// one focusable, every string through <see cref="Loc"/>.
///
/// Direct IP only, no matchmaking service: on a LAN the host's local address
/// works as is; over the internet the host forwards the port, or everyone
/// joins the same free virtual LAN (ZeroTier, Tailscale). The page says so.
/// </summary>
public sealed partial class CoopPage : Control
{
    /// <summary>Set when a co-op run ended under the player (the host left):
    /// the title opens on this page so the message is seen.</summary>
    public static bool OpenOnTitle { get; set; }

    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color Accent = new(0.45f, 0.82f, 1.00f);
    private static readonly Color Warn = new(1.00f, 0.72f, 0.45f);
    private static readonly Color PanelBack = new(0.05f, 0.06f, 0.10f, 0.86f);

    private const string SettingsPath = "user://settings.cfg";

    private readonly Action _back;

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
    private readonly System.Collections.Generic.List<Button> _arrows = new();
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

        _name = Field(NetSession.PlayerName, 16);
        _name.TextChanged += t => { NetSession.PlayerName = NetSession.CleanName(t); SaveSettings(); };
        column.AddChild(Row("Your name", _name));

        _port = Field(_savedPort, 5);
        _host = MainMenu.MenuButton("HOST", Host);
        column.AddChild(Row("Port", _port, _host));

        _address = Field(_savedAddress, 40);
        _address.PlaceholderText = "192.168.1.20:7777";
        _join = MainMenu.MenuButton("JOIN", Join);
        column.AddChild(Row("Address", _address, _join));

        // Who you play as, chosen here (the PLAY page starts a solo run on a
        // click). Only unlocked characters and skins; the choice is saved and
        // the lobby updates at once.
        _heroName = Picker(column, "Character", () => CycleCharacter(-1), () => CycleCharacter(+1));
        _skinName = Picker(column, "Skin", () => CycleSkin(-1), () => CycleSkin(+1));

        column.AddChild(MainMenu.MenuButton("BACK", () => { Leave(); _back(); }));

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
        _status.AutoTranslateMode = AutoTranslateModeEnum.Disabled;   // already translated, may hold an address
        box.AddChild(_status);

        box.AddChild(MainMenu.MakeLabel("PLAYERS (up to 4)", 15, InkDim));
        _lobby = new VBoxContainer();
        _lobby.AddThemeConstantOverride("separation", 4);
        _lobby.CustomMinimumSize = new Vector2(0f, 150f);
        box.AddChild(_lobby);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        _ready = MainMenu.MenuButton("READY", ToggleReady);
        _start = MainMenu.MenuButton("START", () => NetSession.Instance?.StartGame());
        _leave = MainMenu.MenuButton("LEAVE", Leave);
        foreach (Button b in new[] { _ready, _start, _leave })
        {
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            buttons.AddChild(b);
        }
        box.AddChild(buttons);

        Label note = MainMenu.MakeLabel(
            "Direct connection. On the same network (LAN), join the host's local address, for example 192.168.1.20:7777. " +
            "Over the internet the host must forward the port (UDP) on their router, or everyone joins the same free virtual LAN " +
            "such as ZeroTier or Tailscale and uses that address. Each player keeps their own character, weapons, level-ups and items; " +
            "experience is shared. A downed player is revived by a teammate standing next to them.", 14, InkDim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(note);

        NetSession.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree() => NetSession.Changed -= Refresh;

    /// <summary>Called when the page is shown.</summary>
    public void Open()
    {
        OpenOnTitle = false;
        Refresh();
        if (Coop.IsHost) _start.GrabFocus();
        else if (Coop.IsClient) _ready.GrabFocus();
        else _host.GrabFocus();
    }

    // ------------------------------------------------------------ actions

    private void Host()
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

    private void Join()
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

    private void Leave()
    {
        if (NetSession.Instance is { } s && !Coop.InRun) s.Leave();
        _readyOn = false;
        Refresh();
    }

    // ------------------------------------------------------------ view

    private void Refresh()
    {
        if (!IsInsideTree() || _status is null) return;
        bool active = Coop.Active;

        CoopPeer me = NetSession.LocalProfile();
        CharacterDef c = Characters.ById(me.CharacterId) ?? Characters.Default;
        _heroName.Text = Loc.T(c.DisplayName);
        _skinName.Text = SkinName(c, me.SkinId);
        // The look can change in the lobby, not once the run has started.
        foreach (Button arrow in _arrows) arrow.Disabled = Coop.InRun || Launch.Automated;

        string status = NetSession.Instance?.Status ?? "";
        if (!active) status = Coop.Notice ?? Loc.T("Host a game or join one.");
        else if (Coop.IsHost && Coop.Roster.Count >= 2)
            status += "\n" + Loc.T(NetSession.Instance?.CanStart == true ? "Everyone is ready. Press START." : "Waiting for everyone to press READY...");
        if (active && Coop.LocalPeer is { } self) _name.Text = self.Name;
        _status.Text = status;
        _status.AddThemeColorOverride("font_color", !active && Coop.Notice is not null ? Warn : Ink);

        foreach (Node n in _lobby.GetChildren()) n.QueueFree();
        foreach (CoopPeer p in Coop.Roster)
        {
            CharacterDef pc = Characters.ById(p.CharacterId) ?? Characters.Default;
            string state = p.Id == 1 ? Loc.T("host") : p.Ready ? Loc.T("ready") : Loc.T("not ready");
            string line = Loc.F("{0}  ·  {1} ({2})  ·  {3}", p.Name, pc.DisplayName, SkinName(pc, p.SkinId), state);
            Label l = MainMenu.MakeLabel(line, 17, Coop.SlotColor(p.Slot));
            l.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
            _lobby.AddChild(l);
        }

        _host.Disabled = active;
        _join.Disabled = active;
        _port.Editable = !active;
        _address.Editable = !active;
        _name.Editable = !active;
        _ready.Visible = Coop.IsClient;
        _ready.Text = Loc.T(_readyOn ? "NOT READY" : "READY");
        _start.Visible = Coop.IsHost;
        _start.Disabled = NetSession.Instance?.CanStart != true;
        _leave.Visible = active;
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
        var open = new System.Collections.Generic.List<CharacterDef>();
        foreach (CharacterDef c in Characters.All) if (c.IsAvailable) open.Add(c);
        if (open.Count == 0) return;
        int i = Math.Max(0, open.IndexOf(Current()));
        Profile.SelectedCharacter = open[((i + step) % open.Count + open.Count) % open.Count].Id;
        LookChanged();
    }

    private void CycleSkin(int step)
    {
        CharacterDef c = Current();
        var open = new System.Collections.Generic.List<SkinDef>();
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
