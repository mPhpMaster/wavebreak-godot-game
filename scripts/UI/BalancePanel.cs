using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Stats;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The Shrine of Balance's confirmation: every weighed stat with its value now
/// and after, the heaviest one highlighted, and two big buttons. Built on
/// <see cref="ModalPanel"/>, so it pauses, frees the mouse, ignores keys for a
/// moment after opening and closes on Esc or E; 1 confirms, 2 cancels.
///
/// The "after" column is exact: the plan is applied to the real sheet under a
/// probe source, read, and removed again (source tags make that a filter).
/// </summary>
public sealed partial class BalancePanel : ModalPanel
{
    private readonly object _probe = new();
    private VBoxContainer _rows = null!;
    private Label _rule = null!;
    private Action? _confirm;

    public override void _Ready()
    {
        base._Ready();
        Name = "BalancePanel";
        if (Body.GetParent() is PanelContainer frame)
        {
            frame.AddThemeStyleboxOverride("panel", CuteStyle.Box(CuteStyle.Cream, CuteStyle.Lavender, 24, 4, 22));
            frame.CustomMinimumSize = new Vector2(560f, 0f);
        }
        Body.AddThemeConstantOverride("separation", 10);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        head.AddThemeConstantOverride("separation", 12);
        IconView icon = CuteStyle.Icon(new IconSpec("scales", CuteStyle.Lavender), 52f, CuteStyle.Lavender);
        icon.Disc = false;
        head.AddChild(icon);
        head.AddChild(CuteStyle.Text(Loc.T("Shrine of Balance"), 30, CuteStyle.Ink));
        Body.AddChild(head);

        _rule = CuteStyle.Text("", 15, CuteStyle.InkSoft, HorizontalAlignment.Center);
        _rule.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _rule.CustomMinimumSize = new Vector2(520f, 0f);
        Body.AddChild(_rule);

        var card = CuteStyle.Panel(CuteStyle.Paper, new Color(CuteStyle.Lavender, 0.5f), 16, 12);
        _rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _rows.AddThemeConstantOverride("separation", 4);
        card.AddChild(_rows);
        Body.AddChild(card);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        Button yes = CuteStyle.Pill(Loc.T("1  Balance me!"), CuteStyle.Mint, () => { if (Armed) Confirm(); }, 18, on: true);
        Button no = CuteStyle.Pill(Loc.T("2  Not now"), CuteStyle.Peach, () => { if (Armed) Close(); }, 18);
        foreach (Button b in new[] { yes, no })
        {
            b.CustomMinimumSize = new Vector2(200f, 46f);
            b.FocusMode = FocusModeEnum.None;
            buttons.AddChild(b);
        }
        Body.AddChild(buttons);
    }

    public void Open(StatSheet sheet, (BalanceStat Top, float Take, float Give) plan, Action confirm)
    {
        _confirm = confirm;
        _rule.Text = Loc.F("Takes half of your strongest bonus and shares it, plus {0:0}%, with the other {1}. Once per shrine.",
                           (CuteShrines.BalanceReturn - 1f) * 100f, CuteShrines.BalanceStats.Count - 1);
        foreach (Node n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }

        var before = new Dictionary<Stat, string>();
        foreach (BalanceStat b in CuteShrines.BalanceStats) before[b.Stat] = CuteShrines.Show(sheet, b.Stat);
        CuteShrines.ApplyBalance(sheet, plan, _probe);
        var after = new Dictionary<Stat, string>();
        foreach (BalanceStat b in CuteShrines.BalanceStats) after[b.Stat] = CuteShrines.Show(sheet, b.Stat);
        sheet.RemoveAllFrom(_probe);

        foreach (BalanceStat b in CuteShrines.BalanceStats)
        {
            bool top = ReferenceEquals(b, plan.Top);
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 10);
            IconView ic = CuteStyle.Icon(new IconSpec(b.Glyph, b.Color), 26f, b.Color);
            ic.Disc = false;
            row.AddChild(ic);
            Label name = CuteStyle.Text(Loc.T(Catalog.StatName(b.Stat)), 16, CuteStyle.Ink);
            name.CustomMinimumSize = new Vector2(150f, 0f);
            row.AddChild(name);
            Label from = CuteStyle.Text(Loc.Ltr(before[b.Stat]), 16, CuteStyle.InkSoft, HorizontalAlignment.Right);
            from.CustomMinimumSize = new Vector2(90f, 0f);
            row.AddChild(from);
            // The arrow points the way the row reads: before -> after.
            row.AddChild(CuteStyle.Text(Loc.IsRtl ? "  ←  " : "  →  ", 16, CuteStyle.InkSoft));
            Color c = top ? CuteStyle.Coral.Darkened(0.2f) : new Color(0.20f, 0.62f, 0.42f);
            Label to = CuteStyle.Text(Loc.Ltr(after[b.Stat]), 17, c);
            to.CustomMinimumSize = new Vector2(90f, 0f);
            row.AddChild(to);
            string delta = top ? CuteShrines.DescribeUnits(b, -plan.Take) : CuteShrines.DescribeUnits(b, plan.Give);
            row.AddChild(CuteStyle.Text(Loc.Ltr(delta.Replace(Loc.T(Catalog.StatName(b.Stat)), "").Trim().Trim(Loc.Lri, Loc.Pdi)), 13, c));

            _rows.AddChild(row);
        }
        OpenPanel();
    }

    /// <summary>Apply and close (also the 1 key, and the capture path).</summary>
    public void Confirm()
    {
        Action? confirm = _confirm;
        _confirm = null;
        Close();
        confirm?.Invoke();
    }

    protected override bool OnNumber(int index)
    {
        if (index == 0) { Confirm(); return true; }
        if (index == 1) { Close(); return true; }
        return false;
    }

    protected override void OnClosed() => _confirm = null;
}
