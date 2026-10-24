using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// The CO-OP page's beach line-up: every player in the lobby standing side by
/// side on the title backdrop's sand (<see cref="MainMenu"/>.Backdrop), in
/// their chosen character, skin and hat, with their name over their head and
/// their READY state under their feet. Out of a session it is just you.
///
/// It follows the lobby live: <see cref="Coop.Roster"/> is polled a few
/// times a second and a figure is rebuilt only when that player's look
/// changed. The 3D figures live in the title scene; their labels sit on the
/// CO-OP page, placed every frame where the camera sees each head. The label
/// layer is forced left-to-right because it is positioned in screen pixels;
/// the figures themselves mirror for right-to-left layouts like the hero.
/// </summary>
public sealed partial class CoopLineup : Node3D
{
    private const float FigureScale = 1.55f;
    private const float StripLeft = 500f;     // right of the CO-OP page's left column
    private const float StripMargin = 90f;

    private static readonly Color Ready = new(0.55f, 1.00f, 0.55f);
    private static readonly Color NotReady = new(0.70f, 0.74f, 0.82f);
    private static readonly Color HostInk = new(1.00f, 0.84f, 0.38f);

    private sealed class Figure
    {
        public string Key = "";
        public long PeerId;
        public Node3D Holder = null!;
        public Control Tag = null!;
        public Label Name = null!;
        public Label State = null!;
    }

    private readonly Control _overlay;
    private readonly Control _page;
    private readonly List<Figure> _figures = new();
    private readonly List<CoopPeer> _people = new();
    private double _pollAt;

    public CoopLineup(Control page)
    {
        Name = "CoopLineup";
        _page = page;
        _overlay = new Control { Name = "LineupTags", MouseFilter = Control.MouseFilterEnum.Ignore };
        _overlay.LayoutDirection = Control.LayoutDirectionEnum.Ltr;
        _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        page.AddChild(_overlay);
        page.MoveChild(_overlay, 0);   // under the page's panels
    }

    /// <summary>Who is on the beach right now (for checks and captures).</summary>
    public int Count => _figures.Count;

    /// <summary>Per frame from the title scene. <paramref name="show"/> is
    /// true while the CO-OP page is up; the lone hero steps aside meanwhile.</summary>
    public void Tick(double delta, Camera3D camera, bool show, Node3D? hero)
    {
        Visible = show;
        _overlay.Visible = show;
        if (hero is not null) hero.Visible = !show;
        if (!show) return;

        _pollAt -= delta;
        if (_pollAt <= 0)
        {
            _pollAt = 0.2;
            Sync();
        }
        Place(camera);
    }

    /// <summary>Rebuild only what changed: a new player, one who left, or a
    /// new character, skin, hat or name.</summary>
    private void Sync()
    {
        _people.Clear();
        if (Coop.Active && Coop.Roster.Count > 0)
        {
            _people.AddRange(Coop.Roster);
            _people.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        }
        else
        {
            CoopPeer me = NetSession.LocalProfile();
            _people.Add(me);
        }

        while (_figures.Count > _people.Count)
        {
            Figure gone = _figures[^1];
            gone.Holder.QueueFree();
            gone.Tag.QueueFree();
            _figures.RemoveAt(_figures.Count - 1);
        }
        for (int i = 0; i < _people.Count; i++)
        {
            CoopPeer p = _people[i];
            if (i >= _figures.Count) _figures.Add(NewFigure());
            Figure f = _figures[i];
            string key = $"{p.CharacterId}|{p.SkinId}|{p.HatId}";
            if (f.Key != key) Dress(f, p, key);
            f.PeerId = p.Id;
            Color colour = Coop.Active ? Coop.SlotColor(p.Slot) : new Color(0.45f, 0.85f, 1.00f);
            f.Name.Text = p.Name;
            f.Name.AddThemeColorOverride("font_color", colour);
            if (!Coop.Active) f.State.Text = "";
            else if (p.Id == 1)
            {
                f.State.Text = Loc.T("host").ToUpperInvariant();   // "HOST" alone is the host-a-game button
                f.State.AddThemeColorOverride("font_color", HostInk);
            }
            else
            {
                f.State.Text = Loc.T(p.Ready ? "READY" : "NOT READY");
                f.State.AddThemeColorOverride("font_color", p.Ready ? Ready : NotReady);
            }
        }
    }

    private Figure NewFigure()
    {
        var f = new Figure { Holder = new Node3D() };
        AddChild(f.Holder);
        f.Holder.AddChild(new OmniLight3D { Position = new Vector3(0.8f, 2.4f, -2.2f), LightEnergy = 1.4f, OmniRange = 6f });

        var tag = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        tag.AddThemeConstantOverride("separation", 0);
        f.Name = MainMenu.MakeLabel("", 20, Colors.White);
        f.Name.AutoTranslateMode = AutoTranslateModeEnum.Disabled;   // a player's name
        f.Name.HorizontalAlignment = HorizontalAlignment.Center;
        f.Name.AddThemeConstantOverride("outline_size", 7);
        f.State = MainMenu.MakeLabel("", 15, Colors.White);
        f.State.AutoTranslateMode = AutoTranslateModeEnum.Disabled;  // already translated
        f.State.HorizontalAlignment = HorizontalAlignment.Center;
        f.State.AddThemeConstantOverride("outline_size", 6);
        tag.AddChild(f.Name);
        tag.AddChild(f.State);
        _overlay.AddChild(tag);
        f.Tag = tag;
        return f;
    }

    private static void Dress(Figure f, CoopPeer p, string key)
    {
        f.Key = key;
        foreach (Node child in f.Holder.GetChildren())
        {
            if (child is Light3D) continue;
            f.Holder.RemoveChild(child);
            child.QueueFree();
        }
        CharacterDef c = Characters.ById(p.CharacterId) ?? Characters.Default;
        SkinDef skin = c.Skins[0];
        foreach (SkinDef s in c.Skins) if (s.Id == p.SkinId) skin = s;
        Node3D model = Visuals.PlayerLooks.Build(skin.LookId, skin.Paint);
        f.Holder.AddChild(model);
        if (p.HatId.Length > 0) HatOn(model, p.HatId);
    }

    private static void HatOn(Node n, string id)
    {
        if (n is Visuals.CharacterRig rig) { rig.SetHat(id); return; }
        foreach (Node c in n.GetChildren()) HatOn(c, id);
    }

    /// <summary>Stand everyone along the open strip of beach right of the
    /// page's left column, facing the camera, and put each tag over its head.</summary>
    private void Place(Camera3D camera)
    {
        int n = _figures.Count;
        if (n == 0) return;
        Vector2 view = GetViewport().GetVisibleRect().Size;
        bool rtl = Loc.IsRtl;
        float left = StripLeft, right = view.X - StripMargin;
        float y = view.Y * 0.84f;
        int slots = Math.Max(n, 2);
        // Shoulder to shoulder on the sand, not spread to the water on a wide window.
        float span = Mathf.Min((right - left) / slots, 300f);
        float start = left + (right - left - span * n) * 0.5f;

        // Name tags must stay below the page's session panel, whose height
        // changes (friends list, note): stand the line further down if needed.
        float top = _page is CoopPage page ? page.PanelBottom + 6f : 0f;
        for (int i = 0; i < n; i++)
        {
            Figure f = _figures[i];
            float sx = start + span * (i + 0.5f);
            if (rtl) sx = view.X - sx;
            Vector2 at = Stand(camera, f, sx, y);
            float tagH = f.Tag.GetCombinedMinimumSize().Y;
            if (at.Y - tagH < top)
                at = Stand(camera, f, sx, Mathf.Min(view.Y - 12f, y + (top - (at.Y - tagH))));
            if (camera.IsPositionBehind(f.Holder.GlobalPosition)) { f.Tag.Visible = false; continue; }
            // No room under a tall panel (the public games list): no tag rather than one across the panel.
            if (at.Y - tagH < top - 2f) { f.Tag.Visible = false; continue; }
            Vector2 size = f.Tag.GetCombinedMinimumSize();
            float w = Mathf.Max(size.X, 160f);
            f.Tag.Visible = true;
            f.Tag.Size = new Vector2(w, size.Y);
            f.Tag.Position = new Vector2(at.X - w * 0.5f, at.Y - size.Y);
        }
    }

    /// <summary>Stand one figure on the sand under screen point (sx, y),
    /// facing the camera. Returns where its head is on screen.</summary>
    private static Vector2 Stand(Camera3D camera, Figure f, float sx, float y)
    {
        {
            var screen = new Vector2(sx, y);
            Vector3 from = camera.ProjectRayOrigin(screen), dir = camera.ProjectRayNormal(screen);
            float tHit = Mathf.Abs(dir.Y) > 1e-4f ? (1.0f - from.Y) / dir.Y : 15f;
            f.Holder.Position = from + dir * Mathf.Clamp(tHit, 6f, 40f);
            f.Holder.Scale = Vector3.One * FigureScale;
            Vector3 toCam = (camera.GlobalPosition - f.Holder.Position) with { Y = 0f };
            if (toCam.LengthSquared() > 0.01f)
                f.Holder.Rotation = new Vector3(0f, Mathf.Atan2(toCam.X, toCam.Z) + Mathf.Pi, 0f);

            Vector3 head = f.Holder.Position + Vector3.Up * (2.05f * FigureScale);
            return camera.UnprojectPosition(head);
        }
    }
}
