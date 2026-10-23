using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// "UNLOCKED: ..." and "SECRET FOUND: ..." banners, top centre, one at a time.
///
/// Queued rather than stacked: two unlocks can land in the same frame (the end
/// of a run often completes several) and a stack of banners is unreadable,
/// while a short queue gives each one its moment. Runs while paused, because
/// the run-end unlocks fire under the results screen, which pauses the tree.
/// </summary>
public sealed partial class UnlockToast : Control
{
    private const float ShowSeconds = 3.4f;
    private const float FadeSeconds = 0.35f;
    private const float Width = 520f;

    private static readonly Color Back = new(0.06f, 0.07f, 0.11f, 0.94f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    private static readonly Color Ink = new(0.92f, 0.95f, 0.99f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);

    private readonly Queue<(string head, string body, Color accent)> _queue = new();
    private PanelContainer _panel = null!;
    private Label _head = null!;
    private Label _body = null!;
    private StyleBoxFlat _style = null!;
    private float _timer;

    public override void _Ready()
    {
        Name = "UnlockToast";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _style = new StyleBoxFlat { BgColor = Back, BorderColor = Gold, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 10, ContentMarginBottom = 12 };
        _style.SetBorderWidthAll(0);
        _style.BorderWidthTop = 4;
        _style.SetCornerRadiusAll(6);

        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", _style);
        _panel.AnchorLeft = 0.5f; _panel.AnchorRight = 0.5f;
        _panel.OffsetLeft = -Width / 2f; _panel.OffsetRight = Width / 2f;
        // Top edge: at 92 px it landed on the results screen's title ("YOU
        // SURVIVED"), which is exactly when the end-of-run unlocks arrive.
        _panel.OffsetTop = 8f;
        AddChild(_panel);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddChild(box);
        _head = MakeLabel(22, Gold);
        _body = MakeLabel(16, Ink);
        box.AddChild(_head);
        box.AddChild(_body);

        EventBus.Subscribe<UnlockEarned>(OnUnlock);
        EventBus.Subscribe<SecretDiscovered>(OnSecret);
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<UnlockEarned>(OnUnlock);
        EventBus.Unsubscribe<SecretDiscovered>(OnSecret);
    }

    private void OnUnlock(UnlockEarned e)
    {
        UnlockDef? u = Unlocks.ById(e.Id);
        if (u is null) return;
        Enqueue($"UNLOCKED: {u.Name}", Unlocks.DescribeReward(u), Gold);
    }

    private void OnSecret(SecretDiscovered e)
    {
        foreach (SecretKindDef k in SecretSpawner.Kinds)
            if (k.Id == e.Kind) Enqueue($"SECRET FOUND: {k.DisplayName}", k.RewardText, k.Glow);
    }

    /// <summary>Public so other systems (or a test) can raise a banner.</summary>
    public void Enqueue(string head, string body, Color accent)
    {
        _queue.Enqueue((head, body, accent));
        if (!_panel.Visible) Next();
    }

    private void Next()
    {
        if (_queue.Count == 0) { _panel.Visible = false; return; }
        var (head, body, accent) = _queue.Dequeue();
        _head.Text = head;
        _head.AddThemeColorOverride("font_color", accent);
        _body.Text = body;
        _style.BorderColor = accent;
        _panel.Visible = true;
        _panel.Modulate = Colors.White with { A = 0f };
        _timer = 0f;
    }

    public override void _Process(double delta)
    {
        if (!_panel.Visible) return;
        _timer += (float)delta;
        float a = _timer < FadeSeconds ? _timer / FadeSeconds
                : _timer > ShowSeconds - FadeSeconds ? (ShowSeconds - _timer) / FadeSeconds : 1f;
        _panel.Modulate = Colors.White with { A = Mathf.Clamp(a, 0f, 1f) };
        if (_timer >= ShowSeconds) Next();
    }

    private static Label MakeLabel(int size, Color color)
    {
        var l = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", Outline);
        l.AddThemeConstantOverride("outline_size", 4);
        return l;
    }
}
