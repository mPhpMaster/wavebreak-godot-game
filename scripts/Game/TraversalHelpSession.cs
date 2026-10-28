using Godot;

namespace Wavebreak.Game;

/// <summary>Timed help that can be dismissed or replayed without pausing a run.</summary>
public sealed class TraversalHelpSession
{
    private float _remaining = 20f;
    private bool _replayHeld;
    private bool _skipHeld;
    public bool Visible { get; private set; } = true;

    public void Tick(float delta)
    {
        bool replay = Input.IsPhysicalKeyPressed(Key.F1);
        bool skip = Input.IsPhysicalKeyPressed(Key.F2);
        if (replay && !_replayHeld) _remaining = 20f;
        if (skip && !_skipHeld) _remaining = 0f;
        _replayHeld = replay;
        _skipHeld = skip;
        _remaining = Mathf.Max(0f, _remaining - delta);
        Visible = _remaining > 0f;
    }
}
