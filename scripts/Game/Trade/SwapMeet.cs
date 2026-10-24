using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Meta;

namespace Wavebreak.Game.Trade;

/// <summary>What pressing interact at the swap meet does, and what its
/// prompt says. The station itself is <see cref="Trader"/>; the windows are
/// <see cref="UI.TradeUi"/>.</summary>
public static class SwapMeet
{
    public static bool HasPartners => Net.Coop.InRun && Net.NetSession.Instance is { } net && net.TradePartners().Count > 0;

    public static string Prompt(string key)
    {
        if (TradeView.Open) return Loc.F("{0} — back to your trade", key);
        if (!HasPartners) return Loc.F("{0} — swap meet (trading needs another player)", key);
        return Loc.F("{0} — swap with a teammate", key);
    }

    public static void Interact(Trader trader)
    {
        UI.TradeUi? ui = UI.TradeUi.Instance;
        if (TradeView.Open) { ui?.ShowTrade(); return; }
        if (!HasPartners)
        {
            EventBus.Publish(new PopupRequested(trader.Top, Loc.T("Trading needs another player"), Trader.TraderTint.Lerp(Colors.White, 0.4f)));
            GD.Print("[trade] swap meet: nobody to trade with");
            return;
        }
        ui?.ShowPicker();
    }
}
