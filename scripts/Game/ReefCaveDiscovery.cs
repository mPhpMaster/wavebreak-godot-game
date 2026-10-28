using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Net;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>Portal discovery requires visiting its 3D location with an unobstructed view.</summary>
public sealed partial class ReefCaveDiscovery : Node3D
{
    public const float DiscoverRadius = 18f;
    public static ReefCaveDiscovery? Instance { get; private set; }
    public int Mask { get; private set; }
    private float _tick;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }
    public static Vector3 PortalPosition(CaveMapMarkerKind kind) => kind == CaveMapMarkerKind.Entrance ? ReefCavePath.Entrance : ReefCavePath.Exit;
    public bool Discovered(CaveMapMarkerKind kind) => (Mask & (1 << (int)kind)) != 0;
    public void Merge(int mask) => Mask |= mask & 3;

    public bool CanDiscover(Vector3 observer, CaveMapMarkerKind kind)
    {
        Vector3 at = PortalPosition(kind);
        return observer.DistanceSquaredTo(at) <= DiscoverRadius * DiscoverRadius
            && FogOfWar.Instance is { Active: true } && FogOfWar.IsRevealed(at)
            && WorldSight.Clear(this, observer, at + Vector3.Up * 0.5f);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick -= (float)delta;
        if (_tick > 0 || Mask == 3 || !Arena.Current.Underwater) return;
        _tick = 0.2f;
        int before = Mask;
        if (Player.Instance is { IsAlive: true } me) DiscoverAt(me.CenterPosition);
        if (NetSession.Instance is { } session && Coop.InRun)
            foreach (RemotePlayer remote in session.Remotes.Values)
                if (IsInstanceValid(remote) && remote.HasState && remote.IsAlive) DiscoverAt(remote.GlobalPosition + Vector3.Up);
        if (Mask != before && Coop.IsHost) NetSession.Instance?.BroadcastCaveDiscovery(Mask);
    }

    private void DiscoverAt(Vector3 observer)
    {
        foreach (CaveMapMarkerKind kind in new[] { CaveMapMarkerKind.Entrance, CaveMapMarkerKind.Exit })
            if (!Discovered(kind) && CanDiscover(observer, kind)) Merge(1 << (int)kind);
    }
}
