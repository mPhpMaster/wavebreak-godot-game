using Godot;
using Wavebreak.Actors;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// The co-op overlay in a run: the team (name, health, down) on the left,
/// a waiting line until everyone has loaded, and, when this player is down,
/// how to get back up and how far the revive has got.
/// </summary>
public sealed partial class CoopHud : Control
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color Down = new(1.00f, 0.45f, 0.40f);

    private VBoxContainer _team = null!;
    private Label _center = null!;
    private float _refresh;

    public override void _Ready()
    {
        Name = "CoopHud";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.03f, 0.04f, 0.07f, 0.55f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6 };
        style.SetCornerRadiusAll(5);
        panel.AddThemeStyleboxOverride("panel", style);
        // Bottom-left: the only corner the HUD leaves free (level, weapons and
        // tomes run down the left from the top). Grows upward with the team.
        panel.AnchorLeft = 0f; panel.AnchorRight = 0f; panel.AnchorTop = 1f; panel.AnchorBottom = 1f;
        panel.OffsetLeft = 14f; panel.OffsetRight = 250f; panel.OffsetTop = -16f; panel.OffsetBottom = -16f;
        panel.GrowVertical = GrowDirection.Begin;
        AddChild(panel);
        _team = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _team.AddThemeConstantOverride("separation", 2);
        panel.AddChild(_team);

        _center = MainMenu.MakeLabel("", 24, Ink);
        _center.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        _center.HorizontalAlignment = HorizontalAlignment.Center;
        _center.AnchorLeft = 0f; _center.AnchorRight = 1f; _center.AnchorTop = 0.62f; _center.AnchorBottom = 0.62f;
        _center.OffsetTop = 0f; _center.OffsetBottom = 70f;
        AddChild(_center);
    }

    public override void _Process(double delta)
    {
        _refresh -= (float)delta;
        if (_refresh > 0f) return;
        _refresh = 0.1f;

        NetSession? s = NetSession.Instance;
        if (s is null || !Coop.InRun)
        {
            Visible = false;
            return;
        }
        Visible = true;

        _row = 0;
        Player? me = Player.Instance;
        if (Coop.LocalPeer is { } mine)
            AddRow(Coop.LocalId, mine.Name, mine.Slot, me?.Health?.Fraction ?? 0f, me is not null && !me.IsAlive, true);
        foreach (RemotePlayer r in s.Remotes.Values)
            if (IsInstanceValid(r)) AddRow(r.PeerId, r.DisplayName, r.Slot, r.Health01, r.Down, false);
        for (int i = 0; i < _team.GetChildCount(); i++) ((Label)_team.GetChild(i)).Visible = i < _row;

        if (!s.Started)
            _center.Text = Loc.T("Waiting for everyone to load...");
        else if (me is not null && !me.IsAlive && Game.Main.Instance?.Run.Over != true)
            _center.Text = Loc.F("You are down. A teammate standing next to you revives you ({0}%).",
                                 Mathf.RoundToInt(s.LocalRevive01 * 100f));
        else
            _center.Text = "";
        _center.AddThemeColorOverride("font_color", me is not null && !me.IsAlive ? Down : Ink);
    }

    private void AddRow(long peerId, string name, int slot, float health, bool down, bool you)
    {
        string text = down ? Loc.F("{0}  DOWN", name)
                    : Loc.F("{0}  {1}%", name, Mathf.RoundToInt(Mathf.Clamp(health, 0f, 1f) * 100f));
        Game.RunScoreboard.RefreshMeters();
        text += "  ·  " + Loc.F("DPS {0}", Game.RunScoreboard.Short(Game.RunScoreboard.Find(peerId)?.Dps ?? 0));
        if (you) text = Loc.F("{0} (you)", text);
        Label l;
        if (_row < _team.GetChildCount()) l = (Label)_team.GetChild(_row);
        else
        {
            l = MainMenu.MakeLabel("", 16, Ink);
            l.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
            _team.AddChild(l);
        }
        _row++;
        l.Text = text;
        l.AddThemeColorOverride("font_color", down ? Down : Coop.SlotColor(slot));
    }

    private int _row;
}
