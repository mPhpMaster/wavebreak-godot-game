using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class Interactables
{
    // ------------------------------------------------------------ autopilot

    private float _nextBotAction;
    private float _botClock;

    /// <summary>True when the last <see cref="AutopilotSteer"/> found a wanted
    /// station in reach: the bot is using it and should stay put.</summary>
    public bool BotBusy { get; private set; }

    /// <summary>
    /// The autopilot's errand: walk to the merchant when it can afford something
    /// there (or has not looked yet and holds enough for the cheapest stock) and
    /// buy the dearest thing it can; use the microwave on its rarest non-maxed
    /// item when it can pay. Buys go straight through <see cref="TryBuy"/>
    /// rather than the panel, which pauses the tree and would stall an
    /// unattended run, exactly as level-ups skip their panel in auto runs.
    /// </summary>
    public Vector3 AutopilotSteer(Vector3 me, float nearestEnemy, float dt)
    {
        _botClock += dt;
        BotBusy = false;
        Inventory? inv = Inventory.Current;
        if (inv is null || _stations.Count == 0) return Vector3.Zero;

        Station? target = null;
        float bestSq = 50f * 50f;
        foreach (Station s in _stations)
        {
            if (!IsInstanceValid(s) || !s.Usable || !WantsToUse(s, inv)) continue;
            if (s.InReach(me))
            {
                BotBusy = true;
                if (_botClock >= _nextBotAction)
                {
                    _nextBotAction = _botClock + 0.6f;
                    BotUse(s, inv);
                }
                continue;
            }
            float sq = Flat(s.GlobalPosition).DistanceSquaredTo(Flat(me));
            if (sq < bestSq) { bestSq = sq; target = s; }
        }

        if (target is null || nearestEnemy < 6f) return Vector3.Zero;
        Vector3 d = target.GlobalPosition - me; d.Y = 0f;
        float dist = d.Length();
        float safety = Mathf.Clamp((nearestEnemy - 6f) / 4f, 0f, 1f);
        return dist < 0.3f ? Vector3.Zero : d / dist * 2.6f * safety;
    }

    private bool WantsToUse(Station s, Inventory inv)
    {
        switch (s)
        {
            case Merchant m when m.Offers is null:
                return inv.Gold >= MerchantPrice(MerchantFloor);
            case Merchant m:
                for (int i = 0; i < m.Offers!.Length; i++)
                    if (m.Offers[i]?.Item is { } item && !inv.IsMaxed(item) && inv.Gold >= m.Prices[i]) return true;
                return false;
            case Microwave { Used: false }:
                return BestCook(inv) is not null;
            case TideTotem totem:
                return BotWantsTotem(totem);
            case Campfire fire:
                // Once per room, and again whenever hurt.
                return fire.Rests == 0 || Player.Instance is { } p && p.Health.Current < p.Health.Max - 0.5f;
            default:
                return false;
        }
    }

    private ItemDef? BestCook(Inventory inv)
    {
        ItemDef? best = null;
        foreach (ItemStack st in inv.Items)
        {
            if (st.Count <= 0 || inv.IsMaxed(st.Def) || inv.Gold < MicrowavePrice(st.Rarity)) continue;
            if (best is null || st.Rarity > best.Rarity) best = st.Def;
        }
        return best;
    }

    private void BotUse(Station s, Inventory inv)
    {
        if (s is Merchant m)
        {
            EnsureStocked(m);
            int pick = -1;
            for (int i = 0; i < m.Offers!.Length; i++)
                if (m.Offers[i]?.Item is { } item && !inv.IsMaxed(item) && inv.Gold >= m.Prices[i]
                    && (pick < 0 || m.Prices[i] > m.Prices[pick]))
                    pick = i;
            if (pick >= 0) TryBuy(m, pick);
        }
        else if (s is Microwave mw && BestCook(inv) is { } def)
        {
            TryCook(mw, def);
        }
        else if (s is Campfire fire)
        {
            Rest(fire);
        }
        else if (s is TideTotem totem)
        {
            UseTotem(totem);
        }
    }

    // ---------------------------------------------------------------- audit

    /// <summary>Data checks for <see cref="ContentAudit"/>: per-map counts in
    /// their intended ranges and prices that rise with rarity and phase.</summary>
    public static void Audit(Action<bool, string> check)
    {
        foreach (StageDef s in Data.Stages.Run)
        {
            check(s.Breakables is >= 25 and <= 40, $"stage {s.Id} breakables {s.Breakables} outside 25-40");
            // The merchant lives in the rest room between maps now (Game/RestRoom).
            check(s.Merchants == 0, $"stage {s.Id} places {s.Merchants} merchants; the shop belongs to the rest room");
            check(s.Microwaves >= 1, $"stage {s.Id} has no microwave");
        }
        check(MerchantBase.Length == 5 && MicrowaveBase.Length == 5, "station price tables cover every rarity");
        for (int r = 1; r < MerchantBase.Length; r++)
        {
            check(MerchantBase[r] > MerchantBase[r - 1], $"merchant price not rising at rarity {r}");
            check(MicrowaveBase[r] > MicrowaveBase[r - 1], $"microwave price not rising at rarity {r}");
        }
        for (int r = 0; r < MerchantBase.Length; r++)
            check(MicrowaveBase[r] < MerchantBase[r], $"microwave dearer than merchant at rarity {r}");
        TotemAudit(check);   // Interactables.Totems.cs
    }
}
