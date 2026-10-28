using Godot;

namespace Wavebreak.Data;

public sealed class CityPathState
{
    public Vector2I[] Points = System.Array.Empty<Vector2I>();
    public int Next;
    public float Repath;
    public Vector2I Goal;
    public Vector2I TargetCell;
    public void Clear() { Points = System.Array.Empty<Vector2I>(); Next = 0; Repath = 0f; }
}
