using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Wavebreak.Data;
using Wavebreak.Game.Trade;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

/// <summary>
/// The two-sided trade window. Left (right in Arabic): what I give, and my
/// bag to pick from; the other column: what my teammate gives and what I
/// will hold afterwards. Tiles are the stats sheet's build-box tiles
/// (<see cref="IconView"/>, rarity ring, count badge, hover tooltip), made
/// clickable.
///
/// Mouse: click a bag tile to offer one copy (Shift-click or right-click: the
/// whole stack), click an offered tile to take one back (right-click: all of
/// it); the gold buttons step by 1 / 10 / 100. Keyboard: Tab and the arrows
/// move between tiles and buttons, Enter/Space presses, + and - change the
/// gold by 10 (Shift: 100), 1 is READY, 2 is CONFIRM, Esc or E cancels.
///
/// A tile the teammate could not hold is greyed out with the reason in its
/// tooltip, and any rule the offers break is spelled out under the columns
/// (the host refuses READY while one stands).
/// </summary>
public sealed partial class TradePanel : ModalPanel
{
    private const float Tile = 46f;
    private const float ColumnWidth = 540f;

    private static readonly Color InkC = CuteStyle.Ink;
    private static readonly Color Soft = CuteStyle.InkSoft;
    private static readonly Color BadC = new(0.85f, 0.25f, 0.25f);

    private Label _subtitle = null!;
    private Label _myHead = null!, _theirHead = null!;
    private PanelContainer _myBadge = null!, _theirBadge = null!;
    private HFlowContainer _myOffer = null!, _theirOffer = null!, _bag = null!;
    private Label _myGold = null!, _theirGold = null!, _after = null!, _notice = null!, _status = null!;
    private Button _ready = null!, _confirm = null!, _cancel = null!;
    private readonly List<Button> _goldButtons = new();
    private bool _quiet;

    public override void _Ready()
    {
        Name = "TradePanel";
        base._Ready();
        CuteFrame(Body);
        Body.AddThemeConstantOverride("separation", 8);

        // ---- header
        var head = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 10);
        head.AddChild(CuteStyle.Icon(new IconSpec("pouch", Actors.Trader.TraderTint, "coin", new Color(1f, 0.82f, 0.3f)), 40f, Actors.Trader.TraderTint));
        head.AddChild(CuteStyle.Title(Loc.T("SWAP MEET"), 30, Actors.Trader.TraderTint));
        Body.AddChild(head);
        _subtitle = CuteStyle.Text("", 16, Soft, HorizontalAlignment.Center);
        Body.AddChild(_subtitle);

        // ---- two columns
        var cols = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        cols.AddThemeConstantOverride("separation", 14);
        Body.AddChild(cols);

        VBoxContainer mine = Column(cols, CuteStyle.Mint, out _myHead, out _myBadge);
        _myOffer = OfferArea(mine);
        mine.AddChild(GoldRow(out _myGold, true));
        mine.AddChild(CuteStyle.Text(Loc.T("YOUR BAG  ·  click to offer, right-click for the whole stack"), 13, Soft));
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0f, 168f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        mine.AddChild(scroll);
        _bag = Flow();
        scroll.AddChild(_bag);

        VBoxContainer theirs = Column(cols, CuteStyle.Sky, out _theirHead, out _theirBadge);
        _theirOffer = OfferArea(theirs);
        theirs.AddChild(GoldRow(out _theirGold, false));
        theirs.AddChild(CuteStyle.Text(Loc.T("AFTER THE TRADE YOU WILL HAVE"), 13, Soft));
        _after = CuteStyle.Text("", 15, InkC);
        _after.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _after.CustomMinimumSize = new Vector2(ColumnWidth - 30f, 150f);
        theirs.AddChild(_after);

        // ---- problems, status, buttons
        _notice = CuteStyle.Text("", 15, BadC, HorizontalAlignment.Center);
        _notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _notice.CustomMinimumSize = new Vector2(ColumnWidth * 2f, 40f);
        Body.AddChild(_notice);

        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bottom.AddThemeConstantOverride("separation", 12);
        Body.AddChild(bottom);
        _ready = CuteStyle.Pill(Loc.T("1  READY"), CuteStyle.Mint, ToggleReady, 18);
        _confirm = CuteStyle.Pill(Loc.T("2  CONFIRM"), CuteStyle.Butter, Confirm, 18);
        _cancel = CuteStyle.Pill(Loc.T("Esc  CANCEL"), CuteStyle.Coral, Cancel, 18);
        foreach (Button b in new[] { _ready, _confirm, _cancel }) { b.CustomMinimumSize = new Vector2(170f, 44f); bottom.AddChild(b); }
        _status = CuteStyle.Text("", 15, InkC, HorizontalAlignment.Center);
        Body.AddChild(_status);
    }

    /// <summary>The cute cream card instead of the station panels' dark one.</summary>
    internal static void CuteFrame(Control body)
    {
        if (body.GetParent() is PanelContainer frame)
            frame.AddThemeStyleboxOverride("panel", CuteStyle.Box(CuteStyle.Paper, new Color(Actors.Trader.TraderTint, 0.9f), 22, 4, 18));
    }

    public void Open()
    {
        _quiet = false;
        Refresh();
        OpenPanel();
        _ready.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>The trade ended (done or cancelled elsewhere): close without
    /// sending a cancel.</summary>
    public void CloseQuietly()
    {
        _quiet = true;
        Close();
        _quiet = false;
    }

    protected override void OnClosed()
    {
        // Esc / E / the run ending while the trade is still open: cancel it.
        if (!_quiet && TradeView.Open) NetSession.Instance?.TradeCancel();
    }

    protected override bool OnNumber(int index)
    {
        if (index == 0) { ToggleReady(); return true; }
        if (index == 1) { Confirm(); return true; }
        return false;
    }

    public override void _Input(InputEvent e)
    {
        if (IsOpen && Armed && e is InputEventKey { Pressed: true } key)
        {
            int step = key.ShiftPressed ? 100 : 10;
            if (key.Keycode is Key.Equal or Key.Plus or Key.KpAdd) { StepGold(step); GetViewport().SetInputAsHandled(); return; }
            if (key.Keycode is Key.Minus or Key.KpSubtract) { StepGold(-step); GetViewport().SetInputAsHandled(); return; }
        }
        base._Input(e);
    }

    // ================================================================ actions

    private static NetSession? Net => NetSession.Instance;

    private TradeOffer Draft => Net?.TradeDraft.Clone() ?? new TradeOffer();

    private bool Locked => !TradeView.Open || TradeView.Committing;

    private void Send(TradeOffer o)
    {
        if (Locked) return;
        Net?.TradeSetOffer(o);
        Refresh();
    }

    private void StepGold(int delta)
    {
        if (Locked) return;
        TradeOffer o = Draft;
        o.Gold = Math.Clamp(o.Gold + delta, 0, TradeLedger.Snapshot().Gold);
        Send(o);
    }

    private void AllGold()
    {
        if (Locked) return;
        TradeOffer o = Draft;
        o.Gold = o.Gold >= TradeLedger.Snapshot().Gold ? 0 : TradeLedger.Snapshot().Gold;
        Send(o);
    }

    private void AddItem(string id, bool whole)
    {
        TradeOffer o = Draft;
        int held = TradeLedger.Snapshot().ItemCount(id);
        ItemDef? def = Catalog.ItemById(id);
        if (def is null) return;
        int room = TradeRules.RoomFor(def, TradeView.TheirHold, TradeView.Theirs, o);
        int now = o.ItemCount(id);
        int want = whole ? held : now + 1;
        int n = Math.Min(Math.Min(want, held), now + room);
        if (n <= now) return;
        o.Items[id] = n;
        Send(o);
    }

    private void RemoveItem(string id, bool whole)
    {
        TradeOffer o = Draft;
        int now = o.ItemCount(id);
        if (now <= 0) return;
        if (whole || now <= 1) o.Items.Remove(id);
        else o.Items[id] = now - 1;
        Send(o);
    }

    private void ToggleTome(string id)
    {
        TradeOffer o = Draft;
        if (o.Tomes.ContainsKey(id)) o.Tomes.Remove(id);
        else
        {
            TomeDef? def = Data.Tomes.ById(id);
            if (def is null || TradeRules.TomeBlock(def, TradeView.TheirHold, TradeView.Theirs, o) is not null) return;
            o.Tomes[id] = TradeLedger.Snapshot().TomeLevel(id);
        }
        Send(o);
    }

    private void ToggleReady()
    {
        if (!Armed || Locked || Net is null || Net.TradeDraftPending) return;
        Net.TradeSetReady(!TradeView.Ready[TradeView.Side]);
    }

    private void Confirm()
    {
        if (!Armed || Locked || Net is null || !TradeView.Ready[0] || !TradeView.Ready[1]) return;
        Net.TradeConfirm();
    }

    private void Cancel()
    {
        if (!Armed) return;
        Close();   // OnClosed sends the cancel
    }

    // ================================================================ drawing

    public void Refresh()
    {
        if (!TradeView.Open || _bag is null) return;
        int me = TradeView.Side, other = TradeView.Other;
        string partner = TradeView.PartnerName;
        TradeOffer mine = Net?.TradeDraft ?? TradeView.Mine;
        TradeOffer theirs = TradeView.Theirs;
        TradeHoldings myHold = TradeLedger.Snapshot();
        TradeHoldings theirHold = TradeView.TheirHold;

        // Keyboard: the tiles are rebuilt below; keep focus on the same spot.
        HFlowContainer? focusFlow = null;
        int focusIndex = -1;
        if (GetViewport()?.GuiGetFocusOwner() is Button fb && fb.GetParent() is HFlowContainer ff && (ff == _bag || ff == _myOffer))
        {
            focusFlow = ff;
            focusIndex = fb.GetIndex();
        }

        _subtitle.Text = Loc.F("Trading with {0}", partner);
        _myHead.Text = Loc.T("YOU GIVE");
        _theirHead.Text = Loc.F("{0} GIVES", partner);
        Badge(_myBadge, TradeView.Ready[me], TradeView.Confirm[me]);
        Badge(_theirBadge, TradeView.Ready[other], TradeView.Confirm[other]);

        // ---- my offer
        Clear(_myOffer);
        foreach (var (id, n) in mine.Items)
            if (Catalog.ItemById(id) is { } def)
            {
                string tip = ItemTip(def, n) + "\n\n" + Loc.T("Click: take one back · Right-click: take all back");
                _myOffer.AddChild(TileButton(Icons.For(def), Catalog.RarityColor(def.Rarity), Loc.F("x{0}", n), tip, true,
                                             () => RemoveItem(id, Input.IsKeyPressed(Key.Shift)), () => RemoveItem(id, true)));
            }
        foreach (var (id, level) in mine.Tomes)
            if (Data.Tomes.ById(id) is { } t)
                _myOffer.AddChild(TileButton(Icons.For(t), t.Tint, Loc.F("Lv{0}", level),
                                             TomeTip(t, level) + "\n\n" + Loc.T("Click: take it back"), true, () => ToggleTome(id), () => ToggleTome(id)));
        EmptyNote(_myOffer, Loc.T("nothing yet — pick from your bag"));
        _myGold.Text = Loc.F("{0} gold  (you have {1})", mine.Gold, myHold.Gold);

        // ---- my bag: what I hold and have not offered yet
        Clear(_bag);
        foreach (var (id, level) in myHold.Tomes)
        {
            if (mine.HasTome(id) || Data.Tomes.ById(id) is not { } t) continue;
            string? block = TradeRules.TomeBlock(t, theirHold, theirs, mine);
            string tip = TomeTip(t, level) + "\n\n" + (block is null ? Loc.T("Click: offer this tome") : Loc.F(block, partner));
            _bag.AddChild(TileButton(Icons.For(t), t.Tint, Loc.F("Lv{0}", level), tip, block is null, () => ToggleTome(id), () => ToggleTome(id)));
        }
        foreach (var (id, n) in myHold.Items)
        {
            int left = n - mine.ItemCount(id);
            if (left <= 0 || Catalog.ItemById(id) is not { } def) continue;
            int room = TradeRules.RoomFor(def, theirHold, theirs, mine);
            string tip = ItemTip(def, left) + "\n\n" + (room > 0
                ? Loc.T("Click: offer one · Right-click: offer the stack")
                : Loc.F("{0} already has the most {1} anyone can hold.", partner, def.DisplayName));
            _bag.AddChild(TileButton(Icons.For(def), Catalog.RarityColor(def.Rarity), left > 1 ? Loc.F("x{0}", left) : "", tip, room > 0,
                                     () => AddItem(id, Input.IsKeyPressed(Key.Shift)), () => AddItem(id, true)));
        }
        EmptyNote(_bag, Loc.T("no items or tomes to trade"));

        // ---- their offer
        Clear(_theirOffer);
        foreach (var (id, n) in theirs.Items)
            if (Catalog.ItemById(id) is { } def)
                _theirOffer.AddChild(TileButton(Icons.For(def), Catalog.RarityColor(def.Rarity), Loc.F("x{0}", n), ItemTip(def, n), true, null, null));
        foreach (var (id, level) in theirs.Tomes)
            if (Data.Tomes.ById(id) is { } t)
                _theirOffer.AddChild(TileButton(Icons.For(t), t.Tint, Loc.F("Lv{0}", level), TomeTip(t, level), true, null, null));
        EmptyNote(_theirOffer, Loc.T("nothing yet"));
        _theirGold.Text = Loc.F("{0} gold", theirs.Gold);

        if (focusFlow is not null && IsOpen)
        {
            Control? next = null;
            for (int i = Math.Min(focusIndex, focusFlow.GetChildCount() - 1); i >= 0 && next is null; i--)
                if (focusFlow.GetChild(i) is Button { Disabled: false } c) next = c;
            (next ?? _ready).CallDeferred(Control.MethodName.GrabFocus);
        }

        // ---- after the trade
        int goldAfter = myHold.Gold - mine.Gold + theirs.Gold;
        int tomesAfter = myHold.Tomes.Count - mine.Tomes.Count + theirs.Tomes.Count;
        int itemsAfter = myHold.Items.Values.Sum() - mine.Items.Values.Sum() + theirs.Items.Values.Sum();
        var after = new StringBuilder();
        after.Append(Loc.F("Gold: {0}  ({1})", goldAfter, Signed(theirs.Gold - mine.Gold))).Append('\n');
        after.Append(Loc.F("Items: {0}  ({1})", itemsAfter, Signed(theirs.Items.Values.Sum() - mine.Items.Values.Sum()))).Append('\n');
        after.Append(Loc.F("Tomes: {0}/{1}", tomesAfter, myHold.TomeSlots));
        foreach (var (id, level) in theirs.Tomes)
            if (Data.Tomes.ById(id) is { } t) after.Append('\n').Append(Loc.F("+ {0} Lv {1}", t.DisplayName, level));
        after.Append('\n').Append(Loc.T("Weapons are not traded."));
        _after.Text = after.ToString();

        // ---- problems and status
        List<TradeProblem> problems = TradeView.Problems();
        string notice = "";
        if (TradeView.Notice.Length > 0)
        {
            object[] args = TradeView.NoticeArg.Length > 0 ? TradeView.NoticeArg.Split('\u001f') : Array.Empty<object>();
            notice = SafeF(TradeView.Notice, args);
        }
        foreach (TradeProblem p in problems)
        {
            if (p.Side < 0 && mine.IsEmpty && theirs.IsEmpty) continue;   // the status line says it
            string line = p.Text;
            if (!notice.Contains(line)) notice = notice.Length > 0 ? notice + "\n" + line : line;
        }
        _notice.Text = notice;
        _notice.AddThemeColorOverride("font_color", TradeView.Notice.StartsWith("The offer changed") && problems.Count == 0 ? new Color(0.75f, 0.45f, 0.05f) : BadC);

        bool pending = Net?.TradeDraftPending == true;
        bool iReady = TradeView.Ready[me], theyReady = TradeView.Ready[other];
        bool bothReady = iReady && theyReady;
        _ready.Text = iReady ? Loc.T("1  NOT READY") : Loc.T("1  READY");
        CuteStyle.StylePill(_ready, CuteStyle.Mint, iReady);
        _ready.Disabled = Locked || pending || (!iReady && problems.Count > 0);
        _confirm.Disabled = Locked || pending || !bothReady || TradeView.Confirm[me];
        CuteStyle.StylePill(_confirm, CuteStyle.Butter, TradeView.Confirm[me]);
        _cancel.Disabled = TradeView.Committing;
        foreach (Button b in _goldButtons) b.Disabled = Locked;

        _status.Text = TradeView.Committing ? Loc.T("Swapping...")
            : mine.IsEmpty && theirs.IsEmpty ? Loc.T("Put something on the table first.")
            : TradeView.Confirm[me] ? Loc.F("Waiting for {0} to CONFIRM...", partner)
            : bothReady ? Loc.T("Both READY: press CONFIRM to swap.")
            : iReady ? Loc.F("Waiting for {0} to be READY...", partner)
            : theyReady ? Loc.F("{0} is READY. Press READY when you are happy.", partner)
            : Loc.T("Press READY when you are happy. Any change resets READY.");
    }

    private static string SafeF(string fmt, object[] args)
    {
        try { return Loc.F(fmt, args); }
        catch (FormatException) { return Loc.T(fmt); }
    }

    private static string Signed(int v) => Loc.Ltr(v > 0 ? $"+{v}" : v.ToString());

    // ================================================================ parts

    private VBoxContainer Column(HBoxContainer parent, Color accent, out Label head, out PanelContainer badge)
    {
        PanelContainer card = CuteStyle.Panel(CuteStyle.Cream, new Color(accent, 0.8f), 16, 12);
        card.CustomMinimumSize = new Vector2(ColumnWidth, 0f);
        card.MouseFilter = MouseFilterEnum.Pass;
        parent.AddChild(card);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        col.AddThemeConstantOverride("separation", 6);
        card.AddChild(col);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        head = CuteStyle.Text("", 19, InkC);
        head.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(head);
        badge = CuteStyle.Tag("", CuteStyle.Muted, InkC, 13);
        row.AddChild(badge);
        col.AddChild(row);
        return col;
    }

    private HFlowContainer OfferArea(VBoxContainer col)
    {
        PanelContainer well = CuteStyle.Panel(new Color(0.93f, 0.90f, 0.86f, 0.9f), new Color(0, 0, 0, 0), 12, 8);
        well.CustomMinimumSize = new Vector2(0f, 2f * Tile + 22f);
        well.MouseFilter = MouseFilterEnum.Pass;
        col.AddChild(well);
        HFlowContainer flow = Flow();
        well.AddChild(flow);
        return flow;
    }

    private Control GoldRow(out Label label, bool editable)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        IconView coin = CuteStyle.Icon(Icons.ForSupply(0f, 1), 28f, new Color(1f, 0.82f, 0.3f));
        coin.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(coin);
        label = CuteStyle.Text("", 17, new Color(0.62f, 0.42f, 0.02f));
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (editable)
        {
            foreach (int step in new[] { -100, -10, -1 })
                row.AddChild(GoldButton(step));
            row.AddChild(label);
            foreach (int step in new[] { 1, 10, 100 })
                row.AddChild(GoldButton(step));
            Button all = CuteStyle.Pill(Loc.T("ALL"), CuteStyle.Butter, AllGold, 13);
            all.CustomMinimumSize = new Vector2(44f, 30f);
            _goldButtons.Add(all);
            row.AddChild(all);
        }
        else row.AddChild(label);
        return row;
    }

    private Button GoldButton(int step)
    {
        Button b = CuteStyle.Pill(Loc.Ltr(step > 0 ? $"+{step}" : $"{step}"), CuteStyle.Butter, () => StepGold(step), 13);
        b.CustomMinimumSize = new Vector2(step is 100 or -100 ? 50f : 40f, 30f);
        _goldButtons.Add(b);
        return b;
    }

    private static void Badge(PanelContainer badge, bool ready, bool confirmed)
    {
        if (badge.GetChildCount() > 0 && badge.GetChild(0) is Label l)
            l.Text = confirmed ? Loc.T("CONFIRMED") : ready ? Loc.T("READY") : Loc.T("not ready");
        var s = new StyleBoxFlat { BgColor = confirmed ? CuteStyle.Butter : ready ? CuteStyle.Mint : new Color(0.86f, 0.84f, 0.88f) };
        s.SetCornerRadiusAll(10);
        s.ContentMarginLeft = 8; s.ContentMarginRight = 8; s.ContentMarginTop = 1; s.ContentMarginBottom = 1;
        badge.AddThemeStyleboxOverride("panel", s);
    }

    private static HFlowContainer Flow()
    {
        // Left to right in every language, like the stats sheet's build box.
        var flow = new HFlowContainer { MouseFilter = MouseFilterEnum.Pass, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                                        LayoutDirection = LayoutDirectionEnum.Ltr };
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        return flow;
    }

    private static void Clear(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            n.RemoveChild(c);
            c.QueueFree();
        }
    }

    private static void EmptyNote(Container flow, string text)
    {
        if (flow.GetChildCount() == 0) flow.AddChild(CuteStyle.Text(text, 14, Soft));
    }

    /// <summary>A build-box tile that is also a button: icon, ring, badge,
    /// tooltip; greyed and inert when <paramref name="enabled"/> is false.</summary>
    private Button TileButton(IconSpec spec, Color ring, string badge, string tip, bool enabled, Action? click, Action? alt)
    {
        var b = new Button
        {
            CustomMinimumSize = new Vector2(Tile + 6f, Tile + 6f),
            FocusMode = click is null ? FocusModeEnum.None : FocusModeEnum.All,
            TooltipText = tip,
            MouseFilter = MouseFilterEnum.Stop,
            LayoutDirection = LayoutDirectionEnum.Ltr,
            Disabled = click is null || !enabled,
        };
        var normal = CuteStyle.Box(new Color(0.18f, 0.17f, 0.26f, 0.95f), new Color(ring, 0.7f), 10, 2, 3);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("disabled", CuteStyle.Box(new Color(0.18f, 0.17f, 0.26f, click is null ? 0.95f : 0.45f), new Color(ring, click is null ? 0.7f : 0.2f), 10, 2, 3));
        b.AddThemeStyleboxOverride("hover", CuteStyle.Box(new Color(0.24f, 0.23f, 0.34f, 1f), Colors.White, 10, 3, 3));
        b.AddThemeStyleboxOverride("pressed", CuteStyle.Box(new Color(0.30f, 0.29f, 0.42f, 1f), Colors.White, 10, 3, 3));
        b.AddThemeStyleboxOverride("focus", CuteStyle.Box(new Color(0, 0, 0, 0), Colors.White, 10, 3, 3));

        IconView icon = IconView.Make(Tile);
        icon.Set(spec, ring);
        icon.MouseFilter = MouseFilterEnum.Ignore;
        // Anchored like the badge (full rect, 3 px in): a manual position
        // drifted out of the box, differently in each layout direction.
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        icon.OffsetLeft = 3f;
        icon.OffsetTop = 3f;
        icon.OffsetRight = -3f;
        icon.OffsetBottom = -3f;
        if (click is not null && !enabled) icon.Modulate = new Color(1f, 1f, 1f, 0.35f);
        b.AddChild(icon);
        if (badge.Length > 0)
        {
            var l = new Label
            {
                Text = badge,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                MouseFilter = MouseFilterEnum.Ignore,
                AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", Colors.White);
            l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
            l.AddThemeConstantOverride("outline_size", 4);
            l.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            l.OffsetRight = -3f;
            l.OffsetBottom = -1f;
            b.AddChild(l);
        }
        if (click is not null)
        {
            b.Pressed += () => { if (Armed) click(); };
            b.GuiInput += e =>
            {
                if (alt is not null && e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && Armed && enabled)
                {
                    alt();
                    b.AcceptEvent();
                }
            };
        }
        return b;
    }

    private static string ItemTip(ItemDef def, int count)
    {
        var sb = new StringBuilder(Loc.T(def.DisplayName));
        sb.Append("  ·  ").Append(Loc.T(def.Rarity.ToString()));
        if (count > 1) sb.Append('\n').Append(Loc.F("x{0}", count));
        if (def.MaxStacks > 0) sb.Append('\n').Append(Loc.F("stacks up to {0}", def.MaxStacks));
        string d = Loc.T(def.Description.Trim());
        if (d.Length > 0) sb.Append("\n\n").Append(d);
        foreach (ItemEffect e in def.Effects) if (e is not null) sb.Append('\n').Append(Loc.T(Catalog.DescribeEffect(e)));
        return sb.ToString();
    }

    private static string TomeTip(TomeDef t, int level)
    {
        var sb = new StringBuilder(Loc.T(t.DisplayName));
        sb.Append('\n').Append(Loc.F("Lv {0}/{1}  {2}", level, t.MaxLevel, t.DescribeTotal(level)));
        string d = Loc.T(t.Description.Trim());
        if (d.Length > 0) sb.Append("\n\n").Append(d);
        return sb.ToString();
    }
}
