using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

/// <summary>
/// Rest-room captures and self-check.
///
/// <c>--restroom-shot=&lt;png&gt;</c>: start a run, go straight to the rest room
/// after map 1, photograph the arrival view, quit.
///
/// <c>--check-restroom</c> (with <c>--auto</c>; add <c>--invulnerable</c>):
/// drives map 1 -> rest room -> map 2 with the real autopilot and prints
/// RESTROOM lines ending in RESTROOM PASS/FAIL. It checks that the maps place
/// no merchant, that the room has no enemies and holds the run clock, that the
/// bot buys from the merchant (it is handed gold for that), heals at the
/// campfire (it is wounded first), walks into the CONTINUE portal on its own,
/// and arrives on map 2 at full health with the clock where it was.
/// </summary>
public static class RestRoomCheck
{
    public static bool Requested(out string? shot)
    {
        shot = null;
        bool check = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--check-restroom") check = true;
            else if (arg.StartsWith("--restroom-shot=")) shot = arg["--restroom-shot=".Length..];
        }
        return check || shot is not null;
    }

    private static bool CheckRequested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-restroom") >= 0;

    public static async Task Run(Main main, string? shot)
    {
        SceneTree tree = main.GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        async Task<bool> Until(Func<bool> cond, double timeout)
        {
            double start = Time.GetTicksMsec() / 1000.0;
            while (!cond())
            {
                if (Time.GetTicksMsec() / 1000.0 - start > timeout) return false;
                await Wait(0.1);
            }
            return true;
        }

        await Wait(1.5);
        StageDirector stages = main.Stages;

        if (!CheckRequested)
        {
            if (!stages.DevEnterRestRoom()) GD.Print("[restroom] could not enter the rest room");
            await Wait(2.5);
            await Screenshot.Capture(tree, main, shot!);
            GD.Print($"[restroom] shot saved to {shot}");
            tree.Quit();
            return;
        }

        bool ok = true;
        void Expect(string what, bool condition)
        {
            GD.Print($"RESTROOM {(condition ? "ok  " : "FAIL")} {what}");
            ok &= condition;
        }

        Interactables? hub = Interactables.Instance;
        Expect("autopilot on (--auto)", Screenshot.Auto);
        Expect("map 1 places no merchant", hub is not null && !hub.Stations.Any(s => s is Merchant));
        Expect("map 1 still has its microwave", hub is not null && hub.Stations.Any(s => s is Microwave));

        // Skip the boss: the portal opens at the centre and the bot walks in.
        Expect("portal opens after map 1", stages.DevOpenPortal());
        Expect("bot takes the portal into the rest room", await Until(() => stages.InRestRoom, 60));

        if (stages.Rest is { } room && hub is not null && main.Player is { } player)
        {
            await Wait(0.3);
            Expect("no enemies in the rest room", main.Enemies.AliveCount == 0);
            Expect("map is gone, island bounds in use", stages.DevArena is null && Arena.Current == RestRoom.Bounds);
            Expect("merchant, campfire and microwave are stations",
                   hub.Stations.Contains(room.Merchant) && hub.Stations.Contains(room.Campfire) && hub.Stations.Contains(room.Microwave));
            Expect("player arrived on the island", player.GlobalPosition.DistanceTo(RestRoom.SpawnPoint) < 6f);

            // Something to buy with, and something to heal.
            Progression.Inventory.Current?.DevSetGold(2000);
            player.Health.DevSetHealth(player.Health.Max * 0.4f);
            int buys = hub.MerchantBuys, rests = hub.Rests;
            float clock = main.Run.Seconds;
            await Wait(2.0);
            Expect($"run clock held in the room ({clock:0.00} -> {main.Run.Seconds:0.00})", Mathf.Abs(main.Run.Seconds - clock) < 0.01f);
            Expect("still no enemies after 2 s", main.Enemies.AliveCount == 0);

            float hpAtRest = -1f;
            bool left = await Until(() =>
            {
                if (hpAtRest < 0f && hub.Rests > rests) hpAtRest = player.Health.Fraction;
                return !stages.InRestRoom;
            }, 90);
            Expect("bot walks into the CONTINUE portal on its own", left && stages.RestExitReason == "portal");
            Expect($"bot bought from the merchant ({hub.MerchantBuys - buys} items, merchantspent={hub.MerchantSpent})", hub.MerchantBuys > buys);
            Expect($"bot rested at the campfire (health {hpAtRest * 100f:0}% after resting)", hub.Rests > rests && hpAtRest >= 0.999f);
            Expect($"clock unchanged across the stay ({stages.RestClockIn:0.00} / {stages.RestClockOut:0.00})",
                   Mathf.Abs(stages.RestClockOut - stages.RestClockIn) < 0.05f);
        }
        else Expect("rest room built", false);

        Expect("map 2 begins", main.Run.Phase == 2 && stages.State == StageDirector.PhaseState.Waves);
        await Wait(1.0);
        Expect("rest room freed with the new map", stages.Rest is null && stages.DevArena is not null && Arena.Current != RestRoom.Bounds);
        Expect("map 2 places no merchant", hub is not null && !hub.Stations.Any(s => s is Merchant));
        Expect("enemies return on map 2", await Until(() => main.Enemies.AliveCount > 0, 15));

        GD.Print(ok ? "RESTROOM PASS" : "RESTROOM FAIL");
        GD.Print(main.Summary());
        tree.Quit();
    }
}
