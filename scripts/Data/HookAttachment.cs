using Godot;

namespace Wavebreak.Data;

public enum HookSide { Left, Right }

public sealed class HookAttachment
{
    public bool Attached;
    public Vector3 Anchor;
    public float Length;
    public float SightIn;
    public bool BlockedUntilRelease;
    public void Release() { Attached = false; SightIn = 0f; }
}
