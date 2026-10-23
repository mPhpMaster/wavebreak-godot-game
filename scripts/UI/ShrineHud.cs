using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// Shrine overlay, drawn by the shrine spawner's own canvas layer so the shared
/// HUD is untouched:
///
/// - a banner under the phase readout while a challenge runs ("KILL 23/40 · 17s");
/// - a one-line readout of greed stacks once any are taken;
/// - small diamond markers over unused shrines in view, with the distance, the
///   cheap stand-in for a minimap.
/// </summary>
public sealed partial class ShrineHud : Control
{
    private const float MarkerRange = 70f;
    private static readonly Color Shadow = new(0.02f, 0.03f, 0.05f, 0.85f);

    private Label _banner = null!;
    private Label _greed = null!;

    public override void _Ready()
    {
        Name = "ShrineHud";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        // Runs while paused only to hide itself: this layer sits above the
        // shared UI, and markers must not draw over the level-up cards.
        ProcessMode = ProcessModeEnum.Always;

        _banner = MakeLabel(24);
        Place(_banner, 118f, 34f);
        AddChild(_banner);

        _greed = MakeLabel(15);
        _greed.AddThemeColorOverride("font_color", MapShrines.Tint(MapShrineKind.Greed).Lightened(0.35f));
        Place(_greed, 154f, 22f);
        AddChild(_greed);
    }

    public override void _Process(double delta)
    {
        Visible = !GetTree().Paused;
        ShrineSpawner? s = ShrineSpawner.Instance;
        if (s is null || !Visible) { _banner.Visible = false; _greed.Visible = false; return; }

        string? text = s.ChallengeText;
        _banner.Visible = text is not null;
        if (text is not null)
        {
            _banner.Text = text;
            float pulse = s.ChallengeSecondsLeft < 8f ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.GetTicksMsec() / 160f)) : 1f;
            _banner.AddThemeColorOverride("font_color", MapShrines.Tint(MapShrineKind.Challenge).Lightened(0.3f) * new Color(1f, 1f, 1f, pulse));
        }

        _greed.Visible = s.GreedStacks > 0;
        if (_greed.Visible) _greed.Text = Loc.F("GREED x{0}", s.GreedStacks);

        QueueRedraw();
    }

    public override void _Draw()
    {
        ShrineSpawner? s = ShrineSpawner.Instance;
        Camera3D? cam = GetViewport().GetCamera3D();
        if (s is null || cam is null || Player.Instance is not { } player || !IsInstanceValid(player)) return;
        Font font = ThemeDB.FallbackFont;
        Vector2 size = GetViewportRect().Size;

        foreach (MapShrine shrine in s.Shrines)
        {
            if (shrine.Spent || !IsInstanceValid(shrine)) continue;
            Vector3 world = shrine.Top + Vector3.Up * 0.6f;
            float dist = new Vector2(world.X - player.GlobalPosition.X, world.Z - player.GlobalPosition.Z).Length();
            if (dist > MarkerRange || dist < 9f || cam.IsPositionBehind(world)) continue;
            Vector2 p = cam.UnprojectPosition(world);
            if (p.X < 0f || p.Y < 0f || p.X > size.X || p.Y > size.Y) continue;

            Color c = shrine.Tint;
            float r = 9f;
            Vector2[] diamond = { p + new Vector2(0, -r), p + new Vector2(r, 0), p + new Vector2(0, r), p + new Vector2(-r, 0) };
            Vector2[] back = { p + new Vector2(0, -r - 2), p + new Vector2(r + 2, 0), p + new Vector2(0, r + 2), p + new Vector2(-r - 2, 0) };
            DrawColoredPolygon(back, Shadow);
            DrawColoredPolygon(diamond, c);
            string glyph = MapShrines.Glyph(shrine.Kind);
            DrawString(font, p + new Vector2(-4f, 5f), glyph, HorizontalAlignment.Left, -1, 13, Shadow);
            DrawString(font, p + new Vector2(-13f, r + 16f), Loc.F("{0:0}m", dist), HorizontalAlignment.Left, -1, 12, c.Lightened(0.4f));

        }
    }

    private static Label MakeLabel(int size)
    {
        var l = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_outline_color", Shadow);
        l.AddThemeConstantOverride("outline_size", 6);
        return l;
    }

    private static void Place(Control c, float top, float height)
    {
        c.AnchorLeft = 0.5f; c.AnchorRight = 0.5f; c.AnchorTop = 0f; c.AnchorBottom = 0f;
        c.OffsetLeft = -360f; c.OffsetRight = 360f; c.OffsetTop = top; c.OffsetBottom = top + height;
    }
}
