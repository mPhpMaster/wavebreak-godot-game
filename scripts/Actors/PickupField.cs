using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

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
