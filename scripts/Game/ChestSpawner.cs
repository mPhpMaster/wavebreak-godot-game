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
        Offer reward = OfferPool.Current is { } pool
            ? pool.RollChest(inv, player.Stats,
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

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("ChestSpawner.proc");
        float dt = (float)delta;
        _clock += dt;
        _chests.RemoveAll(c => !IsInstanceValid(c) || c.IsQueuedForDeletion());

        if (_map is not null && !IsInstanceValid(_map)) Clear();
        TopUp();

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

        int gold = Inventory.Current?.Gold ?? 0;
        _prompt.ShowFor(_focus.Top, _keyName, PriceFor(_focus), _focus.IsFree, gold);
        if (pressed) TryOpen(_focus);
    }

    /// <summary>Unhandled, so an open panel that wants the key gets it first.
    /// Paused with the tree: no chests behind the level-up screen.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!_hasAction || e is InputEventKey { Echo: true }) return;
        if (!e.IsActionPressed(InteractAction)) return;
        _interactRequested = true;
        if (_focus is not null) GetViewport().SetInputAsHandled();
    }

    public override void _PhysicsProcess(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("ChestSpawner.phys");
        if (_pending.Count == 0) return;
        if (_map is null || !IsInstanceValid(_map) || !_map.IsInsideTree()) { _pending.Clear(); return; }

        PhysicsDirectSpaceState3D? space = _map.GetWorld3D()?.DirectSpaceState;
        if (space is null) return;

        // A few per frame: a fresh map asks for five at once and each costs up
        // to a few dozen rays, which is nothing, but there is no reason to spike.
        for (int i = 0; i < 3 && _pending.Count > 0; i++)
        {
            Request r = _pending.Dequeue();
            Place(space, r);
        }
    }

    // ------------------------------------------------------------ placement

    private ChestTier RollTier() => Rng.Chance(GildedChance) ? ChestTier.Gilded : ChestTier.Standard;

    private void TopUp()
    {
        if (_clock < _nextTopUp) return;
        _nextTopUp = _clock + TopUpInterval;
        if (_map is null) return;

        int paid = 0;
        foreach (Chest c in _chests) if (Usable(c) && !c.IsFree) paid++;
        if (paid + _pending.Count < MaxWorldChests)
            _pending.Enqueue(new Request(RollTier(), free: false));
    }

    private void OnEnemyKilled(EnemyKilled e)
    {
        if (!e.WasElite || _map is null) return;

        bool boss = e.GoldValue >= BossGoldThreshold;
        if (!boss)
        {
            if (!Rng.Chance(EliteDropChance + _pity)) { _pity += ElitePityStep; return; }
        }

        int free = 0;
        foreach (Chest c in _chests) if (Usable(c) && c.IsFree) free++;
        if (free >= MaxFreeChests) return;

        _pity = 0f;
        Dropped++;
        _pending.Enqueue(new Request(boss ? ChestTier.Gilded : ChestTier.Standard, free: true, at: e.Position));
    }

    private void Place(PhysicsDirectSpaceState3D space, Request r)
    {
        Vector3 playerPos = Player.Instance is { } p && IsInstanceValid(p) ? p.GlobalPosition : _centre;

        if (r.At is { } at)
        {
            // A drop lands where the enemy died; only the ground height and the
            // map edge are checked. Moving it somewhere "valid" would break the
            // link between the kill and the reward, which is the whole point.
            Vector3 flat = ClampToArea(at);
            Vector3 ground = Ground(space, flat, out _) ?? new Vector3(flat.X, _centre.Y, flat.Z);
            Spawn(r, ground, playerPos);
            return;
        }

        for (int attempt = 0; attempt < 48; attempt++)
        {
            Vector3 candidate;
            if (r.NearStart)
            {
                // The first chest is a tutorial: close enough to be seen at the
                // start, far enough that reaching it is a choice.
                Vector3 dir = Rng.OnUnitCircleXZ();
                candidate = playerPos + dir * Rng.Range(7f, 11f);
            }
            else
            {
                float d = MathF.Sqrt(Rng.Float()) * (_radius - 5f);
                candidate = _centre + Rng.OnUnitCircleXZ() * d;
            }

            // Relaxed after many failures so a crowded map still gets its chests.
            float spacing = attempt < 32 ? MinSpacing : MinSpacing * 0.5f;
            if (TryValidate(space, candidate, playerPos, spacing, out Vector3 spot))
            {
                Spawn(r, spot, playerPos);
                return;
            }
        }

        GD.Print("[chest] no valid spot found; request dropped");
    }

    private bool TryValidate(PhysicsDirectSpaceState3D space, Vector3 candidate, Vector3 playerPos,
                             float spacing, out Vector3 spot)
    {
        spot = default;
        Vector3 flat = new(candidate.X, _centre.Y, candidate.Z);
        if (new Vector2(flat.X - _centre.X, flat.Z - _centre.Z).Length() > _radius - 3f) return false;

        Vector3? hit = Ground(space, flat, out Vector3 normal);
        if (hit is not { } g) return false;
        if (normal.Y < 0.8f) return false;                          // slope or wall face
        if (g.Y - _centre.Y > MaxPerchHeight) return false;         // out of jump reach
        if (g.Y - _centre.Y < -2f) return false;                    // pit

        // Headroom: something solid just above means it is under an overhang or
        // wedged in geometry.
        if (Ray(space, g + Vector3.Up * 0.1f, g + Vector3.Up * 1.6f, out _, out _)) return false;

        Vector3 dp = g - playerPos; dp.Y = 0f;
        if (dp.Length() < 5f) return false;

        foreach (Chest c in _chests)
        {
            if (!Usable(c)) continue;
            Vector3 d = c.GlobalPosition - g; d.Y = 0f;
            if (d.Length() < spacing) return false;
        }

        if (OverlapsObstacle(g)) return false;
        spot = g;
        return true;
    }

    private void Spawn(Request r, Vector3 at, Vector3 facePoint)
    {
        if (_map is null) return;
        var chest = new Chest(r.Tier, r.Free) { SpawnedAt = _clock };
        _map.AddChild(chest);
        chest.GlobalPosition = at;

        // Face the lock toward the player when there is one to face, so the
        // first look at a chest is at its front.
        Vector3 to = facePoint - at; to.Y = 0f;
        chest.Rotation = new Vector3(0f, to.LengthSquared() > 0.01f ? MathF.Atan2(to.X, to.Z) : Rng.Range(0f, Mathf.Tau), 0f);

        _chests.Add(chest);
        Spawned++;
        EventBus.Publish(new ChestSpawned(at, r.Tier, r.Free));
        if (r.Free)
            EventBus.Publish(new PopupRequested(at + Vector3.Up * 2.4f, "FREE CHEST", new Color(0.55f, 1f, 0.65f), 1.4f));
        GD.Print($"[chest] spawned tier={r.Tier} free={r.Free} at=({at.X:0.0},{at.Y:0.0},{at.Z:0.0}) t={_clock:0}s");
    }

    private Vector3 ClampToArea(Vector3 p)
    {
        var flat = new Vector2(p.X - _centre.X, p.Z - _centre.Z);
        float max = _radius - 3f;
        if (flat.Length() > max) flat = flat.Normalized() * max;
        return new Vector3(_centre.X + flat.X, p.Y, _centre.Z + flat.Y);
    }

    /// <summary>Top of whatever solid world geometry is under (x, z).</summary>
    private Vector3? Ground(PhysicsDirectSpaceState3D space, Vector3 at, out Vector3 normal)
    {
        Vector3 from = new(at.X, _centre.Y + MaxPerchHeight + 25f, at.Z);
        Vector3 to = new(at.X, _centre.Y - 10f, at.Z);
        return Ray(space, from, to, out Vector3 point, out normal) ? point : null;
    }

    private bool Ray(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal)
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

    /// <summary>
    /// Records the boxes of the map's visible props. Some scenery is mesh only,
    /// with no collider for a ray to find, and a chest half inside a pillar
    /// looks broken whether or not anything can collide with it. Very large
    /// meshes (the floor, the boundary) and flat ones (floor markings) are
    /// skipped — they are the ground, not obstacles on it.
    /// </summary>
    private void CollectObstacles(Node root)
    {
        _obstacles.Clear();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n is Chest) continue;
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

    private bool OverlapsObstacle(Vector3 g)
    {
        Vector3 half = Chest.HalfExtents + new Vector3(0.5f, 0f, 0.5f);
        var footprint = new Aabb(g - new Vector3(half.X, 0f, half.Z) + Vector3.Up * 0.05f,
                                 new Vector3(half.X * 2f, 1.2f, half.Z * 2f));
        foreach (Aabb box in _obstacles)
        {
            // Standing on top of a prop (a crate with a collider) is a perch,
            // not an overlap.
            if (g.Y >= box.End.Y - 0.05f) continue;
            if (footprint.Intersects(box)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------- helpers

    private static bool Usable(Chest c) => IsInstanceValid(c) && !c.IsQueuedForDeletion() && !c.IsOpen;

    private Chest? Focus(Vector3 at)
    {
        Chest? best = null;
        float bestSq = float.MaxValue;
        foreach (Chest c in _chests)
        {
            if (!Usable(c) || !InReach(c, at)) continue;
            float sq = (c.GlobalPosition - at).LengthSquared();
            if (sq < bestSq) { best = c; bestSq = sq; }
        }
        return best;
    }

    /// <summary>The bound key's name, so the prompt follows a rebind.</summary>
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

    // --------------------------------------------------------- verification

    /// <summary>
    /// <c>--check-chests=&lt;dir&gt;</c>: drives one chest through every path with
    /// real key events — refused for gold, paid and opened, a boss drop opened
    /// free — saves a frame at each step into &lt;dir&gt;, prints CHEST lines and
    /// quits. A chest that opens in a log but not from the keyboard is exactly
    /// the bug a log cannot show, so the key goes through Input like a player's.
    /// </summary>
    private async System.Threading.Tasks.Task CheckChests(string dir)
    {
        SceneTree tree = GetTree();
        bool ok = true;
        void Expect(string what, bool condition)
        {
            GD.Print($"CHEST {(condition ? "ok  " : "FAIL")} {what}");
            ok &= condition;
        }
        void Press()
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = false });
        }
        async System.Threading.Tasks.Task Wait(double seconds)
            => await tree.ToSignal(tree.CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        async System.Threading.Tasks.Task Shot(string name)
            => await Screenshot.Capture(tree, this, $"{dir}/{name}.png");

        ChestOpened? last = null;
        void OnOpened(ChestOpened e) => last = e;
        EventBus.Subscribe<ChestOpened>(OnOpened);

        DirAccess.MakeDirRecursiveAbsolute(dir);
        await Wait(1.2);

        Player? player = Player.Instance;
        Inventory? inv = Inventory.Current;
        Camera3D? cam = GetViewport().GetCamera3D();
        if (player is null || inv is null || cam is null || _map is null)
        {
            GD.Print("CHEST FAIL setup: no player, inventory, camera or map");
            tree.Quit(1);
            return;
        }

        Expect($"map populated ({Standing} standing, {Spawned} spawned)", Spawned >= DefaultCount);

        // In front of the player as the camera sees it, a little to the side so
        // the body does not hide the lock.
        Vector3 fwd = -cam.GlobalBasis.Z; fwd.Y = 0f; fwd = fwd.Normalized();
        Vector3 right = fwd.Cross(Vector3.Up);
        Vector3 me = player.GlobalPosition;
        var chest = new Chest(ChestTier.Standard, free: false) { SpawnedAt = _clock };
        _map.AddChild(chest);
        chest.GlobalPosition = new Vector3(me.X, _centre.Y, me.Z) + fwd * 2.1f + right * 0.7f;
        chest.Rotation = new Vector3(0f, MathF.Atan2(-fwd.X, -fwd.Z), 0f);
        _chests.Add(chest);
        await Wait(0.5);

        // Refused: an empty purse.
        if (inv.Gold > 0) inv.SpendGold(inv.Gold);
        int price = PriceFor(chest);
        Expect($"prompt shows for the chest in reach (focus={_focus == chest}, visible={_prompt.Visible})",
               _focus == chest && _prompt.Visible);
        Expect($"prompt names the price: \"{_prompt.Text}\"", _prompt.Text.Contains($"{price} gold"));
        await Shot("chest-prompt-poor");
        int deniedBefore = Denied;
        Press();
        await Wait(0.12);
        Expect("E with no gold is refused", Denied == deniedBefore + 1 && !chest.IsOpen && inv.Gold == 0);
        await Shot("chest-denied");
        await Wait(0.6);

        // Paid: give exactly enough.
        while (inv.Gold < price) inv.GrantGold(price - inv.Gold);
        await Wait(0.3);
        await Shot("chest-prompt");
        int goldBefore = inv.Gold;
        int stacksBefore = inv.TotalStacks;
        Press();
        await Wait(0.15);
        await Shot("chest-burst");
        Expect($"E with {goldBefore} gold opens it (open={chest.IsOpen})", chest.IsOpen);
        Expect($"price {price} was charged (gold {goldBefore} -> {inv.Gold})", inv.Gold == goldBefore - price);
        Expect($"reward granted: \"{last?.Reward}\" (items {stacksBefore} -> {inv.TotalStacks})",
               last is { Free: false } && (inv.TotalStacks == stacksBefore + 1 || last.Value.RewardRarity == Rarity.Common));
        Expect($"price climbs: {price} -> {NextPrice(ChestTier.Standard)}", NextPrice(ChestTier.Standard) > price);
        await Wait(0.45);
        await Shot("chest-reward");

        // Free: a boss-sized kill through the real event, then E.
        await Wait(1.0);
        int dropsBefore = Dropped;
        Vector3 dropAt = new Vector3(me.X, _centre.Y + 0.35f, me.Z) + fwd * 1.6f - right * 1.3f;
        EventBus.Publish(new EnemyKilled(dropAt, 0, BossGoldThreshold + 100, true));
        await Wait(0.3);
        Chest? drop = Nearest(dropAt, 2f, c => c.IsFree);
        Expect($"boss kill drops a free chest (drops {dropsBefore} -> {Dropped}, tier={drop?.Tier})",
               drop is { Tier: ChestTier.Gilded });
        if (drop is not null)
        {
            await Shot("chest-free");
            int spentBefore = GoldSpent;
            Press();
            await Wait(0.15);
            Expect($"free chest opens with E at no cost (reward \"{last?.Reward}\")",
                   drop.IsOpen && last is { Free: true, Cost: 0 } && GoldSpent == spentBefore);
            await Wait(0.4);
            await Shot("chest-free-open");
        }

        EventBus.Unsubscribe<ChestOpened>(OnOpened);
        GD.Print($"CHEST {SummaryFields}");
        GD.Print(ok ? "CHEST PASS" : "CHEST FAIL");
        tree.Quit(ok ? 0 : 1);
    }
}
