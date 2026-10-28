using Godot;
using Wavebreak.Combat;
using Wavebreak.Combat.Weapons;

namespace Wavebreak.Data;

public static partial class ContentAudit
{
    private static void AuditStormBlades(WeaponSystem system)
    {
        WeaponDef definition = Catalog.WeaponById(Signatures.Kite)!;
        Vector3 muzzle = system.MuzzlePosition;
        var elevated = new FakeTarget(muzzle + new Vector3(6f, 4f, 0f));
        var below = new FakeTarget(muzzle + new Vector3(6f, -8f, 0f));
        var outside = new FakeTarget(muzzle + new Vector3(60f, 0f, 0f));
        var runtime = new WeaponRuntime(definition);
        try
        {
            Targeting.Source = targets =>
            {
                targets.Add(elevated); targets.Add(below); targets.Add(outside);
                return targets.Count;
            };
            Targeting.Refresh();
            SignatureFx.Clear();
            Check(Signatures.TryFire(runtime, system, system.Stats), "storm blades use signature delivery");
            for (int frame = 0; frame < 18; frame++) Projectile.UpdateAll(1f / 60f);
            Check(elevated.Hits == 2, "stationary twin blades auto-aim at elevated enemy on outward pass");
            for (int frame = 0; frame < 150; frame++) Projectile.UpdateAll(1f / 60f);
            Check(elevated.Hits == 4, "both blades hit again on return without repeat damage per pass");
            Check(below.Hits == 0 && outside.Hits == 0, "blades respect full 3D separation and reach");
            Check(SignatureFx.LiveCount == 0, "returning blades clean up after reaching the owner");
            float firstDamage = elevated.Damage;
            while (runtime.Level < definition.MaxLevel && runtime.LevelUp()) { }
            Targeting.Refresh();
            Signatures.TryFire(runtime, system, system.Stats);
            for (int frame = 0; frame < 180; frame++) Projectile.UpdateAll(1f / 60f);
            Check(elevated.Damage > firstDamage * 2f, "upgraded storm blades increase damage");
            Line($"storm-blades stationary/elevated/outward/return/cleanup passed damage={firstDamage:0.0}");
        }
        finally
        {
            runtime.Dispose();
            SignatureFx.Clear();
            Targeting.Source = null;
            Targeting.Refresh();
        }
    }
}
