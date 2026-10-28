using System;
using System.Collections.Generic;
using System.Linq;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Data;
using Godot;

namespace Wavebreak.Game;

internal static class LeviathanVerification
{
    internal static void Combat(Player player, Enemy boss)
    {
        float health = boss.Health;
        player.Teleport(boss.AimPoint + Vector3.Right * (boss.Def!.Radius + 6) - Vector3.Up * .9f);
        WeaponSystem system = Main.Instance!.Weapons;
        system.Tick(.0001f); Targeting.Refresh();
        var weapon = new WeaponRuntime(Catalog.WeaponById("reef_shotgun")!);
        weapon.Tick(.01f, system);
        for (int i = 0; i < 60; i++) Projectile.UpdateAll(1f / 60);
        weapon.Dispose(); Projectile.ClearLiveCombat();
        if (boss.Health >= health) throw new InvalidOperationException("A real shotgun must hit the enlarged boss body from six metres outside its surface.");
        var targets = new List<IDamageable>();
        Targeting.Overlap(boss.AimPoint + Vector3.Right * (boss.Def.Radius - .2f), .2f, targets);
        if (!targets.Contains(boss)) throw new InvalidOperationException("The boss body surface is missing from overlap hit tests.");
        GD.Print("LEVIATHAN OK actual shotgun damages the enlarged body surface");
        if (boss.Def.BossMoves.Any(move => move.MaxRange < boss.Def.Radius + 18))
            throw new InvalidOperationException("Boss attacks cannot acquire a player outside the enlarged collider.");
        var receiver = new ExpeditionWeaponTarget { Position = boss.AimPoint + new Vector3(25, 4, 0) };
        boss.ForceMove(1, 0);
        for (int i = 0; i < 400 && receiver.Hits == 0; i++)
        {
            boss.Step(1f / 60, receiver.Position - Vector3.Up * .9f, receiver, Vector3.Zero);
            EnemyAttacks.Tick(1f / 60, receiver.Position - Vector3.Up * .9f, receiver);
        }
        EnemyAttacks.Clear();
        if (receiver.Hits == 0) throw new InvalidOperationException("The huge Leviathan's volley must hit a swimmer at body height.");
        GD.Print("LEVIATHAN OK scaled ranged attack acquires and hits a swimmer outside its collider in 3D");
    }
}
