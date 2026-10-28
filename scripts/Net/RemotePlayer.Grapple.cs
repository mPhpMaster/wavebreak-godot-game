using Godot;
using Wavebreak.Visuals;
using Wavebreak.Actors;

namespace Wavebreak.Net;

public sealed partial class RemotePlayer
{
    private readonly GrappleTether?[] _grappleCables = new GrappleTether?[2];
    private Vector3 _leftAnchor, _rightAnchor;
    private bool _leftFiring, _rightFiring;
    private Vector3 _leftTip, _rightTip;
    private double _drawAt;
    private Enemy? _leftBody, _rightBody;
    private Vector3 _leftLocal, _rightLocal;
    private int _leftGeneration, _rightGeneration;
    private double _grappleReceived;
    public bool LeftHookAttached { get; private set; }
    public bool RightHookAttached { get; private set; }
    public int HookBoosts { get; private set; }
    public bool GrapplingMovingTarget => LeftHookAttached && IsInstanceValid(_leftBody) && _leftBody!.IsAlive
        || RightHookAttached && IsInstanceValid(_rightBody) && _rightBody!.IsAlive;
    public float MovingHookError => LeftHookAttached && IsInstanceValid(_leftBody)
        ? _leftAnchor.DistanceTo(_leftBody!.ToGlobal(_leftLocal))
        : RightHookAttached && IsInstanceValid(_rightBody) ? _rightAnchor.DistanceTo(_rightBody!.ToGlobal(_rightLocal)) : 0;
    public bool Grappling => LeftHookAttached || RightHookAttached;

    internal void SetGrapple(bool left, Vector3 leftAnchor, bool right, Vector3 rightAnchor, int boosts, Enemy? leftBody = null, Vector3 leftLocal = default, Enemy? rightBody = null, Vector3 rightLocal = default)
    {
        if (left && !LeftHookAttached && !_leftFiring) _leftTip = CenterPosition;
        if (right && !RightHookAttached && !_rightFiring) _rightTip = CenterPosition;
        LeftHookAttached = left; RightHookAttached = right;
        _leftAnchor = leftAnchor; _rightAnchor = rightAnchor; HookBoosts = boosts;
        _leftBody = leftBody; _rightBody = rightBody; _leftLocal = leftLocal; _rightLocal = rightLocal;
        _leftGeneration = leftBody?.SpawnGeneration ?? 0; _rightGeneration = rightBody?.SpawnGeneration ?? 0;
        _grappleReceived = Time.GetTicksMsec() / 1000.0;
    }

    internal void SetHookFlights(bool left, Vector3 leftTip, bool right, Vector3 rightTip)
    {
        _leftFiring = left; _rightFiring = right;
        if (left) _leftTip = leftTip;
        if (right) _rightTip = rightTip;
    }

    private void DrawGrapple()
    {
        if (_leftBody is not null)
        {
            LeftHookAttached &= IsInstanceValid(_leftBody) && _leftBody.IsAlive && _leftBody.SpawnGeneration == _leftGeneration;
            if (!LeftHookAttached) _leftFiring = false;
            if (LeftHookAttached) _leftAnchor = _leftBody.ToGlobal(_leftLocal);
        }
        if (_rightBody is not null)
        {
            RightHookAttached &= IsInstanceValid(_rightBody) && _rightBody.IsAlive && _rightBody.SpawnGeneration == _rightGeneration;
            if (!RightHookAttached) _rightFiring = false;
            if (RightHookAttached) _rightAnchor = _rightBody.ToGlobal(_rightLocal);
        }
        double now = Time.GetTicksMsec() / 1000.0;
        float travel = Mathf.Clamp((float)(now - _drawAt), 0, .05f) * 240;
        _drawAt = now;
        if (LeftHookAttached || _leftFiring) _leftTip = _leftTip.MoveToward(_leftAnchor, travel);
        if (RightHookAttached || _rightFiring) _rightTip = _rightTip.MoveToward(_rightAnchor, travel);
        bool valid = Game.Arena.IsCity && !Down && Time.GetTicksMsec() / 1000.0 - _grappleReceived < 0.4;
        Vector3 side = new(Mathf.Cos(_netYaw), 0f, -Mathf.Sin(_netYaw));
        for (int i = 0; i < 2; i++)
        {
            bool attached = valid && (i == 0 ? LeftHookAttached || _leftFiring : RightHookAttached || _rightFiring);
            if (_grappleCables[i] is null && attached)
            { _grappleCables[i] = new GrappleTether(); AddChild(_grappleCables[i]!); }
            _grappleCables[i]?.Set(CenterPosition + side * (i == 0 ? -.32f : .32f), i == 0 ? _leftTip : _rightTip, attached);
        }
    }
}

