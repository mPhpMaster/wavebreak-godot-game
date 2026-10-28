using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Combat.Weapons;
using Wavebreak.Data;

namespace Wavebreak.Game;

public sealed partial class ExpeditionWeaponsCheck : Node
{
    private readonly Player _player;
    public ExpeditionWeaponsCheck(Player player) => _player = player;
    public override void _Ready() => _ = Run();
    private async Task Wait(float time) => await ToSignal(GetTree().CreateTimer(time, true, true), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool value, string label)
    {
        GD.Print($"EXPEDITION WEAPONS {(value ? "OK" : "FAIL")} {label}");
        if (!value) throw new InvalidOperationException(label);
    }
    private async Task Run()
    {
        try
        {
            await Wait(.7f); Main main = Main.Instance!; main.SetProcess(false);
            EnemyManager.Instance!.ClearField(999); EnemyManager.Instance.Quiet = true;
            _player.MoveOverride = static () => Vector3.Zero; _player.Teleport(new Vector3(0, 10, 0)); await Wait(.05f);
            WeaponSystem system = main.Weapons; system.Tick(.001f);
            var target = new ExpeditionWeaponTarget { Position = system.MuzzlePosition + new Vector3(0, 3, -6) };
            Targeting.Source = into => { into.Add(target); return 1; };
            var shotgun = new WeaponRuntime(Catalog.WeaponById("reef_shotgun")!);
            int shots = system.ShotsFired, recoil = _player.ShotgunRecoilBursts;
            bool bubbles = false;
            for (int frame = 0; frame < 60; frame++)
            { Targeting.Refresh(); shotgun.Tick(1f / 60, system); bubbles |= SignatureFx.LiveCount > 0; Projectile.UpdateAll(1f / 60); }
            Require(system.ShotsFired - shots == 6, "shotgun fires six pellets and waits during the first second");
            Require(target.Hits > 0, "pellets hit a target above the swimmer in three dimensions");
            Require(_player.ShotgunRecoilBursts == recoil + 1 && bubbles, "one shot applies recoil and a bubble burst");
            await Wait(.06f);
            Require(_player.Velocity.Dot(new Vector3(0, 3, -6).Normalized()) < -1, "recoil visibly pushes the swimmer opposite the shot");
            for (int frame = 0; frame < 120; frame++)
            { Targeting.Refresh(); shotgun.Tick(1f / 60, system); Projectile.UpdateAll(1f / 60); }
            Require(system.ShotsFired - shots == 12, "base firing interval is approximately two seconds");
            float cooldown = shotgun.BaseCooldown;
            shotgun.LevelUp(new WeaponUpgrade(Rarity.Common, new[] { (UpgradeStat.AttackSpeed, .5f) }));
            Require(shotgun.BaseCooldown < cooldown, "attack-speed weapon upgrades shorten the real cooldown");
            Targeting.Source = into => 0; Targeting.Refresh(); shots = system.ShotsFired;
            shotgun.Tick(5, system);
            Require(system.ShotsFired == shots, "no enemy means no automatic shot or particle burst");
            Projectile.ClearLiveCombat(); shotgun.Dispose();
            system.Tick(.001f);
            target = new ExpeditionWeaponTarget { Position = system.MuzzlePosition + new Vector3(0, 1, -2) };
            Targeting.Source = into => { into.Add(target); return 1; }; Targeting.Refresh();
            var swords = new WeaponRuntime(Catalog.WeaponById("skyline_swords")!);
            swords.Tick(1f / 60, system);
            Require(target.Hits == 2 && target.Damage >= 30, "each twin sword delivers a separate real three-dimensional hit");
            swords.Dispose(); Projectile.ClearLiveCombat();
            GD.Print("EXPEDITION WEAPONS PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("EXPEDITION WEAPONS FAIL " + error); GetTree().Quit(1); }
        finally { Targeting.Source = null; }
    }
}
