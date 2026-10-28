using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// A small round minimap in the top-right corner, under the kills and gold.
///
/// North is always up: the map stays still and your arrow turns to show where
/// you face (players found a map that spun with the camera hard to read).
///
/// Shows: you (the arrow), other players (coloured dots with their initial),
/// enemies (small red dots; elites a little bigger, bosses big and pinned to
/// the rim when out of range), unopened chests, unused shrines, the open
/// portal, and downed players as a pulsing red SOS cross. Players, bosses,
/// the portal and SOS markers stay on the rim when they are out of range.
///
/// Cheap by construction: one Control, everything in <see cref="_Draw"/>,
/// redrawn 15 times a second rather than every frame.
/// </summary>
public sealed partial class Minimap : Control
{
    /// <summary>Downed-player markers drawn this run (the co-op test checks it).</summary>
    public static int DownMarkersDrawn;

    private const float RangeMeters = 42f;
    private const float RefreshSeconds = 1f / 15f;
    private const float Right = 18f;
    private const float Top = 90f;   // under "N KILLS" / "N GOLD"

    private static readonly Color Back = new(0.07f, 0.11f, 0.17f, 0.74f);
    private static readonly Color Rim = new(1.00f, 0.95f, 0.84f, 0.95f);
    private static readonly Color RimShadow = new(0.02f, 0.03f, 0.05f, 0.55f);
    private static readonly Color Ring = new(1f, 1f, 1f, 0.10f);
    private static readonly Color EnemyDot = new(1.00f, 0.32f, 0.36f);
    private static readonly Color EliteDot = new(1.00f, 0.55f, 0.30f);
    private static readonly Color BossDot = new(0.95f, 0.16f, 0.22f);
    private static readonly Color ChestDot = new(1.00f, 0.82f, 0.32f);
    private static readonly Color ShrineDot = new(0.78f, 0.62f, 1.00f);
    private static readonly Color CuteShrineDot = new(1.00f, 0.62f, 0.86f);
    private static readonly Color PortalDot = new(0.55f, 1.00f, 0.92f);
    private static readonly Color SosRed = new(1.00f, 0.22f, 0.26f);
    private static readonly Color Ink = new(0.04f, 0.05f, 0.08f);

    private float _timer;
    private float _time;
    private int _layoutVersion = -1;

    // The frame's view, set at the top of _Draw.
    private Vector2 _centre;
    private float _radius, _scale;
    private Vector3 _origin;
    private Vector2 _fwd, _right;

    public override void _Ready()
    {
        Name = "Minimap";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;   // soft fog edges
        HudSettings.Load();
        Layout();
    }

    private void Layout()
    {
        _layoutVersion = HudSettings.Version;
        float s = HudSettings.MinimapPixels;
        AnchorLeft = 1f; AnchorRight = 1f; AnchorTop = 0f; AnchorBottom = 0f;
        OffsetLeft = -(Right + s); OffsetRight = -Right;
        OffsetTop = Top; OffsetBottom = Top + s;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        using var timing = Wavebreak.Game.Prof.Scope("UI.minimap");
        if (_layoutVersion != HudSettings.Version) Layout();
        bool want = HudSettings.Minimap && Player.Instance is { } p && IsInstanceValid(p) && Main.Instance is not null;
        if (Visible != want) Visible = want;
        if (!want) return;
        _time += (float)delta;
        _timer -= (float)delta;
        if (_timer > 0f) return;
        _timer = RefreshSeconds;
        QueueRedraw();
    }

    // ================================================================ draw

    public override void _Draw()
    {
        if (Player.Instance is not { } me || !IsInstanceValid(me)) return;

        _radius = Size.X * 0.5f;
        _centre = Size * 0.5f;
        _scale = (_radius - 7f) / RangeMeters;
        _origin = me.GlobalPosition;
        // North-up and fixed: the map no longer turns with the camera; your
        // arrow turns instead.
        const float yaw = 0f;
        _fwd = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));     // world (x, z) the camera looks along
        _right = new Vector2(MathF.Cos(yaw), -MathF.Sin(yaw));

        // Face: a soft disc with two faint range rings.
        DrawCircle(_centre + new Vector2(0f, 2f), _radius + 1f, RimShadow);
        DrawCircle(_centre, _radius, Back);
        DrawArc(_centre, _radius * 0.5f, 0f, Mathf.Tau, 48, Ring, 1f, true);
        DrawArc(_centre, _radius * 0.25f, 0f, Mathf.Tau, 32, Ring, 1f, true);
        DrawFog();      // Minimap.Fog.cs: the explored map under everything

        DrawChestsAndShrines();
        DrawEnemies();
        DrawPortal();
        DrawPings();
        DrawCaveMarkers(me);
        DrawPlayers(me);

        // Rim and the north badge on top.
        DrawArc(_centre, _radius - 1.5f, 0f, Mathf.Tau, 64, Rim, 3f, true);
        Vector2 north = Map(_origin + new Vector3(0f, 0f, -1f));
        if (north.LengthSquared() > 1e-4f)
        {
            Vector2 at = _centre + north.Normalized() * (_radius - 1.5f);
            DrawCircle(at, 7.5f, Rim);
            DrawCentred("N", at, 11, Ink);
        }
        DrawZoneName(me);   // Minimap.Fog.cs
    }

    /// <summary>World point to map offset from the centre (pixels, camera-up).</summary>
    private Vector2 Map(Vector3 w)
    {
        float dx = w.X - _origin.X, dz = w.Z - _origin.Z;
        return new Vector2(dx * _right.X + dz * _right.Y, -(dx * _fwd.X + dz * _fwd.Y)) * _scale;
    }

    /// <summary>Inside the face (with a margin), or null.</summary>
    private Vector2? Inside(Vector3 w, float margin = 5f)
    {
        Vector2 p = Map(w);
        float lim = _radius - margin;
        return p.LengthSquared() <= lim * lim ? _centre + p : null;
    }

    /// <summary>Inside, or pinned to the rim in its direction.</summary>
    private Vector2 Pinned(Vector3 w, float margin, out bool pinned)
    {
        Vector2 p = Map(w);
        float lim = _radius - margin;
        pinned = p.LengthSquared() > lim * lim;
        if (pinned) p = p.Normalized() * lim;
        return _centre + p;
    }

    private void DrawChestsAndShrines()
    {
        if (ChestSpawner.Instance is { } chests)
            foreach (Chest c in chests.Chests)
            {
                if (!IsInstanceValid(c) || c.IsOpen || !c.IsInsideTree() || !FogOfWar.IsRevealed(c.GlobalPosition)) continue;
                if (Inside(c.GlobalPosition) is not Vector2 at) continue;
                var r = new Rect2(at - new Vector2(3.5f, 3f), new Vector2(7f, 6f));
                DrawRect(r.Grow(1f), Ink);
                DrawRect(r, ChestDot);
            }
        if (ShrineSpawner.Instance is { } shrines)
            foreach (MapShrine s in shrines.Shrines)
                if (IsInstanceValid(s) && !s.Spent && FogOfWar.IsRevealed(s.GlobalPosition) && Inside(s.GlobalPosition) is Vector2 at) Diamond(at, 4.5f, ShrineDot);
        if (CuteShrineSpawner.Instance is { } cute)
            foreach (CuteShrine s in cute.Shrines)
                if (IsInstanceValid(s) && !s.Spent && FogOfWar.IsRevealed(s.GlobalPosition) && Inside(s.GlobalPosition) is Vector2 at) Diamond(at, 4.5f, CuteShrineDot);
        foreach (Vector3 t in TotemIcon.Visible())   // Tide Totems (UI/TotemIcon)
            if (Inside(t) is Vector2 at) { TotemIcon.Draw(this, at, 4.6f); TotemsDrawn++; }
    }

    /// <summary>Tide Totem markers drawn so far (the totem self-check).</summary>
    public static int TotemsDrawn;

    private void DrawEnemies()
    {
        if (EnemyManager.Instance is not { } em) return;
        var alive = em.AliveList;
        for (int i = 0; i < alive.Count; i++)
        {
            Enemy e = alive[i];
            if (!e.IsAlive || !FogOfWar.IsRevealed(e.GlobalPosition)) continue;   // nothing drawn in fog
            if (e.IsBoss)
            {
                Vector2 at = Pinned(e.GlobalPosition, 8f, out bool pinned);
                float pulse = pinned ? 0.6f + 0.4f * MathF.Sin(_time * 6f) : 1f;
                DrawCircle(at, 7.5f, Ink);
                DrawCircle(at, 6f, BossDot with { A = pulse });
                DrawCircle(at, 2.2f, new Color(1f, 0.9f, 0.5f));
                continue;
            }
            if (Inside(e.GlobalPosition, 3f) is not Vector2 p) continue;
            if (e.IsElite) DrawCircle(p, 2.8f, EliteDot);
            else DrawCircle(p, 1.8f, EnemyDot);
        }
    }

    private void DrawPortal()
    {
        if (Main.Instance?.Stages?.PortalPosition is not Vector3 portal) return;
        Vector2 at = Pinned(portal, 8f, out _);
        float r = 5.5f + 1.2f * MathF.Sin(_time * 4f);
        DrawCircle(at, r + 1.5f, Ink);
        DrawArc(at, r, 0f, Mathf.Tau, 20, PortalDot, 2.5f, true);
        DrawCircle(at, 2f, PortalDot);
    }

    /// <summary>Live pings: a ring pulsing in the pinger's colour, on the rim when out of range.</summary>
    private void DrawPings()
    {
        foreach (Ping p in Pings.Active)
        {
            Color c = Coop.Active ? Coop.SlotColor(p.Slot) : PortalDot;
            Vector2 at = Pinned(p.Position, 8f, out _);
            float k = (float)(p.Age % 0.8) / 0.8f;
            DrawArc(at, 4f + 7f * k, 0f, Mathf.Tau, 20, c with { A = 1f - k }, 2f, true);
            DrawCircle(at, 4.5f, Ink);
            DrawCircle(at, 3.4f, c);
        }
    }

    private void DrawPlayers(Player me)
    {
        if (Coop.InRun && NetSession.Instance is { } s)
            foreach (RemotePlayer r in s.Remotes.Values)
            {
                if (!IsInstanceValid(r) || !r.HasState) continue;
                Vector2 at = Pinned(r.GlobalPosition, 9f, out _);
                if (r.Down) Sos(at);
                else
                {
                    Color c = Coop.SlotColor(r.Slot);
                    DrawCircle(at, 8f, Ink);
                    DrawCircle(at, 6.8f, c);
                    DrawCentred(Initial(r.DisplayName), at, 10, Ink);
                }
            }

        // You: an arrow at the centre pointing where the camera looks (up),
        // turned by how far your body faces away from it.
        if (Coop.Active && !me.IsAlive) { Sos(_centre); return; }
        float turn = -me.FacingYaw;   // north-up map: the arrow shows where you face
        Vector2 dir = Vector2.Up.Rotated(turn);
        Vector2 side = new(-dir.Y, dir.X);
        Color mine = Coop.Active && Coop.LocalPeer is { } lp ? Coop.SlotColor(lp.Slot) : new Color(0.62f, 0.98f, 1f);
        Vector2[] tri = { _centre + dir * 8f, _centre - dir * 5f + side * 5.5f, _centre - dir * 2.5f, _centre - dir * 5f - side * 5.5f };
        Vector2[] outline = { _centre + dir * 10.5f, _centre - dir * 6.8f + side * 7.4f, _centre - dir * 3.5f, _centre - dir * 6.8f - side * 7.4f };
        DrawColoredPolygon(outline, Ink);
        DrawColoredPolygon(tri, mine);
    }

    /// <summary>A downed player: a pulsing red cross in a white badge.</summary>
    private void Sos(Vector2 at)
    {
        DownMarkersDrawn++;
        float pulse = 0.5f + 0.5f * MathF.Sin(_time * 7f);
        DrawCircle(at, 9f + 3f * pulse, SosRed with { A = 0.35f * (1f - pulse) + 0.15f });
        DrawCircle(at, 8f, Ink);
        DrawCircle(at, 7f, Colors.White);
        DrawRect(new Rect2(at - new Vector2(1.6f, 5f), new Vector2(3.2f, 10f)), SosRed);
        DrawRect(new Rect2(at - new Vector2(5f, 1.6f), new Vector2(10f, 3.2f)), SosRed);
    }

    private void Diamond(Vector2 at, float r, Color color)
    {
        DrawColoredPolygon(new[] { at + new Vector2(0f, -r - 1.2f), at + new Vector2(r + 1.2f, 0f), at + new Vector2(0f, r + 1.2f), at + new Vector2(-r - 1.2f, 0f) }, Ink);
        DrawColoredPolygon(new[] { at + new Vector2(0f, -r), at + new Vector2(r, 0f), at + new Vector2(0f, r), at + new Vector2(-r, 0f) }, color);
    }

    private void DrawCentred(string text, Vector2 at, int size, Color color)
    {
        Font font = ThemeDB.FallbackFont;
        Vector2 ext = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        float ascent = font.GetAscent(size), descent = font.GetDescent(size);
        DrawString(font, at + new Vector2(-ext.X * 0.5f, (ascent - descent) * 0.5f), text, HorizontalAlignment.Left, -1, size, color);
    }

    private static string Initial(string name)
    {
        foreach (char ch in name)
            if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
        return "?";
    }
}
