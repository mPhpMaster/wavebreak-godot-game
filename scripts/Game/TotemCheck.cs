using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Progression;

namespace Wavebreak.Game;

/// <summary>
/// <c>--check-totem</c> (add <c>--invulnerable</c>; no <c>--auto</c>): the
/// Tide Totem end to end in single-player. Prints TOTEM lines ending in
/// TOTEM PASS/FAIL, then quits.
///
/// It checks the map placed its <see cref="Interactables.TotemCountFor"/>
/// totems, walks the player up to one (the prompt must offer it), drops 40
/// experience orbs, 20 coins and 3 med-packs 60 m and more away, presses the
/// real interact key, and requires every orb and coin collected within
/// <see cref="MaxSeconds"/> (gold and experience paid), the med-packs left
/// where they were, the totem asleep and refusing a second use, a minimap
/// marker drawn while it was awake, and no totem in the rest room afterwards.
///
/// <c>--totem-shot=&lt;prefix&gt;</c> also saves <c>prefix-idle.png</c> (the
/// totem awake from a few steps back, and the minimap), <c>prefix-prompt.png</c>
/// (in reach, with its prompt), <c>prefix-whoosh.png</c>
/// (0.25 s after E), <c>prefix-asleep.png</c> (after the pull),
/// <c>prefix-minimap.png</c> and <c>prefix-map.png</c> (the minimap and the
/// Tab big map with the marker, from 26 m away). Add <c>--lang=ar</c> for the Arabic prompt.
/// </summary>
public static class TotemCheck
{
    public const float MaxSeconds = 5f;

    public static bool Requested => OS.GetCmdlineUserArgs().Any(a => a == "--check-totem" || a.StartsWith("--totem-shot="));

    public static async Task Run(Main main)
    {
        SceneTree tree = main.GetTree();
        string? shot = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--totem-shot="))?["--totem-shot=".Length..];
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        async Task Frame() => await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool ok = true;
        void Expect(string what, bool condition)
        {
            GD.Print($"TOTEM {(condition ? "ok  " : "FAIL")} {what}");
            ok &= condition;
        }

        Main.CatchUpPicks = 100000;          // level-ups pick themselves, never pause
        Interactables.BotTotemsOff = true;
        await Wait(1.5);
        Interactables? hub = Interactables.Instance;
        Player? player = Player.Instance;
        Inventory? inv = Inventory.Current;
        if (hub is null || player is null || inv is null)
        {
            GD.Print("TOTEM FAIL no run");
            tree.Quit();
            return;
        }
        Data.StageDef stage = main.Stages.Stage;
        var totems = hub.Totems.ToList();
        Expect($"map {stage.Id} placed {totems.Count} totems, wants {Interactables.TotemCountFor(stage)}",
               totems.Count == Interactables.TotemCountFor(stage) && totems.Count > 0);
        if (totems.Count == 0) { Finish(tree, ok); return; }

        Actors.EnemyManager.DevFrozen = true;
        main.Enemies.ClearField(9999f);

        // Stand in front of the totem with the camera behind us.
        TideTotem totem = totems[0];
        Camera3D? cam = main.GetViewport().GetCamera3D();
        Vector3 fwd = cam is null ? Vector3.Forward : -cam.GlobalBasis.Z;
        fwd.Y = 0f;
        fwd = fwd.LengthSquared() > 0.01f ? fwd.Normalized() : Vector3.Forward;
        Vector3 right = new(-fwd.Z, 0f, fwd.X);
        // First a few steps back, for the look of it awake and its map marker
        // clear of the player's arrow.
        player.Teleport(totem.GlobalPosition - fwd * 8f + right * 2.5f + Vector3.Up * 0.5f);
        Func<Vector3>? steer = player.MoveOverride;
        player.MoveOverride = () => Vector3.Zero;
        await Wait(1.2);
        main.Enemies.ClearField(9999f);

        // The haul: far away, all round.
        var rng = new Core.Rng(0x707E3UL);
        float reach = stage.InRadius - 8f;
        Vector3 me = player.GlobalPosition;
        Vector3 Far()
        {
            for (int i = 0; i < 40; i++)
            {
                Vector3 p = me + rng.OnUnitCircleXZ() * rng.Range(60f, 140f);
                if (new Vector2(p.X, p.Z).Length() < reach) return p with { Y = 0.5f };
            }
            return (-me with { Y = 0f }).Normalized() * reach * 0.5f + Vector3.Up * 0.5f;
        }
        int xpRaw = 0, goldRaw = 0;
        for (int i = 0; i < 40; i++) { Pickup.Spawn(PickupKind.Xp, Far(), 8f); xpRaw += 8; }
        for (int i = 0; i < 20; i++) { Pickup.Spawn(PickupKind.Gold, Far(), 6f); goldRaw += 6; }
        for (int i = 0; i < 3; i++) Pickup.Spawn(PickupKind.Health, Far(), 0.2f);
        await Wait(1.0);

        if (shot is not null) await Screenshot.Capture(tree, main, shot + "-idle.png");
        // Further back for the maps, so the marker stands clear of our arrow.
        player.Teleport(totem.GlobalPosition - fwd * 26f + right * 6f + Vector3.Up * 0.5f);
        await Wait(1.0);
        Expect("minimap drew the awake totem", UI.Minimap.TotemsDrawn > 0);
        if (shot is not null)
        {
            await Screenshot.Capture(tree, main, shot + "-minimap.png");
            UI.StatsPanel.ForceOpen = true;
            await Wait(0.6);
            await Screenshot.Capture(tree, main, shot + "-map.png");
            UI.StatsPanel.ForceOpen = false;
            await Wait(0.4);
        }

        // Then up to it, beside it so the camera sees both.
        player.Teleport(totem.GlobalPosition - fwd * 0.8f + right * 2.1f + Vector3.Up * 0.5f);
        await Wait(0.8);
        int orbs = Pickup.TideOrbsLeft();
        int healthBefore = Pickup.Active.Count(p => GodotObject.IsInstanceValid(p) && !p.IsPooled && p.Kind == PickupKind.Health);
        int gold0 = inv.Gold, xp0 = inv.Xp;
        Expect($"haul on the floor: {orbs} orbs and coins, {healthBefore} med-packs", orbs >= 60 && healthBefore == 3);
        Expect("totem in reach and awake", totem.InReach(player.GlobalPosition) && !totem.Used);
        if (shot is not null) await Screenshot.Capture(tree, main, shot + "-prompt.png");

        // The real key.
        Input.ParseInputEvent(new InputEventAction { Action = "interact", Pressed = true });
        await Frame();
        Input.ParseInputEvent(new InputEventAction { Action = "interact", Pressed = false });
        double t0 = Time.GetTicksMsec() / 1000.0;
        if (shot is not null)
        {
            await Wait(0.25);
            await Screenshot.Capture(tree, main, shot + "-whoosh.png");
        }
        while (Pickup.TideOrbsLeft() > 0 && Time.GetTicksMsec() / 1000.0 - t0 < 10.0) await Wait(0.05);
        double took = Time.GetTicksMsec() / 1000.0 - t0;
        await Wait(0.2);
        if (shot is not null) { await Wait(0.6); await Screenshot.Capture(tree, main, shot + "-asleep.png"); }

        int healthAfter = Pickup.Active.Count(p => GodotObject.IsInstanceValid(p) && !p.IsPooled && p.Kind == PickupKind.Health);
        Expect($"every orb and coin collected in {took:0.00}s (limit {MaxSeconds:0}s), {Pickup.TideOrbsLeft()} left",
               Pickup.TideOrbsLeft() == 0 && took <= MaxSeconds);
        Expect($"gold paid: +{inv.Gold - gold0} for {goldRaw} dropped", inv.Gold - gold0 >= goldRaw * 0.9f);
        Expect($"experience paid: +{inv.Xp - xp0} for {xpRaw} dropped", inv.Xp - xp0 >= xpRaw * 0.9f);
        Expect($"med-packs left alone ({healthAfter}/{healthBefore})", healthAfter == healthBefore);
        Expect($"totem asleep, used once (uses={hub.TotemUses})", totem.Used && !totem.Usable && hub.TotemUses == 1);
        Expect("a sleeping totem refuses a second call", !hub.UseTotem(totem) && hub.TotemUses == 1);

        // Never in the rest room.
        player.MoveOverride = steer;
        if (main.Stages.DevEnterRestRoom())
        {
            await Wait(2.5);
            Expect($"rest room has no totems ({hub.Totems.Count()})", !hub.Totems.Any());
        }
        else Expect("entered the rest room", false);

        Finish(tree, ok);
    }

    private static void Finish(SceneTree tree, bool ok)
    {
        Main.CatchUpPicks = 0;
        Interactables.BotTotemsOff = false;
        Actors.EnemyManager.DevFrozen = false;
        GD.Print(ok ? "TOTEM PASS" : "TOTEM FAIL");
        tree.Quit();
    }
}
