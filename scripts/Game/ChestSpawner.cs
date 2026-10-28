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
/// Owns every chest in the run: where they stand, what they cost, what they pay,
/// and the key that opens them.
///
/// <b>Maps call it; it does not know about maps.</b> <see cref="Populate"/> takes
/// the node to parent chests under and a radius to scatter them in, and finds
/// ground with physics rays — so any map built from colliders works without a
/// list of spawn points. Chests are parented to the map, so a map that frees
/// itself frees its chests; <see cref="Clear"/> is for a map that is rebuilt in
/// place. The spawner itself lives for the whole run because the price ladder
/// does: it would be a strange economy where changing phase reset the price.
///
/// <b>The price climbs with every paid chest.</b> See <see cref="NextPrice"/>.
/// Gold piles up otherwise; an escalating price turns it into a decision —
/// open this one now, or save for the gilded one across the arena.
///
/// <b>Rewards come from the offer pool.</b> A chest rolls through
/// <see cref="OfferPool.RollChest"/> — the same catalogue, caps and luck curve as
/// a level-up, bent further by the chest's tier — so a new item is chest loot the
/// moment it is in the catalogue, and a maxed item can never come out of a box.
///
/// <b>Placement waits for physics.</b> A map built this frame has bodies the
/// physics server has not seen yet, so a ray cast now would fall through the
/// floor. Requests queue and are resolved on the next physics frame.
/// </summary>
public sealed partial class ChestSpawner : Node
{
    // -------------------------------------------------------------- tuning

    /// <summary>Chests a fresh map starts with.</summary>
    public const int DefaultCount = 5;

    /// <summary>Paid chests standing at once. Top-ups stop here, so an ignored
    /// arena does not silt up with boxes.</summary>
    public const int MaxWorldChests = 7;

    /// <summary>Unopened free drops at once. Past this a lucky elite streak drops
    /// nothing rather than carpeting the floor; the pity counter keeps its
    /// progress, so the drop is postponed, not lost.</summary>
    public const int MaxFreeChests = 4;

    /// <summary>Seconds between top-up attempts. Long enough that chests stay a
    /// thing you notice, not a thing you trip over.</summary>
    public const float TopUpInterval = 40f;

    /// <summary>Reach, horizontal and vertical. Generous horizontally so walking
    /// past at speed still offers the prompt; tight vertically so a chest on a
    /// ledge is not opened from the floor beneath it.</summary>
    public const float InteractRadius = 2.4f;
    public const float InteractHeight = 1.6f;

    /// <summary>Closest two placed chests may stand. Spreading them out is what
    /// makes the arena worth crossing.</summary>
    public const float MinSpacing = 9f;

    /// <summary>Highest ground a chest may be placed on, above the map's centre.
    /// The jump apex is 6 m and the air jump adds more, so 4.5 m is reachable
    /// with room for a mistimed press.</summary>
    public const float MaxPerchHeight = 4.5f;

    /// <summary>Chance a placed chest is gilded.</summary>
    public const float GildedChance = 0.15f;

    /// <summary>Price ladder: cost(n) = Base + Step·n + Curve·n², n = paid chests
    /// opened so far. 25, 45, 75, 115, 165, 225… Quadratic rather than
    /// exponential: gold income also grows through a run, and an exponential
    /// price outruns it by minute eight and the chests stop mattering.</summary>
    public const int BaseCost = 25;
    public const int CostStep = 15;
    public const int CostCurve = 5;

    public const float GildedCostMultiplier = 2.5f;

    /// <summary>Luck added to the player's for the roll, and the lowest rarity a
    /// tier can pay. Gilded: at least rare, and the curve bent hard upward.</summary>
    public const float StandardLuckBonus = 0.5f;
    public const float GildedLuckBonus = 2f;

    /// <summary>Elite drop odds. Each miss adds the step, a drop resets it — so a
    /// dry streak ends within a handful of elites instead of being possible
    /// forever. Bosses always drop, and drop gilded.</summary>
    public const float EliteDropChance = 0.12f;
    public const float ElitePityStep = 0.06f;

    /// <summary>
    /// <see cref="EnemyKilled"/> folds bosses into <c>WasElite</c>, so a boss is
    /// recognised by its payout: every boss in the catalogue is worth 260+ gold,
    /// every elite under 50. A <c>WasBoss</c> flag on the event would be cleaner;
    /// this keeps the chest system from touching the enemy code.
    /// </summary>
    public const int BossGoldThreshold = 150;

    private const string InteractAction = "interact";

    // --------------------------------------------------------------- state

    /// <summary>The run's spawner, or null between runs.</summary>
    public static ChestSpawner? Instance { get; private set; }

    /// <summary>Placement and drop rolls. A stream of its own, forked from the
    /// run seed: chests are reproducible per seed, and adding one does not
    /// shift which enemies or cards the run's main stream produces.</summary>
    public Rng Rng { get; private set; }

    private readonly List<Chest> _chests = new();
    private readonly Queue<Request> _pending = new();
    private readonly List<Aabb> _obstacles = new();

    private Node3D? _map;
    private Vector3 _centre;
    private float _radius;

    private ChestPrompt _prompt = null!;
    private string _keyName = "E";
    private bool _hasAction;
    private bool _interactRequested;
    private Chest? _focus;

    private float _clock;
    private float _nextTopUp = TopUpInterval;
    private float _pity;
    private int _paidOpened;

    // Run totals, for the summary line and the autopilot's verification.
    public int Opened { get; private set; }
    public int OpenedFree { get; private set; }
    public int GoldSpent { get; private set; }
    public int Denied { get; private set; }
    public int Spawned { get; private set; }
    public int Dropped { get; private set; }

    /// <summary>Unopened chests in the world right now.</summary>
    public int Standing
    {
        get
        {
            int n = 0;
            foreach (Chest c in _chests) if (Usable(c)) n++;
            return n;
        }
    }

    /// <summary>Fields appended to Main's SUMMARY line.</summary>
    public string SummaryFields =>
        $"chests={Opened} chestfree={OpenedFree} chestspent={GoldSpent} " +
        $"chestdenied={Denied} chestspawned={Spawned} chestdrops={Dropped}";

    private readonly struct Request
    {
        public readonly ChestTier Tier;
        public readonly bool Free;
        public readonly Vector3? At;
        public readonly bool NearStart;

        public Request(ChestTier tier, bool free, Vector3? at = null, bool nearStart = false)
        {
            Tier = tier; Free = free; At = at; NearStart = nearStart;
        }
    }

    public ChestSpawner() => Rng = new Rng(0xC4E57UL);

    public ChestSpawner(Rng rng) => Rng = rng;

    /// <summary>
    /// Creates the run's spawner under <paramref name="parent"/>. Call once per
    /// run, then <see cref="Populate"/> per map.
    /// </summary>
    public static ChestSpawner Attach(Node parent, ulong runSeed)
    {
        // An odd constant mixed in so seed 1's chests do not share a stream with
        // anything else seeded from 1.
        var spawner = new ChestSpawner(new Rng(runSeed ^ 0x9E3779B97F4A7C15UL));
        parent.AddChild(spawner);
        return spawner;
    }

    public override void _Ready()
    {
        Name = "Chests";
        Instance = this;

        _prompt = new ChestPrompt();
        AddChild(_prompt);

        _hasAction = InputMap.HasAction(InteractAction);
        _keyName = KeyName();

        EventBus.Subscribe<EnemyKilled>(OnEnemyKilled);

        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--check-chests="))
                _ = CheckChests(arg["--check-chests=".Length..]);
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<EnemyKilled>(OnEnemyKilled);
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------- public API

    /// <summary>Populate with the spawner's own stream. See the full overload.</summary>
    public ChestSpawner Populate(Node3D map, float arenaRadius, int count = DefaultCount)
        => Populate(map, Rng, arenaRadius, count);

    /// <summary>
    /// Replaces the chests on the current map with <paramref name="count"/> new
    /// ones scattered under <paramref name="map"/>, and makes it the area that
    /// top-ups and drops use from now on. Call whenever a map is (re)built.
    /// </summary>
    /// <param name="map">Parent for the chests, and the subtree scanned for
    /// obstacles to keep them out of.</param>
    /// <param name="rng">Placement stream; becomes the spawner's stream.</param>
    /// <param name="arenaRadius">Scatter radius around <paramref name="centre"/>.</param>
    /// <param name="count">Chests to place now. The first goes near the start.</param>
    /// <param name="centre">Middle of the playable area, in world space.</param>
    public ChestSpawner Populate(Node3D map, Rng rng, float arenaRadius, int count = DefaultCount,
                                 Vector3 centre = default)
    {
        Clear();
        Rng = rng ?? Rng;
        _map = map;
        _centre = centre;
        _radius = MathF.Max(6f, arenaRadius);
        _nextTopUp = _clock + TopUpInterval;
        // Co-op client: the host places every chest and sends it.
        if (Net.Coop.IsClient) return this;

        CollectObstacles(map);

        for (int i = 0; i < count; i++)
            _pending.Enqueue(new Request(RollTier(), free: false, nearStart: i == 0));
        return this;
    }

    /// <summary>Removes every chest, opened or not, and forgets the map. The
    /// price ladder and run totals are kept: they belong to the run.</summary>
    public void Clear()
    {
        foreach (Chest c in _chests)
            if (IsInstanceValid(c) && !c.IsQueuedForDeletion()) c.QueueFree();
        _chests.Clear();
        _pending.Clear();
        _obstacles.Clear();
        _map = null;
        _focus = null;
        _prompt?.HideNow();
    }

    /// <summary>
    /// What the next paid chest of <paramref name="tier"/> costs. Quadratic in
    /// chests already opened; see <see cref="BaseCost"/>.
    /// </summary>
    public int NextPrice(ChestTier tier)
    {
        int n = _paidOpened;
        float cost = BaseCost + CostStep * n + CostCurve * n * n;
        if (tier == ChestTier.Gilded) cost *= GildedCostMultiplier;
        return (int)MathF.Round(cost);
    }

    public int PriceFor(Chest chest) => chest.IsFree ? 0 : NextPrice(chest.Tier);

    public bool CanAfford(Chest chest)
        => chest.IsFree || (Inventory.Current?.Gold ?? 0) >= PriceFor(chest);

    /// <summary>Press the interact key on the next frame, for the autopilot.
    /// Goes through the same path as the real key.</summary>
    public void RequestInteract() => _interactRequested = true;

    /// <summary>Nearest unopened chest within <paramref name="maxDistance"/>
    /// (horizontal), optionally filtered. For the autopilot.</summary>
    public Chest? Nearest(Vector3 from, float maxDistance, Func<Chest, bool>? filter = null)
    {
        Chest? best = null;
        float bestSq = maxDistance * maxDistance;
        foreach (Chest c in _chests)
        {
            if (!Usable(c) || (filter is not null && !filter(c))) continue;
            Vector3 d = c.GlobalPosition - from;
            d.Y = 0f;
            float sq = d.LengthSquared();
            if (sq >= bestSq) continue;
            best = c;
            bestSq = sq;
        }
        return best;
    }

    public static bool InReach(Chest c, Vector3 at)
    {
        Vector3 d = c.GlobalPosition - at;
        float dy = MathF.Abs(d.Y);
        d.Y = 0f;
        return d.LengthSquared() <= InteractRadius * InteractRadius && dy <= InteractHeight;
    }

    /// <summary>
    /// Pays for and opens <paramref name="chest"/>, or refuses. The reward is
    /// granted here, before the animation starts, so nothing about the visuals
    /// (a freed node, a paused tree) can lose it.
    /// </summary>
    public bool TryOpen(Chest chest)
    {
        if (!Usable(chest)) return false;
        // Co-op: the host decides who gets a chest first (ChestSpawner.Coop.cs).
        if (chest.NetId > 0 && Net.Coop.InRun && !_netApproved) return AskHost(chest);
        Inventory? inv = Inventory.Current;
        Player? player = Player.Instance;
        if (inv is null || player is null) return false;

        int cost = PriceFor(chest);
        if (cost > 0 && !inv.SpendGold(cost))
        {
            Denied++;
            chest.Deny();
            _prompt.Deny();
            EventBus.Publish(new ChestDenied(chest.GlobalPosition, cost, inv.Gold));
            GD.Print($"[chest] denied cost={cost} gold={inv.Gold}");
            return false;
        }

        if (cost > 0) { _paidOpened++; GoldSpent += cost; }
        else OpenedFree++;
        Opened++;

        bool gilded = chest.Tier == ChestTier.Gilded;

        // A held hero signature weapon can be honed by a chest: 15% of standard
        // chests, 35% of gilded ones, while it still has levels to gain.
        if (TrySignatureUpgrade(chest, gilded, cost, player)) return true;
        // Sometimes a perk level instead of an item (Combat/Weapons/PerkProcs.Chests.cs).
        Offer reward = OfferPool.Current is { } pool
            ? Combat.Weapons.PerkProcs.RollChestPerk(pool, gilded, Rng) ?? pool.RollChest(inv, player.Stats,
                             gilded ? GildedLuckBonus : StandardLuckBonus,
                             gilded ? Rarity.Rare : Rarity.Common, Rng)
            : null!;

        string name;
        Rarity rarity;
        Color colour;
        if (reward is null)
        {
            // No pool means a stripped-down test scene; pay something rather
            // than eat the gold.
            inv.GrantGold(Math.Max(10, cost));
            name = "Gold cache"; rarity = Rarity.Common; colour = Catalog.RarityColor(rarity);
        }
        else
        {
            if (reward.Item is not null) inv.Add(reward.Item);
            else if (reward.Tome is not null) Progression.TomeBook.Current?.Add(reward.Tome);
            else reward.Apply(player, inv);
            name = reward.Title;
            rarity = reward.Rarity;
            colour = reward.IsBonus ? reward.Tint : Catalog.RarityColor(rarity);
        }

        chest.Open(reward is null || reward.IsBonus ? "SUPPLY" : Catalog.RarityName(rarity), colour,
                   reward is null ? UI.Icons.ForSupply(0f, 10) : UI.Icons.For(reward));
        EventBus.Publish(new PopupRequested(chest.Top + Vector3.Up * 0.9f, name, colour, 1.8f));
        if (cost > 0)
            EventBus.Publish(new PopupRequested(chest.Top, $"-{cost} gold", new Color(1f, 0.78f, 0.25f)));
        EventBus.Publish(new ChestOpened(chest.GlobalPosition, chest.Tier, chest.IsFree, cost, name, rarity));
        GD.Print($"[chest] opened tier={chest.Tier} free={chest.IsFree} cost={cost} reward=\"{name}\" " +
                 $"rarity={rarity} t={_clock:0}s next={NextPrice(ChestTier.Standard)}");
        return true;
    }

    private bool TrySignatureUpgrade(Chest chest, bool gilded, int cost, Player player)
    {
        if (Main.Instance?.Weapons is not { } weapons) return false;
        Combat.WeaponRuntime? sig = null;
        foreach (Combat.WeaponRuntime w in weapons.Weapons)
            if (Catalog.SignatureIds.Contains(w.Def.Id) && weapons.CanAdd(w.Def)) { sig = w; break; }
        if (sig is null || !Rng.Chance(gilded ? 0.35f : 0.15f)) return false;

        float luck = player.Stats.Get(Stats.Stat.Luck) + (gilded ? GildedLuckBonus : StandardLuckBonus);
        Combat.WeaponUpgrade up = Combat.WeaponUpgrades.Roll(sig.Def, Rng, luck);
        weapons.Add(sig.Def, up);
        string name = Meta.Loc.F("{0} → Lv {1}", sig.Def.DisplayName, sig.Level);
        Color colour = Catalog.RarityColor(up.Rarity);
        chest.Open(Catalog.RarityName(up.Rarity), colour, UI.Icons.For(sig.Def));
        EventBus.Publish(new PopupRequested(chest.Top + Vector3.Up * 0.9f, name, colour, 1.8f));
        if (cost > 0)
            EventBus.Publish(new PopupRequested(chest.Top, $"-{cost} gold", new Color(1f, 0.78f, 0.25f)));
        EventBus.Publish(new ChestOpened(chest.GlobalPosition, chest.Tier, chest.IsFree, cost, name, up.Rarity));
        GD.Print($"[chest] opened tier={chest.Tier} cost={cost} signature upgrade {sig.Def.Id} lv{sig.Level} rarity={up.Rarity}");
        return true;
    }

}
