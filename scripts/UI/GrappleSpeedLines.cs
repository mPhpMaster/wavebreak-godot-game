using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

public sealed partial class GrappleSpeedLines : Control
{
    private float _refresh, _phase, _intensity;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        _refresh -= (float)delta;
        if (_refresh > 0f) return;
        _refresh = 1f / 30f;
        Player? player = Player.Instance;
        float next = player is null || GetTree().Paused ? 0f : GrappleWind.Intensity(Arena.IsCity,
            player.Grappling, player.IsAlive, player.Velocity.Length(), GraphicsSettings.ScreenFxScale);
        if (_intensity == 0f && next == 0f) return;
        _intensity = next;
        _phase = (_phase + .065f * (1f + next)) % 1f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_intensity <= 0f) return;
        Vector2 center = Size * .5f;
        Color color = new(.75f, .91f, 1f, _intensity * .34f);
        for (int i = 0; i < 12; i++)
        {
            float angle = Mathf.Tau * (i + .28f) / 12f;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            float phase = (_phase + i * .317f) % 1f;
            Vector2 start = center + direction * center * (.78f + phase * .32f);
            DrawLine(start, start + direction * (12f + 72f * _intensity), color, 1.5f);
        }
    }
}
