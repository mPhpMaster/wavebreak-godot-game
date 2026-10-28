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
    // ------------------------------------------------------------- merchant

    /// <summary>Stocks the merchant the first time anyone looks: three distinct
    /// items from the offer pool (same catalogue, caps and luck curve as a chest,
    /// floored at uncommon). A slot the pool cannot fill stays empty.</summary>
    public void EnsureStocked(Merchant m)
    {
        if (m.Offers is not null) return;
        var offers = new Offer?[Merchant.SlotCount];
        Inventory? inv = Inventory.Current;
        OfferPool? pool = OfferPool.Current;
        var taken = new HashSet<string>();
        for (int slot = 0; slot < offers.Length && pool is not null; slot++)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Offer o = pool.RollChest(inv, Player.Instance?.Stats, MerchantLuckBonus, MerchantFloor, Rng);
                if (o.Item is null) break;                    // pool exhausted: supply only
                if (!taken.Add(o.Item.Id)) continue;
                offers[slot] = o;
                m.Prices[slot] = MerchantPrice(o.Item.Rarity);
                break;
            }
        }
        // A held hero signature weapon with levels left takes the last slot:
        // the merchant hones it for gold (Rare-price), so it grows from the shop too.
        if (Main.Instance?.Weapons is { } ws)
            foreach (Combat.WeaponRuntime w in ws.Weapons)
                if (Catalog.SignatureIds.Contains(w.Def.Id) && ws.CanAdd(w.Def))
                {
                    int last = offers.Length - 1;
                    Combat.WeaponUpgrade up = Combat.WeaponUpgrades.Roll(w.Def, Rng, (Player.Instance?.Stats.Get(Stats.Stat.Luck) ?? 0f) + MerchantLuckBonus);
                    offers[last] = Offer.ForWeapon(w.Def, w.Level, up);
                    m.Prices[last] = MerchantPrice(Rarity.Rare) + 15 * w.Level;
                    break;
                }
        m.Offers = offers;
        m.Refresh();
        GD.Print($"[merchant] stocked phase={_phase} " + string.Join(" ",
                 Array.ConvertAll(offers, o => o?.Item is { } i ? $"{i.Id}:{i.Rarity}" : "-")));
    }

    /// <summary>Buys one slot. Refuses (and says why) when it is empty, maxed or
    /// unaffordable; the slot is emptied on success.</summary>
    public bool TryBuy(Merchant m, int slot)
    {
        EnsureStocked(m);
        Inventory? inv = Inventory.Current;
        if (inv is null || m.Offers is null || slot < 0 || slot >= m.Offers.Length) return false;
        if (m.Offers[slot] is { Weapon: { } wdef } wo) return TryBuyWeapon(m, slot, wdef, wo);
        if (m.Offers[slot]?.Item is not { } item) return false;

        if (inv.IsMaxed(item))
        {
            EventBus.Publish(new PopupRequested(m.Top, "MAXED", new Color(1f, 0.38f, 0.34f)));
            return false;
        }

        int price = m.Prices[slot];
        if (!inv.SpendGold(price))
        {
            EventBus.Publish(new PopupRequested(m.Top, $"need {price - inv.Gold} more gold", new Color(1f, 0.38f, 0.34f)));
            return false;
        }

        inv.Add(item);
        m.Offers[slot] = null;
        m.Refresh();
        MerchantBuys++;
        MerchantSpent += price;
        Color colour = Catalog.RarityColor(item.Rarity);
        EventBus.Publish(new PopupRequested(m.Top + Vector3.Up * 0.9f, item.DisplayName, colour, 1.6f));
        EventBus.Publish(new PopupRequested(m.Top, $"-{price} gold", new Color(1f, 0.78f, 0.25f)));
        GD.Print($"[merchant] bought {item.Id} rarity={item.Rarity} price={price} gold={inv.Gold}");
        return true;
    }

    private bool TryBuyWeapon(Merchant m, int slot, WeaponDef def, Offer offer)
    {
        Inventory? inv = Inventory.Current;
        if (inv is null || Main.Instance?.Weapons is not { } ws) return false;
        if (!ws.CanAdd(def))
        {
            EventBus.Publish(new PopupRequested(m.Top, "MAXED", new Color(1f, 0.38f, 0.34f)));
            return false;
        }
        int price = m.Prices[slot];
        if (!inv.SpendGold(price))
        {
            EventBus.Publish(new PopupRequested(m.Top, $"need {price - inv.Gold} more gold", new Color(1f, 0.38f, 0.34f)));
            return false;
        }
        ws.Add(def, offer.Upgrade);
        m.Offers![slot] = null;
        m.Refresh();
        MerchantBuys++;
        MerchantSpent += price;
        EventBus.Publish(new PopupRequested(m.Top + Vector3.Up * 0.9f, Meta.Loc.F("{0} → Lv {1}", def.DisplayName, ws.LevelOf(def.Id)), Catalog.RarityColor(offer.Rarity), 1.6f));
        EventBus.Publish(new PopupRequested(m.Top, $"-{price} gold", new Color(1f, 0.78f, 0.25f)));
        GD.Print($"[merchant] honed {def.Id} to lv{ws.LevelOf(def.Id)} price={price} gold={inv.Gold}");
        return true;
    }

    // ------------------------------------------------------------ microwave

    /// <summary>Duplicates one held item (+1 stack) for gold. Once per microwave;
    /// refuses an item at its stack cap or one not held.</summary>
    public bool TryCook(Microwave mw, ItemDef item)
    {
        Inventory? inv = Inventory.Current;
        if (inv is null || mw.Used || item is null) return false;
        if (inv.StacksOf(item) <= 0 || inv.IsMaxed(item)) return false;

        int price = MicrowavePrice(item.Rarity);
        if (!inv.SpendGold(price))
        {
            EventBus.Publish(new PopupRequested(mw.Top, $"need {price - inv.Gold} more gold", new Color(1f, 0.38f, 0.34f)));
            return false;
        }
        if (!inv.Add(item))
        {
            // Refund unscaled: a foreign GoldGained is credited as-is.
            EventBus.Publish(new GoldGained(price, inv.Gold + price));
            return false;
        }

        mw.MarkUsed();
        MicrowaveUses++;
        MicrowaveSpent += price;
        EventBus.Publish(new PopupRequested(mw.Top + Vector3.Up * 0.9f, $"{item.DisplayName} +1",
                                            Catalog.RarityColor(item.Rarity), 1.6f));
        EventBus.Publish(new PopupRequested(mw.Top, $"-{price} gold", new Color(1f, 0.78f, 0.25f)));
        GD.Print($"[microwave] duplicated {item.Id} x{inv.StacksOf(item)} price={price} gold={inv.Gold}");
        return true;
    }

    public void NotePotBroken() => PotsBroken++;

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("Interactables.proc");
        _stations.RemoveAll(s => !IsInstanceValid(s) || s.IsQueuedForDeletion());
        if (_map is not null && !IsInstanceValid(_map)) Clear();

        bool pressed = _interactRequested;
        _interactRequested = false;

        Player? player = Player.Instance;
        bool live = player is not null && player.IsAlive && Main.Instance?.Run.Over != true;
        _focus = live ? Focus(player!.GlobalPosition) : null;

        if (_focus is null)
        {
            _prompt.HideNow();
            return;
        }

        _prompt.ShowFor(_focus.Top, PromptText(_focus), _focus.Tint.Lerp(Colors.White, 0.5f));
        if (pressed) Open(_focus);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_hasAction || e is InputEventKey { Echo: true } || Net.Coop.LocalHold) return;
        if (!e.IsActionPressed(InteractAction)) return;
        if (_focus is null) return;
        _interactRequested = true;
        GetViewport().SetInputAsHandled();
    }

    private void Open(Station s)
    {
        switch (s)
        {
            case Merchant m:
                EnsureStocked(m);
                _merchantPanel.Open(m);
                break;
            case Microwave mw when !mw.Used:
                _microwavePanel.Open(mw);
                break;
            case Campfire fire:
                Rest(fire);
                break;
            case Trader trader:
                Trade.SwapMeet.Interact(trader);   // co-op trading (Game/Trade)
                break;
            case TideTotem totem when !totem.Used:
                UseTotem(totem);                   // Interactables.Totems.cs
                break;
        }
    }

    // ------------------------------------------------------------- campfire

    /// <summary>Rest at a campfire: health back to full. Always works (the
    /// rest room is safe, so there is nothing to ration); at full health it
    /// only says so.</summary>
    public bool Rest(Campfire fire)
    {
        Player? p = Player.Instance;
        if (p is null || !IsInstanceValid(p) || !p.IsAlive) return false;
        float missing = p.Health.Max - p.Health.Current;
        fire.Rests++;
        Rests++;
        if (missing <= 0.5f)
        {
            EventBus.Publish(new PopupRequested(fire.Top, Meta.Loc.T("already at full health"), Campfire.FireTint.Lerp(Colors.White, 0.4f)));
            GD.Print($"[restroom] rested at full health hp={p.Health.Current:0}/{p.Health.Max:0}");
            return true;
        }
        p.Health.Heal(missing);
        EventBus.Publish(new PopupRequested(fire.Top + Vector3.Up * 0.6f, Meta.Loc.T("Rested: health full"), new Color(0.55f, 1f, 0.6f), 1.4f));
        GD.Print($"[restroom] rested: healed {missing:0} hp={p.Health.Current:0}/{p.Health.Max:0}");
        return true;
    }

    private string PromptText(Station s) => s switch
    {
        Merchant m when m.Offers is not null && !m.Usable => Meta.Loc.F("{0} — merchant (sold out)", _keyName),
        Merchant => Meta.Loc.F("{0} — trade with merchant", _keyName),
        Microwave { Used: true } => Meta.Loc.T("microwave (used)"),
        Microwave => Meta.Loc.F("{0} — use microwave · from {1} gold", _keyName, MicrowavePrice(Rarity.Common)),
        Campfire when Player.Instance is { } pl && pl.Health.Current >= pl.Health.Max - 0.5f
            => Meta.Loc.F("{0} — rest by the campfire (health full)", _keyName),
        Campfire => Meta.Loc.F("{0} — rest by the campfire · heal to full", _keyName),
        Trader => Trade.SwapMeet.Prompt(_keyName),
        TideTotem => Meta.Loc.F("{0} — call the tide (pull all XP and gold)", _keyName),
        _ => $"{_keyName} — {Meta.Loc.T(s.Verb)}",

    };

    private Station? Focus(Vector3 at)
    {
        // A chest in reach owns the key.
        if (ChestSpawner.Instance is { } chests && chests.Nearest(at, ChestSpawner.InteractRadius) is { } c
            && ChestSpawner.InReach(c, at))
            return null;

        Station? best = null;
        float bestSq = float.MaxValue;
        foreach (Station s in _stations)
        {
            if (!IsInstanceValid(s) || !s.InReach(at)) continue;
            if (s is Microwave { Used: true } or TideTotem { Used: true }) continue;
            float sq = (s.GlobalPosition - at).LengthSquared();
            if (sq < bestSq) { best = s; bestSq = sq; }
        }
        return best;
    }

}
