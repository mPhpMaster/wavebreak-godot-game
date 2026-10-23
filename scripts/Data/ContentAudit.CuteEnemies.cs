using System;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>
/// The cute wave in the audit. The generic enemy audit already builds, hurts
/// and probes every one of them (the four new behaviours connect through
/// <see cref="EnemyAttacks"/> like any other); this adds what only these have:
/// roster size and pacing, theme tags, their hand-built models, and live
/// probes of the passive traits — a mimic stays a pot until you come close, a
/// tether stings, slime slows, the brood hatches, a bubble holds the real
/// player and lets go cleanly, and the gloom dims and lifts.
/// </summary>
public static partial class ContentAudit
{
    /// <summary>--showcase-cute: the live showcase stands the cute wave in its
    /// arc instead of the behaviour monsters. Null when not asked for.</summary>
    private static string[]? ShowcaseEnemyIds()
        => OS.GetCmdlineUserArgs().Contains("--showcase-cute")
            // Three Linklings side by side, so the tethers have someone to link to.
            ? Catalog.CuteEnemyIds.SelectMany(id => id == "linkling" ? new[] { id, id, id } : new[] { id }).ToArray()
            : null;

    private static void AuditCute(WeaponSystem system)
    {
        string[] ids = Catalog.CuteEnemyIds;
        Line($"cute enemies={Catalog.Enemies.Count} wave={ids.Length}");
        Check(Catalog.Enemies.Count >= 53, $"only {Catalog.Enemies.Count} enemies (want 53)");
        Check(Catalog.Enemies.Select(e => e.Id).Distinct().Count() == Catalog.Enemies.Count, "duplicate enemy ids");
        foreach (EnemyDef e in Catalog.Enemies)
            Check(e.Themes.Length > 0, $"enemy {e.Id} has no theme tags");

        Visuals.BehaviourModels.EnsureRegistered();
        foreach (string id in ids)
        {
            EnemyDef? def = Catalog.EnemyById(id);
            Check(def is not null, $"cute enemy {id} missing");
            if (def is null) continue;
            // The opening is tuned against the original roster: nothing new
            // may appear in the first three minutes.
            Check(def.UnlockAtSeconds >= 170f, $"cute {id} unlocks at {def.UnlockAtSeconds}s (opening must stay as it was)");
            Check(def.HasCuteTraits, $"cute {id} has no cute behaviour or trait");
            Check(Visuals.CuteModels.Has(def), $"cute {id} has no hand-built model");
            Check(def.WindUp >= 0.4f || def.Behavior == EnemyBehavior.Chaser, $"cute {id} wind-up {def.WindUp} too short to read");
            if (def.Behavior == EnemyBehavior.Bubbler)
                Check(def.AbilitySpeed is > 2f and <= 6f && def.HoldSeconds is > 0f and <= 1.5f, $"bubbler {id} speed/hold not fair");
            if (def.Behavior is EnemyBehavior.Mimic or EnemyBehavior.Puffer)
                Check(def.AbilitySpeed > def.MoveSpeed * 2f && def.AbilityRange >= 4f, $"lunger {id} speed/range");
            if (def.TrailSlow > 0f) Check(def.TrailSlow <= 0.5f, $"trail {id} slows more than half");
            if (def.BroodCount > 0) Check(Catalog.EnemyById(def.BroodId) is { IsBoss: false, IsElite: false }, $"brood {id} lays unknown '{def.BroodId}'");
            Line($"cute {id,-14} {def.Behavior,-8} unlock={def.UnlockAtSeconds:0}s cost={def.Cost} themes={string.Join("/", def.Themes)}");
        }

        if (EnemyManager.Instance is not { } em)
        {
            Check(false, "cute: no enemy manager");
            return;
        }
        Vector3 feet = system.MuzzlePosition + new Vector3(60f, -200f, 0f);
        try
        {
            ProbeMimic(em, feet);
            ProbeTether(em, feet);
            ProbeSlime(em, feet);
            ProbeBrood(em, feet);
            ProbeHolds();
        }
        finally
        {
            EnemyAttacks.Clear();
            em.DespawnBelow(-100f);
        }
    }

    private static Enemy? Place(EnemyManager em, string id, Vector3 at, float damage = 1f)
    {
        if (Catalog.EnemyById(id) is not { } def || em.SpawnAt(def, at) is not { } e) return null;
        e.Configure(def, 1f, damage, 1f);
        e.Spawn(at);
        return e;
    }

    private static void ProbeMimic(EnemyManager em, Vector3 feet)
    {
        Enemy? m = Place(em, "crock_mimic", feet + new Vector3(9f, 0f, 0f), 0f);
        if (m is null) { Check(false, "mimic probe: could not spawn"); return; }
        var dummy = new FakeTarget(feet + new Vector3(0f, 0.9f, 0f));
        Vector3 start = m.GlobalPosition;
        for (int i = 0; i < 60; i++) m.Step(1f / 60f, feet, dummy, Vector3.Zero);
        bool sat = m.IsDormant && m.GlobalPosition.DistanceTo(start) < 0.05f;
        Vector3 near = m.GlobalPosition + new Vector3(-3f, 0f, 0f);
        for (int i = 0; i < 3; i++) m.Step(1f / 60f, near, dummy, Vector3.Zero);
        bool woke = !m.IsDormant;
        m.Despawn();
        Check(sat && woke, $"mimic: dormant-and-still={sat} woke-when-near={woke}");
        Line($"behaviour crock_mimic       Disguise     {(sat && woke ? "ok" : "FAILED")} sat={sat} woke={woke}");
    }

    private static void ProbeTether(EnemyManager em, Vector3 feet)
    {
        Enemy? a = Place(em, "linkling", feet + new Vector3(-2.5f, 0f, 0f));
        Enemy? b = Place(em, "linkling", feet + new Vector3(2.5f, 0f, 0f));
        if (a is null || b is null) { Check(false, "tether probe: could not spawn"); return; }
        // The dummy stands on the line between them, out of contact reach.
        Vector3 mid = feet + new Vector3(0f, 0f, 0f);
        var dummy = new FakeTarget(mid + new Vector3(0f, 0.9f, 0f));
        int connects = EnemyAttacks.Connects;
        bool linked = false;
        for (int i = 0; i < 120 && EnemyAttacks.Connects == connects; i++)
        {
            // Hold them in place: the probe is about the link, not the walk.
            // Height too: linklings hover, and out here (no floor under the
            // probe) how far they sink in two seconds depended on unrelated
            // timing, which decided whether the dummy was "under the knees".
            float y = feet.Y + (a.Def?.Hover ?? 0f);
            a.GlobalPosition = new Vector3(feet.X - 2.5f, y, feet.Z);
            b.GlobalPosition = new Vector3(feet.X + 2.5f, y, feet.Z);
            a.Step(1f / 60f, mid, dummy, Vector3.Zero);
            b.Step(1f / 60f, mid, dummy, Vector3.Zero);
            EnemyAttacks.Tick(1f / 60f, mid, dummy);
            linked |= a.TetherPartner == b || b.TetherPartner == a;
        }
        bool stung = EnemyAttacks.Connects > connects;
        bool oneWay = !(a.TetherPartner == b && b.TetherPartner == a);
        a.Despawn();
        b.Despawn();
        Check(linked && stung && oneWay, $"tether: linked={linked} stung={stung} one-link-per-pair={oneWay}");
        Line($"behaviour linkling          Tether       {(linked && stung ? "ok" : "FAILED")} dummyDamage={dummy.Damage:0}");
    }

    private static void ProbeSlime(EnemyManager em, Vector3 feet)
    {
        Enemy? s = Place(em, "glimmer_snail", feet + new Vector3(-8f, 0f, 6f), 0f);
        if (s is null) { Check(false, "slime probe: could not spawn"); return; }
        var dummy = new FakeTarget(feet + new Vector3(0f, 0.9f, 0f));
        int before = CuteHazards.PuddlesDropped;
        Vector3 trailAt = Vector3.Zero;
        for (int i = 0; i < 150; i++)
        {
            s.Step(1f / 60f, feet, dummy, Vector3.Zero);
            EnemyAttacks.Tick(1f / 60f, feet, dummy);
            if (i == 140) trailAt = s.GlobalPosition;
        }
        int dropped = CuteHazards.PuddlesDropped - before;
        // Somewhere along the path it walked, the floor must now be slow.
        Vector3 path = (trailAt + (feet + new Vector3(-8f, 0f, 6f))) * 0.5f;
        float slowNear = 0f;
        for (int i = 0; i <= 10; i++)
            slowNear = MathF.Max(slowNear, CuteHazards.SlowAt((feet + new Vector3(-8f, 0f, 6f)).Lerp(trailAt, i / 10f)));
        s.Despawn();
        Check(dropped >= 2 && slowNear > 0.3f, $"slime: puddles={dropped} slow-on-trail={slowNear:0.##}");
        Line($"behaviour glimmer_snail     Slime        {(dropped >= 2 && slowNear > 0.3f ? "ok" : "FAILED")} puddles={dropped} slow={slowNear:0.##} at {path.X:0},{path.Z:0}");
    }

    private static void ProbeBrood(EnemyManager em, Vector3 feet)
    {
        Enemy? m = Place(em, "mother_crab", feet + new Vector3(0f, 0f, -12f), 0f);
        if (m is null || m.Def is not { } def) { Check(false, "brood probe: could not spawn"); return; }
        var dummy = new FakeTarget(feet + new Vector3(0f, 0.9f, 0f));
        int before = em.AliveCount;
        m.ForceBrood();
        m.Step(1f / 60f, feet, dummy, Vector3.Zero);
        int born = em.AliveCount - before;
        m.Despawn();
        Check(born == def.BroodCount, $"brood: laid {born}, expected {def.BroodCount}");
        Line($"behaviour mother_crab       Brood        {(born == def.BroodCount ? "ok" : "FAILED")} {born}x {def.BroodId}");
    }

    /// <summary>The holds go on the real player's stat sheet, so they get the
    /// residue test: trapped is near-still, and after a clear the sheet is
    /// exactly what it was.</summary>
    private static void ProbeHolds()
    {
        if (Player.Instance is not { } p || !p.IsAlive) { Line("cute holds: no live player, skipped"); return; }
        CuteHazards.Clear();
        float before = p.Stats.Get(Stat.MoveSpeed);
        Vector3 at = p.GlobalPosition;

        CuteHazards.Trap(p, 1f);
        CuteHazards.Tick(1f / 60f, at, p);
        float trapped = p.Stats.Get(Stat.MoveSpeed);

        CuteHazards.Clear();
        CuteHazards.Pinch(p, 1f, 0.45f);
        CuteHazards.Tick(1f / 60f, at, p);
        float pinched = p.Stats.Get(Stat.MoveSpeed);

        CuteHazards.NoteGloom(at + new Vector3(0f, 1f, -4f), 0.8f);
        CuteHazards.Tick(1f / 60f, at, p);
        float gloom = CuteHazards.GloomTarget;

        CuteHazards.Clear();
        float after = p.Stats.Get(Stat.MoveSpeed);
        bool ok = trapped < before * 0.2f && MathF.Abs(pinched - before * 0.55f) < 0.05f
                  && gloom > 0.3f && MathF.Abs(after - before) < 1e-4f && CuteHazards.Gloom == 0f;
        Check(ok, $"holds: speed {before:0.##} trapped={trapped:0.##} pinched={pinched:0.##} gloom={gloom:0.##} after={after:0.##}");
        Line($"cute holds {(ok ? "ok" : "FAILED")} speed={before:0.##} trapped={trapped:0.##} pinched={pinched:0.##} gloom={gloom:0.##} after={after:0.##}");
    }
}
