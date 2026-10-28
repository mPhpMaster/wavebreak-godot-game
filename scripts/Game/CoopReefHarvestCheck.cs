using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Net;

namespace Wavebreak.Game;

public sealed partial class CoopReefHarvestCheck : Node
{
    private float _time;
    private bool _asked, _retried;
    private bool _lateState;

    public override void _PhysicsProcess(double delta)
    {
        if (NetSession.Instance is not { Started: true } session || ReefHarvest.Instance is not { Count: > 0 } reef
            || Player.Instance is not { } player) return;
        _time += (float)delta;
        bool late = session.LateJoined;
        _lateState |= late && Array.IndexOf(reef.SpentIds, 0) >= 0;
        if (_time > 1 && !_asked)
        {
            player.MoveOverride = static () => Vector3.Zero;
            player.Teleport(reef.Definitions[0].Position + Vector3.Up);
            _asked = true;
        }
        if (Coop.IsClient && _time > 2 && !_retried)
        { session.AskReefHarvest(0); session.AskReefHarvest(0); _retried = true; }
        float end = late ? 5 : Coop.IsHost ? 45 : 43;
        if (_time < end) return;
        bool spent = Array.IndexOf(reef.SpentIds, 0) >= 0;
        bool pass = spent && (!late || _lateState) && Arena.Current.Id == "lumenreef";
        GD.Print($"COOP REEF {(pass ? "PASS" : "FAIL")} role={Coop.Mode} late={late} depleted={spent} initial_snapshot={_lateState}");
        SetPhysicsProcess(false); GetTree().Quit(pass ? 0 : 1);
    }
}
