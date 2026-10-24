using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// Two short messages:
/// <list type="bullet">
/// <item>The boss-kill toast, "BOSS DEFEATED / Guest defeated Thresher
/// Prime!", in the killer's colour, under where the boss bar was. Raised by
/// <see cref="BossDefeated"/> (single-player: this machine; co-op: the host's
/// <c>BossSlain</c> to everyone, crediting the last hitter).</item>
/// <item>Co-op: "Guest is down! Go revive them" under the phase line, for a
/// few seconds after a teammate goes down, and gone the moment they are back
/// up. Polled from the remote players' down flags (10 Hz), so it needs no
/// message of its own.</item>
/// </list>
/// </summary>
public sealed partial class AwarenessAlerts : Control
{
    /// <summary>Down alerts raised this run (the co-op test checks it).</summary>
    public static int DownAlertsShown;

    private const float ToastSeconds = 4.2f, FadeSeconds = 0.35f, ToastWidth = 560f;
    private const float AlertSeconds = 6f;

    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    private static readonly Color Ink = new(0.94f, 0.96f, 0.99f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);
    private static readonly Color DownRed = new(1.00f, 0.42f, 0.40f);

    private readonly Queue<(string Body, Color Accent)> _toasts = new();
    private PanelContainer _panel = null!;
    private StyleBoxFlat _style = null!;
    private Label _head = null!, _body = null!;
    private float _toastTimer;

    private Label _alert = null!;
    private readonly Dictionary<long, bool> _wasDown = new();
    private long _alertFor;
    private float _alertLeft;
    private float _poll, _time;

    public override void _Ready()
    {
        Name = "AwarenessAlerts";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _style = new StyleBoxFlat { BgColor = new Color(0.06f, 0.07f, 0.11f, 0.92f), BorderColor = Gold, ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 8, ContentMarginBottom = 10 };
        _style.SetBorderWidthAll(0);
        _style.BorderWidthTop = 4;
        _style.BorderWidthBottom = 4;
        _style.SetCornerRadiusAll(14);
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", _style);
        _panel.AnchorLeft = 0.5f; _panel.AnchorRight = 0.5f;
        _panel.AnchorTop = 1f; _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = -ToastWidth / 2f; _panel.OffsetRight = ToastWidth / 2f;
        // Low centre, above the dash meter: the upper middle belongs to the
        // map title card ("PORTAL OPEN" lands the same moment).
        _panel.OffsetTop = -186f; _panel.OffsetBottom = -96f;
        _panel.GrowVertical = GrowDirection.Begin;
        AddChild(_panel);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddChild(box);
        _head = MakeLabel(15, Gold);
        _body = MakeLabel(24, Ink);
        box.AddChild(_head);
        box.AddChild(_body);

        _alert = MakeLabel(20, DownRed);
        _alert.AnchorLeft = 0f; _alert.AnchorRight = 1f;
        _alert.OffsetTop = 122f; _alert.OffsetBottom = 150f;
        _alert.Visible = false;
        AddChild(_alert);

        EventBus.Subscribe<BossDefeated>(OnBossDefeated);
    }

    public override void _ExitTree() => EventBus.Unsubscribe<BossDefeated>(OnBossDefeated);

    private void OnBossDefeated(BossDefeated e)
    {
        Color accent = Coop.Active && Coop.Peer(e.KillerId) is { } p ? Coop.SlotColor(p.Slot) : Gold;
        _toasts.Enqueue((Loc.F("{0} defeated {1}!", e.KillerName, Loc.T(e.Boss)), accent));
        GD.Print($"[hud] boss toast: {e.KillerName} defeated {e.Boss}");
        if (!_panel.Visible) NextToast();
    }

    private void NextToast()
    {
        if (_toasts.Count == 0) { _panel.Visible = false; return; }
        var (body, accent) = _toasts.Dequeue();
        _head.Text = Loc.T("BOSS DEFEATED");
        _body.Text = body;
        _body.AddThemeColorOverride("font_color", accent);
        _style.BorderColor = accent;
        _panel.Visible = true;
        _panel.Modulate = Colors.White with { A = 0f };
        _toastTimer = 0f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        if (_panel.Visible)
        {
            _toastTimer += dt;
            float a = _toastTimer < FadeSeconds ? _toastTimer / FadeSeconds
                    : _toastTimer > ToastSeconds - FadeSeconds ? (ToastSeconds - _toastTimer) / FadeSeconds : 1f;
            _panel.Modulate = Colors.White with { A = Mathf.Clamp(a, 0f, 1f) };
            if (_toastTimer >= ToastSeconds) NextToast();
        }

        if (_alert.Visible)
        {
            _alertLeft -= dt;
            _alert.Modulate = Colors.White with { A = 0.65f + 0.35f * Mathf.Sin(_time * 6f) };
        }
        _poll -= dt;
        if (_poll > 0f) return;
        _poll = 0.1f;
        PollDowns();
    }

    private void PollDowns()
    {
        if (!Coop.InRun || NetSession.Instance is not { } s)
        {
            _alert.Visible = false;
            return;
        }
        RemotePlayer? shown = null;
        foreach (RemotePlayer r in s.Remotes.Values)
        {
            if (!IsInstanceValid(r) || !r.HasState) continue;
            _wasDown.TryGetValue(r.PeerId, out bool was);
            if (r.Down && !was)
            {
                DownAlertsShown++;
                _alertFor = r.PeerId;
                _alertLeft = AlertSeconds;
            }
            _wasDown[r.PeerId] = r.Down;
            if (r.PeerId == _alertFor) shown = r;
        }
        // Revived (or gone): clear at once. Otherwise a few seconds, then the
        // tag, the minimap SOS and the beam keep saying it.
        bool on = shown is { Down: true } && _alertLeft > 0f;
        if (on) _alert.Text = Loc.F("{0} is down! Go revive them", shown!.DisplayName);
        _alert.Visible = on;
    }

    private static Label MakeLabel(int size, Color color)
    {
        var l = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, AutoTranslateMode = AutoTranslateModeEnum.Disabled };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", Outline);
        l.AddThemeConstantOverride("outline_size", 5);
        return l;
    }
}
