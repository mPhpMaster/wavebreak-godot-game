using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.UI;

/// <summary>Nearby ordinary/elite bars; bosses retain the existing dedicated BossBar.</summary>
public sealed partial class EnemyHealthBars : Control
{
    private readonly NearbyEnemySelection _selection = new();
    private readonly List<Rect2> _occupied = new(NearbyEnemySelection.MaxBars);
    private float _refresh;
    private Minimap? _minimap;
    public int BarsDrawn { get; private set; }

    public override void _Ready()
    {
        Name = "NearbyEnemyHealthBars"; MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _minimap = GetParent().GetNodeOrNull<Minimap>("Minimap");
    }

    public override void _PhysicsProcess(double delta)
    {
        using var timing = Prof.Scope("UI.enemyBarsSelect");
        _refresh -= (float)delta;
        if (_refresh > 0) return;
        _refresh = 0.05f;
        _selection.Refresh(Player.Instance, GetViewport().GetCamera3D(), EnemyManager.Instance?.AliveList);
        QueueRedraw();
    }

    public override void _Draw()
    {
        using var timing = Prof.Scope("UI.enemyBarsDraw");
        BarsDrawn = 0; _occupied.Clear();
        if (Player.Instance is not { IsAlive: true } player || GetViewport().GetCamera3D() is not { } camera) return;
        foreach (Enemy enemy in _selection.Visible)
        {
            if (!NearbyEnemySelection.Nearby(enemy, player.CenterPosition) || camera.IsPositionBehind(NearbyEnemySelection.Anchor(enemy))) continue;
            Vector2 at = camera.UnprojectPosition(NearbyEnemySelection.Anchor(enemy));
            var bar = new Rect2(at - new Vector2(28, 4), new Vector2(56, 7));
            if (!GetViewportRect().Encloses(bar)) continue;
            if (bar.Position.Y < 150 || bar.End.Y > GetViewportRect().Size.Y - 110) continue;
            if (_minimap is { Visible: true } map
                && new Rect2(map.GetGlobalRect().Position, map.Size + new Vector2(0, 50)).Intersects(bar)) continue;
            bool crowded = false;
            foreach (Rect2 other in _occupied) if (bar.Grow(3).Intersects(other)) { crowded = true; break; }
            if (crowded) continue;
            _occupied.Add(bar);
            Color edge = enemy.IsElite ? new Color(1, 0.82f, 0.4f) : new Color(0.92f, 0.95f, 1);
            DrawRect(bar.Grow(2), new Color(0.02f, 0.03f, 0.05f, 0.95f));
            DrawRect(bar, new Color(0.16f, 0.19f, 0.22f));
            float fraction = Mathf.Clamp(enemy.Health / enemy.MaxHealth, 0, 1);
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fraction, bar.Size.Y)), new Color(0.88f, 0.3f, 0.35f));
            DrawRect(bar.Grow(1), edge, false, 1);
            DrawLine(bar.Position + Vector2.Right * bar.Size.X * 0.5f, bar.Position + new Vector2(bar.Size.X * 0.5f, bar.Size.Y),
                new Color(0.05f, 0.07f, 0.1f, 0.8f), 1);
            if (enemy.IsElite) DrawRect(new Rect2(bar.Position - new Vector2(4, 1), new Vector2(2, bar.Size.Y + 2)), edge);
            BarsDrawn++;
        }
    }
}
