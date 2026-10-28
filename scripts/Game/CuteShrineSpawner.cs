using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.Stats;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>
/// Places and runs the Shrine of Balance and the Wishing Well
/// (<see cref="CuteShrines"/> holds the numbers). Its own node beside
/// <see cref="ShrineSpawner"/>, attached once per run by Meta/ShopExtras and
/// populated per map by the stage director right after the map shrines, so
/// the map-shrine code, its HUD markers and the autopilot (which only walks to
/// charge rings) never see these: the bot ignores them by construction.
///
/// E in reach: the balance shrine opens <see cref="BalancePanel"/> (confirm or
/// cancel; never in an unattended run), the well takes gold for a blessing at
/// once. Every grant is a <see cref="StatSheet"/> modifier with its own source.
///
/// Captures: <c>--cute-shrine-shot=&lt;png&gt;</c> walks to each kind and
/// photographs it (<c>-balance</c> / <c>-well</c> / <c>-panel</c> suffixes).
/// </summary>
public sealed partial class CuteShrineSpawner : Node
{
    private const string InteractAction = "interact";

    public static CuteShrineSpawner? Instance { get; private set; }

    public Rng Rng { get; private set; }

    private readonly List<CuteShrine> _shrines = new();
    private ShrinePrompt _prompt = null!;
    private BalancePanel _panel = null!;
    private string _keyName = "E";
    private bool _hasAction;
    private bool _interactRequested;
    private CuteShrine? _focus;

    public int Placed { get; private set; }
    public int Balanced { get; private set; }
    public int WellThrows { get; private set; }
    public int WellBonuses { get; private set; }
    public int WellGold { get; private set; }

    public IReadOnlyList<CuteShrine> Shrines => _shrines;

    public string SummaryFields => $"cuteshrines={Placed} balanced={Balanced} wellthrows={WellThrows} wellbonus={WellBonuses} wellgold={WellGold}";

    public CuteShrineSpawner(Rng rng) => Rng = rng;

    public CuteShrineSpawner() => Rng = new Rng(0xC0751E5UL);

    public static CuteShrineSpawner Attach(Node parent, ulong runSeed)
    {
        var s = new CuteShrineSpawner(new Rng(runSeed ^ 0xC0FFEE5BA1A2CEUL));
        parent.AddChild(s);
        return s;
    }

    public override void _Ready()
    {
        Name = "CuteShrines";
        Instance = this;
        _prompt = new ShrinePrompt { Name = "CuteShrinePrompt" };
        AddChild(_prompt);
        var layer = new CanvasLayer { Name = "CuteShrineUI", Layer = 3 };
        AddChild(layer);
        _panel = new BalancePanel();
        layer.AddChild(_panel);
        _hasAction = InputMap.HasAction(InteractAction);
        _keyName = KeyName();

        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--cute-shrine-shot=")) _ = Capture(arg["--cute-shrine-shot=".Length..]);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------ placement

    /// <summary>Called by the stage director after the map shrines: open floor
    /// in the same band, clear of every map shrine and of each other.</summary>
    public static void Populate(Node3D map, Rng rng, float arenaRadius, StageDef stage)
    {
        if (Instance is not { } self || map is null || !GodotObject.IsInstanceValid(map)) return;
        self._shrines.Clear();
        self._focus = null;
        self._prompt.HideNow();

        var kinds = new List<CuteShrineKind>();
        for (int i = 0; i < Math.Max(0, stage.BalanceShrines); i++) kinds.Add(CuteShrineKind.Balance);
        for (int i = 0; i < Math.Max(0, stage.WishingWells); i++) kinds.Add(CuteShrineKind.Well);

        var avoid = new List<Vector3>();
        if (ShrineSpawner.Instance is { } maps)
            foreach (MapShrine s in maps.Shrines)
                if (GodotObject.IsInstanceValid(s)) avoid.Add(s.Position);

        Arena? arena = map as Arena;
        float outer = arenaRadius * 0.72f;
        float inner = Math.Max(stage.ClearRadius + 10f, 20f);
        foreach (CuteShrineKind kind in kinds)
        {
            Vector3? found = null;
            for (int attempt = 0; attempt < 60 && found is null; attempt++)
            {
                float d = Mathf.Lerp(inner, outer, MathF.Sqrt(rng.Float()));
                Vector3 c = rng.OnUnitCircleXZ() * d;
                if (arena is not null) c = arena.ClearSpotNear(c, 2.2f);
                c.Y = stage.Underwater ? UnderwaterTerrain.Height(c) : 0f;
                float flat = new Vector2(c.X, c.Z).Length();
                if (flat < inner * 0.8f || flat > arenaRadius * 0.8f) continue;
                float spacing = attempt < 40 ? CuteShrines.MinSpacing : CuteShrines.MinSpacing * 0.5f;
                if (avoid.Any(a => new Vector2(a.X - c.X, a.Z - c.Z).Length() < spacing)) continue;
                found = c;
            }
            if (found is not { } at)
            {
                GD.Print($"[cute-shrine] no spot for {kind}; skipped");
                continue;
            }
            avoid.Add(at);
            var shrine = new CuteShrine(kind) { Position = at, Rotation = new Vector3(0f, rng.Range(0f, Mathf.Tau), 0f) };
            map.AddChild(shrine);
            if (stage.Underwater) Visuals.ReefPlacement.Ground(shrine, 2f);
            self._shrines.Add(shrine);
            self.Placed++;
            GD.Print($"[cute-shrine] placed {kind} at=({at.X:0.0},{at.Z:0.0})");
        }
    }

    // ---------------------------------------------------------------- frame

    public static bool InReach(CuteShrine s, Vector3 at)
    {
        Vector3 d = s.GlobalPosition - at;
        float dy = MathF.Abs(d.Y); d.Y = 0f;
        return d.LengthSquared() <= CuteShrines.InteractRadius * CuteShrines.InteractRadius && dy <= CuteShrines.InteractHeight;
    }

    public override void _Process(double delta)
    {
        _shrines.RemoveAll(s => !IsInstanceValid(s) || s.IsQueuedForDeletion());
        bool pressed = _interactRequested;
        _interactRequested = false;

        Player? player = Player.Instance;
        bool live = player is not null && IsInstanceValid(player) && player.IsAlive && Main.Instance?.Run.Over != true;
        Vector3 me = live ? player!.GlobalPosition : Vector3.Zero;

        _focus = null;
        float best = float.MaxValue;
        foreach (CuteShrine s in _shrines)
        {
            if (s.Spent) continue;
            Vector3 d = s.GlobalPosition - me; d.Y = 0f;
            bool reach = live && InReach(s, me);
            s.ShowName(live && d.LengthSquared() < 16f * 16f && !reach);
            if (reach && d.LengthSquared() < best) { best = d.LengthSquared(); _focus = s; }
        }

        if (_focus is null || _panel.IsOpen) { _prompt.HideNow(); return; }
        ShowPrompt(_focus, player!);
        if (pressed) Interact(_focus, player!);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_hasAction || e is InputEventKey { Echo: true } || Net.Coop.LocalHold) return;
        if (!e.IsActionPressed(InteractAction) || _focus is null || _panel.IsOpen) return;
        _interactRequested = true;
        GetViewport().SetInputAsHandled();
    }

    private void ShowPrompt(CuteShrine s, Player player)
    {
        if (s.Kind == CuteShrineKind.Balance)
        {
            var plan = CuteShrines.PlanBalance(player.Stats);
            _prompt.ShowText(s.Top, plan is { } p
                    ? Meta.Loc.F("{0} — find your balance\nspread your {1} around", _keyName, Catalog.StatName(p.Top.Stat))
                    : Meta.Loc.T(CuteShrines.Name(s.Kind)) + "\n" + Meta.Loc.T("come back with a stronger bonus"),
                plan is null ? ShrinePrompt.TooPoor : CuteShrines.Tint(s.Kind).Lightened(0.55f));
            return;
        }
        int gold = Inventory.Current?.Gold ?? 0;
        int price = CuteShrines.WellPrice(WellThrows);
        int left = CuteShrines.WellUsesPerShrine - s.Uses;
        bool ok = gold >= price;
        _prompt.ShowText(s.Top,
            ok ? Meta.Loc.F(left == 1 ? "{0} — toss {1} gold and make a wish\n{2} wish left" : "{0} — toss {1} gold and make a wish\n{2} wishes left",
                            _keyName, price, left)
               : Meta.Loc.F("{0} — toss {1} gold  (need {2} more)", _keyName, price, price - gold),
            ok ? CuteShrines.Tint(s.Kind).Lightened(0.55f) : ShrinePrompt.TooPoor);
    }

    private void Interact(CuteShrine s, Player player)
    {
        if (s.Kind == CuteShrineKind.Balance) OpenBalance(s, player);
        else Wish(s, player);
    }

    // -------------------------------------------------------------- balance

    private void OpenBalance(CuteShrine s, Player player)
    {
        var plan = CuteShrines.PlanBalance(player.Stats);
        if (plan is null)
        {
            s.Deny();
            _prompt.Deny();
            return;
        }
        if (Screenshot.Auto) return;   // never pause an unattended run
        _panel.Open(player.Stats, plan.Value, () => Balance(s, player));
    }

    /// <summary>Apply the plan (recomputed now, so it matches the sheet).</summary>
    private void Balance(CuteShrine s, Player player)
    {
        if (!IsInstanceValid(s) || s.Spent || !IsInstanceValid(player)) return;
        var plan = CuteShrines.PlanBalance(player.Stats);
        if (plan is null) return;
        CuteShrines.ApplyBalance(player.Stats, plan.Value, new BalanceGrant(Balanced + 1));
        Balanced++;
        s.Celebrate();
        s.MarkSpent();
        EventBus.Publish(new PopupRequested(s.Top, "IN BALANCE!", CuteShrines.Tint(s.Kind).Lightened(0.3f), 1.6f));
        EventBus.Publish(new PopupRequested(s.Top + Vector3.Down * 0.7f,
            Meta.Loc.F("{0}, +{1:0.#} units to the rest", CuteShrines.DescribeUnits(plan.Value.Top, -plan.Value.Take), plan.Value.Give),
            new Color(0.95f, 0.92f, 1f), 1f));
        GD.Print($"[cute-shrine] balance top={plan.Value.Top.Stat} take={plan.Value.Take:0.00}u give={plan.Value.Give:0.00}u each");
    }

    /// <summary>Source tag for one balancing.</summary>
    public sealed class BalanceGrant
    {
        public int Index { get; }
        public BalanceGrant(int index) => Index = index;
        public override string ToString() => $"Shrine of Balance x{Index}";
    }

    // ----------------------------------------------------------------- well

    private void Wish(CuteShrine s, Player player)
    {
        Inventory? inv = Inventory.Current;
        if (inv is null) return;
        int price = CuteShrines.WellPrice(WellThrows);
        if (!inv.SpendGold(price))
        {
            s.Deny();
            _prompt.Deny();
            return;
        }
        WellThrows++;
        WellGold += price;
        s.Uses++;
        float luck = player.Stats.Get(Stat.Luck);
        List<ShrineBoon> boons = MapShrines.RollBoons(Rng, 2, luck);
        bool bonus = Rng.Float() < CuteShrines.WellBonus(luck);
        int grants = bonus && boons.Count > 1 ? 2 : 1;
        EventBus.Publish(new PopupRequested(s.Top + Vector3.Down * 0.4f, $"-{price} gold  ·  splash!", new Color(1f, 0.82f, 0.4f)));
        for (int i = 0; i < grants; i++)
        {
            ShrineBoon b = boons[i];
            player.Stats.Add(b.Stat, b.Op, b.Value, new WishGrant(WellThrows, b));
            EventBus.Publish(new PopupRequested(s.Top + Vector3.Up * (0.6f * i), $"{Meta.Loc.T(b.Title)} {b.Effect}", b.Color, 1.4f));

        }
        if (grants > 1)
        {
            WellBonuses++;
            EventBus.Publish(new PopupRequested(s.Top + Vector3.Up * 1.4f, "THE WELL LIKED THAT ONE!", new Color(0.7f, 0.95f, 1f), 1.5f));
        }
        s.Celebrate();
        if (s.Uses >= CuteShrines.WellUsesPerShrine) s.MarkSpent();
        GD.Print($"[cute-shrine] wish price={price} bonus={grants > 1} grants={string.Join(" | ", boons.Take(grants).Select(b => $"{b.Title} {b.Effect}"))}");
    }

    public sealed class WishGrant
    {
        public int Index { get; }
        public ShrineBoon Boon { get; }
        public WishGrant(int index, ShrineBoon boon) { Index = index; Boon = boon; }
        public override string ToString() => $"Wishing Well: {Boon.Title}";
    }

    // -------------------------------------------------------------- capture

    /// <summary>--cute-shrine-shot=&lt;png&gt;: stand in front of each kind and
    /// photograph it, then open the balance panel with a lopsided build and
    /// photograph that, then quit.</summary>
    private async Task Capture(string path)
    {
        SceneTree tree = GetTree();
        async Task Wait(double s) => await ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        string stem = path.EndsWith(".png") ? path[..^4] : path;
        await Wait(4.5);   // after the map's title card

        foreach (CuteShrineKind kind in new[] { CuteShrineKind.Balance, CuteShrineKind.Well })
        {
            CuteShrine? s = _shrines.FirstOrDefault(x => x.Kind == kind);
            if (s is null || Player.Instance is not { } p) continue;
            Camera3D? cam = GetViewport().GetCamera3D();
            Vector3 fwd = cam is null ? Vector3.Forward : -cam.GlobalBasis.Z;
            fwd.Y = 0f;
            fwd = fwd.LengthSquared() < 0.01f ? Vector3.Forward : fwd.Normalized();
            Vector3 right = fwd.Cross(Vector3.Up);
            // Beside it and in reach, so the shrine and its prompt are both in frame.
            p.Teleport(s.GlobalPosition - fwd * 0.6f - right * 2.3f + Vector3.Up * 0.3f);
            EnemyManager.Instance?.ClearField(3f);
            await Wait(1.2);
            await Screenshot.Capture(tree, this, $"{stem}-{kind.ToString().ToLowerInvariant()}.png");
            GD.Print($"[cute-shrine] shot {kind}");
        }

        if (Player.Instance is { } pl && _shrines.FirstOrDefault(x => x.Kind == CuteShrineKind.Balance) is { } bal)
        {
            // A lopsided build: lots of damage, a little armour.
            pl.Stats.Add(Stat.Damage, ModifierOp.Additive, 0.64f, this);
            pl.Stats.Add(Stat.Armor, ModifierOp.Flat, 4f, this);
            if (CuteShrines.PlanBalance(pl.Stats) is { } plan)
            {
                _panel.Open(pl.Stats, plan, () => Balance(bal, pl));
                await Wait(0.8);
                await Screenshot.Capture(tree, this, $"{stem}-panel.png");
                _panel.Confirm();
                await Wait(0.4);
                GD.Print($"[cute-shrine] panel shot; balanced={Balanced} dmg={pl.Stats.Get(Stat.Damage):0.00} armor={pl.Stats.Get(Stat.Armor):0.0}");
            }
        }
        if (_shrines.FirstOrDefault(x => x.Kind == CuteShrineKind.Well) is { } well && Player.Instance is { } pw)
        {
            Inventory.Current?.GrantGold(500);
            for (int i = 0; i < 3; i++) Wish(well, pw);
            GD.Print($"[cute-shrine] wished {WellThrows} spent={WellGold} spent_well={well.Spent}");
        }
        GD.Print("[cute-shrine] " + SummaryFields);
        tree.Quit();
    }

    private static string KeyName()
    {
        if (!InputMap.HasAction(InteractAction)) return "E";
        foreach (InputEvent ev in InputMap.ActionGetEvents(InteractAction))
        {
            if (ev is not InputEventKey k) continue;
            Key code = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            if (code != Key.None) return OS.GetKeycodeString(code);
        }
        return "E";
    }
}
