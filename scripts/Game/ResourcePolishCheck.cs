using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public sealed partial class ResourcePolishCheck : Node
{
    public override void _Ready() => _ = Run();
    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool value, string label)
    {
        GD.Print($"RESOURCES {(value ? "OK" : "FAIL")} {label}");
        if (!value) throw new InvalidOperationException(label);
    }
    private async Task Run()
    {
        try
        {
            await Wait(.8f);
            Main main = Main.Instance!;
            main.Player.MoveOverride = static () => Vector3.Zero;
            foreach (StageDef map in Stages.All.Append(Stages.Lumenreef).Append(Stages.Bellspire))
            {
                main.Stages.DevSetMap(map);
                await Wait(.35f);
                main.Enemies.Quiet = true;
                ReefHarvest resources = ReefHarvest.Instance!;
                Require(resources is { Count: > 20 and <= 128 }, map.Id + " has bounded collectible resources");
                Vector3[] positions = resources.Definitions.Select(def => def.Position).ToArray();
                Require(!resources.TryHarvest(-1, Vector3.Zero) && !resources.TryHarvest(0, Vector3.One * 10000), "invalid and distant requests are refused");
                foreach (ReefHarvestDef def in resources.Definitions.Take(3))
                {
                    Require(resources.TryHarvest(def.Id, def.Position + def.Normal), map.Id + " resource can be gathered");
                    Require(!resources.TryHarvest(def.Id, def.Position + def.Normal), "spent resource cannot grant duplicate rewards");
                }
                main.Stages.DevSetMap(map);
                await Wait(.35f);
                Require(positions.SequenceEqual(ReefHarvest.Instance!.Definitions.Select(def => def.Position)), map.Id + " resource IDs and positions survive a rebuild deterministically");
                foreach (PickupKind kind in new[] { PickupKind.Xp, PickupKind.Gold })
                {
                    ArrayMesh model = PickupModels.Get(kind)!;
                    Require(model is not null && ReferenceEquals(model, PickupModels.Get(kind)), map.Id + " shares imported pickup geometry");
                    Vector3 size = model!.GetAabb().Size;
                    Require(size.X > 0 && size.Y > 0 && size.Z > 0 && size.Length() < 1, "pickup shape is normalized without per-drop scenes");
                }
            }
            var combatTarget = new ExpeditionWeaponTarget { Position = main.Player.CenterPosition + Vector3.Forward * 6 };
            Combat.Targeting.Source = targets => { targets.Add(combatTarget); return 1; };
            main.Weapons.DevMax(Catalog.WeaponById("reef_shotgun")!);
            main.Weapons.Tick(.05f);
            Require(Combat.Projectile.ActiveCount > 0, "live combat projectiles exist before entering rest");
            main.Stages.DevEnterRestRoom();
            await Wait(.3f);
            Require(main.Stages.InRestRoom && Combat.Projectile.ActiveCount == 0 && Combat.Weapons.SignatureFx.LiveCount == 0,
                "rest room retires active weapon projectiles and signature effects");
            Combat.Targeting.Source = null;
            GD.Print("RESOURCES PASS");
            GetTree().Quit();
        }
        catch (Exception error) { GD.Print("RESOURCES FAIL " + error); GetTree().Quit(1); }
    }
}


