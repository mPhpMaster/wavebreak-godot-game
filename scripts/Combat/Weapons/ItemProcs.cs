using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// The rules carried by the expansion items (see <c>Data/Catalog.Expansion.cs</c>),
/// as one node per run.
///
/// Same habits as <see cref="Meta.CharacterPassives"/>: it hears the run on the
/// bus instead of being called from combat, and every stat it changes is one
/// tagged modifier per (item, stat) that is replaced rather than adjusted, so a
/// buff ending can never leave residue. Stack counts are read live from the
/// inventory, so an item picked up mid-buff takes effect on the next event.
///
/// Damage it deals is tagged <c>item:</c> with a proc coefficient of zero, and
/// hits with either are ignored on the way back in — a proc can never trigger
/// another proc, which is the loop every on-hit system eventually finds.
///
/// It draws from its own random stream so items never shift the run's seeded
/// spawns and offers.
/// </summary>
public sealed partial class ItemProcs : Node
{
    /// <summary>Every item id this node has a rule for. The audit checks that
    /// each one exists and that no rule-describing item is missing here.</summary>
    public static readonly string[] Handled =
    {
        "sparking_limpet", "riptooth_charm", "brine_bomb_locket", "shatterglass_coral",
        "wraith_lantern", "scrimshaw_tally", "powder_keg", "gilded_scale", "pyre_totem",
        "urchin_shell", "ink_sac", "wrathful_conch", "lifebuoy",
        "gale_sail", "slickfin_greaves", "wavecrest_fins", "spindrift_cape", "plunge_weights", "anchored_stance",
        "diving_bell", "votive_candle", "misers_anchor", "coin_divers_glove",
        "eye_of_the_maelstrom", "mariners_last_wish", "tithe_of_the_deep",
    };

    private static readonly Color ProcColor = new(0.80f, 0.95f, 1.00f);
    private static readonly List<IDamageable> Buffer = new(64);

    private Main _main = null!;
    private Rng _rng = null!;

    private readonly Dictionary<(string, Stat), (object tag, float value)> _mods = new();

    private int _kills;              // all kills, for the tally and the maelstrom
    private int _notches;
    private int _frenzy;
    private float _frenzyLeft;
    private float _wrathLeft;
    private float _inkLeft;
    private float _capeLeft;
    private float _still;
    private float _lifebuoyReady;
    private float _wishReady;
    private bool _reentry;

    public static void Attach(Main main)
    {
        var node = new ItemProcs { Name = "ItemProcs", _main = main };
        node._rng = new Rng(main.Run.Seed ^ 0x17E3_9A0CUL);
        main.AddChild(node);
    }

    public override void _Ready()
    {
        EventBus.Subscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Subscribe<EnemyKilled>(OnEnemyKilled);
        EventBus.Subscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Subscribe<PlayerMoved>(OnPlayerMoved);
        EventBus.Subscribe<ChestOpened>(OnChestOpened);
        EventBus.Subscribe<MapShrineUsed>(OnShrineUsed);
        EventBus.Subscribe<GoldGained>(OnGoldGained);
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<EnemyDamaged>(OnEnemyDamaged);
        EventBus.Unsubscribe<EnemyKilled>(OnEnemyKilled);
        EventBus.Unsubscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Unsubscribe<PlayerMoved>(OnPlayerMoved);
        EventBus.Unsubscribe<ChestOpened>(OnChestOpened);
        EventBus.Unsubscribe<MapShrineUsed>(OnShrineUsed);
        EventBus.Unsubscribe<GoldGained>(OnGoldGained);
    }

    // ------------------------------------------------------------ helpers

    private Player? LivePlayer
    {
        get
        {
            if (_main is null || !IsInstanceValid(_main)) return null;
            Player p = _main.Player;
            return p is not null && IsInstanceValid(p) ? p : null;
        }
    }

    private int N(string id) => _main.Inventory?.StacksOf(id) ?? 0;

    private float Dur(Player p) => Mathf.Max(0.25f, p.Stats.Get(Stat.Duration));

    private void SetMod(Player p, string id, Stat stat, ModifierOp op, float value)
    {
        var key = (id, stat);
        if (!_mods.TryGetValue(key, out var cur)) cur = (new object(), 0f);
        if (Mathf.Abs(cur.value - value) < 0.004f && !(value == 0f && cur.value != 0f)) return;
        p.Stats.RemoveAllFrom(cur.tag);
        if (value != 0f) p.Stats.Add(stat, op, value, cur.tag);
        _mods[key] = (cur.tag, value);
    }

    /// <summary>Area damage from an item. Never re-triggers item procs.</summary>
    private void Blast(Vector3 at, float radius, float amount, string id, IDamageable? skip = null, int max = 99)
    {
        if (amount <= 0f) return;
        int n = Targeting.Overlap(at, radius, Buffer);
        int hit = 0;
        WeaponSystem? ws = _main.Weapons is { } w && IsInstanceValid(w) ? w : null;
        for (int i = 0; i < n && hit < max; i++)
        {
            IDamageable t = Buffer[i];
            if (ReferenceEquals(t, skip) || !t.IsAlive) continue;
            var info = new DamageInfo
            {
                Amount = amount,
                Source = "item:" + id,
                ProcCoefficient = 0f,
                CanCrit = false,
                Knockback = 1f,
                Direction = Projectile.FlatDirection(t.Position - at, Vector3.Forward),
            };
            _reentry = true;
            try
            {
                t.TakeDamage(info);
                ws?.ReportHit(t, info);
            }
            finally { _reentry = false; }
            hit++;
        }
    }

    private static bool IsProc(DamageInfo info)
        => info.ProcCoefficient <= 0f || info.Source.StartsWith("item:") || info.Source is "thorns" or "burn" or "overcharge";

    // --------------------------------------------------------------- tick

    public override void _Process(double delta)
    {
        if (LivePlayer is not { } p || _main.Run is null || _main.Run.Over) return;
        float dt = (float)delta;
        _lifebuoyReady -= dt;
        _wishReady -= dt;

        // Airborne.
        int gale = N("gale_sail");
        SetMod(p, "gale_sail", Stat.Damage, ModifierOp.Additive, gale > 0 && !p.IsOnFloor() ? 0.12f * gale : 0f);

        // Sliding.
        int fin = N("slickfin_greaves");
        bool sliding = fin > 0 && p.IsSliding;
        SetMod(p, "slickfin_greaves", Stat.Damage, ModifierOp.Additive, sliding ? 0.20f * fin : 0f);
        SetMod(p, "slickfin_greaves", Stat.AttackSpeed, ModifierOp.Additive, sliding ? 0.10f * fin : 0f);

        // Speed above walking pace. Walk speed is read without this item's own
        // contribution changing anything: the bonus is damage, not speed.
        int crest = N("wavecrest_fins");
        if (crest > 0)
        {
            float over = Mathf.Max(0f, p.PlanarVelocity.Length() - p.Stats.Get(Stat.MoveSpeed));
            SetMod(p, "wavecrest_fins", Stat.Damage, ModifierOp.Additive, Mathf.Min(0.30f, over * 0.015f) * crest);
        }
        else SetMod(p, "wavecrest_fins", Stat.Damage, ModifierOp.Additive, 0f);

        // Standing still.
        int anchor = N("anchored_stance");
        _still = p.PlanarVelocity.Length() < 0.4f ? _still + dt : 0f;
        bool braced = anchor > 0 && _still >= 1f;
        SetMod(p, "anchored_stance", Stat.Armor, ModifierOp.Flat, braced ? 8f * anchor : 0f);
        SetMod(p, "anchored_stance", Stat.Damage, ModifierOp.Additive, braced ? 0.12f * anchor : 0f);

        // Gold held.
        int miser = N("misers_anchor");
        int gold = _main.Inventory?.Gold ?? 0;
        SetMod(p, "misers_anchor", Stat.Damage, ModifierOp.Additive, miser * Mathf.Min(0.25f, Mathf.Floor(gold / 25f) * 0.01f));

        // Timed buffs.
        if (_frenzyLeft > 0f && (_frenzyLeft -= dt) <= 0f) _frenzy = 0;
        SetMod(p, "pyre_totem", Stat.AttackSpeed, ModifierOp.Additive, _frenzy * 0.02f);

        if (_wrathLeft > 0f) _wrathLeft -= dt;
        SetMod(p, "wrathful_conch", Stat.Damage, ModifierOp.Additive, _wrathLeft > 0f ? 0.15f * N("wrathful_conch") : 0f);

        if (_inkLeft > 0f) _inkLeft -= dt;
        SetMod(p, "ink_sac", Stat.MoveSpeed, ModifierOp.Additive, _inkLeft > 0f ? 0.30f : 0f);

        if (_capeLeft > 0f) _capeLeft -= dt;
        SetMod(p, "spindrift_cape", Stat.AttackSpeed, ModifierOp.Additive, _capeLeft > 0f ? 0.15f * N("spindrift_cape") : 0f);

        SetMod(p, "scrimshaw_tally", Stat.Damage, ModifierOp.Additive, 0.01f * _notches * N("scrimshaw_tally"));
    }

    // ------------------------------------------------------------- events

    private void OnEnemyDamaged(EnemyDamaged e)
    {
        if (_reentry || e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f || e.Target is null) return;
        if (IsProc(e.Info)) return;
        DamageInfo info = e.Info;
        Vector3 at = e.Target.Position;
        float pc = info.ProcCoefficient;
        bool killed = !e.Target.IsAlive;

        int limpet = N("sparking_limpet");
        if (limpet > 0 && _rng.Chance(0.07f * limpet * pc))
        {
            IDamageable? next = NearestOther(at, 6f, e.Target);
            if (next is not null)
            {
                Projectile.SpawnStreak(at + Vector3.Up * 0.5f, next.Position + Vector3.Up * 0.5f, 0.12f, new Color(0.95f, 0.92f, 0.45f));
                Blast(next.Position, 0.3f, info.Amount * 0.6f, "sparking_limpet", null, 1);
            }
        }

        int rip = N("riptooth_charm");
        if (rip > 0 && !killed && e.Target is Enemy en && en.HealthFraction > 0.8f)
            Blast(at, 0.1f, info.Amount * 0.20f * rip, "riptooth_charm", null, 1);

        int locket = N("brine_bomb_locket");
        if (locket > 0 && _rng.Chance(0.05f * locket * pc))
        {
            Blast(at, 2.5f, info.Amount * 0.8f, "brine_bomb_locket", e.Target);
            ExplosionFx.Play(at, 2.5f, new Color(0.40f, 0.86f, 0.70f));
        }

        int coral = N("shatterglass_coral");
        if (coral > 0 && info.Crit)
            Blast(at, 2.5f, info.Amount * 0.35f * coral, "shatterglass_coral", e.Target);

        int keg = N("powder_keg");
        if (keg > 0 && killed && _rng.Chance(0.15f))
        {
            Blast(at, 3f, Mathf.Max(10f, info.Amount * 0.6f * keg), "powder_keg");
            ExplosionFx.Play(at, 3f, new Color(0.95f, 0.50f, 0.25f));
        }
    }

    private IDamageable? NearestOther(Vector3 from, float radius, IDamageable skip)
    {
        int n = Targeting.Overlap(from, radius, Buffer);
        IDamageable? best = null;
        float bestSq = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            IDamageable t = Buffer[i];
            if (ReferenceEquals(t, skip) || !t.IsAlive) continue;
            Vector3 d = t.Position - from;
            d.Y = 0f;
            if (d.LengthSquared() < bestSq) { bestSq = d.LengthSquared(); best = t; }
        }
        return best;
    }

    private void OnEnemyKilled(EnemyKilled e)
    {
        if (e.XpValue <= 0 || LivePlayer is not { } p) return;   // boss-chest drops publish a kill with no xp
        _kills++;

        int lantern = N("wraith_lantern");
        if (lantern > 0) p.Health?.Heal(0.6f * lantern);

        if (N("scrimshaw_tally") > 0 && _kills % 40 == 0 && _notches < 30) _notches++;

        int scale = N("gilded_scale");
        if (scale > 0 && e.WasElite) _main.Inventory?.GrantGold(15 * scale);

        int pyre = N("pyre_totem");
        if (pyre > 0)
        {
            _frenzy = Mathf.Min(5 * pyre, _frenzy + 1);
            _frenzyLeft = 3f * Dur(p);
        }

        if (N("eye_of_the_maelstrom") > 0 && _kills % 10 == 0)
        {
            int level = _main.Inventory?.Level ?? 1;
            int n = Targeting.Overlap(p.CenterPosition, 12f, Buffer);
            int struck = 0;
            var targets = new List<IDamageable>(5);
            for (int i = 0; i < n && struck < 5; i++, struck++) targets.Add(Buffer[i]);
            foreach (IDamageable t in targets)
            {
                Projectile.SpawnStreak(t.Position + new Vector3(0.3f, 6f, 0f), t.Position, 0.18f, new Color(0.70f, 0.70f, 1.00f), 0.2f);
                Blast(t.Position, 0.2f, 40f + 4f * level, "eye_of_the_maelstrom", null, 1);
            }
        }
    }

    private void OnPlayerDamaged(PlayerDamaged e)
    {
        if (e.Info is null || e.Info.Nullified || e.Info.Amount <= 0f || LivePlayer is not { } p) return;
        Vector3 at = p.CenterPosition;

        int urchin = N("urchin_shell");
        if (urchin > 0) Blast(at, 3f, 15f * urchin, "urchin_shell");

        int ink = N("ink_sac");
        if (ink > 0)
        {
            p.Health?.GrantInvulnerability(PlayerHealth.InvulnerabilityWindow + 0.3f * ink);
            _inkLeft = 2f * Dur(p);
        }

        if (N("wrathful_conch") > 0) _wrathLeft = 4f * Dur(p);

        if (p.Health is not { } health) return;

        // Last chance first: a killing blow is the one case the buoy cannot help.
        if (e.RemainingHealth <= 0f && N("mariners_last_wish") > 0 && _wishReady <= 0f)
        {
            _wishReady = 120f;
            health.HealFraction(0.5f);
            health.GrantInvulnerability(2f);
            EventBus.Publish(new PopupRequested(at + Vector3.Up * 1.2f, "NOT YET", ProcColor, 1.3f));
            return;
        }

        int buoy = N("lifebuoy");
        if (buoy > 0 && _lifebuoyReady <= 0f && e.RemainingHealth > 0f && e.RemainingHealth < health.Max * 0.25f)
        {
            _lifebuoyReady = Mathf.Max(15f, 60f - 15f * (buoy - 1));
            health.HealFraction(0.30f);
            EventBus.Publish(new PopupRequested(at + Vector3.Up * 1.2f, "AFLOAT", ProcColor, 1.1f));
        }
    }

    private void OnPlayerMoved(PlayerMoved e)
    {
        if (LivePlayer is not { } p) return;
        if (e.Verb == MoveVerb.Dash && N("spindrift_cape") > 0) _capeLeft = 1.5f * Dur(p);

        int weights = N("plunge_weights");
        if (weights > 0 && (e.Verb == MoveVerb.SlamImpact || (e.Verb == MoveVerb.Land && e.Strength >= 0.5f)))
        {
            Blast(e.Position, 3f, 20f * weights, "plunge_weights");
            ExplosionFx.Play(e.Position, 3f, new Color(0.70f, 0.70f, 0.78f));
        }
    }

    private void OnChestOpened(ChestOpened e)
    {
        int bell = N("diving_bell");
        if (bell > 0) LivePlayer?.Health?.HealFraction(0.10f * bell);
    }

    private void OnShrineUsed(MapShrineUsed e)
    {
        if (e.Outcome is not (ShrineOutcome.Blessed or ShrineOutcome.ChallengeWon)) return;
        int candle = N("votive_candle");
        if (candle <= 0) return;
        _main.Inventory?.GrantGold(25 * candle);
        LivePlayer?.Health?.HealFraction(0.05f * candle);
    }

    private void OnGoldGained(GoldGained e)
    {
        if (e.Amount <= 0 || _reentry) return;
        int glove = N("coin_divers_glove");
        if (glove > 0) LivePlayer?.Health?.Heal(0.5f * glove);

        if (N("tithe_of_the_deep") > 0)
        {
            _reentry = true;
            try { _main.Inventory?.GrantXp(e.Amount); }
            finally { _reentry = false; }
        }
    }
}
