using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// Part of <c>--check-boundary</c>: on every map, the Tide Totems
/// (Game/Interactables.Totems.cs) must number <see cref="Interactables.TotemCountFor"/>,
/// stand inside the map, outside every blocker and every shrine's ring, and
/// apart from each other. Prints a TOTEMS line per map.
/// </summary>
public sealed partial class StageDirector
{
    private bool CheckTotemPlacement(StageDef stage)
    {
        var totems = Interactables.Instance?.Totems.ToList() ?? new();
        int want = Interactables.TotemCountFor(stage);
        int inBlocker = 0, nearShrine = 0, outside = 0;
        float closest = float.MaxValue;
        for (int i = 0; i < totems.Count; i++)
        {
            Vector3 p = totems[i].GlobalPosition;
            if (MapBlockers.Inside(p with { Y = -1f }, 0.5f)) inBlocker++;
            if (Interactables.NearShrine(p)) nearShrine++;
            if (new Vector2(p.X, p.Z).Length() > stage.InRadius - 6f) outside++;
            for (int j = i + 1; j < totems.Count; j++)
                closest = Mathf.Min(closest, new Vector2(p.X - totems[j].GlobalPosition.X, p.Z - totems[j].GlobalPosition.Z).Length());
        }
        bool pass = totems.Count == want && inBlocker == 0 && nearShrine == 0 && outside == 0 && (totems.Count < 2 || closest >= 20f);
        GD.Print($"TOTEMS map={stage.Id} placed={totems.Count} want={want} in_blocker={inBlocker} near_shrine={nearShrine} " +
                 $"outside={outside} closest={(totems.Count < 2 ? "-" : closest.ToString("0.0"))} " +
                 $"at={string.Join(" ", totems.Select(t => $"({t.GlobalPosition.X:0},{t.GlobalPosition.Z:0})"))} {(pass ? "PASS" : "FAIL")}");
        return pass;
    }
}
