using Godot;

namespace Wavebreak.UI;

/// <summary>Original destination silhouettes scaled to the card bounds.</summary>
public sealed partial class ExpeditionPreview : Control
{
    public bool City { get; init; }
    public override void _Draw()
    {
        for (int i = 0; i < 24; i++)
        {
            float t = i / 23f;
            Color top = City ? new Color(.32f, .32f, .43f) : new Color(.045f, .35f, .42f);
            Color bottom = City ? new Color(.10f, .16f, .24f) : new Color(.025f, .10f, .20f);
            DrawRect(new Rect2(0, i * Size.Y / 24f, Size.X, Size.Y / 24f + 1), top.Lerp(bottom, t));
        }
        if (City) DrawCity(); else DrawReef();
    }

    private void DrawCity()
    {
        DrawCircle(new Vector2(Size.X * .79f, 27), 17, new Color(.97f, .75f, .48f));
        for (int layer = 0; layer < 2; layer++)
            for (int i = 0; i < 10; i++)
            {
                float width = Size.X / 9f;
                float x = i * width - layer * 19;
                float height = 24 + (i * 31 + layer * 17) % 43;
                float y = Size.Y - height + (layer == 0 ? -13 : 10);
                Color wall = layer == 0 ? new Color(.23f, .29f, .36f) : new Color(.48f, .48f, .47f);
                DrawRect(new Rect2(x, y, width - 8, height), wall);
                DrawColoredPolygon(new[] { new Vector2(x - 3, y), new Vector2(x + (width - 8) / 2, y - 17), new Vector2(x + width - 5, y) }, new Color(.25f, .19f, .23f));
                if (layer == 1)
                    for (float wy = y + 9; wy < Size.Y - 6; wy += 14)
                        DrawRect(new Rect2(x + 9, wy, 5, 7), new Color(.97f, .76f, .40f));
            }
        DrawLine(new Vector2(Size.X * .28f, 31), new Vector2(Size.X * .57f, 60), new Color(.64f, .94f, .92f), 1.5f, true);
        DrawCircle(new Vector2(Size.X * .57f, 60), 3, Colors.White);
    }

    private void DrawReef()
    {
        for (int i = 0; i < 5; i++)
            DrawColoredPolygon(new[] { new Vector2(35 + i * 70, 0), new Vector2(60 + i * 70, 0), new Vector2(10 + i * 70, Size.Y) }, new Color(.35f, .85f, .85f, .06f));
        DrawCircle(new Vector2(Size.X * .79f, Size.Y + 6), 65, new Color(.07f, .22f, .28f));
        DrawCircle(new Vector2(Size.X * .79f, Size.Y + 6), 41, new Color(.025f, .085f, .14f));
        for (int i = 0; i < 14; i++)
        {
            float x = i * Size.X / 14;
            float y = Size.Y - 8 - i * 7 % 28;
            Color coral = i % 3 == 0 ? new Color(.86f, .45f, .47f) : new Color(.16f, .58f, .56f);
            DrawLine(new Vector2(x, Size.Y), new Vector2(x + 5, y), coral, 4, true);
            DrawLine(new Vector2(x + 4, y + 9), new Vector2(x - 3, y + 1), coral, 3, true);
            DrawCircle(new Vector2(x + 13, 15 + i * 13 % 45), 1.5f, new Color(.49f, .88f, .88f, .7f));
        }
    }
}
