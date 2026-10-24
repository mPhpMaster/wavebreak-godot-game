using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// Co-op: every other player's name in a tag of their colour over their head,
/// with their health bar under it, and an arrow on the screen edge with their
/// name and distance while they are off-screen.
///
/// Drawn on the canvas rather than as a <see cref="Label3D"/>: canvas drawing
/// is always on top, so a tag reads through walls, trees and props, and the
/// bar and the edge arrow come from the same projection. Tags fade with
/// distance (never below 40%) so a crowd of far tags does not clutter the
/// middle of the screen. A downed player's tag turns red, says so, and fills
/// with their revive progress. Does nothing in single-player.
/// </summary>
public sealed partial class TeamTags : Control
{
    private const float EdgeMargin = 64f;
    private const float FadeStart = 22f, FadeEnd = 80f, MinAlpha = 0.4f;

    private static readonly Color Shadow = new(0.02f, 0.03f, 0.05f, 0.85f);
    private static readonly Color Trough = new(0.05f, 0.06f, 0.09f, 0.85f);
    private static readonly Color HealthFull = new(0.36f, 0.86f, 0.46f);
    private static readonly Color HealthLow = new(0.95f, 0.30f, 0.28f);
    private static readonly Color DownRed = new(1.00f, 0.32f, 0.32f);
    private static readonly Color ReviveGreen = new(0.45f, 1.00f, 0.55f);

    private float _time;
    private bool _drewSomething;

    public override void _Ready()
    {
        Name = "TeamTags";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        bool any = Coop.InRun && NetSession.Instance is { } s && s.Remotes.Count > 0;
        if (any || _drewSomething) QueueRedraw();
    }

    public override void _Draw()
    {
        _drewSomething = false;
        if (!Coop.InRun || NetSession.Instance is not { } s) return;
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        Vector2 size = GetViewportRect().Size;
        // Edge arrows keep clear of the HUD: the clock and phase lines on top,
        // the dash meter at the bottom.
        var safe = new Rect2(EdgeMargin, 150f, size.X - EdgeMargin * 2f, size.Y - 150f - 110f);
        Vector3 from = Player.Instance is { } me && IsInstanceValid(me) ? me.GlobalPosition : camera.GlobalPosition;

        foreach (RemotePlayer r in s.Remotes.Values)
        {
            if (!IsInstanceValid(r) || !r.HasState || !r.IsInsideTree()) continue;
            _drewSomething = true;
            Vector3 head = r.GlobalPosition + Vector3.Up * (Player.BodyHeight + 0.55f);
            float dist = new Vector2(r.GlobalPosition.X - from.X, r.GlobalPosition.Z - from.Z).Length();
            Color tint = Coop.SlotColor(r.Slot);
            bool behind = camera.IsPositionBehind(head);
            Vector2 screen = camera.UnprojectPosition(head);
            if (!behind && safe.HasPoint(screen))
            {
                float alpha = Mathf.Clamp(1f - (dist - FadeStart) / (FadeEnd - FadeStart), MinAlpha, 1f);
                DrawTag(screen, r, tint, alpha, dist);
            }
            // A teammate right beside you needs no arrow.
            else if (dist > 4f) DrawEdge(size, safe, screen, behind, r, tint, dist);
        }
    }

    private void DrawTag(Vector2 at, RemotePlayer r, Color tint, float alpha, float dist)
    {
        Font font = ThemeDB.FallbackFont;
        int fontSize = dist > 40f ? 15 : 17;
        string name = r.Down ? Loc.F("{0} (down)", r.DisplayName) : r.DisplayName;
        Vector2 ext = font.GetStringSize(name, HorizontalAlignment.Left, -1, fontSize);
        float pulse = r.Down ? 0.55f + 0.45f * MathF.Sin(_time * 7f) : 1f;
        Color edge = r.Down ? DownRed : tint;

        // The tag: a dark pill with a border in the player's colour.
        const float barW = 64f, barH = 6f;
        float w = MathF.Max(ext.X, barW) + 18f, h = ext.Y + barH + 12f;
        var box = new Rect2(at.X - w * 0.5f, at.Y - h, w, h);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.05f, 0.08f, 0.62f * alpha),
            BorderColor = edge with { A = alpha * pulse },
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(9);
        DrawStyleBox(style, box);

        Vector2 textAt = new(at.X - ext.X * 0.5f, box.Position.Y + 4f + font.GetAscent(fontSize));
        DrawStringOutline(font, textAt, name, HorizontalAlignment.Left, -1, fontSize, 4, Shadow with { A = Shadow.A * alpha });
        DrawString(font, textAt, name, HorizontalAlignment.Left, -1, fontSize, edge with { A = alpha });

        // Health (or, while down, revive progress) under the name.
        var trough = new Rect2(at.X - barW * 0.5f, box.End.Y - barH - 5f, barW, barH);
        DrawRect(trough, r.Down ? DownRed with { A = 0.45f * alpha } : Trough with { A = Trough.A * alpha });
        float fill = r.Down ? r.Revive01 : r.Health01;
        Color fillColor = r.Down ? ReviveGreen : HealthLow.Lerp(HealthFull, Mathf.Clamp((r.Health01 - 0.2f) / 0.6f, 0f, 1f));
        if (fill > 0f) DrawRect(new Rect2(trough.Position, new Vector2(barW * Mathf.Clamp(fill, 0f, 1f), barH)), fillColor with { A = alpha });
    }

    private void DrawEdge(Vector2 size, Rect2 safe, Vector2 screen, bool behind, RemotePlayer r, Color tint, float dist)
    {
        Vector2 centre = size * 0.5f;
        Vector2 dir = screen - centre;
        if (behind) dir = -dir;
        if (dir.LengthSquared() < 1f) dir = Vector2.Down;
        dir = dir.Normalized();
        Vector2 half = safe.Size * 0.5f;
        float sx = Mathf.Abs(dir.X) > 1e-4f ? half.X / Mathf.Abs(dir.X) : float.MaxValue;
        float sy = Mathf.Abs(dir.Y) > 1e-4f ? half.Y / Mathf.Abs(dir.Y) : float.MaxValue;
        Vector2 tip = centre + dir * Mathf.Min(sx, sy);

        Color c = r.Down ? DownRed with { A = 0.55f + 0.45f * MathF.Sin(_time * 7f) } : tint;
        Vector2 side = new(-dir.Y, dir.X);
        Vector2[] arrow = { tip, tip - dir * 22f + side * 12f, tip - dir * 15f, tip - dir * 22f - side * 12f };
        var shadow = new Vector2[arrow.Length];
        for (int i = 0; i < arrow.Length; i++) shadow[i] = arrow[i] + new Vector2(2f, 2f);
        DrawColoredPolygon(shadow, Shadow);
        DrawColoredPolygon(arrow, c);

        Font font = ThemeDB.FallbackFont;
        const int fontSize = 15;
        string label = r.Down ? Loc.F("{0} (down)", r.DisplayName) : r.DisplayName;
        string meters = Loc.F("{0}m", Mathf.RoundToInt(dist));
        Vector2 e1 = font.GetStringSize(label, HorizontalAlignment.Left, -1, fontSize);
        Vector2 e2 = font.GetStringSize(meters, HorizontalAlignment.Left, -1, 13);
        Vector2 anchor = tip - dir * 46f;
        Vector2 p1 = anchor + new Vector2(-e1.X * 0.5f, 0f);
        Vector2 p2 = anchor + new Vector2(-e2.X * 0.5f, 15f);
        DrawStringOutline(font, p1, label, HorizontalAlignment.Left, -1, fontSize, 5, Shadow);
        DrawString(font, p1, label, HorizontalAlignment.Left, -1, fontSize, c with { A = 1f });
        DrawStringOutline(font, p2, meters, HorizontalAlignment.Left, -1, 13, 4, Shadow);
        DrawString(font, p2, meters, HorizontalAlignment.Left, -1, 13, Colors.White);
    }
}
