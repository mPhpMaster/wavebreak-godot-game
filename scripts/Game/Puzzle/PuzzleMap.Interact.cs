using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game.Puzzle;

/// <summary>The piece in reach of this player, and what interact does to it.</summary>
public readonly record struct PuzzleFocus(PuzzleAction Action, int Room, int Index, Vector3 At, string Verb, bool Claim = false, int Arg = 0);

/// <summary>
/// Interacting: the nearest usable piece within <see cref="Reach"/> is the
/// focus (each room's kind offers its pieces, <see cref="PuzzleKind.Offer"/>),
/// the HUD shows its verb with the interact key's name, and the key (or the
/// bot's <see cref="RequestInteract"/>) uses it. Plates, tiles, rings and
/// crates need no key: standing and walking are the input.
/// </summary>
public sealed partial class PuzzleMap
{
    public PuzzleFocus? Focus { get; private set; }

    private void UpdateFocus(Player me, int r)
    {
        Focus = null;
        if (r < 0 || !me.IsAlive) return;
        RoomPlan room = Plan.Rooms[r];
        Vector3 p = me.GlobalPosition;
        float best = Reach * Reach;
        void Offer(PuzzleAction action, int index, Vector3 at, string verb, int arg = 0)
        {
            Vector3 d = at - p;
            float sq = d.X * d.X + d.Z * d.Z;
            if (sq > best) return;
            best = sq;
            Focus = new PuzzleFocus(action, r, index, at, verb, false, arg);
        }
        if (room.Kind == RoomKind.Reward)
        {
            for (int i = 0; i < _pedestals.Count; i++)
                if (!_claimed && _offers is { } offers && i < offers.Count)
                {
                    Vector3 d = _pedestals[i].GlobalPosition - p;
                    float sq = d.X * d.X + d.Z * d.Z;
                    if (sq > best) continue;
                    best = sq;
                    Focus = new PuzzleFocus(PuzzleAction.None, r, i, _pedestals[i].GlobalPosition, Meta.Loc.F(PuzzleText.VerbClaim, Meta.Loc.T(offers[i].Title)), Claim: true);
                }
            return;
        }
        if (room.Def is { } def && !State.Solved(r)) def.Offer(this, room, Offer);
    }

    private void Use(PuzzleFocus f)
    {
        if (f.Claim)
        {
            Claim(f.Index);
            return;
        }
        Ask(f.Action, f.Room, f.Index, f.Arg);
    }
}
