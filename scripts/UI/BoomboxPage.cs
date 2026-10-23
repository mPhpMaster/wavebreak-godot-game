using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The Boombox (Megabonk's jukebox): every music track as a row with PLAY/STOP
/// and PIN, the mode (Map default / Shuffle / Pinned), a skip button and the
/// music volume. Locked rows say how to earn them (<see cref="MusicTracks"/>).
///
/// The menu id is "boombox" (<c>--menu-page=boombox</c> for captures). In an
/// automated capture there is no <see cref="MusicDirector"/>, so the buttons
/// are inert and nothing is saved; the page still draws exactly as a player
/// sees it with nothing playing.
/// </summary>
public sealed partial class BoomboxPage : Control
{
    private readonly Action _back;
    private readonly List<(TrackDef Track, Button Play, Button Pin, Label Sub, PanelContainer Row)> _rows = new();
    private readonly List<(BoomboxMode Mode, Button Button)> _modePills = new();
    private Label _now = null!, _status = null!, _counts = null!, _volValue = null!;
    private HSlider _volume = null!;
    private ScrollContainer _scroll = null!;
    private Button _backButton = null!;
    private bool _built, _previewed;
    private string? _shownNow = "";

    public BoomboxPage(Action back)
    {
        _back = back;
        Name = "BoomboxPage";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    private void Build()
    {
        if (_built) return;
        _built = true;

        Label heading = CuteStyle.Title("BOOMBOX", 20, CuteStyle.OnNightDim);
        MainMenu.Place(heading, 64f, 182f, 300f, 26f);
        AddChild(heading);
        _counts = CuteStyle.Title("", 15, CuteStyle.Butter, HorizontalAlignment.Right);
        MainMenu.Place(_counts, 520f, 184f, 700f, 24f);
        AddChild(_counts);

        // ---- left: the track list
        PanelContainer board = CuteStyle.Panel(new Color(CuteStyle.Night, 0.86f), new Color(CuteStyle.Lavender, 0.45f), 24, 14);
        MainMenu.Place(board, 48f, 212f, 790f, 450f);
        AddChild(board);
        var scroll = _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        board.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);

        foreach (TrackDef def in MusicTracks.All)
        {
            TrackDef t = def;
            PanelContainer row = CuteStyle.Panel(CuteStyle.Cream, new Color(KindColor(t.Kind), 0.7f), 14, 6);
            var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            h.AddThemeConstantOverride("separation", 10);
            row.AddChild(h);

            Button play = CuteStyle.Pill("PLAY", CuteStyle.Mint, () => Toggle(t), 14);
            play.CustomMinimumSize = new Vector2(78f, 34f);
            play.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            h.AddChild(play);

            PanelContainer tag = CuteStyle.Tag(KindName(t.Kind), KindColor(t.Kind), CuteStyle.Ink, 11);
            tag.CustomMinimumSize = new Vector2(64f, 0f);
            tag.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            h.AddChild(tag);

            var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            col.AddThemeConstantOverride("separation", 0);
            Label title = CuteStyle.Text(t.Title, 16, CuteStyle.Ink);
            title.ClipText = true;
            col.AddChild(title);
            Label sub = CuteStyle.Text("", 12, CuteStyle.InkSoft);
            sub.ClipText = true;
            col.AddChild(sub);
            h.AddChild(col);

            Button pin = CuteStyle.Pill("PIN", CuteStyle.Butter, () => PinTrack(t), 14);
            pin.CustomMinimumSize = new Vector2(70f, 34f);
            pin.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            pin.Visible = t.Loop;
            h.AddChild(pin);
            if (!t.Loop)
            {
                // Keep the column aligned where stings have no PIN.
                var gap = new Control { CustomMinimumSize = new Vector2(70f, 0f), MouseFilter = MouseFilterEnum.Ignore };
                h.AddChild(gap);
            }

            play.FocusEntered += () => Describe(t);
            pin.FocusEntered += () => Describe(t);
            list.AddChild(row);
            _rows.Add((t, play, pin, sub, row));
        }

        // ---- right: mode, now playing, volume
        PanelContainer side = CuteStyle.Panel(new Color(CuteStyle.Night, 0.86f), new Color(CuteStyle.Mint, 0.45f), 24, 16);
        MainMenu.Place(side, 852f, 212f, 380f, 450f);
        AddChild(side);
        var sc = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        sc.AddThemeConstantOverride("separation", 10);
        side.AddChild(sc);

        sc.AddChild(CuteStyle.Title("NOW PLAYING", 14, CuteStyle.OnNightDim));
        _now = CuteStyle.Title("", 20, CuteStyle.OnNight);
        _now.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _now.CustomMinimumSize = new Vector2(0f, 54f);
        sc.AddChild(_now);

        sc.AddChild(CuteStyle.Title("IN RUNS AND MENUS", 14, CuteStyle.OnNightDim));
        var modes = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        modes.AddThemeConstantOverride("separation", 6);
        sc.AddChild(modes);
        foreach ((BoomboxMode mode, string label) in new[] { (BoomboxMode.MapDefault, "Map default"), (BoomboxMode.Shuffle, "Shuffle"), (BoomboxMode.Pinned, "Pinned") })
        {
            BoomboxMode m = mode;
            Button b = CuteStyle.Pill(label, CuteStyle.Sky, () => SetMode(m), 14);
            b.CustomMinimumSize = new Vector2(0f, 36f);
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            modes.AddChild(b);
            _modePills.Add((m, b));
        }

        Button skip = CuteStyle.Pill("SKIP / NEXT", CuteStyle.Peach, Skip, 15);
        skip.CustomMinimumSize = new Vector2(0f, 38f);
        sc.AddChild(skip);
        Button stop = CuteStyle.Pill("STOP", CuteStyle.Rose, () => { MusicDirector.Stop(); _previewed = true; Refresh(); }, 15);
        stop.CustomMinimumSize = new Vector2(0f, 38f);
        sc.AddChild(stop);

        sc.AddChild(CuteStyle.Title("MUSIC VOLUME", 14, CuteStyle.OnNightDim));
        var vol = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vol.AddThemeConstantOverride("separation", 10);
        sc.AddChild(vol);
        _volume = new HSlider { MinValue = 0, MaxValue = 10, Step = 1, FocusMode = FocusModeEnum.All,
                                SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200f, 28f),
                                SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _volume.ValueChanged += v =>
        {
            int steps = (int)Math.Round(v - Math.Round(AudioSettings.Get(VolumeChannel.Music) * 10f));
            if (steps != 0) AudioSettings.Nudge(VolumeChannel.Music, steps);
            _volValue.Text = $"{(int)_volume.Value}/10";
        };
        vol.AddChild(_volume);
        _volValue = CuteStyle.Title("", 16, CuteStyle.OnNight);
        vol.AddChild(_volValue);
        Label keys = CuteStyle.Text("In a run: [ / ] music volume. The pause menu's Settings has the same mode and skip.", 12, CuteStyle.OnNightDim);
        keys.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        sc.AddChild(keys);

        // ---- bottom
        var bottom = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bottom.AddThemeConstantOverride("separation", 14);
        MainMenu.Place(bottom, 60f, 670f, 1160f, 40f);
        AddChild(bottom);
        _backButton = CuteStyle.Pill("BACK", CuteStyle.Peach, Leave, 18);
        _backButton.CustomMinimumSize = new Vector2(180f, 40f);
        bottom.AddChild(_backButton);
        _status = CuteStyle.Title("", 15, CuteStyle.OnNightDim);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.ClipText = true;
        _status.VerticalAlignment = VerticalAlignment.Center;
        bottom.AddChild(_status);

        // Now-playing changes on its own (fades, stings ending): poll it.
        var timer = new Timer { WaitTime = 0.4, Autostart = true };
        timer.Timeout += () => { if (IsVisibleInTree() && MusicDirector.NowPlaying != _shownNow) Refresh(); };
        AddChild(timer);
    }

    private static string KindName(TrackKind k) => k switch
    {
        TrackKind.Menu => "MENU",
        TrackKind.Map => "MAP",
        TrackKind.Remix => "REMIX",
        TrackKind.Boss => "BOSS",
        TrackKind.Swarm => "SWARM",
        _ => "STING",
    };

    private static Color KindColor(TrackKind k) => k switch
    {
        TrackKind.Menu => CuteStyle.Lavender,
        TrackKind.Map => CuteStyle.Mint,
        TrackKind.Remix => CuteStyle.Seafoam,
        TrackKind.Boss => CuteStyle.Coral,
        TrackKind.Swarm => CuteStyle.Rose,
        _ => CuteStyle.Butter,
    };

    // ============================================================ actions

    /// <summary>Called by the menu on entry. <paramref name="arg"/> may be a
    /// track id to focus.</summary>
    public void Open(string arg = "")
    {
        Build();
        Boombox.Load();
        _previewed = false;
        _volume.SetValueNoSignal(Mathf.Round(AudioSettings.Get(VolumeChannel.Music) * 10f));
        _volValue.Text = $"{(int)_volume.Value}/10";
        _status.Text = "PLAY to listen, PIN to play a loop everywhere. Locked tracks open as you reach maps and clear quests.";
        _status.AddThemeColorOverride("font_color", CuteStyle.OnNightDim);
        Refresh();
        Button focus = _rows[0].Play;
        foreach (var r in _rows) if (r.Track.Id == arg) focus = r.Play;
        focus.GrabFocus();
        // The list has not been laid out on the frame the page appears, so
        // scroll to the focused row once it has.
        GetTree().CreateTimer(0.05).Timeout += () => { if (IsInstanceValid(focus)) _scroll.EnsureControlVisible(focus); };
    }

    private void Toggle(TrackDef t)
    {
        if (!MusicTracks.IsUnlocked(t)) { Say($"{t.Title} is locked: {MusicTracks.HowToUnlock(t)}.", CuteStyle.Coral); return; }
        _previewed = true;
        if (MusicDirector.NowPlaying == t.Id) MusicDirector.Stop();
        else MusicDirector.Preview(this, t.Id);
        Refresh();
    }

    private void PinTrack(TrackDef t)
    {
        if (!MusicTracks.IsUnlocked(t)) { Say($"{t.Title} is locked: {MusicTracks.HowToUnlock(t)}.", CuteStyle.Coral); return; }
        if (Boombox.Mode == BoomboxMode.Pinned && Boombox.Pinned == t.Id)
        {
            Boombox.SetMode(BoomboxMode.MapDefault);
            Say($"Unpinned {t.Title}. Back to each map's own music.", CuteStyle.Mint);
        }
        else if (Boombox.Pin(t.Id))
        {
            MusicDirector.Preview(this, t.Id);
            _previewed = true;
            Say($"Pinned {t.Title}: it plays in menus and runs until you change it.", CuteStyle.Mint);
        }
        Refresh();
    }

    private void SetMode(BoomboxMode mode)
    {
        if (mode == BoomboxMode.Pinned && !MusicTracks.IsUnlocked(Boombox.Pinned))
        {
            Say("Press PIN on a loop first.", CuteStyle.Peach);
            return;
        }
        Boombox.SetMode(mode);
        MusicDirector.Reapply();
        _previewed = false;
        Say(mode switch
        {
            BoomboxMode.Shuffle => "Shuffle: a random track you own at every new map, boss and menu.",
            BoomboxMode.Pinned => $"Pinned: {MusicTracks.ById(Boombox.Pinned)?.Title} everywhere.",
            _ => "Map default: each map, boss and the swarm play their own music.",
        }, CuteStyle.Mint);
        Refresh();
    }

    private void Skip()
    {
        MusicDirector.Reapply(skip: true);
        _previewed = false;
        Refresh();
    }

    /// <summary>Back: if a preview replaced the menu music, put the mode's choice back.</summary>
    private void Leave() => _back();

    /// <summary>The menu is leaving this page (BACK or Esc).</summary>
    public void Closing()
    {
        if (_previewed) MusicDirector.Reapply();
        _previewed = false;
    }

    private void Say(string text, Color color)
    {
        _status.Text = Loc.T(text);
        _status.AddThemeColorOverride("font_color", color);
    }

    private void Describe(TrackDef t)
    {
        bool open = MusicTracks.IsUnlocked(t);
        Say(open ? $"{Loc.T(t.Title)}  ·  {Loc.T(t.Where)}{(t.Loop ? "" : "  ·  " + Loc.T("one-shot sting"))}" : Loc.F("Locked: {0}", MusicTracks.HowToUnlock(t)),
            open ? CuteStyle.OnNightDim : CuteStyle.Peach);
    }

    private void Refresh()
    {
        string? now = MusicDirector.NowPlaying;
        _shownNow = now;
        int owned = 0;
        foreach (var (t, play, pin, sub, row) in _rows)
        {
            bool open = MusicTracks.IsUnlocked(t);
            if (open) owned++;
            bool playing = now == t.Id;
            bool pinned = Boombox.Mode == BoomboxMode.Pinned && Boombox.Pinned == t.Id;
            play.Text = !open ? "LOCKED" : playing ? "STOP" : "PLAY";
            CuteStyle.StylePill(play, playing ? CuteStyle.Rose : CuteStyle.Mint, playing);
            pin.Text = pinned ? "PINNED" : "PIN";
            CuteStyle.StylePill(pin, CuteStyle.Butter, pinned);
            pin.Disabled = !open;
            sub.Text = open ? Loc.T(t.Where) : MusicTracks.HowToUnlock(t, full: false);
            sub.AddThemeColorOverride("font_color", open ? CuteStyle.InkSoft : CuteStyle.Coral.Darkened(0.25f));
            row.Modulate = open ? Colors.White : new Color(1f, 1f, 1f, 0.75f);
        }
        _counts.Text = Loc.F("tracks {0}/{1}   ·   mode: {2}", owned, _rows.Count, Boombox.Describe());
        foreach (var (m, b) in _modePills) CuteStyle.StylePill(b, CuteStyle.Sky, m == Boombox.Mode);
        TrackDef? cur = MusicTracks.ById(now);
        _now.Text = cur is null ? Loc.T(MusicDirector.Active ? "nothing" : "nothing  (music is off in this launch)") : $"{Loc.T(cur.Title)}\n{Loc.T(cur.Where)}";
    }
}
