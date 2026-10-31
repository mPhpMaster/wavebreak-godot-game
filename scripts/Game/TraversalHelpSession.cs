using Godot;

namespace Wavebreak.Game;

/// <summary>Timed help that can be dismissed or replayed without pausing a run.</summary>
public sealed class TraversalHelpSession
{
    private float _remaining = 20f;
    private bool _replayHeld;
    private bool _skipHeld;
    private bool _toggleHeld;
    public bool Visible { get; private set; } = true;

    /// <summary>The line under the help that says how to dismiss it.</summary>
    public static string Line() => UI.PadInput.UsingPad
        ? Meta.Loc.F("{0}: hide or replay help", Meta.Keybinds.Hint("help_toggle"))
        : Meta.Loc.T("F2: skip help  |  F1: replay help");

    public void Tick(float delta)
    {
        bool replay = Input.IsPhysicalKeyPressed(Key.F1);
        bool skip = Input.IsPhysicalKeyPressed(Key.F2);
        bool toggle = Input.IsActionPressed("help_toggle") && UI.PadInput.PlayHasPad;
        if (toggle && !_toggleHeld) _remaining = _remaining > 0f ? 0f : 20f;
        _toggleHeld = toggle;
        if (replay && !_replayHeld) _remaining = 20f;
        if (skip && !_skipHeld) _remaining = 0f;
        _replayHeld = replay;
        _skipHeld = skip;
        _remaining = Mathf.Max(0f, _remaining - delta);
        Visible = _remaining > 0f;
    }
}
