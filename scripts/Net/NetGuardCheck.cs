using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Net;

/// <summary>--check-netguard: the host's limits on client input, with no network.
/// Prints NETGUARD lines and NETGUARD PASS/FAIL.</summary>
public static class NetGuardCheck
{
    public static bool Requested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-netguard") >= 0;

    public static bool Run()
    {
        int checks = 0;
        var failed = new List<string>();
        void Expect(string what, bool ok)
        {
            checks++;
            GD.Print($"NETGUARD {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failed.Add(what);
        }

        // Hits: never in the way of a real one.
        Expect("an ordinary hit is untouched", HitGuard.ClampAmount(250f, 80f) == 250f);
        Expect("a huge hit on a huge boss is untouched", HitGuard.ClampAmount(5e7f, 1e6f) == 5e7f);
        Expect("overkill through a 95% block still kills", HitGuard.ClampAmount(1e9f, 2e6f) * 0.05f >= 2e6f);
        Expect("absurd damage is capped by the target", HitGuard.ClampAmount(1e30f, 100f) == HitGuard.MinHitCap);
        Expect("NaN, infinity and negative damage are nothing",
               HitGuard.ClampAmount(float.NaN, 100f) == 0f && HitGuard.ClampAmount(float.PositiveInfinity, 100f) == 0f && HitGuard.ClampAmount(-5f, 100f) == 0f);
        Expect("knockback and status times stay in range",
               HitGuard.ClampKnockback(1e9f) == HitGuard.MaxKnockback && HitGuard.ClampKnockback(float.NaN) == 0f
               && HitGuard.ClampStatus(1e9f) == HitGuard.MaxStatusSeconds && HitGuard.ClampStatus(-1f) == 0f);
        Expect("a direction is at most unit length and finite",
               Mathf.IsEqualApprox(HitGuard.CleanDirection(300f, 400f).Length(), 1f) && HitGuard.CleanDirection(float.NaN, 1f) == Vector3.Zero
               && HitGuard.CleanDirection(0.2f, 0f).X == 0.2f);
        Expect("ids: whole numbers among those handed out",
               HitGuard.ValidId(1f, 2) && HitGuard.ValidId(499f, 500) && !HitGuard.ValidId(0f, 500) && !HitGuard.ValidId(-5f, 500)
               && !HitGuard.ValidId(500f, 500) && !HitGuard.ValidId(1.5f, 500) && !HitGuard.ValidId(float.NaN, 500) && !HitGuard.ValidId(float.PositiveInfinity, 500));
        Expect("a batch is whole records of a sane size",
               HitGuard.ValidBatch(90, 9) && !HitGuard.ValidBatch(91, 9) && !HitGuard.ValidBatch((HitGuard.MaxHitsPerSecond + 1) * 9, 9));

        // Rates.
        var guard = new HitGuard();
        int allowed = 0;
        for (int i = 0; i < HitGuard.MaxHitsPerSecond + 500; i++) if (guard.TakeHit(10.0)) allowed++;
        Expect($"hits stop at the second's count ({allowed})", allowed == HitGuard.MaxHitsPerSecond);
        Expect("the next second is a new count", guard.TakeHit(11.1));

        var budget = new HitGuard();
        float reads = 0f;
        float Field(float health) { reads++; return health; }
        Expect("the flat floor pays for an ordinary second without reading the field",
               budget.TakeDamage(HitGuard.MinDamagePerSecond, 20.0, () => Field(0f)) && reads == 0f);
        Expect("past the floor, a field with health left pays", budget.TakeDamage(1e6f, 20.0, () => Field(1e9f)) && reads == 1f);
        Expect("past the floor and the field, damage stops", !budget.TakeDamage(1e6f, 20.0, () => Field(100f)));
        Expect("the next second is a new budget", budget.TakeDamage(1e6f, 21.1, () => Field(100f)));

        GD.Print(failed.Count == 0 ? $"NETGUARD PASS checks={checks}" : $"NETGUARD FAIL checks={checks} failed={failed.Count}");
        return failed.Count == 0;
    }
}
