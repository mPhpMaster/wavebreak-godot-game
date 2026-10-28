using Godot;
using Wavebreak.Data;
using Wavebreak.Game;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    private void BindHookTarget(HookAttachment hook, Vector3 anchor)
    {
        GrappleAnchors.Probe(this, anchor, out CollisionObject3D? target);
        hook.Target = target;
        hook.LocalAnchor = target?.ToLocal(anchor) ?? anchor;
        hook.TargetGeneration = target is Enemy enemy ? enemy.SpawnGeneration : 0;
    }

    private static bool FollowHookTarget(HookAttachment hook)
    {
        if (!IsInstanceValid(hook.Target) || !hook.Target!.IsInsideTree()) return false;
        if (hook.Target is Enemy enemy && (!enemy.IsAlive || enemy.SpawnGeneration != hook.TargetGeneration)) return false;
        hook.Anchor = hook.Target.ToGlobal(hook.LocalAnchor);
        return hook.Anchor.IsFinite();
    }

    internal int HookEnemyId(HookSide side) => _hooks[(int)side].Target is Enemy enemy ? enemy.NetId : 0;
    internal Vector3 HookLocalAnchor(HookSide side) => _hooks[(int)side].LocalAnchor;
}
