using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// --art-theme=&lt;id&gt;: plays a normal run inside a <see cref="PropTheme"/>.
///
/// A demonstration of the integration a map would do, performed from outside
/// so the arena code stays untouched: it hides the arena's own meshes, removes
/// its sky and sun, and adds the theme's floor, props, environment and sun on
/// top. Collision is the arena's, unchanged. A real map calls the same four
/// <see cref="PropFactory"/> methods directly (see the example on that class).
/// </summary>
public sealed partial class ThemeDresser : Node
{
    private readonly string _theme;

    public ThemeDresser(string theme)
    {
        _theme = theme;
        Name = "ThemeDresser";
    }

    public override void _Ready()
    {
        if (GetParent()?.GetNodeOrNull<Node3D>("Arena") is not { } arena) return;
        foreach (Node child in arena.GetChildren())
        {
            if (child is MeshInstance3D mi) mi.Visible = false;
            else if (child is WorldEnvironment or DirectionalLight3D) child.QueueFree();
        }

        // "<theme>" or "<theme>:<density>".
        string[] parts = _theme.Split(':');
        PropTheme t = PropTheme.Get(parts[0]);
        float density = parts.Length > 1 ? parts[1].ToFloat() : 0.8f;
        float radius = Game.Arena.Radius;
        arena.AddChild(PropFactory.BuildFloor(radius + 30f, t));
        if (density > 0f)
            PropFactory.Scatter(arena, new Core.Rng(20261022), radius - 3f, t, density, clearRadius: 8f);
        arena.AddChild(PropFactory.MakeWorldEnvironment(t));
        arena.AddChild(PropFactory.MakeSun(t));
        Meta.GraphicsSettings.ApplyScene(arena);
        GD.Print($"[art] arena dressed as {t.Id}");
    }

    private double _clock, _nextBeat = 5.0;
    private int _frames;

    // A heartbeat in the log, so a stalled demo run says where it stalled.
    public override void _Process(double delta)
    {
        _clock += delta;
        _frames++;
        if (_clock < _nextBeat) return;
        _nextBeat += 5.0;
        GD.Print($"[art] t={_clock:0} frames={_frames} alive={Game.Main.Instance?.Enemies.AliveCount}");
    }
}
