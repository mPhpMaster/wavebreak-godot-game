using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.UI;

/// <summary>
/// Self-checks and captures for the developer panel.
///
///     -- --check-devpanel            open it with a real Ctrl+Shift+D key event,
///                                    press every kind of button, print DEVPANEL lines,
///                                    end with DEVPANEL PASS/FAIL and quit
///     -- --devpanel-shot=&lt;prefix&gt;    open it in a run and save &lt;prefix&gt;-&lt;tab&gt;.png per tab
///     -- --menu-shot=x.png --devpanel-menu   open it on the title for the menu capture
///
/// All of these are automated launches, so the profile lives in memory only:
/// nothing the check unlocks or wipes reaches the player's real save.
/// </summary>
public sealed partial class DevPanel
{
    private static bool _checkStarted, _shotStarted, _menuStarted;

    private void StartChecks()
    {
        string[] args = OS.GetCmdlineUserArgs();
        bool inRun = Main.Instance is not null;
        if (inRun && !_checkStarted && Array.IndexOf(args, "--check-devpanel") >= 0)
        {
            _checkStarted = true;
            _ = CheckDevPanel(Array.Find(args, a => a.StartsWith("--devpanel-shot=")) is { } after ? after["--devpanel-shot=".Length..] : null);
        }
        else if (inRun && !_shotStarted && !_checkStarted && Array.Find(args, a => a.StartsWith("--devpanel-shot=")) is { } shot)
        {
            _shotStarted = true;
            _ = ShootTabs(shot["--devpanel-shot=".Length..], quit: true);
        }
        else if (!inRun && !_menuStarted && Launch.DevPanelMenu is not null)
        {
            _menuStarted = true;
            _ = OpenOnTitle();
        }
    }

    private async Task Wait(double seconds)
    {
        SceneTree tree = GetTree();
        await ToSignal(tree.CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);
    }

    private static void PressChord()
    {
        foreach (bool down in new[] { true, false })
            Input.ParseInputEvent(new InputEventKey
            {
                Keycode = Key.D, PhysicalKeycode = Key.D, Pressed = down,
                CtrlPressed = true, ShiftPressed = true,
            });
    }

    private static void PressKey(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }

    /// <summary>Press a registered control the way a click would.</summary>
    private bool Press(string id)
    {
        if (!_buttons.TryGetValue(id, out BaseButton? b) || !IsInstanceValid(b))
        {
            GD.Print($"DEVPANEL info no button '{id}'");
            return false;
        }
        LastOk = true;
        if (b.ToggleMode) b.ButtonPressed = !b.ButtonPressed;
        else b.EmitSignal(BaseButton.SignalName.Pressed);
        return LastOk;
    }

    private async Task OpenOnTitle()
    {
        await Wait(0.6);
        PressChord();
        await Wait(0.2);
        GD.Print($"DEVPANEL title open={IsOpen}");
        if (Launch.DevPanelMenu != "unlock") return;
        // Unlock on the title: the menu reloads itself (debounced) so the
        // character list shows everything open; the menu's own capture runs
        // again after the reload and prints the MENU line with the states.
        Press("unlock_all");
        GD.Print($"DEVPANEL title unlock-all pressed: {LastStatus}");
        await Wait(0.8);
        Close();   // so the menu capture shows the refreshed page
    }

    private async Task ShootTabs(string prefix, bool quit)
    {
        await Wait(1.5);
        if (!IsOpen) PressChord();
        await Wait(0.3);
        // Something in every list, so the build column and stats are not empty.
        foreach (string id in new[] { "add_item:" + AllItems.First().Id, "add_tome:" + Tomes.All[0].Id, "gold_1000" }) Press(id);
        string[] names = { "profile", "player", "build", "spawn", "stage", "info" };
        for (int i = 0; i < names.Length; i++)
        {
            _tabs.CurrentTab = i;
            RefreshAll();
            await Wait(0.4);
            await Screenshot.Capture(GetTree(), this, $"{prefix}-{names[i]}.png");
        }
        if (quit) GetTree().Quit();
    }

    private async Task CheckDevPanel(string? shotPrefix)
    {
        SceneTree tree = GetTree();
        bool ok = true;
        void Expect(string what, bool cond)
        {
            GD.Print($"DEVPANEL {(cond ? "ok  " : "FAIL")} {what}{(cond ? "" : $"   [status: {LastStatus}]")}");
            ok &= cond;
        }
        bool Did(string id) => Press(id) && LastOk;

        await Wait(1.5);
        Main m = Main.Instance!;
        Expect("automated run keeps the profile in memory (nothing is saved)", Profile.Path is null);
        Expect("panel starts closed", !IsOpen);
        Expect("run starts as a normal run (counts for quests)", !UsedThisRun);

        // ---- open with the real chord
        PressChord();
        await Wait(0.3);
        Expect("Ctrl+Shift+D opens the panel", IsOpen);
        Expect("mouse is free while open", Input.MouseMode == Input.MouseModeEnum.Visible);
        Expect("run pauses while open (default)", tree.Paused);
        PressKey(Key.D);
        await Wait(0.1);
        Expect("D alone does nothing", IsOpen);

        // ---- profile
        long pearls = Pearls.Balance;
        Expect("unlock everything", Did("unlock_all") && Profile.UnlockedIds.Count == Unlocks.All.Count
               && Characters.All.All(c => c.IsAvailable) && SecretSpawner.AllKindsFound() && DifficultyTiers.IsUnlocked(DifficultyTiers.All.Count - 1)
               && Hats.All.All(h => h.IsOwned));
        UnlockDef first = Unlocks.All[0];
        Expect("an unlock row locks", Did($"unlock:{first.Id}") && !Profile.IsUnlocked(first.Id));
        Expect("and unlocks again", Did($"unlock:{first.Id}") && Profile.IsUnlocked(first.Id));
        Expect("unlock tiers / mark shrines", Did("unlock_tiers") && Did("shrines_found"));
        Expect("pearls +100/+1000/+10000", Did("pearls_100") && Did("pearls_1000") && Did("pearls_10000") && Pearls.Balance == pearls + 11100);
        Expect("max the Pearl Shop", Did("shop_max") && MetaShop.All.All(MetaShop.IsMaxed));
        Expect("reset the Pearl Shop", Did("shop_reset") && MetaShop.All.All(u => MetaShop.LevelOf(u) == 0));
        Press("profile_reset");
        Expect("profile reset needs a second click", Profile.UnlockedIds.Count == Unlocks.All.Count);
        Expect("profile reset wipes on the second click", Did("profile_reset") && Profile.UnlockedIds.Count == 0 && Pearls.Balance == 0);
        Expect("profile actions do not mark the run", !UsedThisRun);

        // ---- player
        Expect("god mode on", Did("god") && Screenshot.Invulnerable);
        Expect("run is now marked developer-assisted", UsedThisRun);
        int gold = m.Inventory.Gold;
        Expect("gold +100/+1000/+10000", Did("gold_100") && Did("gold_1000") && Did("gold_10000") && m.Inventory.Gold == gold + 11100);
        Expect("set gold (500)", Did("gold_set") && m.Inventory.Gold == 500);
        // A real mouse click on a real button, through the paused tree.
        _tabs.CurrentTab = 1;
        await Wait(0.15);
        if (_buttons["gold_100"] is Control goldButton)
        {
            Vector2 at = goldButton.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            foreach (bool down in new[] { true, false })
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at });
            await Wait(0.15);
            Expect("a mouse click on +100 gold works while paused", m.Inventory.Gold == 600);
        }
        Expect("full heal / +100 max health", Did("maxhp") && Did("heal") && m.Player.Health.Current >= m.Player.Health.Max - 0.5f);
        int level = m.Inventory.Level;
        Expect("+100 XP", Did("xp_100") && m.Inventory.Xp >= 100);
        level = m.Inventory.Level;
        Expect("level up x1 queues a choice", Did("level_1") && m.Inventory.Level == level + 1 && m.LevelUp.IsOpen);
        await Wait(0.7);
        PressKey(Key.Key1);
        await Wait(0.1);
        Expect("number keys do not reach the level-up panel while the dev panel is open", m.LevelUp.IsOpen);
        Expect("auto-pick on", Did("autopick"));
        level = m.Inventory.Level;
        Expect("level up x5", Did("level_5") && m.Inventory.Level == level + 5);
        await Wait(1.0);
        Expect("auto-pick took the queued cards", !m.LevelUp.IsOpen);
        Expect("run stays paused behind the panel after the level-up panel closed", tree.Paused);
        Did("autopick");
        var choices = LevelUpChoices.Current!;
        int rr = choices.RerollsLeft, bb = choices.BanishesLeft, ss = choices.SkipsLeft;
        Expect("+5 rerolls/banishes/skips", Did("rerolls") && Did("banishes") && Did("skips")
               && choices.RerollsLeft == rr + 5 && choices.BanishesLeft == bb + 5 && choices.SkipsLeft == ss + 5);
        float baseSpeed = m.Player.Stats.Get(Stat.MoveSpeed);
        Expect("move speed x2", Did("speed_1") && m.Player.Stats.Get(Stat.MoveSpeed) > baseSpeed * 1.5f);
        Expect("move speed back to normal", Did("speed_0") && Mathf.Abs(m.Player.Stats.Get(Stat.MoveSpeed) - baseSpeed) < 0.01f);
        int airBefore = m.Player.ExtraAirJumps;
        Expect("fly on", Did("fly") && m.Player.ExtraAirJumps >= airBefore + FlyJumps);
        Expect("fly off", Did("fly") && m.Player.ExtraAirJumps == airBefore);
        Expect("teleport to map centre", Did("tp_centre") && m.Player.GlobalPosition.DistanceTo(Arena.Centre) < 3f);

        // ---- build
        _tabs.CurrentTab = 2;
        await Wait(0.1);
        // Typing into the search box filters the list and never reaches the game.
        _buildList!.Search.GrabFocus();
        foreach (char c in "spark")
        {
            Key k = Key.A + (c - 'a');
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Unicode = c, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Unicode = c, Pressed = false });
        }
        await Wait(0.15);
        Expect($"typing 'spark' filters the list ({_buildList.Shown} shown, text '{_buildList.Search.Text}')",
               _buildList.Search.Text == "spark" && _buildList.Shown is > 0 and < 20);
        _buildList.Search.Text = "";
        _buildList.Apply();
        _buildList.Search.ReleaseFocus();
        // Auto-picked level-ups may have filled every slot; free one first
        // (a full bar is correctly refused, which is not what this step tests).
        RebuildBuildSide();
        if (m.Weapons.IsFull)
            Expect("remove a weapon to free a slot", Did($"remove_weapon:{m.Weapons.Weapons[^1].Def.Id}") && !m.Weapons.IsFull);
        WeaponDef weapon = AllWeapons.First(w => !m.Weapons.Has(w.Id));
        Expect($"add weapon {weapon.Id}", Did($"add_weapon:{weapon.Id}") && m.Weapons.Has(weapon.Id));
        Expect("max that weapon", Did($"max_weapon:{weapon.Id}") && m.Weapons.LevelOf(weapon.Id) == weapon.MaxLevel);
        ItemDef item = AllItems.First(i => i.MaxStacks <= 0 || i.MaxStacks - m.Inventory.StacksOf(i) >= 3);
        int held = m.Inventory.StacksOf(item);
        Expect($"add item {item.Id}", Did($"add_item:{item.Id}") && m.Inventory.StacksOf(item) == held + 1);
        Expect("add item +5 (up to its cap)", Did($"add5_item:{item.Id}") && m.Inventory.StacksOf(item) > held + 1);
        TomeBook book = TomeBook.Current!;
        if (!Tomes.All.Any(book.CanTake)) { book.Clear(); m.Hud.DevResync(m.Weapons, book); }
        TomeDef tome = Tomes.All.First(book.CanTake);
        int tomeBefore = book.LevelOf(tome);
        Expect($"add tome {tome.Id}", Did($"add_tome:{tome.Id}") && book.LevelOf(tome) == tomeBefore + 1);
        if (book.CanTake(tome)) Press($"max_tome:{tome.Id}");
        Expect("max that tome", book.LevelOf(tome) == tome.MaxLevel);
        RefreshAll();
        await Wait(0.1);
        int stacks = m.Inventory.StacksOf(item);
        Expect("remove one item copy", Did($"remove_item:{item.Id}") && m.Inventory.StacksOf(item) == stacks - 1);
        Expect("remove the weapon", Did($"remove_weapon:{weapon.Id}") && !m.Weapons.Has(weapon.Id));
        Expect("max all weapons / tomes", Did("max_weapons") && Did("max_tomes"));
        Expect("clear the build", Did("clear_build") && m.Weapons.Weapons.Count == 0 && m.Inventory.TotalStacks == 0 && TomeBook.Current!.Count == 0);
        Expect("re-arm after clearing", Did($"add_weapon:{RunMeta.StartingWeapon().Id}") && m.Weapons.Weapons.Count == 1);

        // ---- spawn
        _tabs.CurrentTab = 3;
        await Wait(0.1);
        Did("kill_all");
        await Wait(0.05);
        int alive = m.Enemies.AliveCount;
        EnemyDef normal = Catalog.Enemies.First(e => !e.IsElite && !e.IsBoss);
        EnemyDef elite = Catalog.Enemies.First(e => e.IsElite && !e.IsBoss);
        EnemyDef boss = Catalog.Enemies.First(e => e.IsBoss);
        Expect($"spawn enemy {normal.Id}", Did($"spawn_enemy:{normal.Id}") && m.Enemies.AliveCount == alive + 1);
        Expect($"spawn elite {elite.Id}", Did($"spawn_enemy:{elite.Id}") && m.Enemies.AliveCount == alive + 2);
        Expect($"spawn boss {boss.Id}", Did($"spawn_enemy:{boss.Id}") && m.Enemies.AliveList.Any(e => e.IsBoss && e.IsAlive));
        int chests = ChestSpawner.Instance!.Spawned;
        Expect("spawn chests (all four kinds)", Did("chest_standard") && Did("chest_gilded") && Did("chest_free") && Did("chest_free_gilded")
               && ChestSpawner.Instance.Spawned == chests + 4);
        int shrines = ShrineSpawner.Instance!.Placed;
        Expect("spawn every map shrine kind", Enum.GetValues<MapShrineKind>().All(k => Did($"shrine_{k}")) && ShrineSpawner.Instance.Placed == shrines + 4);
        Expect("spawn every hidden shrine kind", SecretSpawner.Kinds.All(k => Did($"secret_{k.Id}")));
        int stations = Interactables.Instance!.StationsPlaced;
        Expect("spawn merchant + microwave", Did("merchant") && Did("microwave") && Interactables.Instance.StationsPlaced == stations + 2);
        int pots = Interactables.Instance.PotsPlaced;
        Expect("spawn pots", Did("pots") && Interactables.Instance.PotsPlaced == pots + 6);
        int pickups = Pickup.ActiveCount;
        Expect("spawn XP / gold / heal pickups", Did("pickup_xp") && Did("pickup_gold") && Did("pickup_heal") && Pickup.ActiveCount >= pickups + 21);
        Expect("teleport to nearest chest / shrine / merchant / microwave",
               Did("tp_chest") && Did("tp_shrine") && Did("tp_merchant") && Did("tp_microwave"));
        Expect("freeze AI on", Did("freeze") && EnemyManager.DevFrozen);
        Expect("freeze AI off", Did("freeze") && !EnemyManager.DevFrozen);
        Expect("clear enemy projectiles", Did("clear_shots") && Combat.EnemyAttacks.ActiveShots == 0);
        Expect("kill all enemies", Did("kill_all"));
        await Wait(0.1);
        Expect($"field is empty after kill all ({m.Enemies.AliveCount} alive)", m.Enemies.AliveCount == 0);

        // ---- stage
        _tabs.CurrentTab = 4;
        await Wait(0.1);
        Expect("jump to phase 2", Did("phase_2") && m.Run.Phase == 2);
        Expect("boss now", Did("boss_now") && m.Stages.State == StageDirector.PhaseState.Boss);
        Expect("portal now", Did("portal_now") && m.Stages.State == StageDirector.PhaseState.PortalOpen && m.Stages.PortalPosition is not null);
        Expect("teleport to the portal", Did("tp_portal"));
        Expect("jump to phase 3", Did("phase_3") && m.Run.Phase == 3);
        Expect("final swarm", Did("final_swarm") && m.Stages.State == StageDirector.PhaseState.FinalSwarm);
        Expect("jump back to phase 1", Did("phase_1") && m.Run.Phase == 1 && m.Stages.State == StageDirector.PhaseState.Waves);
        StageDef otherMap = Stages.All[^1];
        ((OptionButton)_buttons["map_pick"]).Select(Stages.All.Count - 1);
        Expect($"play another map ({otherMap.Id})", Did("set_map") && m.Run.StageId == otherMap.Id && m.Run.Phase == 1);
        ((OptionButton)_buttons["route_pick"]).Select(Routes.All.Count - 1);
        Expect($"switch route ({Routes.All[^1].Id})", Did("set_route") && m.Stages.Route.Count > 0);
        ChallengeDef ch = Challenges.All[0];
        Expect($"challenge {ch.Id} on", Did($"challenge_{ch.Id}") && Challenges.IsActive(ch.Id));
        Expect($"challenge {ch.Id} off", Did($"challenge_{ch.Id}") && !Challenges.IsActive(ch.Id));
        Expect("tier Hard", Did("tier_hard") && DifficultyTiers.Active.Id == "hard");
        Expect("tier Normal", Did("tier_normal") && DifficultyTiers.Active.Id == "normal");
        Expect("time scale x0.25", Did("timescale_025") && Math.Abs(Engine.TimeScale - 0.25) < 1e-6);
        Expect("time scale x4", Did("timescale_4") && Math.Abs(Engine.TimeScale - 4) < 1e-6);
        Expect("time scale x1", Did("timescale_1") && Math.Abs(Engine.TimeScale - 1) < 1e-6);

        // Level-ups from here on would pause the run behind a card; let them pick themselves.
        Did("autopick");

        // ---- every tab refreshes without errors
        for (int i = 0; i < _tabs.GetTabCount(); i++)
        {
            _tabs.CurrentTab = i;
            RefreshAll();
            await Wait(0.05);
        }
        Expect("every tab refreshes", true);

        // ---- pause toggle
        Expect("pause toggle off resumes the run", Did("pause") && !tree.Paused);
        Expect("pause toggle on pauses it again", Did("pause") && tree.Paused);

        if (shotPrefix is not null) await ShootTabs(shotPrefix, quit: false);

        // ---- close with the chord, reopen, Esc closes
        // The run is live between these steps, so a gem can land a level-up at
        // any moment; that card legitimately holds the pause (auto-pick clears
        // it after its arm delay). What must never happen is the pause menu.
        var pause = m.FindChild("PauseMenu", true, false) as PauseMenu;
        bool Resumed() => !tree.Paused || m.LevelUp.IsOpen;
        PressChord();
        await Wait(0.3);
        Expect("the chord closes the panel and the run resumes", !IsOpen && Resumed());
        PressChord();
        await Wait(0.2);
        PressKey(Key.Escape);
        await Wait(0.2);
        Expect("Esc closes it too, without opening the pause menu", !IsOpen && pause is { IsOpen: false } && Resumed());
        Expect("still a live run", !m.Run.Over);

        // ---- win: the run ends, and nothing is recorded for it
        for (int i = 0; i < 40 && m.LevelUp.IsOpen; i++) await Wait(0.05);
        PressChord();
        await Wait(0.2);
        Press("win");
        for (int i = 0; i < 40 && !m.Run.Over; i++) await Wait(0.05);
        await Wait(0.2);
        Expect("win now ends the run (results showing)", !IsOpen && m.Run.Over && m.Results.Visible);
        Expect("the run was flagged so it skips quests/leaderboard", UsedThisRun);

        GD.Print(ok ? "DEVPANEL PASS" : "DEVPANEL FAIL");
        tree.Quit();
    }
}
