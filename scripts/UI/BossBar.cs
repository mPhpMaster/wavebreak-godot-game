using Godot;
using Wavebreak.Actors;

namespace Wavebreak.UI;

/// <summary>
/// The boss's health across the top of the screen while one is alive.
///
/// A boss is the one enemy whose health the player is meant to track: it decides
/// whether to stand and fight or to kite until the build catches up, and the
/// 50% notch tells them when the fight is about to change. A floating bar over
/// the boss's head would leave the screen whenever the boss does, which in a
/// kiting game is most of the time.
///
/// It polls <see cref="EnemyManager.ActiveBoss"/> rather than listening for
/// events, for the same reason the HUD samples health: the value moves every
/// frame, and sampling one float is the honest implementation.
///
/// It installs itself (see <see cref="Install"/>) so the scene root needs no
/// line for it.
/// </summary>
public sealed partial class BossBar : Control
{
    private const float Width = 560f;
    private const float BarHeight = 16f;
    private const float Top = 82f;          // under the run clock

    private static readonly Color Trough = new(0.05f, 0.06f, 0.09f, 0.85f);
    private static readonly Color Fill = new(0.90f, 0.20f, 0.24f);
    private static readonly Color FillEnraged = new(1.00f, 0.45f, 0.15f);
    private static readonly Color Trail = new(1.00f, 0.92f, 0.80f, 0.85f);
    private static readonly Color Ink = new(0.96f, 0.93f, 0.92f);

    private ColorRect _trail = null!;
    private ColorRect _fill = null!;
    private Label _name = null!;
    private Enemy? _boss;
    private float _shown = 1f;
    private float _trailShown = 1f;
    private float _alpha;
    private int _lastCount = -1;
    private int _lastPhase = -1;

    /// <summary>
    /// Attach a bar to the scene's "UI" canvas layer, found next to
    /// <paramref name="near"/>. Returns false until that layer exists, so the
    /// caller can simply retry; true once attached (or already there).
    /// </summary>
    public static bool Install(Node near)
    {
        Node? root = near.GetParent();
        if (root?.GetNodeOrNull<CanvasLayer>("UI") is not { } ui) return false;
        if (ui.GetNodeOrNull("BossBar") is not null) return true;

        var bar = new BossBar();
        ui.AddChild(bar);
        // Straight after the HUD: above it, but below the level-up panel and the
        // results screen, which must never be covered by a fight in progress.
        ui.MoveChild(bar, Mathf.Min(1, ui.GetChildCount() - 1));
        return true;
    }

    public BossBar()
    {
        Name = "BossBar";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Modulate = new Color(1f, 1f, 1f, 0f);

        _name = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings
            {
                FontSize = 18,
                FontColor = Ink,
                OutlineSize = 6,
                OutlineColor = new Color(0f, 0f, 0f, 0.9f),
            },
        };
        Centre(_name, Top - 2f, 24f);
        AddChild(_name);

        var trough = new ColorRect { Color = Trough, MouseFilter = MouseFilterEnum.Ignore };
        Centre(trough, Top + 24f, BarHeight);
        AddChild(trough);

        _trail = new ColorRect { Color = Trail, MouseFilter = MouseFilterEnum.Ignore };
        _fill = new ColorRect { Color = Fill, MouseFilter = MouseFilterEnum.Ignore };
        foreach (ColorRect r in new[] { _trail, _fill })
        {
            r.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            trough.AddChild(r);
        }

        // The enrage threshold. Marked because it is the moment the fight
        // changes, and a player low on health wants to know how far off it is.
        var notch = new ColorRect { Color = new Color(1f, 1f, 1f, 0.75f), MouseFilter = MouseFilterEnum.Ignore };
        notch.AnchorLeft = notch.AnchorRight = 0.5f;
        notch.AnchorTop = 0f;
        notch.AnchorBottom = 1f;
        notch.OffsetLeft = -1f;
        notch.OffsetRight = 1f;
        notch.OffsetTop = -3f;
        notch.OffsetBottom = 3f;
        trough.AddChild(notch);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        EnemyManager? em = EnemyManager.Instance;

        if (_boss is null || !IsInstanceValid(_boss) || !_boss.IsAlive)
            _boss = em?.ActiveBoss;

        bool show = _boss is not null;
        _alpha = Mathf.MoveToward(_alpha, show ? 1f : 0f, dt * 3f);
        Modulate = new Color(1f, 1f, 1f, _alpha);
        if (_boss is null)
        {
            _shown = _trailShown = 1f;
            return;
        }

        float hp = _boss.HealthFraction;
        // The fill tracks closely; the pale trail behind it lags, so a big hit
        // shows as a chunk that drains rather than a number that jumps.
        _shown = Mathf.Lerp(_shown, hp, 1f - Mathf.Exp(-18f * dt));
        _trailShown = _trailShown < _shown ? _shown : Mathf.MoveToward(_trailShown, _shown, dt * 0.35f);
        _fill.AnchorRight = _shown;
        _trail.AnchorRight = _trailShown;

        int count = em?.BossCount ?? 1;
        int phase = _boss.Phase;
        if (count != _lastCount || phase != _lastPhase)
        {
            _lastCount = count;
            _lastPhase = phase;
            string name = _boss.Def?.DisplayName is { Length: > 0 } n ? n.ToUpperInvariant() : "BOSS";
            _name.Text = name + (phase >= 2 ? "  -  ENRAGED" : "") + (count > 1 ? $"  (+{count - 1})" : "");
            _fill.Color = phase >= 2 ? FillEnraged : Fill;
        }
    }

    private static void Centre(Control c, float top, float height)
    {
        c.AnchorLeft = 0.5f;
        c.AnchorRight = 0.5f;
        c.AnchorTop = 0f;
        c.AnchorBottom = 0f;
        c.OffsetLeft = -Width * 0.5f;
        c.OffsetRight = Width * 0.5f;
        c.OffsetTop = top;
        c.OffsetBottom = top + height;
    }
}
