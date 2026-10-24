using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Audio;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// The perks that put something in the world: Brine Wake's puddles, Flotsam
/// Orbit's motes, Bubble Net's bubbles, Crab Stomp, Tidal Pulse, Lighthouse
/// Keeper's beam and Lifeline's surge. Each has a hard cap on how many of its
/// objects exist, and all of them are cleared on a new map.
/// </summary>
public sealed partial class PerkProcs
{
    // ------------------------------------------------------------- wake

    private sealed class Puddle
    {
        public MeshInstance3D Mesh = null!;
        public Vector3 At;
        public float Left, Life, Radius;
    }

    private const int MaxPuddles = 14;
    private readonly List<Puddle> _wake = new(MaxPuddles);
    private Vector3 _lastDrop = new(1e6f, 0f, 0f);
    private float _wakeTick;
    private static readonly Color WakeTint = new(0.55f, 0.85f, 0.95f);
    private readonly HashSet<IDamageable> _wakeHit = new();

    private void TickWake(Player p, float dt)
    {
        int lw = L(Wake);
        _wakeFor -= dt;
        if (lw > 0 && (p.IsDashing || _wakeFor > 0f))
        {
            Vector3 feet = p.GlobalPosition;
            float radius = Wake.V(2, lw) * 0.5f;
            if (feet.DistanceSquaredTo(_lastDrop) > radius * radius * 1.4f)
            {
                _lastDrop = feet;
                if (_wake.Count >= MaxPuddles) { Give(_wake[0].Mesh); _wake.RemoveAt(0); }
                MeshInstance3D m = Take(CombatVisuals.Disc, WakeTint);
                m.GlobalTransform = new Transform3D(Basis.Identity.Scaled(new Vector3(radius, 1f, radius)),
                                                    feet + Vector3.Up * FxGuard.GroundLift);
                float life = Wake.V(0, lw);
                _wake.Add(new Puddle { Mesh = m, At = feet, Left = life, Life = life, Radius = radius });
                if (_wake.Count == 1) AudioDirector.PlayPerk(PerkSound.Wake);
            }
        }

        for (int i = _wake.Count - 1; i >= 0; i--)
        {
            Puddle w = _wake[i];
            w.Left -= dt;
            if (w.Left <= 0f || !IsInstanceValid(w.Mesh)) { Give(w.Mesh); _wake.RemoveAt(i); continue; }
            w.Mesh.Transparency = GraphicsSettings.FxFade(1f - 0.5f * Mathf.Clamp(w.Left / w.Life, 0f, 1f));
        }

        if ((_wakeTick -= dt) > 0f || _wake.Count == 0) return;
        _wakeTick = 0.25f;
        _wakeHit.Clear();
        float dps = Wake.V(1, Mathf.Max(1, lw));
        foreach (Puddle w in _wake)
        {
            int n = Targeting.Overlap(w.At, w.Radius, Buffer);
            for (int k = 0; k < n; k++) _wakeHit.Add(Buffer[k]);
        }
        int struck = 0;
        foreach (IDamageable t in new List<IDamageable>(_wakeHit))
        {
            if (!t.IsAlive) continue;
            if (t is Enemy en) en.ApplyChill(1f, 99, 0f);
            if (Hit(p, t, Dmg(p, dps * 0.25f), Wake, Feet(t))) struck++;
        }
        if (Trace) GD.Print($"[perk] wake tick puddles={_wake.Count} touching={_wakeHit.Count} struck={struck} first={_wake[0].At}");
        if (struck > 0) Note(Wake);
    }

    // ------------------------------------------------------------- motes

    private sealed class Mote
    {
        public MeshInstance3D Mesh = null!;
        public float Angle, Left, HitCd;
    }

    private readonly List<Mote> _motes = new(8);
    private static readonly Color MoteTint = new(0.70f, 0.95f, 0.55f);

    private void SpawnMote(float life)
    {
        MeshInstance3D m = Take(CombatVisuals.Orb, MoteTint);
        m.Scale = Vector3.One * 0.16f;
        m.Transparency = GraphicsSettings.FxFade(0.2f);
        float angle = _motes.Count > 0 ? _motes[^1].Angle + Mathf.Tau / 5f : _rng.Range(0f, Mathf.Tau);
        _motes.Add(new Mote { Mesh = m, Angle = angle, Left = life });
    }

    private void TickMotes(Player p, float dt)
    {
        _moteCd -= dt;
        int lf = L(Flotsam);
        for (int i = _motes.Count - 1; i >= 0; i--)
        {
            Mote m = _motes[i];
            m.Left -= dt;
            if (lf <= 0 || m.Left <= 0f || !IsInstanceValid(m.Mesh)) { Give(m.Mesh); _motes.RemoveAt(i); continue; }
            m.Angle += 2.6f * dt;
            Vector3 at = p.GlobalPosition + new Vector3(Mathf.Cos(m.Angle) * 1.8f, 0.9f + 0.15f * Mathf.Sin(m.Angle * 2f), Mathf.Sin(m.Angle) * 1.8f);
            m.Mesh.GlobalPosition = at;
            if (m.Left < 0.5f) m.Mesh.Transparency = GraphicsSettings.FxFade(1f - m.Left / 0.5f * 0.8f);
            if ((m.HitCd -= dt) > 0f) continue;
            int n = Targeting.Overlap(at, 0.6f, Buffer);
            for (int k = 0; k < n; k++)
            {
                IDamageable t = Buffer[k];
                if (!t.IsAlive) continue;
                m.HitCd = 0.35f;
                if (Hit(p, t, Dmg(p, Flotsam.V(1, lf)), Flotsam, at, 1f))
                {
                    Note(Flotsam);
                    AudioDirector.PlayPerk(PerkSound.Mote);
                }
                break;
            }
        }
    }

    /// <summary>Motes orbiting right now.</summary>
    public int Motes => _motes.Count;

    // ------------------------------------------------------------ bubbles

    private sealed class Bubble
    {
        public MeshInstance3D Mesh = null!;
        public Vector3 At;
        public float Age;
    }

    private const int MaxBubbles = 5;
    private const float BubbleLife = 15f;
    private readonly List<Bubble> _bubbles = new(MaxBubbles);
    private static readonly Color BubbleTint = new(0.62f, 0.92f, 1.00f);

    private void SpawnBubble(Vector3 feet)
    {
        if (_bubbles.Count >= MaxBubbles) { Give(_bubbles[0].Mesh); _bubbles.RemoveAt(0); }
        MeshInstance3D m = Take(CombatVisuals.Orb, BubbleTint);
        m.Scale = Vector3.One * 0.42f;
        m.Transparency = GraphicsSettings.FxFade(0.45f);
        _bubbles.Add(new Bubble { Mesh = m, At = feet });
        m.GlobalPosition = feet + Vector3.Up * 0.9f;
    }

    private void TickBubbles(Player p, float dt)
    {
        int lb = L(Bubbles);
        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            Bubble b = _bubbles[i];
            b.Age += dt;
            if (lb <= 0 || b.Age >= BubbleLife || !IsInstanceValid(b.Mesh)) { Give(b.Mesh); _bubbles.RemoveAt(i); continue; }
            b.Mesh.GlobalPosition = b.At + Vector3.Up * (0.9f + 0.12f * Mathf.Sin(b.Age * 3f));
            if (BubbleLife - b.Age < 2f) b.Mesh.Visible = Mathf.PosMod(b.Age * 6f, 1f) < 0.6f;   // about to pop

            Vector3 d = p.GlobalPosition - b.At;
            d.Y = 0f;
            if (d.LengthSquared() > 2.0f * 2.0f || p.Health is not { } h) continue;
            Give(b.Mesh);
            _bubbles.RemoveAt(i);
            float shield = Bubbles.V(0, lb);
            _bubbleLeft = Bubbles.V(1, lb);
            SetMod(p, Bubbles, Stat.Shield, ModifierOp.Flat, shield);
            h.PerkShield(shield);
            Popup(p.CenterPosition + Vector3.Up * 1.1f, "BUBBLE", BubbleTint, 1.0f);
            AudioDirector.PlayPerk(PerkSound.Bubble);
            Note(Bubbles);
        }
    }

    /// <summary>Bubbles waiting to be collected.</summary>
    public int BubblesWaiting => _bubbles.Count;

    /// <summary>Seconds left on a collected bubble's shield.</summary>
    public float BubbleShieldLeft => _bubbleLeft;

    // -------------------------------------------------------------- stomp

    private float _stompCd;

    private void TickStomp(Player p)
    {
        _stompCd -= (float)GetProcessDeltaTime();
        int ls = L(Stomp);
        if (ls <= 0 || _stompCd > 0f || p.IsOnFloor() || p.Velocity.Y > -1.5f) return;
        Vector3 feet = p.GlobalPosition;
        int n = Targeting.Overlap(feet, 1.1f, Buffer);
        Enemy? under = null;
        for (int i = 0; i < n; i++)
        {
            if (Buffer[i] is not Enemy en || !en.IsAlive) continue;
            float dy = feet.Y - en.GlobalPosition.Y;
            if (dy > 0.25f && dy < 2.8f && en.AimPoint.Y < feet.Y + 0.3f) { under = en; break; }
        }
        if (under is null) return;

        _stompCd = 0.45f;
        float radius = Stomp.V(1, ls);
        Area(p, under.GlobalPosition, radius, Dmg(p, Stomp.V(0, ls)), Stomp, knock: 3f, stun: Stomp.V(2, ls));
        Ring(under.GlobalPosition, radius, new Color(0.95f, 0.55f, 0.40f), 0.3f);
        p.Velocity = p.Velocity with { Y = 7.5f };   // bounce off
        AudioDirector.PlayPerk(PerkSound.Stomp);
        Note(Stomp);
    }

    // -------------------------------------------------------------- pulse

    private float _pulseTimer = 2f;

    /// <summary>--check-perks: make the next pulse go off within a frame.</summary>
    internal void PrimePulseForCheck() => _pulseTimer = 0.01f;

    private void TickPulse(Player p, float dt)
    {
        int lp = L(Pulse);
        if (lp <= 0 || (_pulseTimer -= dt) > 0f) return;
        _pulseTimer = Pulse.V(0, lp);
        float radius = Pulse.V(2, lp);
        int hits = Area(p, p.CenterPosition, radius, Dmg(p, Pulse.V(1, lp)), Pulse, knock: 6f);
        if (hits == 0) return;   // nothing near: no flash, no sound
        Ring(p.GlobalPosition, radius, new Color(0.40f, 0.75f, 1.00f), 0.4f, centredOnPlayer: true);
        AudioDirector.PlayPerk(PerkSound.Pulse);
        Note(Pulse);
    }

    // ---------------------------------------------------------- lighthouse

    private float _beamTimer = 4f;
    private Chest? _marked;

    private void TickLighthouse(float dt)
    {
        int lk = L(Keeper);
        if (lk <= 0 || (_beamTimer -= dt) > 0f) return;
        Player? p = LivePlayer;
        Chest? best = null;
        float bestSq = float.MaxValue;
        if (p is not null && ChestSpawner.Instance is { } cs)
            foreach (Chest c in cs.Chests)
            {
                if (!IsInstanceValid(c) || c.IsQueuedForDeletion() || c.IsOpen) continue;
                float d = c.GlobalPosition.DistanceSquaredTo(p.GlobalPosition);
                if (d < bestSq) { bestSq = d; best = c; }
            }
        if (best is null) { _beamTimer = 5f; return; }   // nothing closed on this map yet
        _beamTimer = Keeper.V(0, lk);
        _marked = best;
        Beam(best.GlobalPosition, new Color(1f, 0.95f, 0.75f), 5f);
        Popup(best.Top + Vector3.Up * 0.4f, "Chest marked", new Color(1f, 0.95f, 0.75f), 0.9f);
        AudioDirector.PlayPerk(PerkSound.Lighthouse);
        Note(Keeper);
    }

    /// <summary>The chest the lighthouse beam marked, if still closed.</summary>
    public Chest? MarkedChest => _marked is not null && IsInstanceValid(_marked) && !_marked.IsOpen ? _marked : null;

    /// <summary>Times a marked chest was opened (its bonus paid).</summary>
    public int MarkedOpened { get; private set; }

    private void OnChestOpened(ChestOpened e)
    {
        int lk = L(Keeper);
        if (lk <= 0 || _marked is null || !IsInstanceValid(_marked)) return;
        if (_marked.GlobalPosition.DistanceSquaredTo(e.Position) > 0.25f) return;
        _marked = null;
        MarkedOpened++;
        _main.Inventory?.GrantGold((int)Keeper.V(1, lk));
        if (Keeper.V(2, lk) > 0f) LivePlayer?.Health?.HealFraction(Keeper.V(2, lk) / 100f);
    }

    // -------------------------------------------------------------- surge

    private float _surgeCd, _surgeLeft, _surgeRate;

    private void TickSurge(Player p, float dt)
    {
        _surgeCd -= dt;
        if (_surgeLeft > 0f && p.Health is { } hh)
        {
            _surgeLeft -= dt;
            hh.PerkTrickle(_surgeRate * dt);
        }
        int ll = L(Lifeline);
        if (ll <= 0 || _surgeCd > 0f || p.Health is not { } h || h.Fraction >= 0.35f) return;
        _surgeCd = 40f;
        _surgeLeft = 4f;
        _surgeRate = Lifeline.V(2, ll) / 100f * h.Max / 4f;
        Popup(p.CenterPosition + Vector3.Up * 1.2f, "SURGE", new Color(0.55f, 1f, 0.75f), 1.0f);
        AudioDirector.PlayPerk(PerkSound.Surge);
        Note(Lifeline);
    }

    // ----------------------------------------------------------- new map

    private void ClearWorldFx()
    {
        foreach (Puddle w in _wake) Give(w.Mesh);
        _wake.Clear();
        foreach (Bubble b in _bubbles) Give(b.Mesh);
        _bubbles.Clear();
        _lastDrop = new Vector3(1e6f, 0f, 0f);
    }

    // ------------------------------------------------------------- chests

    /// <summary>Chance a chest pays a perk level instead of an item, when a
    /// perk can still be taken (held below max, or a free slot).</summary>
    public const float StandardChestPerkChance = 0.05f, GildedChestPerkChance = 0.30f;

    /// <summary>
    /// A chest's perk, or null for the usual item roll. Same filters as a
    /// level-up card (slots, max level, banished, switched off in the Toggler),
    /// weighted by the perk's own weight and luck; a perk already held is
    /// preferred, as a level-up prefers tome upgrades. Drawn from the chest's
    /// stream, so it never shifts the level-up offers.
    /// </summary>
    public static Offer? RollChestPerk(OfferPool pool, bool gilded, Rng rng)
    {
        if (pool.Tomes is not { } book || Player.Instance is not { } player) return null;
        if (!rng.Chance(gilded ? GildedChestPerkChance : StandardChestPerkChance)) return null;
        float luck = Mathf.Clamp(player.Stats.Get(Stat.Luck), OfferPool.MinLuck, OfferPool.MaxLuck);
        var options = new List<PerkDef>();
        var weights = new List<float>();
        foreach (PerkDef d in Perks.All)
        {
            if (!book.CanTake(d) || pool.Banished.Contains(d) || pool.DisabledTomes.Contains(d.Id)) continue;
            options.Add(d);
            weights.Add(d.Weight * (book.LevelOf(d) > 0 ? 1.5f : 1f) * OfferPool.LuckShift(d.Rarity, luck));
        }
        int pick = rng.PickWeighted(weights);
        return pick < 0 ? null : Offer.ForTome(options[pick], book.LevelOf(options[pick]));
    }
}
