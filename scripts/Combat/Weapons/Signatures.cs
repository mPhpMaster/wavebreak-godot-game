using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// Signature weapons: one per hero of the heroes trio (Meta/Characters.Heroes.cs).
///
/// A signature is an ordinary <see cref="WeaponDef"/> in the catalogue (codex,
/// icons, quests, the stat sheet and rolled upgrades all treat it like any
/// other weapon) with three differences:
///
///   * only its owner starts with it, and the offer pool never offers it as a
///     new weapon (<see cref="Progression.OfferPool"/>), so nobody else can hold
///     it; held, it levels through the usual rolled upgrade cards;
///   * it has two milestones of its own, at <see cref="MilestoneLevel"/> and at
///     max level, named on the card that reaches them (<see cref="MilestoneLine"/>);
///   * it is delivered here rather than by the shape switch, and announces its
///     own sounds (the generic shape sound is swallowed, see <see cref="ConsumeFired"/>).
/// </summary>
public static partial class Signatures
{
    public const string Spyglass = "spyglass_glint";
    public const string Anchor = "mooring_anchor";
    public const string Kite = "skimmer_kite";

    public const int MilestoneLevel = 4;

    /// <summary>Weapon id -> the character it belongs to.</summary>
    public static readonly IReadOnlyDictionary<string, string> Owner = new Dictionary<string, string>
    {
        [Spyglass] = "sunny",
        [Anchor] = "biscuit",
        [Kite] = "flick",
    };

    public static bool Is(string? weaponId) => weaponId is not null && Owner.ContainsKey(weaponId);

    /// <summary>"Lv 4 — Steady Lens: ..." / "Lv 8 — ..." text per weapon, shown
    /// on the card that reaches the level and in the codex.</summary>
    public static (string Name, string Text) Milestone(string id, bool max) => (id, max) switch
    {
        (Spyglass, false) => ("Steady Lens", "+1 pierce, and the sight cone widens."),
        (Spyglass, true) => ("Sun Glint", "A bullseye kill reflects the beam into the farthest enemy nearby."),
        (Anchor, false) => ("Bosun's Knot", "The haul stuns everything still moored."),
        (Anchor, true) => ("Deep Mooring", "The haul drags the moored together, then the anchor slams down again."),
        (Kite, false) => ("Crosswind", "Returning blades deal 50% more damage."),
        (Kite, true) => ("Storm Blades", "Each volley gains a third blade and lightning damage."),
        _ => ("", ""),
    };

    /// <summary>The extra card line when <paramref name="nextLevel"/> is a milestone, else "".</summary>
    public static string MilestoneLine(WeaponDef def, int nextLevel)
    {
        if (!Is(def.Id)) return "";
        bool max = nextLevel >= def.MaxLevel;
        if (!max && nextLevel != MilestoneLevel) return "";
        var (name, text) = Milestone(def.Id, max);
        return "\n" + Meta.Loc.F("★ {0}: {1}", name, text);
    }

    // ------------------------------------------------------------ dispatch

    private static bool _lastWasSignature;

    /// <summary>True once after a signature weapon fired; the audio director
    /// asks this before playing the generic shape sound.</summary>
    public static bool ConsumeFired()
    {
        bool was = _lastWasSignature;
        _lastWasSignature = false;
        return was;
    }

    internal static bool TryFire(WeaponRuntime w, WeaponSystem sys, StatSheet s)
    {
        _lastWasSignature = false;
        switch (w.Def.Id)
        {
            case Spyglass: FireSpyglass(w, sys, s); break;
            case Anchor: FireAnchor(w, sys, s); break;
            case Kite: FireStormBlades(w, sys, s); break;
            default: return false;
        }
        _lastWasSignature = true;
        return true;
    }

    internal static void Cue(SignatureSound sound, Vector3 at) => Core.EventBus.Publish(new SignatureCue(sound, at));

    // ------------------------------------------------------------- shared

    private static readonly List<IDamageable> Buffer = new(64);
    private static readonly List<(IDamageable T, float Along)> Line = new(32);
    private static readonly Core.Rng FxRng = new(0x51C7_A7E5UL);

    private static int Shots(WeaponRuntime w, StatSheet s)
        => Math.Max(1, w.Def.Projectiles + w.Bonus.Projectiles + (int)s.Get(Stat.ProjectileCount));

    private static float Area(WeaponRuntime w, StatSheet s) => s.Get(Stat.AreaSize) * (1f + w.Bonus.Area);
    private static float Reach(WeaponRuntime w) => w.Def.Range * (1f + w.Bonus.Range);
    private static float Duration(StatSheet s) => MathF.Max(0.25f, s.Get(Stat.Duration));
    private static Vector3 Ground(Vector3 muzzle) => muzzle - Vector3.Up * FxGuard.MuzzleHeight;

    /// <summary>Apply a hit and report it, the same way every other weapon does
    /// (co-op forwards it from inside TakeDamage).</summary>
    internal static DamageInfo Strike(WeaponSystem sys, IDamageable t, DamageInfo template, Vector3 from, float scale = 1f)
    {
        DamageInfo info = template.Clone();
        info.Amount *= scale;
        info.Direction = Projectile.FlatDirection(t.Position - from, Vector3.Forward);
        t.TakeDamage(info);
        sys.ReportHit(t, info);
        Count(info);
        return info;
    }

    /// <summary>Signature hits that landed this run (the co-op test reads it).</summary>
    public static int Hits { get; private set; }

    internal static void ResetHits() => Hits = 0;

    private static void Count(DamageInfo info)
    {
        if (!info.Nullified && info.Amount > 0f) Hits++;
    }

    private static void ForceCrit(DamageInfo info, StatSheet s)
    {
        if (info.Crit) return;
        info.Crit = true;
        info.Amount *= 1f + s.Get(Stat.CritDamage);
    }

    /// <summary>Nearest live target from this frame's target set (which the
    /// audit's dummies replace), unlike <see cref="Targeting.Nearest"/>.</summary>
    private static IDamageable? NearestLive(Vector3 from, float range)
    {
        IReadOnlyList<IDamageable> live = Targeting.Live;
        IDamageable? best = null;
        float bestD = range;
        for (int i = 0; i < live.Count; i++)
        {
            IDamageable t = live[i];
            if (!t.IsAlive) continue;
            float d = FlatDistance(t.Position, from);
            if (d <= bestD) { bestD = d; best = t; }
        }
        return best;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.Y = 0f; b.Y = 0f;
        return a.DistanceTo(b);
    }

    internal static void ForgetRigs() { }
}
