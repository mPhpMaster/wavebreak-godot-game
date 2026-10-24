using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The minimap's map layer: the explored part of the map (UI/FogOfWar's
/// texture: floor, wall, raised ground and blockers, dark where nobody has
/// been) clipped to the disc under the markers, and the name of the zone you
/// are in under the disc on maps that have zones.
/// </summary>
public sealed partial class Minimap
{
    private const int FogSegments = 56;
    private readonly Vector2[] _fogPoints = new Vector2[FogSegments];
    private readonly Vector2[] _fogUvs = new Vector2[FogSegments];
    private readonly Color[] _fogColors = new Color[FogSegments];

    private void DrawFog()
    {
        if (FogOfWar.Instance is not { Active: true, Texture: { } tex } fog) return;
        float r = _radius - 1.5f;
        for (int i = 0; i < FogSegments; i++)
        {
            float a = Mathf.Tau * i / FogSegments;
            var o = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            _fogPoints[i] = _centre + o;
            // North-up: a map offset (dx, dz) * scale is the world offset.
            _fogUvs[i] = fog.Uv(_origin.X + o.X / _scale, _origin.Z + o.Y / _scale);
            _fogColors[i] = Colors.White;
        }
        DrawPolygon(_fogPoints, _fogColors, _fogUvs, tex);
    }

    private void DrawZoneName(Player me)
    {
        if (MapBlockers.Zones.Count == 0 || MapBlockers.ZoneAt(me.GlobalPosition) is not { } zone) return;
        string text = Loc.T(zone.Name);
        Font font = ThemeDB.FallbackFont;
        const int size = 13;
        Vector2 ext = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var at = new Vector2(_centre.X - ext.X * 0.5f, Size.Y + 16f);
        DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, size, 4, Ink);
        DrawString(font, at, text, HorizontalAlignment.Left, -1, size, Rim);
    }
}
