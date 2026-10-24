using Godot;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The rest room's card under the phase readout: where you are, what there is
/// to do, where you go next, and in co-op how many of the team stand in the
/// CONTINUE portal (with the host's countdown once someone is in). Owned by
/// the <see cref="RestRoom"/> node, so it leaves with the room.
/// </summary>
public sealed partial class RestRoomHud : Control
{
    private readonly string _next;
    private Label _team = null!;
    private PanelContainer _panel = null!;
    private string _lastTeam = "";

    public RestRoomHud() : this("") { }

    public RestRoomHud(string nextMapName) => _next = nextMapName;

    public override void _Ready()
    {
        Name = "RestRoomHud";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;

        _panel = CuteStyle.Panel(CuteStyle.Cream, CuteStyle.Butter, 18, 10);
        // Bottom centre, above the dash meter: the top of the screen is the
        // phase readout, and the middle is the room itself.
        _panel.AnchorLeft = 0.5f; _panel.AnchorRight = 0.5f;
        _panel.AnchorTop = 1f; _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = -330f; _panel.OffsetRight = 330f;
        _panel.OffsetTop = -96f; _panel.OffsetBottom = -96f;
        _panel.GrowHorizontal = GrowDirection.Both;
        _panel.GrowVertical = GrowDirection.Begin;
        AddChild(_panel);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        _panel.AddChild(col);

        col.AddChild(CuteStyle.Text(Loc.T("REST STOP"), 22, CuteStyle.Ink, HorizontalAlignment.Center));
        Label how = CuteStyle.Text(Loc.T("Shop at the merchant, rest by the campfire, then step into the portal."), 15,
                                   CuteStyle.InkSoft, HorizontalAlignment.Center);
        how.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(how);
        if (_next.Length > 0)
            col.AddChild(CuteStyle.Text(Loc.F("Next: {0}", Loc.T(_next)), 15, CuteStyle.Ink, HorizontalAlignment.Center));
        _team = CuteStyle.Text("", 17, new Color(0.20f, 0.55f, 0.45f), HorizontalAlignment.Center);
        _team.Visible = false;
        col.AddChild(_team);
    }

    public override void _Process(double delta)
    {
        // Out of the way of the level-up cards and the pause menu.
        _panel.Visible = !Net.Coop.IsPaused(GetTree());
        if (RestRoom.Inside < 0)
        {
            _team.Visible = false;
            return;
        }
        string text = Loc.F("In the portal: {0}/{1}", RestRoom.Inside, Mathf.Max(1, RestRoom.Total));
        if (RestRoom.Countdown >= 0f)
            text += "   " + Loc.F("leaving in {0}s", Mathf.CeilToInt(RestRoom.Countdown));
        _team.Visible = true;
        if (text == _lastTeam) return;
        _lastTeam = text;
        _team.Text = text;
    }
}
