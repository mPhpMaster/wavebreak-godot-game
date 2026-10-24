using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>
/// Owns the map's stations and breakables: the shady merchant, the microwave and
/// the pots/vases/crates. Attached once per run (the totals belong to the run),
/// populated per map by <see cref="StageDirector"/> right after the chests.
///
/// <b>Placement copies the chest rules.</b> Ground is found with physics rays on
/// the next physics frames (a map built this frame has no bodies yet), slopes,
/// pits, overhangs and visible prop boxes are refused, and stations keep clear of
/// chests. Placement waits a few physics frames so the chest spawner, which places
/// three per frame, has already put its chests down to be avoided.
///
/// <b>Chests win the key.</b> When a chest and a station are both in reach, E
/// opens the chest; the station prompt only shows when no chest is in reach.
///
/// <b>Prices scale with rarity and phase.</b> See <see cref="MerchantPrice"/>.
/// </summary>
public sealed partial class Interactables : Node
{
    // -------------------------------------------------------------- tuning

    /// <summary>Merchant price by rarity (Common..Legendary) in phase 1. A merchant
    /// item is a <em>chosen</em> item, so it costs more than a chest's random one
    /// (25, rising) at the same rarity.</summary>
    public static readonly int[] MerchantBase = { 30, 45, 75, 120, 200 };

    /// <summary>Microwave price by the rarity of the item put in. About two
    /// thirds of the merchant's: a copy of something you already chose is worth
    /// less than a fresh pick, and the microwave works once.</summary>
    public static readonly int[] MicrowaveBase = { 20, 30, 50, 80, 130 };

    /// <summary>Price multiplier per phase after the first (gold income climbs
    /// with enemy value and pot drops, so a flat price would stop mattering).</summary>
    public const float PhasePriceStep = 0.5f;

    /// <summary>Luck added to the player's when stocking the merchant, and the
    /// lowest rarity it stocks.</summary>
    public const float MerchantLuckBonus = 1f;
    public const Rarity MerchantFloor = Rarity.Uncommon;

    /// <summary>Breakables are scattered in clusters of this many.</summary>
    public const int ClusterMin = 2, ClusterMax = 4;

    public const float StationSpacing = 22f;
    public const float StationChestClearance = 6f;
    public const float PotSpacing = 1.2f;
    public const float MaxPerchHeight = 4.5f;

    /// <summary>Physics frames to wait before placing, so chests go first.</summary>
    private const int PlacementDelayFrames = 5;

    private const string InteractAction = "interact";

    // --------------------------------------------------------------- state

    public static Interactables? Instance { get; private set; }

    public Rng Rng { get; private set; }

    private Node3D? _map;
    private float _radius;
    private Vector3 _centre;
    private int _phase = 1;
    private int _wantMerchants, _wantMicrowaves, _wantPots;
    private int _placeIn = -1;

    private readonly List<Station> _stations = new();
    private readonly List<Aabb> _obstacles = new();
    private Breakables? _pots;

    private StationPrompt _prompt = null!;
    private MerchantPanel _merchantPanel = null!;
    private MicrowavePanel _microwavePanel = null!;
    private string _keyName = "E";
    private bool _hasAction;
    private bool _interactRequested;
    private Station? _focus;

    // Run totals.
    public int MerchantBuys { get; private set; }
    public int MerchantSpent { get; private set; }
    public int MicrowaveUses { get; private set; }
    public int MicrowaveSpent { get; private set; }
    public int PotsBroken { get; private set; }
    public int PotsPlaced { get; private set; }
    public int StationsPlaced { get; private set; }
    /// <summary>Times the player rested at a rest-room campfire.</summary>
    public int Rests { get; private set; }

    public string SummaryFields =>
        $"merchantbuys={MerchantBuys} merchantspent={MerchantSpent} microwaves={MicrowaveUses} " +
        $"microwavespent={MicrowaveSpent} pots={PotsBroken}/{PotsPlaced} stations={StationsPlaced} rests={Rests}";

    public IReadOnlyList<Station> Stations => _stations;

    public Interactables() => Rng = new Rng(0x5A0DUL);

    public Interactables(Rng rng) => Rng = rng;

    /// <summary>Creates the run's hub under <paramref name="parent"/>. Once per run.</summary>
    public static Interactables Attach(Node parent, ulong runSeed)
    {
        var hub = new Interactables(new Rng(runSeed ^ 0x51ADE3E2C4A7F00DUL));
        parent.AddChild(hub);
        return hub;
    }

    public override void _Ready()
    {
        Name = "Interactables";
        Instance = this;

        _prompt = new StationPrompt();
        AddChild(_prompt);

        // Panels on their own layer above the HUD, so they draw over it.
        var layer = new CanvasLayer { Name = "StationUI", Layer = 5 };
        AddChild(layer);
        _merchantPanel = new MerchantPanel();
        layer.AddChild(_merchantPanel);
        _microwavePanel = new MicrowavePanel();
        layer.AddChild(_microwavePanel);

        _hasAction = InputMap.HasAction(InteractAction);
        _keyName = KeyName();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------- populate

    /// <summary>
    /// Replaces the stations and pots with a fresh set for <paramref name="map"/>,
    /// counts taken from <paramref name="stage"/>. Placement happens a few
    /// physics frames later.
    /// </summary>
    public void Populate(Node3D map, Rng rng, float arenaRadius, StageDef stage, int phase,
                         Vector3 centre = default)
    {
        Clear();
        Rng = rng ?? Rng;
        _map = map;
        _radius = MathF.Max(8f, arenaRadius);
        _centre = centre;
        _phase = Math.Max(1, phase);
        _wantMerchants = Math.Max(0, stage.Merchants);
        _wantMicrowaves = Math.Max(0, stage.Microwaves);
        _wantPots = Math.Clamp(stage.Breakables, 0, 200);
        _placeIn = PlacementDelayFrames;
    }

    /// <summary>
    /// The rest room's stations replace the map's: no placement search, the
    /// room has already put them where they go. Prices follow
    /// <paramref name="phase"/>, the map just cleared, so the merchant costs
    /// what it would have cost on that map.
    /// </summary>
    public void UseRestRoom(Node3D room, int phase, IEnumerable<Station> stations)
    {
        Clear();
        _map = room;
        _radius = 16f;
        _centre = Vector3.Zero;
        _phase = Math.Max(1, phase);
        foreach (Station s in stations)
        {
            if (s is null || !IsInstanceValid(s)) continue;
            _stations.Add(s);
            StationsPlaced++;
        }
        GD.Print($"[stations] rest room: {_stations.Count} stations, prices for phase {_phase}");
    }

    public void Clear()
    {
        foreach (Station s in _stations)
            if (IsInstanceValid(s) && !s.IsQueuedForDeletion()) s.QueueFree();
        _stations.Clear();
        if (_pots is not null && IsInstanceValid(_pots) && !_pots.IsQueuedForDeletion()) _pots.QueueFree();
        _pots = null;
        _obstacles.Clear();
        _map = null;
        _focus = null;
        _placeIn = -1;
        _prompt?.HideNow();
        _merchantPanel?.Close();
        _microwavePanel?.Close();
    }

    // --------------------------------------------------------------- prices

    private float PhaseMultiplier => 1f + PhasePriceStep * (_phase - 1);

    public int MerchantPrice(Rarity rarity)
        => (int)MathF.Round(MerchantBase[Math.Clamp((int)rarity, 0, MerchantBase.Length - 1)] * PhaseMultiplier);

    public int MicrowavePrice(Rarity rarity)
        => (int)MathF.Round(MicrowaveBase[Math.Clamp((int)rarity, 0, MicrowaveBase.Length - 1)] * PhaseMultiplier);

    // ------------------------------------------------------------- merchant

    /// <summary>Stocks the merchant the first time anyone looks: three distinct
    /// items from the offer pool (same catalogue, caps and luck curve as a chest,
    /// floored at uncommon). A slot the pool cannot fill stays empty.</summary>
    public void EnsureStocked(Merchant m)
    {
        if (m.Offers is not null) return;
        var offers = new Offer?[Merchant.SlotCount];
        Inventory? inv = Inventory.Current;
        OfferPool? pool = OfferPool.Current;
        var taken = new HashSet<string>();
        for (int slot = 0; slot < offers.Length && pool is not null; slot++)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Offer o = pool.RollChest(inv, Player.Instance?.Stats, MerchantLuckBonus, MerchantFloor, Rng);
                if (o.Item is null) break;                    // pool exhausted: supply only
                if (!taken.Add(o.Item.Id)) continue;
                offers[slot] = o;
                m.Prices[slot] = MerchantPrice(o.Item.Rarity);
                break;
            }
        }
        m.Offers = offers;
        m.Refresh();
        GD.Print($"[merchant] stocked phase={_phase} " + string.Join(" ",
                 Array.ConvertAll(offers, o => o?.Item is { } i ? $"{i.Id}:{i.Rarity}" : "-")));
    }

    /// <summary>Buys one slot. Refuses (and says why) when it is empty, maxed or
    /// unaffordable; the slot is emptied on success.</summary>
    public bool TryBuy(Merchant m, int slot)
    {
        EnsureStocked(m);
        Inventory? inv = Inventory.Current;
        if (inv is null || m.Offers is null || slot < 0 || slot >= m.Offers.Length) return false;
        if (m.Offers[slot]?.Item is not { } item) return false;

        if (inv.IsMaxed(item))
        {
            EventBus.Publish(new PopupRequested(m.Top, "MAXED", new Color(1f, 0.38f, 0.34f)));
            return false;
        }

        int price = m.Prices[slot];
        if (!inv.SpendGold(price))
        {
            EventBus.Publish(new PopupRequested(m.Top, $"need {price - inv.Gold} more gold", new Color(1f, 0.38f, 0.34f)));
            return false;
        }

        inv.Add(item);
        m.Offers[slot] = null;
        m.Refresh();
        MerchantBuys++;
        MerchantSpent += price;
        Color colour = Catalog.RarityColor(item.Rarity);
        EventBus.Publish(new PopupRequested(m.Top + Vector3.Up * 0.9f, item.DisplayName, colour, 1.6f));
        EventBus.Publish(new PopupRequested(m.Top, $"-{price} gold", new Color(1f, 0.78f, 0.25f)));
        GD.Print($"[merchant] bought {item.Id} rarity={item.Rarity} price={price} gold={inv.Gold}");
        return true;
    }

    // ------------------------------------------------------------ microwave

    /// <summary>Duplicates one held item (+1 stack) for gold. Once per microwave;
    /// refuses an item at its stack cap or one not held.</summary>
    public bool TryCook(Microwave mw, ItemDef item)
    {
        Inventory? inv = Inventory.Current;
        if (inv is null || mw.Used || item is null) return false;
        if (inv.StacksOf(item) <= 0 || inv.IsMaxed(item)) return false;

        int price = MicrowavePrice(item.Rarity);
        if (!inv.SpendGold(price))
        {
            EventBus.Publish(new PopupRequested(mw.Top, $"need {price - inv.Gold} more gold", new Color(1f, 0.38f, 0.34f)));
            return false;
        }
        if (!inv.Add(item))
        {
            // Refund unscaled: a foreign GoldGained is credited as-is.
            EventBus.Publish(new GoldGained(price, inv.Gold + price));
            return false;
        }

        mw.MarkUsed();
        MicrowaveUses++;
        MicrowaveSpent += price;
        EventBus.Publish(new PopupRequested(mw.Top + Vector3.Up * 0.9f, $"{item.DisplayName} +1",
                                            Catalog.RarityColor(item.Rarity), 1.6f));
        EventBus.Publish(new PopupRequested(mw.Top, $"-{price} gold", new Color(1f, 0.78f, 0.25f)));
        GD.Print($"[microwave] duplicated {item.Id} x{inv.StacksOf(item)} price={price} gold={inv.Gold}");
        return true;
    }

    public void NotePotBroken() => PotsBroken++;

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("Interactables.proc");
        _stations.RemoveAll(s => !IsInstanceValid(s) || s.IsQueuedForDeletion());
        if (_map is not null && !IsInstanceValid(_map)) Clear();

        bool pressed = _interactRequested;
        _interactRequested = false;

        Player? player = Player.Instance;
        bool live = player is not null && player.IsAlive && Main.Instance?.Run.Over != true;
        _focus = live ? Focus(player!.GlobalPosition) : null;

        if (_focus is null)
        {
            _prompt.HideNow();
            return;
        }

        _prompt.ShowFor(_focus.Top, PromptText(_focus), _focus.Tint.Lerp(Colors.White, 0.5f));
        if (pressed) Open(_focus);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_hasAction || e is InputEventKey { Echo: true } || Net.Coop.LocalHold) return;
        if (!e.IsActionPressed(InteractAction)) return;
        if (_focus is null) return;
        _interactRequested = true;
        GetViewport().SetInputAsHandled();
    }

    private void Open(Station s)
    {
        switch (s)
        {
            case Merchant m:
                EnsureStocked(m);
                _merchantPanel.Open(m);
                break;
            case Microwave mw when !mw.Used:
                _microwavePanel.Open(mw);
                break;
            case Campfire fire:
                Rest(fire);
                break;
        }
    }

    // ------------------------------------------------------------- campfire

    /// <summary>Rest at a campfire: health back to full. Always works (the
    /// rest room is safe, so there is nothing to ration); at full health it
    /// only says so.</summary>
    public bool Rest(Campfire fire)
    {
        Player? p = Player.Instance;
        if (p is null || !IsInstanceValid(p) || !p.IsAlive) return false;
        float missing = p.Health.Max - p.Health.Current;
        fire.Rests++;
        Rests++;
        if (missing <= 0.5f)
        {
            EventBus.Publish(new PopupRequested(fire.Top, Meta.Loc.T("already at full health"), Campfire.FireTint.Lerp(Colors.White, 0.4f)));
            GD.Print($"[restroom] rested at full health hp={p.Health.Current:0}/{p.Health.Max:0}");
            return true;
        }
        p.Health.Heal(missing);
        EventBus.Publish(new PopupRequested(fire.Top + Vector3.Up * 0.6f, Meta.Loc.T("Rested: health full"), new Color(0.55f, 1f, 0.6f), 1.4f));
        GD.Print($"[restroom] rested: healed {missing:0} hp={p.Health.Current:0}/{p.Health.Max:0}");
        return true;
    }

    private string PromptText(Station s) => s switch
    {
        Merchant m when m.Offers is not null && !m.Usable => Meta.Loc.F("{0} — merchant (sold out)", _keyName),
        Merchant => Meta.Loc.F("{0} — trade with merchant", _keyName),
        Microwave { Used: true } => Meta.Loc.T("microwave (used)"),
        Microwave => Meta.Loc.F("{0} — use microwave · from {1} gold", _keyName, MicrowavePrice(Rarity.Common)),
        Campfire when Player.Instance is { } pl && pl.Health.Current >= pl.Health.Max - 0.5f
            => Meta.Loc.F("{0} — rest by the campfire (health full)", _keyName),
        Campfire => Meta.Loc.F("{0} — rest by the campfire · heal to full", _keyName),
        _ => $"{_keyName} — {Meta.Loc.T(s.Verb)}",

    };

    private Station? Focus(Vector3 at)
    {
        // A chest in reach owns the key.
        if (ChestSpawner.Instance is { } chests && chests.Nearest(at, ChestSpawner.InteractRadius) is { } c
            && ChestSpawner.InReach(c, at))
            return null;

        Station? best = null;
        float bestSq = float.MaxValue;
        foreach (Station s in _stations)
        {
            if (!IsInstanceValid(s) || !s.InReach(at)) continue;
            if (s is Microwave { Used: true }) continue;
            float sq = (s.GlobalPosition - at).LengthSquared();
            if (sq < bestSq) { best = s; bestSq = sq; }
        }
        return best;
    }

    // ------------------------------------------------------------ placement

    public override void _PhysicsProcess(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("Interactables.phys");
        if (_placeIn < 0) return;
        if (_placeIn-- > 0) return;
        _placeIn = -1;

        if (_map is null || !IsInstanceValid(_map) || !_map.IsInsideTree()) return;
        PhysicsDirectSpaceState3D? space = _map.GetWorld3D()?.DirectSpaceState;
        if (space is null) return;

        CollectObstacles(_map);
        Vector3 start = Player.Instance is { } p && IsInstanceValid(p) ? p.GlobalPosition : _centre;

        for (int i = 0; i < _wantMerchants; i++) PlaceStation(space, new Merchant(), start, 0.2f, 0.7f);
        for (int i = 0; i < _wantMicrowaves; i++) PlaceStation(space, new Microwave(), start, 0.15f, 0.85f);
        PlacePots(space, start);
    }

    private void PlaceStation(PhysicsDirectSpaceState3D space, Station station, Vector3 start, float minFrac, float maxFrac)
    {
        for (int attempt = 0; attempt < 64; attempt++)
        {
            float lo = _radius * minFrac, hi = _radius * maxFrac;
            float d = MathF.Sqrt(Rng.Range(lo * lo, hi * hi));
            Vector3 candidate = _centre + Rng.OnUnitCircleXZ() * d;
            float spacing = attempt < 40 ? StationSpacing : StationSpacing * 0.5f;

            if (!Validate(space, candidate, start, station.FootprintRadius, 2.8f, 0.9f, 2.5f, out Vector3 spot)) continue;
            if (Flat(spot).DistanceTo(Flat(start)) < 12f) continue;
            if (NearStation(spot, spacing)) continue;
            if (ChestSpawner.Instance?.Nearest(spot, StationChestClearance) is not null) continue;
            if (!Level(space, spot, station.FootprintRadius)) continue;

            _map!.AddChild(station);
            station.GlobalPosition = spot;
            Vector3 to = start - spot; to.Y = 0f;
            station.Rotation = new Vector3(0f, to.LengthSquared() > 0.01f ? MathF.Atan2(to.X, to.Z) : 0f, 0f);
            _stations.Add(station);
            StationsPlaced++;
            GD.Print($"[stations] placed {station.GetType().Name} at=({spot.X:0.0},{spot.Y:0.0},{spot.Z:0.0}) phase={_phase}");
            return;
        }
        station.Free();
        GD.Print("[stations] no valid spot found; station dropped");
    }

    private void PlacePots(PhysicsDirectSpaceState3D space, Vector3 start)
    {
        if (_wantPots <= 0 || _map is null) return;
        var spots = new List<Vector3>(_wantPots);

        for (int cluster = 0; cluster < _wantPots * 2 && spots.Count < _wantPots; cluster++)
        {
            float d = MathF.Sqrt(Rng.Float()) * (_radius - 4f);
            Vector3 hub = _centre + Rng.OnUnitCircleXZ() * d;
            int size = Rng.RangeInt(ClusterMin, ClusterMax + 1);
            for (int k = 0; k < size && spots.Count < _wantPots; k++)
            {
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    Vector3 candidate = hub + Rng.OnUnitCircleXZ() * Rng.Range(0f, 2.6f);
                    if (!Validate(space, candidate, start, 0.45f, 1.2f, 0.8f, MaxPerchHeight, out Vector3 spot)) continue;
                    if (Flat(spot).DistanceTo(Flat(start)) < 6f) continue;
                    if (NearStation(spot, 2.8f)) continue;
                    if (ChestSpawner.Instance?.Nearest(spot, 2.2f) is not null) continue;
                    bool crowded = false;
                    foreach (Vector3 other in spots)
                        if (Flat(other).DistanceSquaredTo(Flat(spot)) < PotSpacing * PotSpacing) { crowded = true; break; }
                    if (crowded) continue;
                    spots.Add(spot);
                    break;
                }
            }
        }

        _pots = new Breakables();
        _map.AddChild(_pots);
        _pots.Build(spots, new Rng(Rng.Seed ^ 0xB0BB1E5UL ^ (ulong)_phase), _phase, _radius + 8f);
        PotsPlaced += spots.Count;
        GD.Print($"[stations] placed {spots.Count}/{_wantPots} breakables phase={_phase}");
    }

    private bool Validate(PhysicsDirectSpaceState3D space, Vector3 candidate, Vector3 start, float footprint,
                          float headroom, float minNormalY, float maxHeight, out Vector3 spot)
    {
        spot = default;
        var flat = new Vector2(candidate.X - _centre.X, candidate.Z - _centre.Z);
        if (flat.Length() > _radius - 3f) return false;

        if (Ground(space, candidate, out Vector3 normal) is not { } g) return false;
        if (normal.Y < minNormalY) return false;
        if (g.Y - _centre.Y > maxHeight || g.Y - _centre.Y < -2f) return false;
        if (Ray(space, g + Vector3.Up * 0.1f, g + Vector3.Up * headroom, out _, out _)) return false;
        if (OverlapsObstacle(g, footprint)) return false;
        spot = g;
        return true;
    }

    /// <summary>The footprint corners are within 0.3 m of the centre's height:
    /// a stall half on a slope looks broken even when the centre is flat.</summary>
    private bool Level(PhysicsDirectSpaceState3D space, Vector3 g, float r)
    {
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Tau * i / 4f + 0.4f;
            Vector3 at = g + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * r;
            if (Ground(space, at, out _) is not { } h || MathF.Abs(h.Y - g.Y) > 0.3f) return false;
        }
        return true;
    }

    private bool NearStation(Vector3 p, float spacing)
    {
        foreach (Station s in _stations)
            if (IsInstanceValid(s) && Flat(s.GlobalPosition).DistanceSquaredTo(Flat(p)) < spacing * spacing) return true;
        return false;
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    private Vector3? Ground(PhysicsDirectSpaceState3D space, Vector3 at, out Vector3 normal)
    {
        Vector3 from = new(at.X, _centre.Y + MaxPerchHeight + 25f, at.Z);
        Vector3 to = new(at.X, _centre.Y - 10f, at.Z);
        return Ray(space, from, to, out Vector3 point, out normal) ? point : null;
    }

    private static bool Ray(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal)
    {
        var q = PhysicsRayQueryParameters3D.Create(from, to, collisionMask: 1u);
        if (Player.Instance is { } p && IsInstanceValid(p))
            q.Exclude = new Godot.Collections.Array<Rid> { p.GetRid() };
        var res = space.IntersectRay(q);
        if (res.Count == 0) { point = default; normal = Vector3.Up; return false; }
        point = (Vector3)res["position"];
        normal = (Vector3)res["normal"];
        return true;
    }

    /// <summary>Visible prop boxes, as the chest spawner collects them: mesh-only
    /// scenery has no collider for a ray to find.</summary>
    private void CollectObstacles(Node root)
    {
        _obstacles.Clear();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n is Chest or Station or Breakables) continue;
            if (n is MeshInstance3D mi && mi.Mesh is not null)
            {
                Aabb box = mi.GlobalTransform * mi.GetAabb();
                bool huge = box.Size.X > 14f || box.Size.Z > 14f;
                bool flat = box.Size.Y < 0.15f;
                if (!huge && !flat) _obstacles.Add(box);
            }
            foreach (Node child in n.GetChildren()) stack.Push(child);
        }
    }

    private bool OverlapsObstacle(Vector3 g, float r)
    {
        var footprint = new Aabb(g - new Vector3(r + 0.3f, -0.05f, r + 0.3f), new Vector3((r + 0.3f) * 2f, 1.4f, (r + 0.3f) * 2f));
        foreach (Aabb box in _obstacles)
        {
            if (g.Y >= box.End.Y - 0.05f) continue;
            if (footprint.Intersects(box)) return true;
        }
        return false;
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

    // ------------------------------------------------------------ autopilot

    private float _nextBotAction;
    private float _botClock;

    /// <summary>True when the last <see cref="AutopilotSteer"/> found a wanted
    /// station in reach: the bot is using it and should stay put.</summary>
    public bool BotBusy { get; private set; }

    /// <summary>
    /// The autopilot's errand: walk to the merchant when it can afford something
    /// there (or has not looked yet and holds enough for the cheapest stock) and
    /// buy the dearest thing it can; use the microwave on its rarest non-maxed
    /// item when it can pay. Buys go straight through <see cref="TryBuy"/>
    /// rather than the panel, which pauses the tree and would stall an
    /// unattended run, exactly as level-ups skip their panel in auto runs.
    /// </summary>
    public Vector3 AutopilotSteer(Vector3 me, float nearestEnemy, float dt)
    {
        _botClock += dt;
        BotBusy = false;
        Inventory? inv = Inventory.Current;
        if (inv is null || _stations.Count == 0) return Vector3.Zero;

        Station? target = null;
        float bestSq = 50f * 50f;
        foreach (Station s in _stations)
        {
            if (!IsInstanceValid(s) || !s.Usable || !WantsToUse(s, inv)) continue;
            if (s.InReach(me))
            {
                BotBusy = true;
                if (_botClock >= _nextBotAction)
                {
                    _nextBotAction = _botClock + 0.6f;
                    BotUse(s, inv);
                }
                continue;
            }
            float sq = Flat(s.GlobalPosition).DistanceSquaredTo(Flat(me));
            if (sq < bestSq) { bestSq = sq; target = s; }
        }

        if (target is null || nearestEnemy < 6f) return Vector3.Zero;
        Vector3 d = target.GlobalPosition - me; d.Y = 0f;
        float dist = d.Length();
        float safety = Mathf.Clamp((nearestEnemy - 6f) / 4f, 0f, 1f);
        return dist < 0.3f ? Vector3.Zero : d / dist * 2.6f * safety;
    }

    private bool WantsToUse(Station s, Inventory inv)
    {
        switch (s)
        {
            case Merchant m when m.Offers is null:
                return inv.Gold >= MerchantPrice(MerchantFloor);
            case Merchant m:
                for (int i = 0; i < m.Offers!.Length; i++)
                    if (m.Offers[i]?.Item is { } item && !inv.IsMaxed(item) && inv.Gold >= m.Prices[i]) return true;
                return false;
            case Microwave { Used: false }:
                return BestCook(inv) is not null;
            case Campfire fire:
                // Once per room, and again whenever hurt.
                return fire.Rests == 0 || Player.Instance is { } p && p.Health.Current < p.Health.Max - 0.5f;
            default:
                return false;
        }
    }

    private ItemDef? BestCook(Inventory inv)
    {
        ItemDef? best = null;
        foreach (ItemStack st in inv.Items)
        {
            if (st.Count <= 0 || inv.IsMaxed(st.Def) || inv.Gold < MicrowavePrice(st.Rarity)) continue;
            if (best is null || st.Rarity > best.Rarity) best = st.Def;
        }
        return best;
    }

    private void BotUse(Station s, Inventory inv)
    {
        if (s is Merchant m)
        {
            EnsureStocked(m);
            int pick = -1;
            for (int i = 0; i < m.Offers!.Length; i++)
                if (m.Offers[i]?.Item is { } item && !inv.IsMaxed(item) && inv.Gold >= m.Prices[i]
                    && (pick < 0 || m.Prices[i] > m.Prices[pick]))
                    pick = i;
            if (pick >= 0) TryBuy(m, pick);
        }
        else if (s is Microwave mw && BestCook(inv) is { } def)
        {
            TryCook(mw, def);
        }
        else if (s is Campfire fire)
        {
            Rest(fire);
        }
    }

    // ---------------------------------------------------------------- audit

    /// <summary>Data checks for <see cref="ContentAudit"/>: per-map counts in
    /// their intended ranges and prices that rise with rarity and phase.</summary>
    public static void Audit(Action<bool, string> check)
    {
        foreach (StageDef s in Data.Stages.Run)
        {
            check(s.Breakables is >= 25 and <= 40, $"stage {s.Id} breakables {s.Breakables} outside 25-40");
            // The merchant lives in the rest room between maps now (Game/RestRoom).
            check(s.Merchants == 0, $"stage {s.Id} places {s.Merchants} merchants; the shop belongs to the rest room");
            check(s.Microwaves >= 1, $"stage {s.Id} has no microwave");
        }
        check(MerchantBase.Length == 5 && MicrowaveBase.Length == 5, "station price tables cover every rarity");
        for (int r = 1; r < MerchantBase.Length; r++)
        {
            check(MerchantBase[r] > MerchantBase[r - 1], $"merchant price not rising at rarity {r}");
            check(MicrowaveBase[r] > MicrowaveBase[r - 1], $"microwave price not rising at rarity {r}");
        }
        for (int r = 0; r < MerchantBase.Length; r++)
            check(MicrowaveBase[r] < MerchantBase[r], $"microwave dearer than merchant at rarity {r}");
    }
}
