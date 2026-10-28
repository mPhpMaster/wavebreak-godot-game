using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Game;

namespace Wavebreak.UI;

public sealed partial class BigMap
{
    public int CaveMarkersDrawn { get; private set; }

    private void DrawCaveMarkers()
    {
        CaveMarkersDrawn = 0;
        if (!Arena.Current.Underwater || ReefCaveDiscovery.Instance is not { } discovery || Player.Instance is not { } player) return;
        for (int i = 0; i < 2; i++)
        {
            var kind = (CaveMapMarkerKind)i;
            if (!discovery.Discovered(kind)) continue;
            Vector3 world = ReefCaveDiscovery.PortalPosition(kind);
            Vector2 at = Map(world);
            CaveMapPresentation.Icon(this, at, kind, 7);
            string text = CaveMapPresentation.Name(kind) + "  " + CaveMapPresentation.Elevation(world, player.GlobalPosition);
            float half = ThemeDB.FallbackFont.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X * 0.5f + 5;
            Vector2 label = at + new Vector2(0, -22);
            label.X = Mathf.Clamp(label.X, _rect.Position.X + half + 3, _rect.End.X - half - 3);
            label.Y = Mathf.Clamp(label.Y, _rect.Position.Y + 16, _rect.End.Y - 16);
            CaveMapPresentation.Label(this, text, label, 13);
            CaveMarkersDrawn++;
        }
    }
}
