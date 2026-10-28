using System.Collections.Generic;
using Godot;

namespace Wavebreak.UI;

public static class ExpeditionPortraits
{
    private static readonly Dictionary<string, Texture2D> Cache = new();
    public static Texture2D? For(string id)
    {
        if (id is not ("nori" or "vesper")) return null;
        if (Cache.TryGetValue(id, out Texture2D? texture)) return texture;
        string shapes = id == "nori"
            ? "<path fill='#ff9960' d='M14 34L2 21v26zM32 8l13 16H23z'/><ellipse cx='35' cy='34' rx='25' ry='20' fill='#29c4b9'/><ellipse cx='45' cy='29' rx='8' ry='9' fill='white'/><circle cx='48' cy='29' r='4' fill='#162a3c'/><path d='M21 32v9m5-9v9' stroke='#126864' stroke-width='3'/>"
            : "<path fill='#874c2c' d='M5 64V43l17-8h20l17 8v21z'/><path fill='#182e3c' d='M17 39l27 25h9L26 36zm29 0L19 64h-9l27-28z'/><circle cx='32' cy='24' r='17' fill='#e6b988'/><path fill='#1c303c' d='M15 24V13L24 5h18l9 10v11L39 15l-14 4z'/><path fill='#338c91' d='M15 39h34v9H15z'/><path d='M55 5v17q0 12-10 12q-8 0-8-8' fill='none' stroke='#c9e8ec' stroke-width='5'/>";
        var image = new Image();
        if (image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 64 64'>" + shapes + "</svg>") != Error.Ok) return null;
        texture = ImageTexture.CreateFromImage(image);
        Cache[id] = texture;
        return texture;
    }
}
