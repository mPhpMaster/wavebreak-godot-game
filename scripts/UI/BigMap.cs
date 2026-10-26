using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// The whole current map, for the Tab sheet (and the pause sheet, which is the
/// same sheet pinned): UI/FogOfWar's texture (the explored floor, the wall,
/// raised ground and the blockers of hand-built maps as shapes; fog where
/// nobody has been), the names of the zones the team has found, and on top
/// you, your teammates, pings, revealed unopened chests and unused shrines,
/// the phase boss when it stands in revealed ground, and the open portal.
///
/// North is up and nothing turns, like the minimap. Always laid out left to
/// right (a map does not mirror in Arabic; its labels are translated).
/// Redrawn ten times a second while visible.
/// </summary>
public sealed partial class BigMap : Control
{
    private static readonly Color Back = new(0.05f, 0.07f, 0.11f, 0.96f);
    private static readonly Color Frame = new(0.30f, 0.55f, 0.70f, 0.8f);
    private static readonly Color Ink = new(0.04f, 0.05f, 0.08f);
    private static readonly Color ChestDot = new(1.00f, 0.82f, 0.32f);
    private static readonly Color ShrineDot = new(0.78f, 0.62f, 1.00f);
    private static readonly Color CuteShrineDot = new(1.00f, 0.62f, 0.86f);
    private static readonly Color PortalDot = new(0.55f, 1.00f, 0.92f);
    private static readonly Color BossDot = new(0.95f, 0.16f, 0.22f);
    private static readonly Color Label = new(1.00f, 0.95f, 0.84f);
    private static readonly Color SosRed = new(1.00f, 0.22f, 0.26f);

    private float _timer;
    private float _time;
    private Rect2 _rect;
    private FogOfWar? _fog;

    public override void _Ready()
    {
        Name = "BigMap";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        LayoutDirection = LayoutDirectionEnum.Ltr;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        _time += (float)delta;
        _timer -= (float)delta;
        if (_timer > 0f) return;
        _timer = 0.1f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float side = Mathf.Min(Size.X, Size.Y);
        _rect = new Rect2((Size - new Vector2(side, side)) * 0.5f, new Vector2(side, side));
        DrawRect(_rect, Back);
        _fog = FogOfWar.Instance;
        if (_fog is not { Active: true, Texture: { } tex })
        {
            string rest = Loc.T(Main.Instance?.Stages is { InRestRoom: true } ? "Rest stop: no map here" : "No map");
            Centred(rest, _rect.GetCenter(), 18, Label);
            DrawRect(_rect, Frame, false, 2f);
            return;
        }

        DrawTextureRect(tex, _rect, false);
        DrawZones();
        DrawFinds();
        DrawBoss();
        DrawPortal();
        DrawPings();
        DrawPlayers();
        DrawRect(_rect, Frame, false, 2f);
        // North badge, top centre.
        var n = new Vector2(_rect.GetCenter().X, _rect.Position.Y + 14f);
        DrawCircle(n, 10f, Label);
        Centred("N", n, 13, Ink);
    }

    private Vector2 Map(Vector3 w) => _rect.Position + _fog!.Uv(w.X, w.Z) * _rect.Size;

    private void DrawZones()
    {
        foreach (MapBlockers.Zone z in MapBlockers.Zones)
        {
            // The ring of streets round the square has no single centre; the
            // square's own label stands for both.
            if (z.Radius > 70f) continue;
            var at = new Vector3(z.Centre.X, 0f, z.Centre.Y);
            if (!FogOfWar.IsRevealed(at)) continue;
            Vector2 p = Map(at) + (z.Centre == Vector2.Zero ? new Vector2(0f, -_rect.Size.Y * 0.06f) : Vector2.Zero);
            Centred(Loc.T(z.Name), p, Mathf.Clamp((int)(_rect.Size.X / 34f), 11, 20), Label, outline: true);
        }
    }

    private void DrawFinds()
    {
        float s = Mathf.Clamp(_rect.Size.X / 110f, 3.5f, 7f);
        if (ChestSpawner.Instance is { } chests)
            foreach (Chest c in chests.Chests)
            {
                if (!IsInstanceValid(c) || c.IsOpen || !c.IsInsideTree() || !FogOfWar.IsRevealed(c.GlobalPosition)) continue;
                Vector2 at = Map(c.GlobalPosition);
                var r = new Rect2(at - new Vector2(s, s * 0.8f), new Vector2(s * 2f, s * 1.6f));
                DrawRect(r.Grow(1.2f), Ink);
                DrawRect(r, ChestDot);
            }
        if (ShrineSpawner.Instance is { } shrines)
            foreach (MapShrine sh in shrines.Shrines)
                if (IsInstanceValid(sh) && !sh.Spent && FogOfWar.IsRevealed(sh.GlobalPosition)) Diamond(Map(sh.GlobalPosition), s * 1.2f, ShrineDot);
        if (CuteShrineSpawner.Instance is { } cute)
            foreach (CuteShrine sh in cute.Shrines)
                if (IsInstanceValid(sh) && !sh.Spent && FogOfWar.IsRevealed(sh.GlobalPosition)) Diamond(Map(sh.GlobalPosition), s * 1.2f, CuteShrineDot);
        foreach (Vector3 t in TotemIcon.Visible())   // Tide Totems (UI/TotemIcon)
            TotemIcon.Draw(this, Map(t), s * 1.3f);
    }

    private void DrawBoss()
    {
        if (Main.Instance?.Stages.BossPosition is not Vector3 boss || !FogOfWar.IsRevealed(boss)) return;
        Vector2 at = Map(boss);
        DrawCircle(at, 9f, Ink);
        DrawCircle(at, 7.5f, BossDot);
        DrawCircle(at, 2.6f, new Color(1f, 0.9f, 0.5f));
    }

    private void DrawPortal()
    {
        // Always shown once open: it opens at the centre, where every map is entered.
        if (Main.Instance?.Stages.PortalPosition is not Vector3 portal) return;
        Vector2 at = Map(portal);
        float r = 8f + 1.6f * MathF.Sin(_time * 4f);
        DrawCircle(at, r + 2f, Ink);
        DrawArc(at, r, 0f, Mathf.Tau, 24, PortalDot, 3f, true);
        DrawCircle(at, 2.6f, PortalDot);
    }

    private void DrawPings()
    {
        foreach (Ping p in Pings.Active)
        {
            Color c = Coop.Active ? Coop.SlotColor(p.Slot) : PortalDot;
            Vector2 at = Map(p.Position);
            float k = (float)(p.Age % 0.8) / 0.8f;
            DrawArc(at, 5f + 9f * k, 0f, Mathf.Tau, 20, c with { A = 1f - k }, 2f, true);
            DrawCircle(at, 5.5f, Ink);
            DrawCircle(at, 4.2f, c);
        }
    }

    private void DrawPlayers()
    {
        if (Coop.InRun && NetSession.Instance is { } s)
            foreach (RemotePlayer r in s.Remotes.Values)
            {
                if (!IsInstanceValid(r) || !r.HasState) continue;
                Vector2 at = Map(r.GlobalPosition);
                if (r.Down) { Sos(at); continue; }
                Color c = Coop.SlotColor(r.Slot);
                DrawCircle(at, 9f, Ink);
                DrawCircle(at, 7.6f, c);
                Centred(Initial(r.DisplayName), at, 11, Ink);
                Centred(r.DisplayName, at + new Vector2(0f, 17f), 12, c, outline: true);
            }

        if (Player.Instance is not { } me || !IsInstanceValid(me)) return;
        Vector2 centre = Map(me.GlobalPosition);
        if (Coop.Active && !me.IsAlive) { Sos(centre); return; }
        Vector2 dir = Vector2.Up.Rotated(-me.FacingYaw);
        Vector2 sideV = new(-dir.Y, dir.X);
        Color mine = Coop.Active && Coop.LocalPeer is { } lp ? Coop.SlotColor(lp.Slot) : new Color(0.62f, 0.98f, 1f);
        float k = 1.25f;
        Vector2[] tri = { centre + dir * 9f * k, centre - dir * 5.5f * k + sideV * 6f * k, centre - dir * 2.8f * k, centre - dir * 5.5f * k - sideV * 6f * k };
        Vector2[] outline = { centre + dir * 12f * k, centre - dir * 7.5f * k + sideV * 8.2f * k, centre - dir * 3.8f * k, centre - dir * 7.5f * k - sideV * 8.2f * k };
        // A soft ring shows the reveal radius around you.
        float reveal = FogOfWar.RevealRadius / (2f * _fog!.Extent) * _rect.Size.X;
        DrawArc(centre, reveal, 0f, Mathf.Tau, 48, mine with { A = 0.35f }, 1.5f, true);
        DrawColoredPolygon(outline, Ink);
        DrawColoredPolygon(tri, mine);
    }

    private void Sos(Vector2 at)
    {
        float pulse = 0.5f + 0.5f * MathF.Sin(_time * 7f);
        DrawCircle(at, 10f + 3f * pulse, SosRed with { A = 0.35f * (1f - pulse) + 0.15f });
        DrawCircle(at, 9f, Ink);
        DrawCircle(at, 8f, Colors.White);
        DrawRect(new Rect2(at - new Vector2(1.8f, 5.5f), new Vector2(3.6f, 11f)), SosRed);
        DrawRect(new Rect2(at - new Vector2(5.5f, 1.8f), new Vector2(11f, 3.6f)), SosRed);
    }

    private void Diamond(Vector2 at, float r, Color color)
    {
        DrawColoredPolygon(new[] { at + new Vector2(0f, -r - 1.4f), at + new Vector2(r + 1.4f, 0f), at + new Vector2(0f, r + 1.4f), at + new Vector2(-r - 1.4f, 0f) }, Ink);
        DrawColoredPolygon(new[] { at + new Vector2(0f, -r), at + new Vector2(r, 0f), at + new Vector2(0f, r), at + new Vector2(-r, 0f) }, color);
    }

    private void Centred(string text, Vector2 at, int size, Color color, bool outline = false)
    {
        Font font = ThemeDB.FallbackFont;
        Vector2 ext = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        float ascent = font.GetAscent(size), descent = font.GetDescent(size);
        var p = at + new Vector2(-ext.X * 0.5f, (ascent - descent) * 0.5f);
        if (outline) DrawStringOutline(font, p, text, HorizontalAlignment.Left, -1, size, 5, Ink);
        DrawString(font, p, text, HorizontalAlignment.Left, -1, size, color);
    }

    private static string Initial(string name)
    {
        foreach (char ch in name)
            if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
        return "?";
    }
}
