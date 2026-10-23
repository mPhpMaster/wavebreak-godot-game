using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Data;
using Wavebreak.Game;

namespace Wavebreak.UI;

/// <summary>
/// --icon-sheet=&lt;prefix&gt;: photograph every weapon, item and tome icon on
/// contact sheets (&lt;prefix&gt;-weapons.png, -items.png, -tomes.png) and quit.
/// The way to review the icon map as a whole, which a level-up roll never shows.
/// </summary>
public static class IconSheet
{
    public static async Task Capture(SceneTree tree, Node host, string prefix)
    {
        async Task Wait(double s)
            => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);

        tree.Paused = true;
        foreach (string clash in Icons.Collisions()) GD.Print("ICON CLASH " + clash);
        var pages = new List<(string Name, List<(IconSpec Spec, Rarity Rarity, string Label)> Rows)>
        {
            ("weapons", Catalog.Weapons.Concat(Meta.MetaContent.Weapons)
                .Select(w => (Icons.For(w), w.Rarity, w.DisplayName)).ToList()),
            ("items", Catalog.Items.Concat(Meta.MetaContent.Items)
                .Select(i => (Icons.For(i), i.Rarity, i.DisplayName)).ToList()),
            ("tomes", Tomes.All.Select(t => (Icons.For(t), t.Rarity, t.DisplayName))
                .Append((Icons.ForSupply(0.35f, 0), Rarity.Common, "Field Dressing"))
                .Append((Icons.ForSupply(0f, 60), Rarity.Common, "Loose Change"))
                .Append((Icons.ForSupply(0.18f, 35), Rarity.Common, "Supply Cache"))
                .ToList()),
        };

        foreach (var (name, rows) in pages)
        {
            var layer = new CanvasLayer { Layer = 120, ProcessMode = Node.ProcessModeEnum.Always };
            var back = new ColorRect { Color = new Color(0.07f, 0.08f, 0.11f) };
            back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(back);

            int cols = rows.Count > 60 ? 12 : 10;
            float icon = rows.Count > 60 ? 50f : 64f;
            var grid = new GridContainer { Columns = cols };
            grid.AddThemeConstantOverride("h_separation", 4);
            grid.AddThemeConstantOverride("v_separation", 2);
            grid.Position = new Vector2(10f, 8f);
            layer.AddChild(grid);

            foreach (var (spec, rarity, label) in rows)
            {
                var cell = new VBoxContainer { CustomMinimumSize = new Vector2(1260f / cols - 4f, 0f) };
                cell.AddThemeConstantOverride("separation", 0);
                var view = IconView.Make(icon);
                view.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
                view.Set(spec, Catalog.RarityColor(rarity));
                cell.AddChild(view);
                var text = new Label { Text = label, HorizontalAlignment = HorizontalAlignment.Center, ClipText = true };
                text.AddThemeFontSizeOverride("font_size", 11);
                cell.AddChild(text);
                grid.AddChild(cell);
            }

            host.AddChild(layer);
            await Wait(0.4);
            await Screenshot.Capture(tree, host, $"{prefix}-{name}.png");
            layer.QueueFree();
            await Wait(0.1);
        }
        tree.Quit();
    }
}
