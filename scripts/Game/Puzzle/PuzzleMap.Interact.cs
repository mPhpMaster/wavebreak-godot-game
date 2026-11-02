using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game.Puzzle;

/// <summary>The piece in reach of this player, and what interact does to it.</summary>
public readonly record struct PuzzleFocus(PuzzleAction Action, int Room, int Index, Vector3 At, string Verb, bool Claim = false);

/// <summary>
/// Interacting: the nearest usable piece within <see cref="Reach"/> is the
/// focus, the HUD shows its verb with the interact key's name, and the key
/// (or the bot's <see cref="RequestInteract"/>) uses it. Plates, wind plates
/// and crates need no key: standing and walking are the input.
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
        void Offer(PuzzleAction action, int index, Vector3 at, string verb, bool claim = false)
        {
            Vector3 d = at - p;
            float sq = d.X * d.X + d.Z * d.Z;
            if (sq > best) return;
            best = sq;
            Focus = new PuzzleFocus(action, r, index, at, verb, claim);
        }
        bool solved = State.Get(room, PuzzleState.RSolved) != 0;
        switch (room.Kind)
        {
            case RoomKind.Crates:
                if (!solved) Offer(PuzzleAction.Reset, 0, room.World(room.ResetCell), PuzzleText.VerbReset);
                break;
            case RoomKind.Mirrors:
                if (solved) break;
                for (int i = 0; i < room.MirrorCells.Length; i++) Offer(PuzzleAction.Mirror, i, room.World(room.MirrorCells[i]), PuzzleText.VerbMirror);
                break;
            case RoomKind.Levers:
                if (solved) break;
                for (int i = 0; i < room.Levers.Length; i++) Offer(PuzzleAction.Lever, i, room.World(room.Levers[i]), PuzzleText.VerbLever);
                break;
            case RoomKind.Runes:
                if (solved) break;
                for (int i = 0; i < room.Bells.Length; i++)
                    Offer(PuzzleAction.Bell, i, room.World(room.Bells[i]), PuzzleText.VerbBell);
                Offer(PuzzleAction.Echo, 0, room.Centre, PuzzleText.VerbEcho);
                break;
            case RoomKind.Reward:
                for (int i = 0; i < _pedestals.Count; i++)
                    if (!_claimed && _offers is { } offers && i < offers.Count)
                        Offer(PuzzleAction.None, i, _pedestals[i].GlobalPosition, Meta.Loc.F(PuzzleText.VerbClaim, Meta.Loc.T(offers[i].Title)), claim: true);
                break;
        }
    }

    private void Use(PuzzleFocus f)
    {
        if (f.Claim)
        {
            Claim(f.Index);
            return;
        }
        Ask(f.Action, f.Room, f.Index);
    }
}
