using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Net;

/// <summary>
/// Co-op self-test, client side: a level-up hand does not stop the player.
///
/// At --coop-levelup-at=S the client opens a real hand through the level-up
/// panel, takes the autopilot off the wheel and holds the real move_forward
/// action for 1.5 s. It checks the hand is the compact one, that no input hold
/// was set, and that the player's position changed while the hand was open;
/// then it presses the 1 key for real and checks the card was taken. With
/// --coop-levelup-shot=&lt;png&gt; it photographs the open hand mid-walk.
/// </summary>
public sealed partial class NetSession
{
    private float _testLevelUpAt = -1f;
    private string? _testLevelUpShot;
    private bool _luArgsRead;
    private int _luPhase;
    private double _luClock;
    private Vector3 _luStart;
    private Func<Vector3>? _luOverride;
    private bool _luOpened, _luCompact = true, _luHeld, _luPicked;
    private float _luMoved;

    private string LevelUpTestFields => _testLevelUpAt < 0f ? "" :
        $"levelup_open={(_luOpened ? 1 : 0)} levelup_compact={(_luCompact ? 1 : 0)} levelup_moved={_luMoved:0.0} levelup_picked={(_luPicked ? 1 : 0)}";

    private void LevelUpTestProblems(List<string> problems)
    {
        if (_testLevelUpAt < 0f) return;
        if (!_luOpened) problems.Add("the level-up hand never opened");
        else
        {
            if (!_luCompact) problems.Add("co-op level-up was not the compact hand");
            if (_luHeld) problems.Add("the level-up hand held the player's input");
            if (_luMoved < 1.0f) problems.Add($"the player moved only {_luMoved:0.0} m while the hand was open");
            if (!_luPicked) problems.Add("pressing 1 did not take a card from the compact hand");
        }
    }

    private void ProcessLevelUpTest(float t, Player? me)
    {
        if (!_luArgsRead)
        {
            _luArgsRead = true;
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--coop-levelup-shot=")) _testLevelUpShot = arg["--coop-levelup-shot=".Length..];
        }
        if (_testLevelUpAt < 0f || !Coop.IsClient || me is null || _main is null) return;
        UI.LevelUpPanel panel = _main.LevelUp;
        double dt = GetProcessDeltaTime();

        switch (_luPhase)
        {
            case 0:
                if (t < _testLevelUpAt || !me.IsAlive) return;
                if (!_main.OpenLevelUpForTest()) return;
                _luOpened = true;
                _luStart = me.GlobalPosition;
                _luOverride = me.MoveOverride;
                me.MoveOverride = null;                 // real input from here on
                Input.ActionPress("move_forward");
                _luClock = 0;
                _luPhase = 1;
                GD.Print($"[coop-test] level-up hand open at t={t:0.0}, walking with move_forward");
                break;

            case 1:
                _luClock += dt;
                _luCompact &= panel.CompactOpen;
                _luHeld |= Coop.LocalHold;
                if (_testLevelUpShot is { } shot && _luClock >= 0.9)
                {
                    _testLevelUpShot = null;
                    _ = Screenshot.Capture(GetTree(), this, shot);
                }
                if (_luClock < 1.6) return;
                Vector3 d = me.GlobalPosition - _luStart;
                d.Y = 0f;
                _luMoved = d.Length();
                Input.ActionRelease("move_forward");
                me.MoveOverride = _luOverride;
                bool stillOpen = panel.IsOpen;
                GD.Print($"[coop-test] moved {_luMoved:0.0} m with the hand open (open={stillOpen} compact={_luCompact} hold={_luHeld})");
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Key1, PhysicalKeycode = Key.Key1, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Key1, PhysicalKeycode = Key.Key1, Pressed = false });
                _luClock = 0;
                _luPhase = 2;
                break;

            case 2:
                _luClock += dt;
                if (_luClock < 0.4) return;
                _luPicked = !panel.IsOpen;
                _luPhase = 3;
                break;
        }
    }
}
