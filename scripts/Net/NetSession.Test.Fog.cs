using Godot;
using Wavebreak.Game;
using Wavebreak.UI;

namespace Wavebreak.Net;

/// <summary>
/// <c>--coop-check-fog</c> (tests/coop.ps1): the shared fog of war.
///
/// At 8 s of run clock each side reveals a probe circle far from where the
/// bots play (host: north-west, client: north-east, 82% of the way to the
/// wall) without anyone standing there, so the only way the other side can
/// learn of it is the FogBits mask. From 15 s each side checks the other's
/// probe is revealed on its own map, then prints
/// <c>COOPFOG PASS|FAIL role=... shared=... sent=... received=... from_team=...</c>.
/// <c>--coop-fog-shot=&lt;png&gt;</c> photographs the Tab sheet's big map then,
/// both probes visible.
/// </summary>
public sealed partial class NetSession
{
    private bool _fogTestParsed, _fogTest, _fogProbed, _fogDone;
    private string? _fogShot;
    private bool _fogOtherSeenBefore;
    private float _fogShotAt = -1f;

    private Vector3 FogProbe(bool host)
    {
        float r = Arena.Radius * 0.82f;
        float a = Mathf.DegToRad(host ? 225f : 315f);
        return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
    }

    /// <summary>Called from FogTick every frame of a co-op run.</summary>
    private void StepFogTest()
    {
        if (!_fogTestParsed)
        {
            _fogTestParsed = true;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--coop-check-fog") _fogTest = true;
                else if (arg.StartsWith("--coop-fog-shot=")) _fogShot = arg["--coop-fog-shot=".Length..];
            }
        }
        if (!_fogTest || _fogDone || _main is null || !IsInstanceValid(_main) || FogOfWar.Instance is not { Active: true } fog) return;
        float t = _main.Run.Seconds;
        if (_main.Run.Phase != 1) return;

        Vector3 mine = FogProbe(Coop.IsHost), theirs = FogProbe(!Coop.IsHost);
        if (!_fogProbed && t >= 8f)
        {
            _fogProbed = true;
            _fogOtherSeenBefore = FogOfWar.IsRevealed(theirs);
            fog.Reveal(new Vector2(mine.X, mine.Z), 14f);
            GD.Print($"[coop-fog] revealed probe at ({mine.X:0},{mine.Z:0}); other probe seen before={_fogOtherSeenBefore}");
        }
        if (_fogProbed && _fogShotAt < 0f && t >= 15f && FogOfWar.IsRevealed(theirs))
        {
            bool ok = !_fogOtherSeenBefore && fog.CellsFromTeam > 0 && FogMasksReceived > 0;
            GD.Print($"COOPFOG {(ok ? "PASS" : "FAIL")} role={(Coop.IsHost ? "host" : "client")} shared=1 " +
                     $"sent={FogMasksSent} received={FogMasksReceived} from_team={fog.CellsFromTeam} " +
                     $"explored={fog.Explored * 100f:0}% before={_fogOtherSeenBefore}");
            _fogShotAt = t;
            if (_fogShot is not null) StatsPanel.ForceOpen = true;
        }
        if (_fogShotAt >= 0f && t >= _fogShotAt + 0.8f)
        {
            _fogDone = true;
            if (_fogShot is not null)
                _ = CaptureFog(_fogShot);
        }
        if (_fogProbed && !_fogDone && _fogShotAt < 0f && t >= 21f)
        {
            _fogDone = true;
            GD.Print($"COOPFOG FAIL role={(Coop.IsHost ? "host" : "client")} shared=0 sent={FogMasksSent} " +
                     $"received={FogMasksReceived} from_team={fog.CellsFromTeam}");
        }
    }

    private async System.Threading.Tasks.Task CaptureFog(string path)
    {
        await Screenshot.Capture(GetTree(), this, path);
        StatsPanel.ForceOpen = false;
    }
}
