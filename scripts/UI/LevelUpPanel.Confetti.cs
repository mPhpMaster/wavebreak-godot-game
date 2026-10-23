using System;
using Godot;

namespace Wavebreak.UI;

/// <summary>
/// Celebration on the level-up screen: pastel confetti drifting down behind the
/// cards, plus one burst from the middle as the panel opens. Both run while the
/// tree is paused (the panel is ProcessMode.Always, the particles inherit it).
/// <see cref="OpenChanged"/> lets the HUD hold its phase title card off-screen
/// while the cards are up, so the two titles never stack.
/// </summary>
public sealed partial class LevelUpPanel
{
    /// <summary>Raised with true when the cards appear and false when they go.</summary>
    public static event Action<bool>? OpenChanged;

    /// <summary>True while any level-up hand is on screen.</summary>
    public static bool AnyOpen { get; private set; }

    private static readonly Color[] Pastels =
    {
        new(1.00f, 0.62f, 0.72f), new(1.00f, 0.84f, 0.45f), new(0.55f, 0.90f, 0.78f),
        new(0.55f, 0.75f, 1.00f), new(0.80f, 0.65f, 1.00f), new(1.00f, 1.00f, 1.00f),
    };

    private CpuParticles2D _confetti = null!;
    private CpuParticles2D _burst = null!;

    private void BuildConfetti()
    {
        _confetti = MakeConfetti(amount: 90, lifetime: 4.0f);
        _confetti.EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle;
        _confetti.Direction = Vector2.Down;
        _confetti.Spread = 25f;
        _confetti.InitialVelocityMin = 40f;
        _confetti.InitialVelocityMax = 110f;
        _confetti.Gravity = new Vector2(0f, 60f);
        _confetti.Preprocess = 0f;
        AddChild(_confetti);

        _burst = MakeConfetti(amount: 70, lifetime: 1.6f);
        _burst.OneShot = true;
        _burst.Explosiveness = 0.95f;
        _burst.Spread = 180f;
        _burst.Direction = Vector2.Up;
        _burst.InitialVelocityMin = 260f;
        _burst.InitialVelocityMax = 520f;
        _burst.Gravity = new Vector2(0f, 700f);
        _burst.DampingMin = 40f;
        _burst.DampingMax = 90f;
        AddChild(_burst);

        // Behind the cards: right after the dimmer, which BuildChrome added first.
        MoveChild(_confetti, 1);
        MoveChild(_burst, 2);
    }

    private static CpuParticles2D MakeConfetti(int amount, float lifetime)
    {
        var p = new CpuParticles2D
        {
            Emitting = false,
            Amount = amount,
            Lifetime = lifetime,
            LocalCoords = false,
            ScaleAmountMin = 7f,
            ScaleAmountMax = 14f,
            AngularVelocityMin = -420f,
            AngularVelocityMax = 420f,
            AngleMin = 0f,
            AngleMax = 360f,
            ColorInitialRamp = Ramp(),
        };
        // Fade the last third out rather than popping at the end of life.
        var fade = new Gradient();
        fade.SetColor(0, Colors.White);
        fade.SetColor(1, new Color(1f, 1f, 1f, 0f));
        fade.AddPoint(0.7f, Colors.White);
        p.ColorRamp = fade;
        return p;
    }

    /// <summary>Hard-edged bands so each piece takes one pastel, not a blend.</summary>
    private static Gradient Ramp()
    {
        var g = new Gradient { InterpolationMode = Gradient.InterpolationModeEnum.Constant };
        g.SetColor(0, Pastels[0]);
        g.SetOffset(1, 1f);
        g.SetColor(1, Pastels[^1]);
        for (int i = 1; i < Pastels.Length; i++) g.AddPoint((float)i / Pastels.Length, Pastels[i]);
        return g;
    }

    private void StartConfetti()
    {
        Vector2 size = GetViewportRect().Size;
        _confetti.Position = new Vector2(size.X * 0.5f, -20f);
        _confetti.EmissionRectExtents = new Vector2(size.X * 0.5f, 4f);
        _confetti.Restart();
        _confetti.Emitting = true;

        _burst.Position = new Vector2(size.X * 0.5f, size.Y * 0.36f);
        _burst.Restart();
        _burst.Emitting = true;
        AnyOpen = true;
        OpenChanged?.Invoke(true);
    }

    private void StopConfetti()
    {
        _confetti.Emitting = false;
        _confetti.Restart();
        _confetti.Emitting = false;
        AnyOpen = false;
        OpenChanged?.Invoke(false);
    }
}
