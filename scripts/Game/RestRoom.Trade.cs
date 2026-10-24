using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

/// <summary>
/// The swap meet: a beaver's stall at the front left of the island, where
/// co-op players trade gold, items and tomes with each other (Game/Trade,
/// Net/NetSession.Trade.cs, UI/TradePanel).
///
/// <b>Single-player:</b> there is nobody to trade with, so the stall is not
/// built and the room is exactly what it was. <c>--swap-meet</c> builds it
/// anyway (captures); pressing E then says "Trading needs another player".
/// A co-op player left alone in a session gets the same message.
/// </summary>
public sealed partial class RestRoom
{
    private static readonly Vector3 TraderAt = new(-6.6f, 0f, 4.6f);

    /// <summary>The trader's stall, or null (single-player).</summary>
    public Trader? Trader { get; private set; }

    public static bool SwapMeetForced { get; } = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--swap-meet") >= 0;

    private void BuildSwapMeet()
    {
        if (!Net.Coop.InRun && !SwapMeetForced) return;
        Trader = Place(new Trader(), TraderAt);
        var layer = new CanvasLayer { Name = "TradeUI", Layer = 6 };
        AddChild(layer);
        layer.AddChild(new UI.TradeUi());
    }
}
