using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// The rules behind the fifteen perks (<see cref="Perks"/>), one node per run,
/// built the way <see cref="ItemProcs"/> is:
///
///   * it hears the run on the bus and is never called from combat;
///   * every stat it changes is one tagged modifier per (perk, stat), replaced
///     rather than adjusted, so a buff ending cannot leave residue, and a perk
///     at level 0 (never taken, or a cleared build) writes nothing;
///   * levels are read live from <see cref="TomeBook.Current"/> and every number
///     from the perk's own table (<see cref="PerkDef.V"/>), so the card and the
///     effect cannot disagree;
///   * damage it deals is tagged <c>perk:</c> with a proc coefficient of zero,
///     and such hits are ignored on the way back in, by these rules and by the
///     item procs: a perk never triggers a perk or an item, and vice versa;
///   * it draws from its own random stream, so taking a perk never shifts the
///     run's seeded spawns and offers.
///
/// Co-op: each machine runs this node for its own player only. A hit it deals
/// goes through <see cref="IDamageable.TakeDamage"/>, which on a client is the
/// puppet's predicted hit forwarded to the host (the same path weapons and
/// items use); <see cref="EnemyDamaged"/> is only ever published for this
/// machine's own hits (the host applies forwarded hits without publishing), so
/// a teammate's crits never heal you and their kills never drop your bubbles.
/// The two team perks are in PerkProcs.Coop.cs.
/// </summary>
public sealed partial class PerkProcs : Node
{
    public static PerkProcs? Instance { get; private set; }

    private static readonly List<IDamageable> Buffer = new(64);

    private static PerkDef Def(string id) => Perks.ById(id) ?? throw new InvalidOperationException("no perk " + id);
    private static readonly PerkDef Storm = Def(Perks.Stormcount), Bubbles = Def(Perks.BubbleNet), Wake = Def(Perks.BrineWake),
        Stomp = Def(Perks.CrabStomp), Tide = Def(Perks.RedTide), Hide = Def(Perks.Barnacle), Flotsam = Def(Perks.Flotsam),
        Ballast = Def(Perks.Ballast), Light = Def(Perks.LastLight), Keeper = Def(Perks.Lighthouse), Winds = Def(Perks.FairWinds),
        Eel = Def(Perks.Cornered), Beacon = Def(Perks.Beacon), Lifeline = Def(Perks.Lifeline), Pulse = Def(Perks.TidalPulse);

    private Main _main = null!;
    private Rng _rng = null!;
    private readonly Dictionary<(string, Stat), (object tag, float value)> _mods = new();
    private bool _reentry;
    private float _clock;

    /// <summary>Times each perk's effect went off this run, by perk id (the
    /// self-check and the end-of-run line read it).</summary>
    public readonly Dictionary<string, int> Fired = new(StringComparer.Ordinal);

    private void Note(PerkDef d) => Fired[d.Id] = Fired.TryGetValue(d.Id, out int n) ? n + 1 : 1;

    public int FiredCount(string id) => Fired.TryGetValue(id, out int n) ? n : 0;

    public static void Attach(Main main)
    {
        var node = new PerkProcs { Name = "PerkProcs", _main = main };
        node._rng = new Rng(main.Run.Seed ^ 0x9E2C_51A7UL);
        Instance = node;
        Net.PerkNet.Reset();
        TeamBeaconSeconds = 0f;
        main.AddChild(node);
        PerksCheck.MaybeStart(main, node);   // --check-perks, --perk-levelup-shot=
    }

    public override void _Ready()
    {
        EventBus.Subscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Subscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Subscribe<PlayerMoved>(OnPlayerMoved);
        EventBus.Subscribe<XpGained>(OnXpGained);
        EventBus.Subscribe<GoldGained>(OnGoldGained);
        EventBus.Subscribe<ChestOpened>(OnChestOpened);
        EventBus.Subscribe<PhaseStarted>(OnPhaseStarted);
        EventBus.Subscribe<TomeAcquired>(OnTomeAcquired);
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Unsubscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Unsubscribe<PlayerMoved>(OnPlayerMoved);
        EventBus.Unsubscribe<XpGained>(OnXpGained);
        EventBus.Unsubscribe<GoldGained>(OnGoldGained);
        EventBus.Unsubscribe<ChestOpened>(OnChestOpened);
        EventBus.Unsubscribe<PhaseStarted>(OnPhaseStarted);
        EventBus.Unsubscribe<TomeAcquired>(OnTomeAcquired);
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------ helpers

    private Player? LivePlayer
    {
        get
        {
            if (_main is null || !IsInstanceValid(_main)) return null;
            Player p = _main.Player;
            return p is not null && IsInstanceValid(p) && p.IsInsideTree() ? p : null;
        }
    }

    /// <summary>Level held of a perk (0 = not held).</summary>
    private static int L(PerkDef d) => TomeBook.Current?.LevelOf(d) ?? 0;

    /// <summary>Param <paramref name="i"/> of a perk at the level held (0 when not held).</summary>
    private static float V(PerkDef d, int i) => d.V(i, L(d));

    private static float Dmg(Player p, float amount) => amount * p.Stats.GetMultiplier(Stat.Damage);

    private void SetMod(Player p, PerkDef d, Stat stat, ModifierOp op, float value)
    {
        var key = (d.Id, stat);
        if (!_mods.TryGetValue(key, out var cur)) cur = (new object(), 0f);
        if (Mathf.Abs(cur.value - value) < 0.0005f && !(value == 0f && cur.value != 0f)) return;
        p.Stats.RemoveAllFrom(cur.tag);
        if (value != 0f) p.Stats.Add(stat, op, value, cur.tag);
        _mods[key] = (cur.tag, value);
    }

    /// <summary>The modifier a perk currently writes to a stat (the self-check reads it).</summary>
    public float ModValue(string id, Stat stat) => _mods.TryGetValue((id, stat), out var m) ? m.value : 0f;

    /// <summary>One perk hit. Never re-triggers perks or items; a kill it lands
    /// still counts for Bubble Net.</summary>
    private bool Hit(Player p, IDamageable t, float amount, PerkDef d, Vector3 from, float knock = 0f)
    {
        if (amount <= 0f || !t.IsAlive) return false;
        var info = new DamageInfo
        {
            Amount = amount,
            Source = "perk:" + d.Id,
            ProcCoefficient = 0f,
            CanCrit = false,
            Knockback = knock,
            Direction = Projectile.FlatDirection(t.Position - from, Vector3.Forward),
        };
        WeaponSystem? ws = _main.Weapons is { } w && IsInstanceValid(w) ? w : null;
        _reentry = true;
        try
        {
            t.TakeDamage(info);
            ws?.ReportHit(t, info);
        }
        finally { _reentry = false; }
        if (!t.IsAlive) OnMyKill(p, t);
        return !info.Nullified && info.Amount > 0f;
    }

    /// <summary>Area damage around <paramref name="at"/>; returns how many it hit.</summary>
    private int Area(Player p, Vector3 at, float radius, float amount, PerkDef d, float knock = 0f, float stun = 0f, int max = 40)
    {
        int n = Targeting.Overlap(at, radius, Buffer);
        // Snapshot: a kill below can drop a bubble or start another rule, and
        // the shared buffer must not change under the loop.
        IDamageable[] targets = Buffer.GetRange(0, Mathf.Min(n, Buffer.Count)).ToArray();
        int hits = 0;
        foreach (IDamageable t in targets)
        {
            if (hits >= max) break;
            if (!t.IsAlive) continue;
            if (Hit(p, t, amount, d, at, knock)) hits++;
            if (stun > 0f && t is Enemy en && en.IsAlive) en.ApplyStun(stun);
        }
        return hits;
    }

    private static IDamageable? NearestNot(Vector3 from, float radius, HashSet<IDamageable> skip)
    {
        int n = Targeting.Overlap(from, radius, Buffer);
        IDamageable? best = null;
        float bestSq = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            IDamageable t = Buffer[i];
            if (!t.IsAlive || skip.Contains(t)) continue;
            Vector3 d = t.Position - from;
            d.Y = 0f;
            if (d.LengthSquared() < bestSq) { bestSq = d.LengthSquared(); best = t; }
        }
        return best;
    }

    private static Vector3 Feet(IDamageable t) => t is Node3D n ? n.GlobalPosition : t.Position - Vector3.Up * 0.5f;

    private static void Popup(Vector3 at, string text, Color c, float scale = 1f)
        => EventBus.Publish(new PopupRequested(at, Meta.Loc.T(text), c, scale));

    // --------------------------------------------------------------- tick

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _clock += dt;
        TickFx(dt);
        if (LivePlayer is not { } p || _main.Run is null || _main.Run.Over) return;

        TickCoop(p, dt);            // PerkProcs.Coop.cs: sync, beacon
        if (!p.IsAlive)
        {
            ClearTimedBuffs(p);
            return;
        }

        TickStats(p, dt);
        TickWake(p, dt);
        TickMotes(p, dt);
        TickBubbles(p, dt);
        TickStomp(p);
        TickSurge(p, dt);
        if (!_main.Stages.InRestRoom)
        {
            TickPulse(p, dt);
            TickLighthouse(dt);
        }
    }

    private void ClearTimedBuffs(Player p)
    {
        _barnacle = 0f;
        SetMod(p, Hide, Stat.Armor, ModifierOp.Flat, 0f);
        SetMod(p, Hide, Stat.Thorns, ModifierOp.Flat, 0f);
        SetMod(p, Winds, Stat.AttackSpeed, ModifierOp.Additive, 0f);
        SetMod(p, Winds, Stat.ProjectileSpeed, ModifierOp.Additive, 0f);
    }

    // ------------------------------------------------- stats with a twist

    private float _barnacle;
    private bool _barnacleFull;
    private float _healBudget, _megaCd;
    private readonly List<float> _ballast = new();
    private float _burstCd;
    private float _bubbleLeft;

    private void TickStats(Player p, float dt)
    {
        // Fair Winds: attack speed from how fast you are moving right now.
        float speed = p.PlanarVelocity.Length();
        int lw = L(Winds);
        SetMod(p, Winds, Stat.AttackSpeed, ModifierOp.Additive, lw > 0 ? Mathf.Min(V(Winds, 1), V(Winds, 0) * speed) / 100f : 0f);
        SetMod(p, Winds, Stat.ProjectileSpeed, ModifierOp.Additive, lw > 0 ? Mathf.Min(40f, V(Winds, 2) * speed) / 100f : 0f);
        if (lw > 0 && speed > 4f && _clock - _windsNoted > 5f) { _windsNoted = _clock; Note(Winds); }

        // Cornered Eel: stronger the lower your health, below half.
        int le = L(Eel);
        float f = p.Health?.Fraction ?? 1f;
        bool hurt = le > 0 && f < 0.5f;
        SetMod(p, Eel, Stat.Damage, ModifierOp.Additive, hurt ? V(Eel, 0) / 100f * (0.5f - f) / 0.5f : 0f);
        SetMod(p, Eel, Stat.Lifesteal, ModifierOp.Flat, hurt ? V(Eel, 1) / 100f : 0f);
        SetMod(p, Eel, Stat.Evasion, ModifierOp.Flat, le > 0 && f < 0.25f ? V(Eel, 2) : 0f);
        if (hurt && _clock - _eelNoted > 5f) { _eelNoted = _clock; Note(Eel); }

        // Barnacle Hide: armour grows while still and is shed slowly on the move.
        int lb = L(Hide);
        if (lb <= 0) _barnacle = 0f;
        else if (speed < 0.4f) _barnacle = Mathf.Min(V(Hide, 1), _barnacle + V(Hide, 0) * dt);
        else _barnacle = Mathf.Max(0f, _barnacle - Mathf.Max(4f, V(Hide, 1) * 0.35f) * dt);
        SetMod(p, Hide, Stat.Armor, ModifierOp.Flat, Mathf.Round(_barnacle * 4f) / 4f);
        SetMod(p, Hide, Stat.Thorns, ModifierOp.Flat, Mathf.Round(_barnacle * V(Hide, 2) * 4f) / 4f);
        bool full = lb > 0 && _barnacle >= V(Hide, 1) - 0.01f;
        if (full && !_barnacleFull) Note(Hide);
        _barnacleFull = full;

        // Golden Ballast: stacks from gold pickups, each with its own clock.
        for (int i = _ballast.Count - 1; i >= 0; i--) if (_ballast[i] <= _clock) _ballast.RemoveAt(i);
        if (L(Ballast) <= 0) _ballast.Clear();
        SetMod(p, Ballast, Stat.Damage, ModifierOp.Additive, _ballast.Count * V(Ballast, 0) / 100f);
        _burstCd -= dt;

        // Red Tide: the per-second healing budget refills continuously.
        float cap = V(Tide, 1);
        _healBudget = Mathf.Min(cap, _healBudget + cap * dt);
        _megaCd -= dt;

        // Bubble Net: the bubble's shield is temporary.
        if (_bubbleLeft > 0f && (_bubbleLeft -= dt) <= 0f)
        {
            SetMod(p, Bubbles, Stat.Shield, ModifierOp.Flat, 0f);
            p.Health?.ClampShield();
        }
    }

    private float _windsNoted = -99f, _eelNoted = -99f;

    // ------------------------------------------------------------ events

    private int _hitCount;

    /// <summary>--check-perks: log each proc's details.</summary>
    internal static bool Trace;

    private void OnEnemyDamaged(EnemyDamaged e)
    {
        if (_reentry || e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f || e.Target is null) return;
        if (LivePlayer is not { } p || !p.IsAlive) return;
        DamageInfo info = e.Info;

        // Any of this machine's own hits that killed (weapons, statuses,
        // items) counts as this player's kill. Perk hits report their own.
        if (!e.Target.IsAlive && !info.Source.StartsWith("perk:")) OnMyKill(p, e.Target);

        // The rest only from real weapon hits: never from a proc or a tick.
        if (info.ProcCoefficient <= 0f || info.Source.StartsWith("item:") || info.Source.StartsWith("perk:")) return;

        int ls = L(Storm);
        if (ls > 0 && ++_hitCount >= (int)Storm.V(0, ls))
        {
            _hitCount = 0;
            Chain(p, e.Target, Mathf.Max(8f, info.Amount) * Storm.V(2, ls) / 100f, (int)Storm.V(1, ls));
        }

        if (info.Crit && L(Tide) > 0 && p.Health is { } h)
        {
            float heal = Mathf.Min(V(Tide, 0), _healBudget);
            if (heal > 0f)
            {
                _healBudget -= heal;
                h.PerkTrickle(heal);
                Note(Tide);
            }
            if (info.Megacrit && V(Tide, 2) > 0f && _megaCd <= 0f)
            {
                _megaCd = 0.5f;
                h.HealFraction(V(Tide, 2) / 100f);
                AudioDirector.PlayPerk(PerkSound.Heal);
            }
        }
    }

    private static readonly Color StormTint = new(0.95f, 0.92f, 0.45f);

    private void Chain(Player p, IDamageable first, float amount, int jumps)
    {
        var done = new HashSet<IDamageable> { first };
        Vector3 at = first.Position;
        int struck = 0;
        for (int j = 0; j < jumps; j++)
        {
            IDamageable? next = NearestNot(at, 7f, done);
            if (next is null) break;
            Projectile.SpawnStreak(at + Vector3.Up * 0.3f, next.Position + Vector3.Up * 0.3f, 0.12f, StormTint);
            Hit(p, next, amount, Storm, at);
            done.Add(next);
            at = next.Position;
            struck++;
        }
        if (Trace) GD.Print($"[perk] stormcount chain from {first.Position} struck={struck} near={Targeting.Overlap(first.Position, 7f, new List<IDamageable>())}");
        if (struck == 0) return;
        Note(Storm);
        AudioDirector.PlayPerk(PerkSound.Chain);
    }

    private void OnMyKill(Player p, IDamageable t)
    {
        int lb = L(Bubbles);
        if (lb <= 0) return;
        bool big = t is Enemy en && (en.IsElite || en.IsBoss);
        if (big || _rng.Chance(Bubbles.V(2, lb) / 100f)) SpawnBubble(Feet(t));
    }

    private void OnPlayerDamaged(PlayerDamaged e)
    {
        if (e.Info is null || e.Info.Nullified || LivePlayer is not { } p || p.Health is not { } h) return;

        // Last Light: a blow that would kill is refused while charges last.
        // PlayerHealth publishes before it checks for death, so health raised
        // here keeps the player standing (single-player) or up (co-op).
        int ll = L(Light);
        if (ll <= 0 || h.Current > 0f) return;
        if (_lightUsed >= (int)Light.V(0, ll)) return;
        _lightUsed++;
        h.HealFraction(Light.V(1, ll) / 100f);
        h.GrantInvulnerability(2f);
        Vector3 feet = p.GlobalPosition;
        if (Light.V(2, ll) > 0f) Area(p, p.CenterPosition, 4f, Dmg(p, Light.V(2, ll)), Light, knock: 8f);
        Ring(feet, 4f, new Color(1f, 0.9f, 0.6f), 0.5f, centredOnPlayer: true);
        Popup(p.CenterPosition + Vector3.Up * 1.3f, "LAST LIGHT", new Color(1f, 0.92f, 0.6f), 1.4f);
        AudioDirector.PlayPerk(PerkSound.LastLight);
        Note(Light);
        GD.Print($"[perk] last light refused a lethal blow ({_lightUsed}/{(int)Light.V(0, ll)} this map)");
    }

    private int _lightUsed;

    /// <summary>Last Light charges left on this map.</summary>
    public int LastLightCharges => Math.Max(0, (int)V(Light, 0) - _lightUsed);

    private void OnPhaseStarted(PhaseStarted e)
    {
        _lightUsed = 0;
        _marked = null;
        ClearWorldFx();
    }

    private float _wakeFor;

    private void OnPlayerMoved(PlayerMoved e)
    {
        if (e.Verb == MoveVerb.Dash && L(Wake) > 0) _wakeFor = 0.3f;
    }

    private float _moteCd;

    private void OnXpGained(XpGained e)
    {
        int lf = L(Flotsam);
        if (e.Amount <= 0 || lf <= 0 || _moteCd > 0f || _motes.Count >= (int)Flotsam.V(0, lf)) return;
        if (LivePlayer is not { IsAlive: true }) return;
        _moteCd = 0.3f;
        SpawnMote(Flotsam.V(2, lf));
    }

    private void OnGoldGained(GoldGained e)
    {
        int lb = L(Ballast);
        if (e.Amount <= 0 || lb <= 0 || _reentry || LivePlayer is not { IsAlive: true } p) return;
        float life = Ballast.V(2, lb);
        if (_ballast.Count < (int)Ballast.V(1, lb))
        {
            _ballast.Add(_clock + life);
            if (_ballast.Count == (int)Ballast.V(1, lb)) Note(Ballast);
            return;
        }
        // Full: a pickup refreshes the oldest stack, and may shake loose a burst.
        int oldest = 0;
        for (int i = 1; i < _ballast.Count; i++) if (_ballast[i] < _ballast[oldest]) oldest = i;
        _ballast[oldest] = _clock + life;
        if (Ballast.V(3, lb) <= 0f || _burstCd > 0f) return;
        _burstCd = 1.5f;
        Area(p, p.CenterPosition, 4f, Dmg(p, Ballast.V(3, lb)), Ballast, knock: 3f);
        Ring(p.GlobalPosition, 4f, new Color(1f, 0.82f, 0.3f), 0.35f, centredOnPlayer: true);
        AudioDirector.PlayPerk(PerkSound.Coin);
        Note(Ballast);
    }

    /// <summary>Golden Ballast stacks held now.</summary>
    public int BallastStacks => _ballast.Count;

    private void OnTomeAcquired(TomeAcquired e)
    {
        if (e.Id == Keeper.Id && e.NewLevel == 1) _beamTimer = Mathf.Min(_beamTimer, 4f);
        if (e.Id == Pulse.Id && e.NewLevel == 1) _pulseTimer = 2f;
        if (e.Id == Beacon.Id || e.Id == Lifeline.Id) _syncTimer = 0f;   // tell the team now
    }
}
