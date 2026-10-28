using Godot;

namespace Wavebreak.UI;

/// <summary>Small original map illustrations, drawn in the existing menu palette.</summary>
public sealed partial class ExpeditionPreview : Control
{
    public bool City { get; init; }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), City ? new Color(0.12f, 0.2f, 0.28f) : new Color(0.02f, 0.23f, 0.31f));
        if (City)
        {
            for (int i = 0; i < 12; i++)
            {
                float height = 22f + i * 19f % 57f;
                float x = i * Size.X / 12f;
                DrawRect(new Rect2(x, Size.Y - height, 24f, height), new Color(0.48f, 0.65f, 0.68f));
                for (float y = Size.Y - height + 5f; y < Size.Y - 4f; y += 9f)
                    DrawRect(new Rect2(x + 5f, y, 4f, 4f), Colors.Gold);
            }
            DrawLine(new Vector2(60f, 12f), new Vector2(150f, 48f), Colors.Aquamarine, 2f);
            DrawCircle(new Vector2(150f, 48f), 4f, Colors.White);
        }
        else
        {
            for (int i = 0; i < 16; i++)
            {
                float x = i * Size.X / 16f;
                DrawLine(new Vector2(x, Size.Y), new Vector2(x + 8f, 48f + i * 7f % 29f), Colors.SeaGreen, 5f);
                DrawCircle(new Vector2(x + 12f, 22f + i * 13f % 40f), 2f, Colors.Aquamarine);
            }
            DrawCircle(new Vector2(Size.X * 0.65f, 37f), 12f, Colors.Coral);
            DrawColoredPolygon(new[] { new Vector2(Size.X * 0.65f + 8f, 37f),
                new Vector2(Size.X * 0.65f + 27f, 25f), new Vector2(Size.X * 0.65f + 27f, 49f) }, Colors.Coral);
        }
    }
}
