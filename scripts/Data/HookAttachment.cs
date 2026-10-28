using Godot;

namespace Wavebreak.Data;

public enum HookSide { Left, Right }

public sealed class HookAttachment
{
    public bool Attached;
    public bool Wanted;
    public bool Firing;
    public float FlightTime, FlightDuration;
    public Vector3 LaunchOrigin, Tip;
    public Node3D? Target;
    public Vector3 LocalAnchor;
    public int TargetGeneration;
    public Vector3 Anchor;
    public float Length;
    public float SightIn;
    public bool BlockedUntilRelease;
    public void Release() { Attached = false; Firing = false; Wanted = false; Target = null; SightIn = 0f; }
}
