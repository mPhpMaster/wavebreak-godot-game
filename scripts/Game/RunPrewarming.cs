using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Data;
using Wavebreak.UI;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public static class RunPrewarming
{
    public static async Task Prepare(Main main, RunLoadingPanel progress)
    {
        var ids = main.Stages.Route.SelectMany(stage => stage.EnemyIds ?? System.Array.Empty<string>()).ToHashSet();
        foreach (StageDef stage in main.Stages.Route) ids.Add(stage.BossId);
        var enemies = Catalog.Enemies.Where(enemy => ids.Contains(enemy.Id) || !enemy.IsBoss).ToArray();
        for (int index = 0; index < enemies.Length; index++)
        {
            ModelSpec spec = EnemyVisuals.SpecFor(enemies[index]);
            if (!spec.IsImported) ModelBuilder.Get(spec);
            CharacterRig preview = EnemyVisuals.Build(enemies[index]);
            main.AddChild(preview);
            preview.Position = main.Player.GlobalPosition + Vector3.Forward * 4;
            await progress.Step("Preparing creature models", 70 + 25f * (index + 1) / System.Math.Max(1, enemies.Length));
            preview.QueueFree();
        }
        PickupModels.Get(Actors.PickupKind.Xp);
        PickupModels.Get(Actors.PickupKind.Gold);
        await progress.Step("Ready", 100);
    }
}


