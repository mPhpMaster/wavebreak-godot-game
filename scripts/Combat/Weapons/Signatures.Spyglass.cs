using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

public static partial class Signatures
{
    // ========================================================= SPYGLASS GLINT
    //
    // The precise one. A thin instant beam along the line to one target. The
    // target is whatever you are looking at: the enemy closest to the centre of
    // the camera's view inside a narrow sight cone (elites and bosses first).
    // A sighted shot is a BULLSEYE: a guaranteed crit on the first body. The
    // first body also takes more the farther away it is, and each body the beam
    // passes through makes it hit the next one harder. With nothing in the
    // sight the beam still fires, at the nearest enemy, without the bullseye.

    private const float SightDegrees = 13f;
    private const float SightDegreesSteady = 19f;
    private const float LongShotPerMetre = 0.025f;   // past 4 m, up to +60%
    private const float LongShotMax = 0.60f;
    private const float Collateral = 0.15f;          // per body already pierced
    private const float RicochetRange = 12f;
    private const float RicochetScale = 0.7f;
    private static readonly Color GlintGold = new(1.00f, 0.88f, 0.45f);

    private static void FireSpyglass(WeaponRuntime w, WeaponSystem sys, StatSheet s)
    {
        WeaponDef d = w.Def;
        Vector3 origin = sys.MuzzlePosition;
        float range = Reach(w);
        int shots = Shots(w, s);
        bool steady = w.Level >= MilestoneLevel;
        bool sunGlint = w.Level >= d.MaxLevel;
        int pierce = d.Pierce + w.Bonus.Pierce + (steady ? 1 : 0);
        float width = MathF.Max(0.25f, d.Radius * MathF.Min(Area(w, s), 2f));
        float sight = MathF.Cos(Mathf.DegToRad(steady ? SightDegreesSteady : SightDegrees));
        Vector3 look = Projectile.FlatDirection(sys.AimDirection, Vector3.Forward);

        // Candidates: sighted ones first (elites/bosses, then nearest the
        // centre of the view), then the rest by distance for the fallback.
        IReadOnlyList<IDamageable> live = Targeting.Live;
        var picks = new List<(IDamageable T, bool Sighted)>(shots);
        for (int k = 0; k < shots; k++)
        {
            IDamageable? best = null;
            float bestScore = float.MinValue;
            bool bestSighted = false;
            for (int i = 0; i < live.Count; i++)
            {
                IDamageable t = live[i];
                if (!t.IsAlive || picks.Exists(p => ReferenceEquals(p.T, t))) continue;
                Vector3 to = t.Position - origin;
                to.Y = 0f;
                float dist = to.Length();
                if (dist > range || dist < 0.05f) continue;
                float cos = to.Dot(look) / dist;
                bool sighted = cos >= sight;
                float score = sighted
                    ? 1000f + (t is Enemy { IsBoss: true } or Enemy { IsElite: true } ? 500f : 0f) + cos * 100f
                    : -dist;
                if (score > bestScore) { bestScore = score; best = t; bestSighted = sighted; }
            }
            if (best is null) break;
            picks.Add((best, bestSighted));
        }

        if (picks.Count == 0)
        {
            // Nothing in range: a glint down the line of sight, so the weapon
            // never looks idle between waves.
            Projectile.SpawnStreak(origin, origin + look * range * 0.6f, 0.08f, d.Tint, 0.1f);
            Cue(SignatureSound.SpyglassCast, origin);
            sys.NoteShots(1);
            return;
        }

        bool anyBullseye = false;
        foreach (var (target, sighted) in picks)
        {
            Vector3 dir = Projectile.FlatDirection(target.Position - origin, look);
            DamageInfo template = sys.BuildDamage(w);
            Beam(sys, origin, dir, range, width, pierce, template, s, sighted, out Vector3 end,
                            out IDamageable? first, out bool killedFirst);
            Projectile.SpawnStreak(origin, end, 0.11f, d.Tint, 0.13f);
            if (first is not null)
            {
                Vector3 at = first.Position + Vector3.Up * 0.7f;
                SignatureFx.Add(new SignatureFx.Flash(at, sighted ? 0.75f : 0.4f, sighted ? GlintGold : d.Tint, sighted ? 0.32f : 0.2f));
                if (sighted) anyBullseye = true;
                if (sighted && killedFirst && sunGlint) Ricochet(sys, w, s, first, template);
            }
        }

        Cue(anyBullseye ? SignatureSound.SpyglassBullseye : SignatureSound.SpyglassCast, origin);
        sys.NoteShots(picks.Count);
    }

    /// <summary>Hit up to 1 + <paramref name="pierce"/> bodies along the line,
    /// nearest first. Returns how many were hit.</summary>
    private static int Beam(WeaponSystem sys, Vector3 origin, Vector3 dir, float range, float width, int pierce,
                            DamageInfo template, StatSheet s, bool bullseye, out Vector3 end,
                            out IDamageable? first, out bool killedFirst)
    {
        Line.Clear();
        IReadOnlyList<IDamageable> live = Targeting.Live;
        float reach = width + Targeting.AssumedTargetRadius;
        for (int i = 0; i < live.Count; i++)
        {
            IDamageable t = live[i];
            if (!t.IsAlive) continue;
            Vector3 rel = t.Position - origin;
            rel.Y = 0f;
            float along = rel.Dot(dir);
            if (along < -0.3f || along > range) continue;
            if ((rel - dir * along).Length() > reach) continue;
            Line.Add((t, along));
        }
        Line.Sort((a, b) => a.Along.CompareTo(b.Along));

        int max = Math.Min(Line.Count, 1 + Math.Max(0, pierce));
        first = null;
        killedFirst = false;
        end = origin + dir * range;
        int landed = 0;
        for (int i = 0; i < max; i++)
        {
            IDamageable t = Line[i].T;
            DamageInfo info = template.Clone();
            if (i == 0)
            {
                float longShot = Mathf.Clamp((Line[i].Along - 4f) * LongShotPerMetre, 0f, LongShotMax);
                info.Amount *= 1f + longShot;
                if (bullseye) ForceCrit(info, s);
            }
            else info.Amount *= 1f + Collateral * landed;

            info.Direction = dir;
            t.TakeDamage(info);
            sys.ReportHit(t, info);
            Count(info);
            if (i == 0) { first = t; killedFirst = !t.IsAlive; }
            if (!info.Nullified) landed++;
        }
        // Stop the drawn beam at the last body when the pierce ran out.
        if (max > 0 && max < Line.Count) end = origin + dir * (Line[max - 1].Along + 0.4f);
        return landed;
    }

    /// <summary>Sun Glint (max level): a bullseye kill reflects the beam into
    /// the farthest enemy within 12 m of the body.</summary>
    private static void Ricochet(WeaponSystem sys, WeaponRuntime w, StatSheet s, IDamageable from, DamageInfo template)
    {
        Vector3 at = from.Position;
        IReadOnlyList<IDamageable> live = Targeting.Live;
        IDamageable? far = null;
        float farD = 0f;
        for (int i = 0; i < live.Count; i++)
        {
            IDamageable t = live[i];
            if (!t.IsAlive || ReferenceEquals(t, from)) continue;
            float dd = FlatDistance(t.Position, at);
            if (dd <= RicochetRange && dd > farD) { farD = dd; far = t; }
        }
        if (far is null) return;
        DamageInfo info = template.Clone();
        ForceCrit(info, s);
        Strike(sys, far, info, at, RicochetScale);
        Vector3 a = at + Vector3.Up * 0.7f, b = far.Position + Vector3.Up * 0.7f;
        Projectile.SpawnStreak(a, b, 0.09f, GlintGold, 0.16f);
        SignatureFx.Add(new SignatureFx.Flash(b, 0.5f, GlintGold, 0.25f));
        Cue(SignatureSound.SpyglassRicochet, b);
    }

}
