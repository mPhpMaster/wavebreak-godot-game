using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// Pings: the key (G or middle mouse, "ping" in Key Bindings) marks the
/// spot under the middle of the screen, where the camera looks. What is near
/// that spot names the ping: an enemy ("Enemy here"), an unopened chest
/// ("Chest"), an unused shrine ("Shrine"); pinging yourself, or pinging
/// while down, calls for "Help!"; anything else is "Go here".
///
/// Every live ping is drawn here, on the canvas so it reads through walls: a
/// pin in the pinger's colour with their name and what it marks, a ring that
/// pings out from it, and an arrow on the screen edge with the distance while
/// it is off-screen. A short two-note chime plays when one appears. The
/// minimap shows a blip (UI/Minimap). Pings live 6 s and each player gets 3
/// per 5 s (Net/Pings). In co-op they go through the host; in single-player a
/// ping is just a local marker.
/// </summary>
public sealed partial class PingMarkers : Control
{
    public const string Action = "ping";
    private const float EdgeMargin = 70f;
    private const float RayLength = 160f;
    private const float TagRadius = 3.5f;

    /// <summary>Pings from other players drawn on this screen (the co-op test checks it).</summary>
    public static int RemoteDrawn;

    private static readonly Color Shadow = new(0.02f, 0.03f, 0.05f, 0.85f);
    private static AudioStreamWav? _chime;
    private AudioStreamPlayer _player = null!;
    private float _time;
    private bool _drew;

    public override void _Ready()
    {
        Name = "PingMarkers";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Pings.Reset();
        RemoteDrawn = 0;
        _chime ??= MakeChime();
        _player = new AudioStreamPlayer { Stream = _chime, Bus = Audio.AudioSettings.SfxBus, VolumeDb = -6f };
        AddChild(_player);
        Pings.Added += OnAdded;
    }

    public override void _ExitTree() => Pings.Added -= OnAdded;

    private void OnAdded(Ping p)
    {
        if (IsInsideTree()) _player.Play();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (InputMap.HasAction(Action) && Input.IsActionJustPressed(Action)) TryPing();
        bool any = Pings.Active.Count > 0;
        if (any || _drew) QueueRedraw();
        _drew = any;
    }

    // ================================================================ placing

    private void TryPing()
    {
        if (DevPanel.IsOpen || Main.Instance is not { } main || main.Run.Over) return;
        if (Coop.IsPaused(GetTree())) return;
        if (Player.Instance is not { } me || !IsInstanceValid(me)) return;
        (PingKind kind, Vector3 at) = Aim(me);
        Place(kind, at);
    }

    /// <summary>Place a ping as this player, within their rate limit.</summary>
    public static void Place(PingKind kind, Vector3 at)
    {
        if (!Pings.Allow(Coop.LocalId)) return;
        if (Coop.InRun && NetSession.Instance is { } s) s.SendPing(kind, at);
        else Pings.Show(Coop.LocalId, kind, at);
    }

    /// <summary>What and where a ping from the middle of the screen marks.</summary>
    private (PingKind, Vector3) Aim(Player me)
    {
        if (!me.IsAlive) return (PingKind.Help, me.GlobalPosition);
        Vector3 at = me.GlobalPosition;
        Camera3D? cam = GetViewport().GetCamera3D();
        if (cam is not null)
        {
            Vector2 mid = GetViewport().GetVisibleRect().Size * 0.5f;
            Vector3 from = cam.ProjectRayOrigin(mid);
            Vector3 dir = cam.ProjectRayNormal(mid);
            var query = PhysicsRayQueryParameters3D.Create(from, from + dir * RayLength, Enemy.WorldLayer | Enemy.EnemyLayer,
                                                           new Godot.Collections.Array<Rid> { me.GetRid() });
            var hit = cam.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count > 0) at = hit["position"].AsVector3();
            else if (Mathf.Abs(dir.Y) > 1e-3f)
            {
                // Nothing hit (looking over the edge): the ground plane at the player's feet.
                float t = (me.GlobalPosition.Y - from.Y) / dir.Y;
                if (t > 0f) at = from + dir * Mathf.Min(t, RayLength);
            }
        }
        return Classify(at, me.GlobalPosition);
    }

    /// <summary>Name a ping at <paramref name="at"/> by what stands near it.</summary>
    public static (PingKind, Vector3) Classify(Vector3 at, Vector3 self)
    {
        if (Flat(at, self) < 2f) return (PingKind.Help, self);
        float best = TagRadius;
        Vector3? enemy = null;
        if (EnemyManager.Instance is { } em)
            foreach (Enemy e in em.AliveList)
                if (e.IsAlive && Flat(e.GlobalPosition, at) is var d && d < best) { best = d; enemy = e.GlobalPosition; }
        if (enemy is Vector3 ep) return (PingKind.Enemy, ep);
        if (ChestSpawner.Instance is { } chests)
            foreach (Chest c in chests.Chests)
                if (IsInstanceValid(c) && !c.IsOpen && Flat(c.GlobalPosition, at) < TagRadius) return (PingKind.Chest, c.GlobalPosition);
        if (ShrineSpawner.Instance is { } shrines)
            foreach (MapShrine s in shrines.Shrines)
                if (IsInstanceValid(s) && !s.Spent && Flat(s.GlobalPosition, at) < TagRadius) return (PingKind.Shrine, s.GlobalPosition);
        if (CuteShrineSpawner.Instance is { } cute)
            foreach (CuteShrine s in cute.Shrines)
                if (IsInstanceValid(s) && !s.Spent && Flat(s.GlobalPosition, at) < TagRadius) return (PingKind.Shrine, s.GlobalPosition);
        return (PingKind.GoHere, at);
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    // ================================================================ drawing

    public override void _Draw()
    {
        var live = Pings.Active;
        if (live.Count == 0) return;
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        Vector2 size = GetViewportRect().Size;
        // Edge arrows keep clear of the HUD: the clock and phase lines on top,
        // the dash meter at the bottom.
        var safe = new Rect2(EdgeMargin, 150f, size.X - EdgeMargin * 2f, size.Y - 150f - 110f);
        Vector3 me = Player.Instance is { } p && IsInstanceValid(p) ? p.GlobalPosition : camera.GlobalPosition;
        Font font = ThemeDB.FallbackFont;

        foreach (Ping ping in live)
        {
            Color tint = Coop.Active ? Coop.SlotColor(ping.Slot) : new Color(0.62f, 0.98f, 1f);
            if (ping.Kind == PingKind.Help) tint = tint.Lerp(new Color(1f, 0.3f, 0.3f), 0.5f);
            float fade = Mathf.Clamp((Pings.Lifetime - (float)ping.Age) / 0.6f, 0f, 1f);
            float pop = Mathf.Clamp((float)ping.Age / 0.18f, 0f, 1f);
            Color c = tint with { A = fade };
            string text = Coop.Active ? Loc.F("{0}: {1}", ping.Name, Pings.Label(ping.Kind)) : Pings.Label(ping.Kind);
            string meters = Loc.F("{0}m", Mathf.RoundToInt(Flat(ping.Position, me)));
            if (ping.Peer != Coop.LocalId) RemoteDrawn++;

            Vector3 aim = ping.Position + Vector3.Up * 0.4f;
            bool behind = camera.IsPositionBehind(aim);
            Vector2 screen = camera.UnprojectPosition(aim);
            if (!behind && safe.HasPoint(screen))
            {
                // A ring pinging out of the spot, then the pin above it.
                float k = (float)(ping.Age % 1.0);
                DrawArc(screen, 8f + 26f * k, 0f, Mathf.Tau, 32, c with { A = fade * (1f - k) }, 3f, true);
                Vector2 tip = screen + new Vector2(0f, -6f);
                float s = 0.5f + 0.5f * pop;
                Pin(tip, s, c, fade);
                Label(font, tip + new Vector2(0f, -40f * s), text, 17, c, fade);
                Label(font, tip + new Vector2(0f, -22f * s), meters, 13, Colors.White with { A = fade }, fade);
                continue;
            }

            Vector2 centre = size * 0.5f;
            Vector2 dir = screen - centre;
            if (behind) dir = -dir;
            if (dir.LengthSquared() < 1f) dir = Vector2.Down;
            dir = dir.Normalized();
            Vector2 half = safe.Size * 0.5f;
            float sx = Mathf.Abs(dir.X) > 1e-4f ? half.X / Mathf.Abs(dir.X) : float.MaxValue;
            float sy = Mathf.Abs(dir.Y) > 1e-4f ? half.Y / Mathf.Abs(dir.Y) : float.MaxValue;
            Vector2 edge = centre + dir * Mathf.Min(sx, sy);
            Vector2 side = new(-dir.Y, dir.X);
            Vector2[] arrow = { edge, edge - dir * 20f + side * 11f, edge - dir * 13f, edge - dir * 20f - side * 11f };
            DrawColoredPolygon(arrow, c);
            Label(font, edge - dir * 40f, text, 15, c, fade);
            Label(font, edge - dir * 40f + new Vector2(0f, 15f), meters, 13, Colors.White with { A = fade }, fade);
        }
    }

    /// <summary>A map pin: a disc on a point, in the pinger's colour.</summary>
    private void Pin(Vector2 tip, float s, Color c, float fade)
    {
        Vector2 head = tip + new Vector2(0f, -15f * s);
        DrawColoredPolygon(new[] { tip, head + new Vector2(-7f * s, 2f * s), head + new Vector2(7f * s, 2f * s) }, Shadow with { A = Shadow.A * fade });
        DrawCircle(head, 9f * s, Shadow with { A = Shadow.A * fade });
        DrawColoredPolygon(new[] { tip + new Vector2(0f, -1.5f), head + new Vector2(-5.5f * s, 2f * s), head + new Vector2(5.5f * s, 2f * s) }, c);
        DrawCircle(head, 7.5f * s, c);
        DrawCircle(head, 3f * s, Colors.White with { A = fade });
    }

    private void Label(Font font, Vector2 centreBottom, string text, int size, Color color, float fade)
    {
        Vector2 ext = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        Vector2 at = centreBottom - new Vector2(ext.X * 0.5f, 0f);
        DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, size, 5, Shadow with { A = Shadow.A * fade });
        DrawString(font, at, text, HorizontalAlignment.Left, -1, size, color);
    }

    // ================================================================ sound

    /// <summary>A soft two-note chime (E6 then B5), built once: no audio file to ship.</summary>
    private static AudioStreamWav MakeChime()
    {
        const int rate = 22050;
        int n = (int)(rate * 0.32f);
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float a = t < 0.14f ? Env(t, 0.14f) * MathF.Sin(Mathf.Tau * 1318.5f * t) : 0f;
            float b = t >= 0.10f ? Env(t - 0.10f, 0.22f) * MathF.Sin(Mathf.Tau * 987.8f * t) : 0f;
            short v = (short)(Mathf.Clamp(0.32f * (a + b), -1f, 1f) * short.MaxValue);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
    }

    private static float Env(float t, float length)
    {
        if (t < 0f || t > length) return 0f;
        float attack = Mathf.Min(1f, t / 0.006f);
        return attack * MathF.Exp(-6f * t / length) * Mathf.Clamp((length - t) / 0.015f, 0f, 1f);
    }
}
