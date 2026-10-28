using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class ChestSpawner
{
    // --------------------------------------------------------- verification

    /// <summary>
    /// <c>--check-chests=&lt;dir&gt;</c>: drives one chest through every path with
    /// real key events — refused for gold, paid and opened, a boss drop opened
    /// free — saves a frame at each step into &lt;dir&gt;, prints CHEST lines and
    /// quits. A chest that opens in a log but not from the keyboard is exactly
    /// the bug a log cannot show, so the key goes through Input like a player's.
    /// </summary>
    private async System.Threading.Tasks.Task CheckChests(string dir)
    {
        SceneTree tree = GetTree();
        bool ok = true;
        void Expect(string what, bool condition)
        {
            GD.Print($"CHEST {(condition ? "ok  " : "FAIL")} {what}");
            ok &= condition;
        }
        void Press()
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Pressed = false });
        }
        async System.Threading.Tasks.Task Wait(double seconds)
            => await tree.ToSignal(tree.CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        async System.Threading.Tasks.Task Shot(string name)
            => await Screenshot.Capture(tree, this, $"{dir}/{name}.png");

        ChestOpened? last = null;
        void OnOpened(ChestOpened e) => last = e;
        EventBus.Subscribe<ChestOpened>(OnOpened);

        DirAccess.MakeDirRecursiveAbsolute(dir);
        await Wait(1.2);

        Player? player = Player.Instance;
        Inventory? inv = Inventory.Current;
        Camera3D? cam = GetViewport().GetCamera3D();
        if (player is null || inv is null || cam is null || _map is null)
        {
            GD.Print("CHEST FAIL setup: no player, inventory, camera or map");
            tree.Quit(1);
            return;
        }

        Expect($"map populated ({Standing} standing, {Spawned} spawned)", Spawned >= DefaultCount);

        // In front of the player as the camera sees it, a little to the side so
        // the body does not hide the lock.
        Vector3 fwd = -cam.GlobalBasis.Z; fwd.Y = 0f; fwd = fwd.Normalized();
        Vector3 right = fwd.Cross(Vector3.Up);
        Vector3 me = player.GlobalPosition;
        var chest = new Chest(ChestTier.Standard, free: false) { SpawnedAt = _clock };
        _map.AddChild(chest);
        chest.GlobalPosition = new Vector3(me.X, _centre.Y, me.Z) + fwd * 2.1f + right * 0.7f;
        chest.Rotation = new Vector3(0f, MathF.Atan2(-fwd.X, -fwd.Z), 0f);
        _chests.Add(chest);
        await Wait(0.5);

        // Refused: an empty purse.
        if (inv.Gold > 0) inv.SpendGold(inv.Gold);
        int price = PriceFor(chest);
        Expect($"prompt shows for the chest in reach (focus={_focus == chest}, visible={_prompt.Visible})",
               _focus == chest && _prompt.Visible);
        Expect($"prompt names the price: \"{_prompt.Text}\"", _prompt.Text.Contains(Meta.Loc.F("{0} gold", price)));
        await Shot("chest-prompt-poor");
        int deniedBefore = Denied;
        Press();
        await Wait(0.12);
        Expect("E with no gold is refused", Denied == deniedBefore + 1 && !chest.IsOpen && inv.Gold == 0);
        await Shot("chest-denied");
        await Wait(0.6);

        // Paid: give exactly enough.
        while (inv.Gold < price) inv.GrantGold(price - inv.Gold);
        await Wait(0.3);
        await Shot("chest-prompt");
        int goldBefore = inv.Gold;
        int stacksBefore = inv.TotalStacks;
        Press();
        await Wait(0.15);
        await Shot("chest-burst");
        Expect($"E with {goldBefore} gold opens it (open={chest.IsOpen})", chest.IsOpen);
        Expect($"price {price} was charged (gold {goldBefore} -> {inv.Gold})", inv.Gold == goldBefore - price);
        Expect($"reward granted: \"{last?.Reward}\" (items {stacksBefore} -> {inv.TotalStacks})",
               last is { Free: false } && (inv.TotalStacks == stacksBefore + 1 || last.Value.RewardRarity == Rarity.Common));
        Expect($"price climbs: {price} -> {NextPrice(ChestTier.Standard)}", NextPrice(ChestTier.Standard) > price);
        await Wait(0.45);
        await Shot("chest-reward");

        // Free: a boss-sized kill through the real event, then E.
        await Wait(1.0);
        int dropsBefore = Dropped;
        Vector3 dropAt = new Vector3(me.X, _centre.Y + 0.35f, me.Z) + fwd * 1.6f - right * 1.3f;
        EventBus.Publish(new EnemyKilled(dropAt, 0, BossGoldThreshold + 100, true));
        await Wait(0.3);
        Chest? drop = Nearest(dropAt, 2f, c => c.IsFree);
        Expect($"boss kill drops a free chest (drops {dropsBefore} -> {Dropped}, tier={drop?.Tier})",
               drop is { Tier: ChestTier.Gilded });
        if (drop is not null)
        {
            await Shot("chest-free");
            int spentBefore = GoldSpent;
            Press();
            await Wait(0.15);
            Expect($"free chest opens with E at no cost (reward \"{last?.Reward}\")",
                   drop.IsOpen && last is { Free: true, Cost: 0 } && GoldSpent == spentBefore);
            await Wait(0.4);
            await Shot("chest-free-open");
        }

        EventBus.Unsubscribe<ChestOpened>(OnOpened);
        GD.Print($"CHEST {SummaryFields}");
        GD.Print(ok ? "CHEST PASS" : "CHEST FAIL");
        tree.Quit(ok ? 0 : 1);
    }
}
