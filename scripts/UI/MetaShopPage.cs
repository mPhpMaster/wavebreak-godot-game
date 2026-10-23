using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The Pearl Shop page of the title screen: permanent upgrades bought with
/// <see cref="Pearls"/> (<see cref="MetaShop"/>).
///
/// Same rules as the other pages: every control is a focusable Button in a
/// vertical list, so ui_up/ui_down and ui_accept (keyboard or gamepad) drive
/// it, and ui_cancel is handled by <see cref="MainMenu"/> to step back. The
/// list scrolls to follow focus, because thirteen lines do not fit at 720p.
/// Refund asks for a second press before it does anything.
/// </summary>
public sealed partial class MetaShopPage : Control
{
    private static readonly Color Ink = new(0.93f, 0.95f, 0.99f);
    private static readonly Color InkDim = new(0.62f, 0.68f, 0.78f);
    private static readonly Color Accent = new(0.45f, 0.82f, 1.00f);
    private static readonly Color Gold = new(1.00f, 0.84f, 0.38f);
    private static readonly Color Muted = new(0.50f, 0.52f, 0.58f);
    private static readonly Color Good = new(0.42f, 0.92f, 0.55f);
    private static readonly Color Bad = new(1.00f, 0.45f, 0.40f);
    private static readonly Color PanelBack = new(0.05f, 0.06f, 0.10f, 0.86f);

    private readonly Action _back;
    private readonly List<(Button button, ShopUpgradeDef def)> _rows = new();
    private Label _balance = null!;
    private Label _name = null!;
    private Label _blurb = null!;
    private Label _now = null!;
    private Label _next = null!;
    private Label _status = null!;
    private Button _refund = null!;
    private bool _refundArmed;
    private ShopUpgradeDef? _shown;

    /// <summary>Set by the menu: open the Toggler page (the bought Toggler line).</summary>
    public Action? OpenToggler { get; set; }

    public MetaShopPage(Action back)
    {
        _back = back;
    }

    public override void _Ready()
    {
        Name = "MetaShopPage";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        Label heading = MainMenu.MakeLabel("PEARL SHOP", 20, InkDim);
        MainMenu.Place(heading, 60f, 196f, 200f, 28f);
        AddChild(heading);
        _balance = MainMenu.MakeLabel("", 20, Gold);
        MainMenu.Place(_balance, 230f, 196f, 400f, 28f);
        AddChild(_balance);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        MainMenu.Place(scroll, 60f, 236f, 390f, 452f);
        AddChild(scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);

        foreach (ShopUpgradeDef def in MetaShop.All)
        {
            ShopUpgradeDef d = def;
            Button b = Compact(MainMenu.MenuButton("", () => Buy(d)));
            b.FocusEntered += () => ShowDetails(d);
            b.MouseEntered += () => b.GrabFocus();
            _rows.Add((b, d));
            list.AddChild(b);
        }

        _refund = Compact(MainMenu.MenuButton("", PressRefund));
        _refund.FocusEntered += ShowRefund;
        _refund.FocusExited += () => { _refundArmed = false; RefreshRefund(); };
        _refund.MouseEntered += () => _refund.GrabFocus();
        list.AddChild(_refund);

        Button back = Compact(MainMenu.MenuButton("BACK", () => _back()));
        back.MouseEntered += () => back.GrabFocus();
        back.FocusEntered += () => { _shown = null; ShowText("", "Spend Pearls on upgrades that last every run.", "", "", ""); };
        list.AddChild(back);

        var panel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = PanelBack, ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 18, ContentMarginBottom = 18 };
        style.SetCornerRadiusAll(6);
        panel.AddThemeStyleboxOverride("panel", style);
        MainMenu.Place(panel, 480f, 236f, 520f, 300f);
        AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        _name = MainMenu.MakeLabel("", 30, Ink);
        _blurb = Wrap(MainMenu.MakeLabel("", 17, Ink));
        _now = Wrap(MainMenu.MakeLabel("", 17, Accent));
        _next = Wrap(MainMenu.MakeLabel("", 17, Gold));
        _status = Wrap(MainMenu.MakeLabel("", 16, Good));
        foreach (Label l in new[] { _name, _blurb, _now, _next, _status }) box.AddChild(l);

        Refresh();
        CutePass();   // soft pills and icons (UI/MetaShopPage.Cute.cs)
    }

    private static Button Compact(Button b)
    {
        b.CustomMinimumSize = new Vector2(0f, 40f);
        b.AddThemeFontSizeOverride("font_size", 18);
        return b;
    }

    private static Label Wrap(Label l)
    {
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    /// <summary>Land focus on the first line. Called by the menu on entry.</summary>
    public void FocusFirst()
    {
        if (_rows.Count > 0) _rows[0].button.GrabFocus();
    }

    /// <summary>Rewrite every label from the profile.</summary>
    public void Refresh()
    {
        if (_balance is null) return;
        _balance.Text = $"{Pearls.Balance} {Pearls.Name}";
        foreach (var (button, def) in _rows)
        {
            int level = MetaShop.LevelOf(def);
            bool maxed = level >= def.MaxLevel;
            int cost = maxed ? 0 : MetaShop.NextCost(def);
            button.Text = maxed
                ? $"{def.Name}   {level}/{def.MaxLevel}   MAX"
                : $"{def.Name}   {level}/{def.MaxLevel}   {cost}";
            Color c = maxed ? Good : cost <= Pearls.Balance ? Ink : Muted;
            button.AddThemeColorOverride("font_color", c);
        }
        RefreshRefund();
        if (_shown is not null) ShowDetails(_shown, keepStatus: true);
    }

    private void RefreshRefund()
    {
        long invested = MetaShop.TotalInvested();
        _refund.Text = _refundArmed ? $"CONFIRM REFUND  (+{invested})" : "REFUND ALL";
        _refund.AddThemeColorOverride("font_color", _refundArmed ? Bad : invested > 0 ? Ink : Muted);
    }

    private void ShowDetails(ShopUpgradeDef def, bool keepStatus = false)
    {
        _shown = def;
        int level = MetaShop.LevelOf(def);
        string now = level > 0 ? $"Now: {def.Describe(level * def.PerLevel)}" : "Not bought yet";
        string next;
        if (level >= def.MaxLevel) next = "Fully upgraded.";
        else
        {
            int cost = MetaShop.NextCost(def);
            string afford = cost <= Pearls.Balance ? "" : $"  (need {cost - Pearls.Balance} more)";
            next = $"Level {level + 1}: {def.Describe((level + 1) * def.PerLevel)}  ·  {cost} {Pearls.Name}{afford}";
        }
        ShowText($"{def.Name}  {level}/{def.MaxLevel}", def.Blurb, now, next, keepStatus ? _status.Text : "");
    }

    private void ShowRefund()
    {
        _shown = null;
        long invested = MetaShop.TotalInvested();
        ShowText("Refund", "Sell back every upgrade at the full price paid, to spend again differently.",
                 $"Invested: {invested} {Pearls.Name}",
                 invested > 0 ? "Press twice to confirm." : "Nothing to refund.", "");
    }

    private void ShowText(string name, string blurb, string now, string next, string status)
    {
        _name.Text = name;
        _blurb.Text = blurb;
        _now.Text = now;
        _next.Text = next;
        _status.Text = status;
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }

    private void Buy(ShopUpgradeDef def)
    {
        if (ShopExtras.IsToggler(def) && MetaShop.IsMaxed(def) && OpenToggler is not null)
        {
            OpenToggler();
            return;
        }
        if (MetaShop.IsMaxed(def))
        {
            SetStatus("Already at max level.", InkDim);
            return;
        }
        int cost = MetaShop.NextCost(def);
        if (!MetaShop.TryBuy(def))
        {
            SetStatus($"Not enough {Pearls.Name}: {cost} needed, {Pearls.Balance} held.", Bad);
            return;
        }
        SetStatus($"Bought {def.Name} level {MetaShop.LevelOf(def)} for {cost}.", Good);
        Refresh();
    }

    private void PressRefund()
    {
        long invested = MetaShop.TotalInvested();
        if (invested <= 0)
        {
            SetStatus("Nothing to refund.", InkDim);
            return;
        }
        if (!_refundArmed)
        {
            _refundArmed = true;
            RefreshRefund();
            SetStatus("Press again to refund everything.", Gold);
            return;
        }
        _refundArmed = false;
        long back = MetaShop.RefundAll();
        Refresh();
        ShowRefund();
        SetStatus($"Refunded {back} {Pearls.Name}.", Good);
    }
}
