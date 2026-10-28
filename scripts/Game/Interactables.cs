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
        $"microwavespent={MicrowaveSpent} pots={PotsBroken}/{PotsPlaced} stations={StationsPlaced} rests={Rests} " +
        $"totems={TotemUses}/{TotemsPlaced}";

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
        CancelPlacement();
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
        _wantTotems = TotemCountFor(stage);   // Interactables.Totems.cs
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
        CancelPlacement();
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

}
