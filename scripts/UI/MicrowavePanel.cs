using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Progression;

namespace Wavebreak.UI;

/// <summary>
/// The microwave: every held item as a button with its stack and the price to
/// duplicate it. Maxed and unaffordable items are disabled. One success closes
/// the panel, because the microwave only works once.
/// </summary>
public sealed partial class MicrowavePanel : ModalPanel
{
    private Microwave? _microwave;
    private Label _gold = null!;
    private Label _empty = null!;
    private GridContainer _grid = null!;
    private readonly List<ItemStack> _shown = new();

    public override void _Ready()
    {
        Name = "MicrowavePanel";
        base._Ready();

        Body.AddChild(MakeLabel("MICROWAVE", 34, Microwave.MicrowaveTint));
        Body.AddChild(MakeLabel("Put one item in, get one more stack out. Works once.", 15, InkDim));
        _gold = MakeLabel("", 18, Gold);
        Body.AddChild(_gold);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(720f, 320f) };
        Body.AddChild(scroll);
        _grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(_grid);

        _empty = MakeLabel("You have nothing to put in it.", 16, InkDim);
        Body.AddChild(_empty);

        Body.AddChild(MakeLabel("click an item or press 1-9      Esc / E to leave", 14, InkDim));
    }

    public void Open(Microwave microwave)
    {
        _microwave = microwave;
        Fill();
        OpenPanel();
    }

    protected override void OnClosed() => _microwave = null;

    protected override bool OnNumber(int index)
    {
        if (index >= _shown.Count) return false;
        Cook(_shown[index]);
        return true;
    }

    private void Cook(ItemStack stack)
    {
        if (_microwave is null || !IsInstanceValid(_microwave)) { Close(); return; }
        if (Interactables.Instance?.TryCook(_microwave, stack.Def) == true) Close();
        else Fill();
    }

    private void Fill()
    {
        foreach (Node c in _grid.GetChildren()) { _grid.RemoveChild(c); c.QueueFree(); }
        _shown.Clear();

        Inventory? inv = Inventory.Current;
        int gold = inv?.Gold ?? 0;
        _gold.Text = $"Your gold: {gold}";
        if (inv is null || _microwave is null) return;

        Interactables? hub = Interactables.Instance;
        foreach (ItemStack stack in inv.Items)
        {
            if (stack.Count <= 0) continue;
            ItemStack captured = stack;
            int price = hub?.MicrowavePrice(stack.Def.Rarity) ?? 0;
            bool maxed = inv.IsMaxed(stack.Def);
            string cap = stack.Def.MaxStacks > 0 ? $"/{stack.Def.MaxStacks}" : "";
            int n = _shown.Count + 1;
            string key = n <= 9 ? $"{n}. " : "";
            Button b = MakeButton($"{key}{stack.DisplayName}  x{stack.Count}{cap}\n" +
                                  (maxed ? "MAXED" : $"{price} gold"), () => Cook(captured));
            b.CustomMinimumSize = new Vector2(232f, 58f);
            b.AddThemeColorOverride("font_color", Catalog.RarityColor(stack.Rarity));
            b.AddThemeColorOverride("font_disabled_color", Catalog.RarityColor(stack.Rarity).Darkened(0.5f));
            b.Disabled = maxed || gold < price;
            IconSpec icon = Icons.For(stack.Def);
            b.Icon = Icons.Texture(icon.Glyph);
            b.AddThemeConstantOverride("icon_max_width", 34);
            foreach (string state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color", "icon_hover_pressed_color" })
                b.AddThemeColorOverride(state, icon.Color);
            b.AddThemeColorOverride("icon_disabled_color", icon.Color.Darkened(0.5f));
            _grid.AddChild(b);
            _shown.Add(stack);
        }
        _empty.Visible = _shown.Count == 0;
    }
}
