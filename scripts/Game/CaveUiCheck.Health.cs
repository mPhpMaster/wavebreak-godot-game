using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class CaveUiCheck
{
    private async Task CheckHealthBars()
    {
        EnemyManager enemies = EnemyManager.Instance!; enemies.Quiet = false;
        EnemyManager.DevFrozen = true;
        Vector3 origin = new(-25, 20, -15);
        _player.Teleport(origin);
        PlayerCamera.Instance!.FaceDirection(Vector3.Forward); PlayerCamera.Instance.SetPitchDegrees(-15); PlayerCamera.Instance.SnapToTarget();
        FogOfWar.Instance!.Reveal(new Vector2(origin.X, origin.Z), 35);
        EnemyDef normalDef = Catalog.EnemyById("gravel_brute")!;
        Enemy normal = enemies.SpawnAt(normalDef, origin + new Vector3(-3, 0, -6))!;
        normal.Configure(normalDef, 100, 1, 1);
        EnemyDef eliteDef = Catalog.Enemies.First(d => d.IsElite && !d.IsBoss);
        Enemy elite = enemies.SpawnAt(eliteDef, origin + new Vector3(4, 0, -7))!;
        elite.Configure(eliteDef, 100, 1, 1);
        Enemy far = enemies.SpawnAt(normalDef, origin + Vector3.Forward * 35)!;
        Enemy vertical = enemies.SpawnAt(normalDef, origin + Vector3.Up * 25)!;
        Enemy boss = enemies.SpawnAt(Catalog.EnemyById("undertow_oracle")!, origin + new Vector3(7, 0, -7))!;
        Enemy unrevealed = enemies.SpawnAt(normalDef, new Vector3(-100, 50, 85))!;
        await Wait(0.3f);
        _selection.Refresh(_player, GetViewport().GetCamera3D(), enemies.AliveList);
        if (!_selection.Visible.Contains(normal))
        {
            var camera = GetViewport().GetCamera3D()!;
            GD.Print($"CAVEUI diagnostic viewport={camera.GetViewport().GetVisibleRect()} camera={camera.GlobalPosition} normal={normal.GlobalPosition} "
                + $"near={NearbyEnemySelection.Nearby(normal, _player.CenterPosition)} behind={camera.IsPositionBehind(NearbyEnemySelection.Anchor(normal))} "
                + $"screen={camera.UnprojectPosition(NearbyEnemySelection.Anchor(normal))} sight={WorldSight.Clear(normal, camera.GlobalPosition, normal.AimPoint)}");
        }
        Check(_selection.Visible.Contains(normal) && _selection.Visible.Contains(elite), "nearby ordinary and elite enemies receive bars");
        Check(!_selection.Visible.Contains(far) && !_selection.Visible.Contains(vertical) && !_selection.Visible.Contains(boss),
            "distance is full 3D and dedicated boss UI is not duplicated");
        Check(!NearbyEnemySelection.Nearby(unrevealed, unrevealed.AimPoint + Vector3.Back), "fog hides enemy bars even within the proximity radius");
        float healthBefore = normal.Health;
        normal.TakeDamage(new DamageInfo { Amount = healthBefore * 0.5f, CanCrit = false, Knockback = 0 });
        Check(Mathf.Abs(normal.Health / normal.MaxHealth - healthBefore * .5f / normal.MaxHealth) < 0.001f,
            "bar health source responds to actual current/max health changes");
        await Wait(0.2f); await Capture("health");
        if (_prefix is not null) Check(ReefCheckHelpers.Find<EnemyHealthBars>(GetTree().Root) is { BarsDrawn: > 0 }, "rendered nearby bars are present");
        var wall = Wall(origin + new Vector3(-1.5f, 1.5f, -3), new Vector3(8, 12, 0.6f));
        await Wait(0.25f);
        _selection.Refresh(_player, GetViewport().GetCamera3D(), enemies.AliveList);
        Check(!_selection.Visible.Contains(normal), "wall-occluded enemy bars stay hidden");
        wall.GetParent().RemoveChild(wall); wall.QueueFree();
        normal.Despawn();
        await Wait(0.2f);
        _selection.Refresh(_player, GetViewport().GetCamera3D(), enemies.AliveList);
        Check(!_selection.Visible.Contains(normal) && _selection.RaysLastRefresh <= NearbyEnemySelection.RayBudget
            && _selection.Visible.Count <= NearbyEnemySelection.MaxBars, "despawned/pool entries clear and overlay work stays bounded");
        Enemy reused = enemies.SpawnAt(normalDef, origin + new Vector3(-3, 0, -6))!;
        await Wait(0.15f);
        _selection.Refresh(_player, GetViewport().GetCamera3D(), enemies.AliveList);
        Check(_selection.Visible.Contains(reused) && reused.HealthFraction > 0.9f, "a reused enemy starts with its fresh health instead of a stale bar");
        EnemyManager.DevFrozen = false; enemies.Quiet = true;
    }
}
