using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class ChestSpawner
{
    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("ChestSpawner.proc");
        float dt = (float)delta;
        _clock += dt;
        _chests.RemoveAll(c => !IsInstanceValid(c) || c.IsQueuedForDeletion());

        if (_map is not null && !IsInstanceValid(_map)) Clear();
        TopUp();

        bool pressed = _interactRequested && !Net.Coop.LocalHold;
        _interactRequested = false;

        Player? player = Player.Instance;
        bool live = player is not null && player.IsAlive && Main.Instance?.Run.Over != true;
        _focus = live ? Focus(player!.GlobalPosition) : null;

        if (_focus is null)
        {
            _prompt.HideNow();
            return;
        }

        int gold = Inventory.Current?.Gold ?? 0;
        _prompt.ShowFor(_focus.Top, _keyName, PriceFor(_focus), _focus.IsFree, gold);
        if (pressed) TryOpen(_focus);
    }

    /// <summary>Unhandled, so an open panel that wants the key gets it first.
    /// Paused with the tree: no chests behind the level-up screen.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (!_hasAction || e is InputEventKey { Echo: true }) return;
        if (!e.IsActionPressed(InteractAction)) return;
        _interactRequested = true;
        if (_focus is not null) GetViewport().SetInputAsHandled();
    }

    public override void _PhysicsProcess(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("ChestSpawner.phys");
        if (_pending.Count == 0) return;
        if (_map is null || !IsInstanceValid(_map) || !_map.IsInsideTree()) { _pending.Clear(); return; }

        PhysicsDirectSpaceState3D? space = _map.GetWorld3D()?.DirectSpaceState;
        if (space is null) return;

        // A few per frame: a fresh map asks for five at once and each costs up
        // to a few dozen rays, which is nothing, but there is no reason to spike.
        for (int i = 0; i < 3 && _pending.Count > 0; i++)
        {
            Request r = _pending.Dequeue();
            Place(space, r);
        }
    }

}
