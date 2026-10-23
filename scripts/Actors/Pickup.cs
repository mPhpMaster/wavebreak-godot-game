using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

/// <summary>What a dropped pickup pays out. The kind decides its colour, its
/// silhouette and what collecting it does — nothing else varies.</summary>
public enum PickupKind
{
    /// <summary>Experience orb. Value is raw experience.</summary>
    Xp,

    /// <summary>Coin. Value is raw gold.</summary>
    Gold,

    /// <summary>Med-pack. Value is a fraction of maximum health, not points, so a
    /// pack found at minute one is still worth taking at minute fourteen.</summary>
    Health,
}

/// <summary>
/// A dropped orb: pops out of a kill, settles, bobs, and then flies at the player
/// once they come within <see cref="Stat.PickupRange"/>.
///
/// Three details make this feel right rather than merely work.
///
/// <b>Attraction latches.</b> Once an orb has started chasing it never stops, even
/// if the player walks back out of range. Un-latching produces orbs that stutter
/// at the edge of the radius and a magnet stat that feels unreliable.
///
/// <b>Attraction accelerates.</b> Constant speed makes distant orbs crawl in;
/// ramping from a slow start to a fast finish reads as a magnet rather than as a
/// homing missile, and keeps the moment of collection punchy.
///
/// <b>Collection is by distance, not by collision.</b> Hundreds of orbs on the
/// floor as physics bodies is a physics-server bill for something that is exactly
/// one distance comparison. There are no <see cref="Area3D"/>s here at all.
///
/// Instances are pooled and driven from one static loop, like projectiles — see
/// <see cref="UpdateAll"/>. Nothing here runs its own <c>_Process</c>.
/// </summary>
public sealed partial class Pickup : Node3D, IPooled
{
    // -------------------------------------------------------------- tuning

    /// <summary>Distance at which an orb counts as touched. Generous on purpose:
    /// the moment of collection should never feel like it was missed.</summary>
    public const float CollectRadius = 0.75f;

    /// <summary>Speed an orb starts chasing at, and the ceiling it ramps to.</summary>
    private const float AttractStartSpeed = 3.5f;
    private const float AttractMaxSpeed = 30f;
    private const float AttractAcceleration = 62f;

    /// <summary>Downward acceleration during the spawn pop, in m/s².</summary>
    private const float Gravity = 16f;

    private const float BobHeight = 0.09f;
    private const float BobRate = 2.4f;
    private const float SpinRate = 1.9f;

    /// <summary>Resting height above the floor. High enough to read over the
    /// arena's grid lines, low enough to look dropped rather than hovering.</summary>
    private const float RestHeight = 0.42f;

    // -------------------------------------------------------------- palette
    //
    // Picked against the rest of the world rather than in isolation, because a
    // pickup is only readable relative to what surrounds it. The arena's palette
    // is already crowded: the player and four weapons are blue or cyan, three
    // enemies and three weapons are yellow through orange, and the commonest enemy
    // of all — the mote swarm — is green. The one hue nothing else uses is violet,
    // so that is what the orb the player sees hundreds of is painted.
    //
    // Gold keeps its gold despite the yellow skitterling, because a coin that is
    // not gold stops reading as money. It is separated on the other two axes
    // instead: much deeper saturation than the pale enemy, and a spinning disc
    // against a capsule. Med-packs take a saturated red that the muted brown-red
    // husk cannot be confused with, and are rare enough that the collision would
    // barely matter if it existed.
    //
    // One material per kind, shared by every instance: an orb is never recoloured
    // per-drop, so a per-instance material would be hundreds of identical shader
    // parameter sets for no visual difference.

    private static readonly Color XpColor = new(0.72f, 0.34f, 1.00f);
    private static readonly Color GoldColor = new(1.00f, 0.72f, 0.12f);
    private static readonly Color HealthColor = new(1.00f, 0.26f, 0.36f);

    private static readonly StandardMaterial3D XpMaterial = Glow(XpColor);
    private static readonly StandardMaterial3D GoldMaterial = Glow(GoldColor);
    private static readonly StandardMaterial3D HealthMaterial = Glow(HealthColor);

    // Distinct silhouettes, because colour alone is not enough on a busy floor:
    // orb, coin on edge, diamond.
    private static readonly SphereMesh XpMesh = new()
    {
        Radius = 0.19f,
        Height = 0.38f,
        RadialSegments = 10,
        Rings = 5,
    };

    private static readonly CylinderMesh GoldMesh = new()
    {
        TopRadius = 0.22f,
        BottomRadius = 0.22f,
        Height = 0.07f,
        RadialSegments = 12,
        Rings = 1,
    };

    private static readonly BoxMesh HealthMesh = new() { Size = new Vector3(0.3f, 0.3f, 0.3f) };

    private static StandardMaterial3D Glow(Color color) => new()
    {
        AlbedoColor = color,
        Roughness = 0.35f,
        Metallic = 0.1f,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = 1.9f,
    };

    // ---------------------------------------------------------------- statics

    private static Pool<Pickup>? _pool;
    private static readonly List<Pickup> Live = new(256);

    /// <summary>Orbs on the floor right now. For debug overlays and tests.</summary>
    public static int ActiveCount => Live.Count;

    /// <summary>
    /// Orbs on the floor right now, read-only. For the autopilot, which needs to
    /// know where the experience is. May briefly hold an orb that was released
    /// since the last <see cref="UpdateAll"/> — callers skip <see cref="IsPooled"/>.
    /// </summary>
    public static IReadOnlyList<Pickup> Active => Live;

    // ------------------------------------------------------------------ state

    private MeshInstance3D _mesh = null!;
    private PickupKind _kind;
    private float _value;
    private float _age;
    private float _phase;
    private float _restY;
    private float _speed;
    private Vector3 _velocity;
    private Vector3 _tilt;
    private bool _settled;
    private bool _attracted;

    /// <inheritdoc/>
    public bool IsPooled { get; set; } = true;

    /// <summary>What this orb is worth: experience, gold, or a fraction of maximum
    /// health depending on <see cref="Kind"/>.</summary>
    public float Value => _value;

    public PickupKind Kind => _kind;

    public Pickup()
    {
        // Driven by UpdateAll, never by the engine — see the class note.
        SetProcess(false);
        SetPhysicsProcess(false);
        Visible = false;

        _mesh = new MeshInstance3D
        {
            Name = "Visual",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_mesh);
    }

    // -------------------------------------------------------------- lifecycle

    /// <summary>
    /// Builds the pool. Safe to call again — it rebuilds against the new parent,
    /// which is what a scene reload needs.
    /// </summary>
    /// <param name="parent">Node the pooled orbs live under. Must not move: orbs
    /// are positioned in world space and a moving parent would drag them.</param>
    /// <param name="capacity">Hard ceiling on orbs in the world. Reaching it folds
    /// new drops into existing orbs rather than dropping them on the floor —
    /// see <see cref="Spawn"/>.</param>
    /// <param name="prewarm">Orbs built up front, so the first wave does not pay
    /// for instantiation inside one frame.</param>
    public static void Initialize(Node parent, int capacity = 400, int prewarm = 64)
    {
        Shutdown();
        _pool = new Pool<Pickup>(parent, static () => new Pickup(), capacity, prewarm);
    }

    /// <summary>Drops every orb and the pool with them. Call when a run ends.</summary>
    public static void Shutdown()
    {
        Live.Clear();
        _pool?.Clear();
        _pool = null;
    }

    /// <summary>
    /// Drops an orb.
    ///
    /// At capacity the value is folded into the live orb of the same kind that is
    /// furthest from the player instead of being discarded. That keeps the run's
    /// total payout exactly right no matter how far behind the player's collecting
    /// falls, and it self-corrects: the pool refills as soon as anything is picked
    /// up.
    /// </summary>
    /// <param name="kind">What it pays out.</param>
    /// <param name="position">Where the kill happened.</param>
    /// <param name="value">Experience, gold, or health fraction.</param>
    /// <param name="launch">Initial velocity for the pop. Zero drops it in place.</param>
    public static Pickup? Spawn(PickupKind kind, Vector3 position, float value, Vector3 launch = default)
    {
        if (value <= 0f) return null;

        Pickup? p = _pool?.Get();
        if (p is null) return Overflow(kind, value);

        p.Activate(kind, position, value, launch);
        Live.Add(p);
        if (Net.Coop.IsHost && !Personal) HostSpawned(p, launch);   // Pickup.Coop.cs
        return p;
    }

    private static Pickup? Overflow(PickupKind kind, float value)
    {
        Vector3 player = Player.Instance is { } p && IsInstanceValid(p) ? p.GlobalPosition : Vector3.Zero;

        Pickup? furthest = null;
        float best = -1f;
        for (int i = 0; i < Live.Count; i++)
        {
            Pickup candidate = Live[i];
            if (candidate._kind != kind || candidate._attracted) continue;

            float d = candidate.GlobalPosition.DistanceSquaredTo(player);
            if (d > best) { best = d; furthest = candidate; }
        }

        if (furthest is null) return null;

        furthest._value += value;
        furthest.Dress();
        return furthest;
    }

    private void Activate(PickupKind kind, Vector3 position, float value, Vector3 launch)
    {
        ResetNetState();   // Pickup.Coop.cs; nothing to reset in single-player
        _kind = kind;
        _value = value;
        _age = 0f;
        _speed = AttractStartSpeed;
        _settled = false;
        _attracted = false;
        _velocity = launch;

        // Orbs rest at a fixed height above the floor rather than at whatever
        // height the kill happened at, so a drop from an airborne enemy still
        // ends up somewhere reachable.
        _restY = MathF.Max(RestHeight, MathF.Min(position.Y, 3f));

        // De-synchronises the bob. Without it every orb on screen rises and falls
        // in lockstep, which reads as a rendering glitch rather than as motion.
        _phase = (position.X * 1.7f + position.Z * 2.3f) % Mathf.Tau;

        GlobalPosition = new Vector3(position.X, MathF.Max(position.Y, _restY), position.Z);
        Dress();
    }

    /// <summary>Applies the kind's mesh, material and resting tilt, and scales the
    /// orb with its value so a fat drop reads as one from across the arena.</summary>
    private void Dress()
    {
        switch (_kind)
        {
            case PickupKind.Gold:
                _mesh.Mesh = GoldMesh;
                _mesh.MaterialOverride = GoldMaterial;
                _tilt = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);   // coin stood on edge
                break;

            case PickupKind.Health:
                _mesh.Mesh = HealthMesh;
                _mesh.MaterialOverride = HealthMaterial;
                _tilt = new Vector3(0.62f, 0f, Mathf.Pi * 0.25f); // cube as a diamond
                break;

            default:
                _mesh.Mesh = XpMesh;
                _mesh.MaterialOverride = XpMaterial;
                _tilt = Vector3.Zero;
                break;
        }

        // Grows with the square root of value: a 100-xp orb should look bigger
        // than a 1-xp orb, not a hundred times bigger.
        float scale = _kind == PickupKind.Health
            ? 1f
            : Mathf.Clamp(0.85f + MathF.Sqrt(_value) * 0.16f, 0.85f, 2.1f);
        _mesh.Scale = new Vector3(scale, scale, scale);
    }

    // ------------------------------------------------------------------- loop

    /// <summary>
    /// Advances every orb by one frame and collects the ones that reached the
    /// player. Iterates backwards so a collection can remove in place.
    /// </summary>
    public static void UpdateAll(float dt)
    {
        // Co-op (Pickup.Coop.cs): the host decides who collects; a client only animates.
        if (Net.Coop.InRun)
        {
            if (Net.Coop.IsHost) UpdateAllHost(dt);
            else UpdateAllClient(dt);
            return;
        }

        Player? player = Player.Instance;
        bool live = player is not null && IsInstanceValid(player) && player.IsAlive;

        // Aim at hip height rather than at the feet, or orbs visibly dive into the
        // floor on the last few centimetres.
        Vector3 target = live ? player!.GlobalPosition + Vector3.Up * 0.9f : Vector3.Zero;
        float range = live ? player!.Stats.Get(Stat.PickupRange) : 0f;
        float rangeSq = range * range;

        for (int i = Live.Count - 1; i >= 0; i--)
        {
            Pickup p = Live[i];

            if (!IsInstanceValid(p) || p.IsPooled)
            {
                SwapRemove(i);
                continue;
            }

            if (p.Step(dt, target, rangeSq, live))
            {
                p.Collect(player);
                SwapRemove(i);
                _pool?.Release(p);
            }
        }
    }

    // Order in the live list carries no meaning, so the tail fills the hole. The
    // element moved into slot i was already visited — the loop runs backwards —
    // so nothing is processed twice or skipped.
    private static void SwapRemove(int index)
    {
        int last = Live.Count - 1;
        Live[index] = Live[last];
        Live.RemoveAt(last);
    }

    /// <summary>Returns true when this orb reached the player this frame.</summary>
    private bool Step(float dt, Vector3 target, float rangeSq, bool playerAlive)
    {
        _age += dt;
        Vector3 position = GlobalPosition;

        if (playerAlive && !_attracted && position.DistanceSquaredTo(target) <= rangeSq)
            _attracted = true;

        if (_attracted && playerAlive)
        {
            _speed = MathF.Min(AttractMaxSpeed, _speed + AttractAcceleration * dt);

            Vector3 to = target - position;
            float distance = to.Length();
            if (distance <= CollectRadius) return true;

            // Never overshoot: stepping past the player would orbit them forever
            // at high magnet speeds.
            float travel = MathF.Min(_speed * dt, distance);
            position += to / distance * travel;
            _mesh.Rotation = _tilt + new Vector3(0f, _age * SpinRate * 3f, 0f);
        }
        else if (!_settled)
        {
            // Spawn pop: a short arc so a cluster of kills scatters instead of
            // stacking every orb on one pixel.
            _velocity.Y -= Gravity * dt;
            position += _velocity * dt;

            if (position.Y <= _restY && _velocity.Y <= 0f)
            {
                position.Y = _restY;
                _velocity = Vector3.Zero;
                _settled = true;
            }

            _mesh.Rotation = _tilt + new Vector3(0f, _age * SpinRate * 2f, 0f);
        }
        else
        {
            position.Y = _restY + BobHeight * MathF.Sin(_age * BobRate + _phase);
            _mesh.Rotation = _tilt + new Vector3(0f, _age * SpinRate, 0f);
        }

        GlobalPosition = position;
        return false;
    }

    /// <summary>
    /// Pays out. Experience and gold go through the inventory so that
    /// <see cref="Stat.XpGain"/> and <see cref="Stat.GoldGain"/> are applied in one
    /// place; if there is no inventory (a test scene, a sandbox) the raw event is
    /// published instead so listeners still see the award.
    /// </summary>
    private void Collect(Player? player)
    {
        switch (_kind)
        {
            case PickupKind.Xp:
            {
                int xp = Math.Max(1, (int)MathF.Round(_value));
                if (Inventory.Current is { } inventory) inventory.GrantXp(xp);
                else EventBus.Publish(new XpGained(xp, xp));
                break;
            }

            case PickupKind.Gold:
            {
                int gold = Math.Max(1, (int)MathF.Round(_value));
                if (Inventory.Current is { } inventory) inventory.GrantGold(gold);
                else EventBus.Publish(new GoldGained(gold, gold));

                EventBus.Publish(new PopupRequested(
                    GlobalPosition + Vector3.Up * 0.6f, $"+{gold}", GoldColor, 0.75f));
                break;
            }

            case PickupKind.Health:
            {
                // PlayerHealth publishes its own heal popup, so nothing is added
                // here — two numbers for one pack reads as a bug.
                if (player is not null && IsInstanceValid(player) && player.Health is { } health)
                    health.HealFraction(_value);
                break;
            }
        }

        _value = 0f;
        _attracted = false;
        Visible = false;
    }

    /// <summary>
    /// Latches every orb on the floor into its chase. The mechanical half of a
    /// "collect everything" effect; exposed here because the attraction rules
    /// belong to the orb, not to whatever triggers them.
    /// </summary>
    public static void AttractAll()
    {
        for (int i = 0; i < Live.Count; i++)
            if (IsInstanceValid(Live[i])) Live[i]._attracted = true;
    }
}

/// <summary>
/// The world's drop table and the host node for the orb pool.
///
/// It listens for <see cref="EnemyKilled"/> and turns each kill into orbs. Which
/// kill drops what is decided here rather than on the enemy, because drop policy
/// is a property of the run's economy, not of any one monster.
///
/// <b>Gold and health drop in bundles, not every kill.</b> A coin from every
/// enemy would double the number of nodes on the floor for a reward the player
/// stops noticing. Instead gold drops one time in five for five times the value —
/// the same gold per kill on average, a fifth of the orbs, and a drop that reads
/// as an event. The expected payout is derived from the chance rather than tuned
/// separately, so changing the chance cannot silently rebalance the economy.
///
/// Nothing in <c>Main</c> constructs this. <see cref="Ensure"/> installs it on
/// demand from the progression objects that <em>are</em> constructed, which keeps
/// pickups working in any scene that has a player and an inventory.
/// </summary>
public sealed partial class PickupField : Node3D
{
    /// <summary>The live field, or null between runs.</summary>
    public static PickupField? Instance { get; private set; }

    /// <summary>Orbs that may exist at once. Past this, drops merge into existing
    /// orbs — see <see cref="Pickup.Spawn"/>.</summary>
    [Export] public int Capacity { get; set; } = 400;

    [Export] public int Prewarm { get; set; } = 64;

    /// <summary>Chance a normal kill drops gold. Elites always do.</summary>
    [Export] public float GoldDropChance { get; set; } = 0.2f;

    /// <summary>Chance a kill drops a med-pack, and the elite's better odds.
    /// Deliberately low: healing that arrives on demand removes the tension that
    /// makes a low health bar interesting.</summary>
    [Export] public float HealthDropChance { get; set; } = 0.025f;

    [Export] public float EliteHealthDropChance { get; set; } = 0.2f;

    /// <summary>Fraction of maximum health a med-pack restores.</summary>
    [Export] public float HealthPackFraction { get; set; } = 0.16f;

    /// <summary>Above this, an experience award is split into several orbs — a
    /// boss paying out one enormous orb is a worse moment than a shower.</summary>
    [Export] public int XpPerOrb { get; set; } = 12;

    [Export] public int MaxOrbsPerKill { get; set; } = 4;

    /// <summary>Health is only worth dropping to a player who can use it.</summary>
    private const float HealthDropBelowFraction = 0.92f;

    // Seeded so drops replay identically for a given run. Ensure() hands it the
    // run's own Rng when the offer pool is built; until then this constant keeps
    // the sequence deterministic rather than tied to wall-clock time.
    private Rng _rng = new(0x5EEDD0DEUL);
    private bool _subscribed;

    /// <summary>
    /// Returns the installed field, creating and parenting it if there is not one.
    /// Idempotent, and safe to call from anywhere including before the scene tree
    /// exists — it returns null rather than throwing in a treeless test.
    /// </summary>
    /// <param name="rng">The run's random source. Supplied when known so drop
    /// rolls join the run's reproducible stream; ignored when null.</param>
    public static PickupField? Ensure(Rng? rng = null)
    {
        if (Instance is not null && IsInstanceValid(Instance))
        {
            if (rng is not null) Instance._rng = rng;
            return Instance;
        }

        if (Engine.GetMainLoop() is not SceneTree tree) return null;

        Node? parent = tree.CurrentScene ?? tree.Root;
        if (parent is null) return null;

        var field = new PickupField { Name = "PickupField" };
        if (rng is not null) field._rng = rng;
        Instance = field;

        // Deferred: Ensure is called from inside another node's construction, and
        // adding a child to a parent that is mid-add is the one thing the scene
        // tree refuses to do.
        parent.CallDeferred(Node.MethodName.AddChild, field);
        return field;
    }

    public override void _Ready()
    {
        Instance = this;
        Pickup.Initialize(this, Capacity, Prewarm);

        EventBus.Subscribe<EnemyKilled>(OnEnemyKilled);
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (_subscribed)
        {
            _subscribed = false;
            EventBus.Unsubscribe<EnemyKilled>(OnEnemyKilled);
        }

        Pickup.Shutdown();
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    public override void _Process(double delta) => Pickup.UpdateAll((float)delta);

    // -------------------------------------------------------------- drop table

    private void OnEnemyKilled(EnemyKilled e)
    {
        // Co-op client: the host rolls the drops and sends them.
        if (Net.Coop.IsClient) return;
        if (e.XpValue > 0) DropXp(e.Position, e.XpValue);
        if (e.GoldValue > 0) DropGold(e.Position, e.GoldValue, e.WasElite);
        DropHealth(e.Position, e.WasElite);
    }

    private void DropXp(Vector3 position, int xp)
    {
        int orbs = Math.Clamp(1 + xp / Math.Max(1, XpPerOrb), 1, Math.Max(1, MaxOrbsPerKill));

        // Integer split with the remainder spread over the first orbs, so the
        // total paid out is exactly the enemy's value — no rounding leak.
        int each = xp / orbs;
        int remainder = xp % orbs;

        for (int i = 0; i < orbs; i++)
        {
            int value = each + (i < remainder ? 1 : 0);
            if (value <= 0) continue;
            Pickup.Spawn(PickupKind.Xp, position, value, Launch());
        }
    }

    private void DropGold(Vector3 position, int gold, bool elite)
    {
        float chance = elite ? 1f : Mathf.Clamp(GoldDropChance, 0.01f, 1f);
        if (!_rng.Chance(chance)) return;

        // Value scaled by the inverse of the chance: same gold per kill on
        // average as dropping every time, a fifth as many orbs.
        int amount = Math.Max(1, (int)MathF.Round(gold / chance));
        Pickup.Spawn(PickupKind.Gold, position, amount, Launch());
    }

    private void DropHealth(Vector3 position, bool elite)
    {
        if (Net.Coop.IsHost)
        {
            // Anyone on the team who could use it.
            if (Net.NetSession.Instance?.AnyPlayerBelow(HealthDropBelowFraction) != true) return;
        }
        else
        {
            if (Player.Instance is not { } player || !IsInstanceValid(player)) return;
            if (player.Health is not { } health || !health.IsAlive) return;
            if (health.Fraction >= HealthDropBelowFraction) return;
        }

        float chance = elite ? EliteHealthDropChance : HealthDropChance;
        if (!_rng.Chance(chance)) return;

        Pickup.Spawn(PickupKind.Health, position, MathF.Max(0.02f, HealthPackFraction), Launch());
    }

    /// <summary>A short random pop so simultaneous drops scatter instead of
    /// landing on the same point.</summary>
    private Vector3 Launch()
        => _rng.OnUnitCircleXZ() * _rng.Range(1.1f, 2.9f) + Vector3.Up * _rng.Range(2.2f, 3.6f);
}
