using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>One bounded visibility query for the shared screen-space enemy-bar overlay.</summary>
public sealed class NearbyEnemySelection
{
    public const float Radius = 14f;
    public const int MaxBars = 16;
    public const int RayBudget = 32;
    private readonly List<EnemyBarCandidate> _candidates = new(64);
    private readonly List<Enemy> _visible = new(MaxBars);
    public IReadOnlyList<Enemy> Visible => _visible;
    public int RaysLastRefresh { get; private set; }

    public static Vector3 Anchor(Enemy enemy) => enemy.GetGlobalTransformInterpolated().Origin
        + (enemy.AimPoint - enemy.GlobalPosition) + Vector3.Up * ((enemy.Def?.Height ?? 1.6f) * 0.5f + 0.3f);
    public static bool Nearby(Enemy enemy, Vector3 player) => GodotObject.IsInstanceValid(enemy)
        && enemy.IsInsideTree() && !enemy.IsPooled && enemy.IsAlive && !enemy.IsBoss
        && enemy.MaxHealth > 0 && enemy.AimPoint.DistanceSquaredTo(player) <= Radius * Radius
        && FogOfWar.IsRevealed(enemy.GlobalPosition);

    public void Refresh(Player? player, Camera3D? camera, IReadOnlyList<Enemy>? enemies)
    {
        _visible.Clear(); _candidates.Clear(); RaysLastRefresh = 0;
        if (player is null || !player.IsAlive || camera is null || enemies is null) return;
        foreach (Enemy enemy in enemies)
        {
            if (!Nearby(enemy, player.CenterPosition) || camera.IsPositionBehind(Anchor(enemy))) continue;
            Vector2 screen = camera.UnprojectPosition(Anchor(enemy));
            if (!camera.GetViewport().GetVisibleRect().HasPoint(screen)) continue;
            _candidates.Add(new EnemyBarCandidate(enemy, enemy.AimPoint.DistanceSquaredTo(player.CenterPosition)));
        }
        _candidates.Sort(static (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
        for (int i = 0; i < _candidates.Count && RaysLastRefresh < RayBudget && _visible.Count < MaxBars; i++)
        {
            Enemy enemy = _candidates[i].Enemy;
            RaysLastRefresh++;
            if (WorldSight.Clear(enemy, camera.GlobalPosition, enemy.AimPoint)) _visible.Add(enemy);
        }
    }
}

