using Godot;

namespace Wavebreak.Actors;

public sealed partial class Pickup
{
    private float _supportIn;
    private static PhysicsRayQueryParameters3D? _supportQuery;
    private static Rid _supportPlayer;
    private static bool _supportExcluded;

    private float FindSupport(Vector3 at)
    {
        _supportQuery ??= PhysicsRayQueryParameters3D.Create(at, at, 1u);
        _supportQuery.From = at + Vector3.Up * 0.3f;
        _supportQuery.To = at + Vector3.Down * 800f;
        if (Player.Instance is { } player && (!_supportExcluded || _supportPlayer != player.GetRid()))
        {
            _supportPlayer = player.GetRid(); _supportExcluded = true;
            _supportQuery.Exclude = new Godot.Collections.Array<Rid> { _supportPlayer };
        }
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(_supportQuery);
        float ground = hit.TryGetValue("position", out Variant position) ? position.AsVector3().Y : 0f;
        return ground + (_kind == PickupKind.Health ? RestHeight : _kind == PickupKind.Gold ? 0.025f : 0.12f);
    }

    private void UpdateSupport(float delta, Vector3 at)
    {
        _supportIn -= delta;
        if (_supportIn > 0f) return;
        _supportIn = 0.15f;
        _restY = FindSupport(at with { Y = Mathf.Max(at.Y, _restY + 0.1f) });
    }

    private static void ClearSupportQuery()
    {
        _supportQuery?.Dispose(); _supportQuery = null; _supportExcluded = false;
    }
}
