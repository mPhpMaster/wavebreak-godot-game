using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Net;

public sealed partial class RemotePlayer
{
    private readonly GrappleTether?[] _grappleCables = new GrappleTether?[2];
    private Vector3 _leftAnchor, _rightAnchor;
    private double _grappleReceived;
    public bool LeftHookAttached { get; private set; }
    public bool RightHookAttached { get; private set; }
    public int HookBoosts { get; private set; }
    public bool Grappling => LeftHookAttached || RightHookAttached;

    internal void SetGrapple(bool left, Vector3 leftAnchor, bool right, Vector3 rightAnchor, int boosts)
    {
        LeftHookAttached = left; RightHookAttached = right;
        _leftAnchor = leftAnchor; _rightAnchor = rightAnchor; HookBoosts = boosts;
        _grappleReceived = Time.GetTicksMsec() / 1000.0;
    }

    private void DrawGrapple()
    {
        bool valid = Game.Arena.IsCity && !Down && Time.GetTicksMsec() / 1000.0 - _grappleReceived < 0.4;
        Vector3 side = new(Mathf.Cos(_netYaw), 0f, -Mathf.Sin(_netYaw));
        for (int i = 0; i < 2; i++)
        {
            bool attached = valid && (i == 0 ? LeftHookAttached : RightHookAttached);
            if (_grappleCables[i] is null && attached)
            { _grappleCables[i] = new GrappleTether(); AddChild(_grappleCables[i]!); }
            _grappleCables[i]?.Set(CenterPosition + side * (i == 0 ? -.32f : .32f), i == 0 ? _leftAnchor : _rightAnchor, attached);
        }
    }
}
