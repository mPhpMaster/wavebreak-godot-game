using System.Collections.Generic;
using System.Text;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Progression;

namespace Wavebreak.UI;

/// <summary>
/// The shady merchant's shop: three item cards with a price each. 1/2/3 or a
/// click buys; a bought slot shows SOLD. Esc or the interact key leaves.
/// </summary>
public sealed partial class MerchantPanel : ModalPanel
{
    private Merchant? _merchant;
    private Label _gold = null!;
    private readonly List<(Label rarity, Label title, Label effect, Button buy, PanelContainer card, IconView icon)> _cards = new();

    public override void _Ready()
    {
        Name = "MerchantPanel";
        base._Ready();

        Body.AddChild(MakeLabel("SHADY MERCHANT", 34, Merchant.MerchantTint));
        Body.AddChild(MakeLabel("\"Everything must go. Mostly to you.\"", 15, InkDim));
        _gold = MakeLabel("", 18, Gold);
        Body.AddChild(_gold);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        Body.AddChild(row);

        for (int i = 0; i < Merchant.SlotCount; i++)
        {
            var card = new PanelContainer { CustomMinimumSize = new Vector2(250f, 280f) };
            row.AddChild(card);
            var col = new VBoxContainer();
            col.AddThemeConstantOverride("separation", 8);
            card.AddChild(col);

            Label key = MakeLabel($"{i + 1}", 14, InkDim, HorizontalAlignment.Left);
            Label rarity = MakeLabel("", 14, Ink);
            Label title = MakeLabel("", 21, Ink);
            title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            Label effect = MakeLabel("", 14, Ink);
            effect.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            effect.SizeFlagsVertical = SizeFlags.ExpandFill;
            int slot = i;
            Button buy = MakeButton("", () => Buy(slot));

            IconView icon = IconView.Make(64f);
            icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

            col.AddChild(key);
            col.AddChild(icon);
            col.AddChild(rarity);
            col.AddChild(title);
            col.AddChild(effect);
            col.AddChild(buy);
            _cards.Add((rarity, title, effect, buy, card, icon));
        }

        Body.AddChild(MakeLabel("press  1 · 2 · 3  to buy      Esc / E to leave", 14, InkDim));
    }

    public void Open(Merchant merchant)
    {
        _merchant = merchant;
        Fill();
        OpenPanel();
    }

    protected override void OnClosed() => _merchant = null;

    protected override bool OnNumber(int index)
    {
        if (index >= Merchant.SlotCount) return false;
        Buy(index);
        return true;
    }

    private void Buy(int slot)
    {
        if (_merchant is null || !IsInstanceValid(_merchant)) { Close(); return; }
        Interactables.Instance?.TryBuy(_merchant, slot);
        Fill();
    }

    private void Fill()
    {
        if (_merchant is null) return;
        int gold = Inventory.Current?.Gold ?? 0;
        _gold.Text = $"Your gold: {gold}";

        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            Offer? offer = _merchant.Offers is { } offers && i < offers.Length ? offers[i] : null;
            if (offer?.Item is not { } item)
            {
                c.rarity.Text = "";
                c.icon.Visible = false;
                c.title.Text = "SOLD";
                c.title.AddThemeColorOverride("font_color", InkDim);
                c.effect.Text = "";
                c.buy.Text = "—";
                c.buy.Disabled = true;
                c.card.AddThemeStyleboxOverride("panel", Box(new Color(0.06f, 0.06f, 0.08f, 0.95f), new Color(1f, 1f, 1f, 0.06f)));
                continue;
            }

            Color colour = Catalog.RarityColor(item.Rarity);
            int price = _merchant.Prices[i];
            bool maxed = Inventory.Current?.IsMaxed(item) == true;
            bool afford = gold >= price;
            int held = Inventory.Current?.StacksOf(item) ?? 0;

            c.icon.Visible = true;
            c.icon.Set(Icons.For(item), colour);
            c.rarity.Text = Catalog.RarityName(item.Rarity) + (held > 0 ? $"   · held x{held}" : "   · NEW");
            c.rarity.AddThemeColorOverride("font_color", colour);
            c.title.Text = item.DisplayName;
            c.title.AddThemeColorOverride("font_color", item.Tint.Lerp(Ink, 0.35f));
            c.effect.Text = Effects(item);
            c.buy.Text = maxed ? "MAXED" : afford ? $"Buy · {price} gold" : $"{price} gold (need {price - gold})";
            c.buy.Disabled = maxed || !afford;
            c.card.AddThemeStyleboxOverride("panel", Box(new Color(0.10f, 0.11f, 0.15f, 0.98f), colour.Darkened(0.2f)));
        }
    }

    private static string Effects(ItemDef item)
    {
        if (item.Effects is null || item.Effects.Count == 0) return item.Description;
        var sb = new StringBuilder();
        foreach (ItemEffect e in item.Effects)
        {
            if (e is null) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Catalog.DescribeEffect(e));
        }
        return sb.ToString();
    }
}
