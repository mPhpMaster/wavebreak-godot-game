using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Game;

namespace Wavebreak.UI;

public sealed partial class Minimap
{
    public int CaveMarkersDrawn { get; private set; }

    private void DrawCaveMarkers(Player player)
    {
        CaveMarkersDrawn = 0;
        if (!Arena.Current.Underwater || ReefCaveDiscovery.Instance is not { } discovery) return;
        for (int i = 0; i < 2; i++)
        {
            var kind = (CaveMapMarkerKind)i;
            if (!discovery.Discovered(kind)) continue;
            Vector3 world = ReefCaveDiscovery.PortalPosition(kind);
            Vector2 at = Pinned(world, 18, out _);
            CaveMapPresentation.Icon(this, at, kind, 5);
            float row = Size.Y + 15 + CaveMarkersDrawn * 22;
            CaveMapPresentation.Icon(this, new Vector2(8, row), kind, 4);
            CaveMapPresentation.Label(this, CaveMapPresentation.Name(kind, compact: true) + "  "
                + CaveMapPresentation.Elevation(world, player.GlobalPosition), new Vector2(Size.X * 0.5f + 6, row), 11);
            CaveMarkersDrawn++;
        }
    }
}
