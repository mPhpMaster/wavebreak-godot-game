using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The unlocks and secrets book: lifetime stats across the top, then every
/// unlock as a card, then the shrines.
///
/// Secret unlocks are drawn as "???" with their cryptic hint until earned; the
/// plain condition and the reward appear only afterwards. Normal unlocks show
/// their condition and, where the profile can measure it, how far along the
/// player is — "412 / 1000" turns a locked card into a goal.
///
/// Every card is a focusable button inside a ScrollContainer that follows
/// focus, which is all keyboard and gamepad scrolling needs.
/// </summary>
public sealed partial class Codex : Control
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color Earned = new(0.45f, 0.92f, 0.58f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    private static readonly Color SecretInk = new(0.78f, 0.58f, 1.00f);

    /// <summary>Set by the menu so the last card can hand focus down to it.</summary>
    public Button? BackButton { get; set; }

    private Label _stats = null!;
    private GridContainer _grid = null!;
    private ScrollContainer _scroll = null!;
    private readonly List<Button> _cards = new();

    public override void _Ready()
    {
        var col = new VBoxContainer();
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 10);
        AddChild(col);

        _stats = MainMenu.MakeLabel("", 16, InkDim);
        col.AddChild(_stats);

        var scroll = _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        col.AddChild(scroll);

        _grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(_grid);

        Refresh();
    }

    public void FocusFirst()
    {
        if (_cards.Count > 0) _cards[0].GrabFocus();
    }

    /// <summary>Focus the first secret card, scrolling it into view. Used by the
    /// menu capture to show the hidden entries.</summary>
    public async void FocusFirstSecret()
    {
        // The cards were just rebuilt; the grid has no layout until it has been
        // sorted, and scrolling to an unplaced card goes nowhere.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        for (int i = 0; i < Unlocks.All.Count && i < _cards.Count; i++)
        {
            if (!Unlocks.All[i].Secret) continue;
            _cards[i].GrabFocus();
            _scroll.EnsureControlVisible(_cards[i]);
            return;
        }
    }

    /// <summary>Rebuild from the profile. Cheap, and only done when the page opens.</summary>
    public void Refresh()
    {
        if (_grid is null) return;
        foreach (Node c in _grid.GetChildren()) { _grid.RemoveChild(c); c.QueueFree(); }
        _cards.Clear();

        long best = Profile.Get(Profile.BestSeconds);
        int secretsTotal = 0, secretsEarned = 0;
        foreach (UnlockDef u in Unlocks.All)
            if (u.Secret) { secretsTotal++; if (Profile.IsUnlocked(u.Id)) secretsEarned++; }
        _stats.Text =
            $"UNLOCKED {Profile.UnlockedIds.Count}/{Unlocks.All.Count}   ·   SECRETS {secretsEarned}/{secretsTotal}   ·   " +
            $"runs {Profile.Get(Profile.Runs)}   wins {Profile.Get(Profile.Wins)}   kills {Profile.Get(Profile.TotalKills)}   " +
            $"bosses {Profile.Get(Profile.BossesKilled)}   best {best / 60:00}:{best % 60:00}   best level {Profile.Get(Profile.BestLevel)}";

        foreach (UnlockDef u in Unlocks.All) AddCard(UnlockCard(u));
        foreach (SecretKindDef k in SecretSpawner.Kinds) AddCard(ShrineCard(k));

        if (BackButton is not null && _cards.Count > 0)
        {
            // The grid's last row may be short; point every card's "down" from
            // the last row at Back so the page can always be left by keyboard.
            for (int i = System.Math.Max(0, _cards.Count - _grid.Columns); i < _cards.Count; i++)
                _cards[i].FocusNeighborBottom = _cards[i].GetPathTo(BackButton);
            BackButton.FocusNeighborTop = BackButton.GetPathTo(_cards[0]);
        }
    }

    private void AddCard(Button card)
    {
        _grid.AddChild(card);
        _cards.Add(card);
    }

    private static Button UnlockCard(UnlockDef u)
    {
        bool done = Profile.IsUnlocked(u.Id);
        bool hidden = u.Secret && !done;

        string title = hidden ? "???" : u.Name;
        string body = hidden ? u.Hint : u.Description;
        string reward = hidden ? "Reward: ???" : Unlocks.DescribeReward(u);
        if (!hidden && u.Reward == RewardKind.Character && Characters.ById(u.RewardId) is { } c)
            reward += $"  (look: {c.LookId})";
        string status = done ? "UNLOCKED" : hidden ? "SECRET" : Progress(u);
        Color accent = done ? Earned : u.Secret ? SecretInk : Gold;

        return Card(title, body, reward, status, accent, done);
    }

    private static Button ShrineCard(SecretKindDef k)
    {
        bool found = Profile.HasFoundSecret(k.Id);
        return Card(found ? k.DisplayName : "???",
                    found ? "A hidden shrine. It answered when touched." : "Something faint waits near the edge of the arena.",
                    found ? $"Gift: {k.RewardText}\nThis run: {SecretKindDef.Describe(k.RunBoons)}\nEvery run after: {SecretKindDef.Describe(k.LastingBoons)}"
                          : "Gift: ???  ·  grants stat boosts when found",
                    found ? "FOUND" : "HIDDEN SHRINE",
                    found ? Earned : SecretInk, found);
    }

    /// <summary>Progress text for conditions the profile can measure between
    /// runs: a running total for lifetime ladders, and for single-run feats the
    /// best attempt so far, labelled "best" so it does not read as a bar that
    /// carries over between runs.</summary>
    private static string Progress(UnlockDef u)
    {
        long have = Quests.StoredProgress(u);
        if (have < 0) return "LOCKED";
        string amount = $"{System.Math.Min(have, (long)u.Threshold):N0} / {u.Threshold:N0}";
        return Quests.IsRunMetric(u.Metric) ? $"BEST  {amount}" : amount;
    }

    private static Button Card(string title, string body, string reward, string status, Color accent, bool done)
    {
        var card = new Button { FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(370f, 132f) };
        var normal = new StyleBoxFlat { BgColor = done ? new Color(0.07f, 0.12f, 0.10f, 0.92f) : new Color(0.06f, 0.07f, 0.11f, 0.90f), BorderColor = accent };
        normal.SetBorderWidthAll(0);
        normal.BorderWidthLeft = 4;
        normal.SetCornerRadiusAll(5);
        var focus = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = Gold };
        focus.SetBorderWidthAll(2);
        focus.SetCornerRadiusAll(5);
        card.AddThemeStyleboxOverride("normal", normal);
        card.AddThemeStyleboxOverride("hover", normal);
        card.AddThemeStyleboxOverride("pressed", normal);
        card.AddThemeStyleboxOverride("focus", focus);

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = 14; box.OffsetRight = -10; box.OffsetTop = 8; box.OffsetBottom = -8;
        box.AddThemeConstantOverride("separation", 2);
        card.AddChild(box);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        Label t = MainMenu.MakeLabel(title, 19, done ? Ink : accent.Lerp(Ink, 0.3f));
        t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(t);
        head.AddChild(MainMenu.MakeLabel(status, 12, accent));
        box.AddChild(head);

        Label b = MainMenu.MakeLabel(body, 14, done ? Ink : InkDim);
        b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        b.SizeFlagsVertical = SizeFlags.ExpandFill;
        box.AddChild(b);
        box.AddChild(MainMenu.MakeLabel(reward, 13, accent));
        return card;
    }
}
