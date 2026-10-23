using Godot;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// Scene root. Builds the world, owns the run, and ticks the systems.
///
/// Systems are ticked from here in a fixed order rather than each running its own
/// <c>_Process</c>. That is a real design choice, not tidiness: with independent
/// callbacks, whether weapons fire before or after enemies move is decided by
/// scene-tree order, so hit registration silently changes the day someone
/// reparents a node. One ordered tick means the frame is reproducible.
///
/// Order is: input/movement already happened in physics -> enemies steer ->
/// weapons fire at where things now are -> pickups chase -> UI reads results.
/// </summary>
public sealed partial class Main : Node3D
{
    public static Main? Instance { get; private set; }

    public RunState Run { get; private set; } = null!;

    private Actors.Player _player = null!;
    private Actors.PlayerCamera _camera = null!;
    private Actors.EnemyManager _enemies = null!;
    private Combat.WeaponSystem _weapons = null!;
    private Progression.Inventory _inventory = null!;
    private Progression.OfferPool _offers = null!;
    private UI.Hud _hud = null!;
    private UI.LevelUpPanel _levelUp = null!;
    private UI.ResultsScreen _results = null!;
    private Audio.AudioDirector _audio = null!;
    private StageDirector _stages = null!;

    public Actors.Player Player => _player;
    public Combat.WeaponSystem Weapons => _weapons;
    public Progression.Inventory Inventory => _inventory;
    public Actors.EnemyManager Enemies => _enemies;
    public StageDirector Stages => _stages;
    public UI.LevelUpPanel LevelUp => _levelUp;
    public UI.Hud Hud => _hud;
    public UI.ResultsScreen Results => _results;

    public override void _Ready()
    {
        Instance = this;
        Screenshot.ParseArgs();
        Meta.Loc.Init(this);
        // Graphics settings at run start (automated runs: the fixed defaults).
        Meta.GraphicsSettings.Load();
        Meta.GraphicsSettings.ApplyAll(this);
        if (Visuals.ArtShowcase.Intercept(this)) return;

        Run = new RunState(Screenshot.Requested ? Screenshot.Seed : (ulong)GD.Randi());
        GD.Print($"[wavebreak] seed {Run.Seed}");

        BuildWorld();
        Meta.RunMeta.Attach(this);

        // The arena is built by the stage director, not here: it is rebuilt on
        // every phase change, and the thing that rebuilds it should be the only
        // thing that ever creates one.
        // The route (Meta/Routes) is Data.Stages.Run unless the player picked another.
        _stages = new StageDirector(this, Run, _enemies, _player, _camera, Meta.Routes.ForRun(Run.Seed), Catalog.Enemies);
        _stages.Begin();

        EventBus.Subscribe<LevelGained>(OnLevelGained);
        EventBus.Subscribe<RunEnded>(OnRunEnded);
        // Ctrl+Shift+Alt+F12 (UI/DevPanel); also runs --check-devpanel.
        UI.DevPanel.Install(this);

        if (Screenshot.Requested)
            _ = RunScreenshot();
        else if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-graphics") >= 0)
            _ = GraphicsCheck.Run(this);
        else if (!_guardsChecked && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-input-guards") >= 0)
            _ = CheckInputGuards();
        else if (System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--levelup-shot=")) is { } shotArg)
            _ = LevelUpShot(shotArg["--levelup-shot=".Length..]);
        else if (System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--icon-sheet=")) is { } sheetArg)
            _ = UI.IconSheet.Capture(GetTree(), this, sheetArg["--icon-sheet=".Length..]);
    }

    /// <summary>--levelup-shot=&lt;png&gt;: open a real level-up hand (after a
    /// few picks, so upgrades and tomes appear too), photograph it, quit.</summary>
    private async System.Threading.Tasks.Task LevelUpShot(string path)
    {
        SceneTree tree = GetTree();
        async System.Threading.Tasks.Task Wait(double s)
            => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        await Wait(1.0);
        for (int i = 0; i < 4; i++)
        {
            var hand = _offers.Roll(3, _weapons, _inventory, _player.Stats);
            if (hand.Count > 0) Choose(hand[i % hand.Count]);
        }
        _levelUp.Show(_offers.Roll(3, _weapons, _inventory, _player.Stats), Choose);
        await Wait(0.8);
        await Screenshot.Capture(tree, this, path);
        tree.Quit();
    }

    // Survives the scene reload a successful restart performs, so the check runs
    // once rather than restarting itself forever.
    private static bool _guardsChecked;

    /// <summary>
    /// --check-input-guards: drives the level-up panel and the results screen
    /// with real key events and prints GUARD lines, ending in GUARD PASS/FAIL.
    ///
    /// It exists because the failure it guards against -- a key meant for the
    /// game landing on a panel that has just opened -- needs a keyboard to
    /// reproduce and is invisible in any screenshot. Space is the key that
    /// matters: it is jump, and also ui_accept on any focused button.
    /// </summary>
    private async System.Threading.Tasks.Task CheckInputGuards()
    {
        _guardsChecked = true;
        // Held in a local: after the restart this node is freed, and the tree is
        // the only thing left to quit through.
        SceneTree tree = GetTree();
        bool ok = true;
        void Expect(string what, bool condition)
        {
            GD.Print($"GUARD {(condition ? "ok  " : "FAIL")} {what}");
            ok &= condition;
        }
        void Press(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        }
        async System.Threading.Tasks.Task Wait(double seconds)
            => await tree.ToSignal(tree.CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);

        await Wait(1.0);
        var choices = _offers.Roll(3, _weapons, _inventory, _player.Stats);
        // Everything a card can change: a new weapon or item, a weapon level, a tome level.
        int Owned() => _inventory.TotalStacks + System.Linq.Enumerable.Sum(_weapons.Weapons, w => w.Level)
            + (Progression.TomeBook.Current is { } book ? System.Linq.Enumerable.Sum(book.Held, t => book.LevelOf(t)) : 0);
        int before = Owned();
        _levelUp.Show(choices, Choose);
        ulong shownAt = Time.GetTicksMsec();
        await Wait(0.05);
        Press(Key.Space);
        await Wait(0.1);
        Expect("level-up: Space right after opening does not pick a card", _levelUp.IsOpen);
        // The arm delay is real milliseconds; with --fixed-fps, game-time waits
        // can pass faster than that on a quick machine. Wait out real time.
        while (Time.GetTicksMsec() - shownAt < 650) await Wait(0.05);
        Press(Key.Key1);
        await Wait(0.1);
        int after = Owned();
        if (_levelUp.IsOpen || after <= before)
            GD.Print($"GUARD info open={_levelUp.IsOpen} before={before} after={after} {_levelUp.DebugState}");
        Expect("level-up: 1 after the delay picks a card", !_levelUp.IsOpen && after > before);

        _player.Health.Kill();
        await Wait(0.1);
        Press(Key.Space);
        Press(Key.Enter);
        await Wait(0.3);
        Expect("results: Space/Enter right after death do not restart", _results.Visible && IsInsideTree());
        await Wait(1.0);
        Press(Key.Space);
        await Wait(0.2);
        Expect("results: Space never restarts (it is jump)", _results.Visible && IsInsideTree());

        GD.Print(ok ? "GUARD PASS (pressing Enter to restart)" : "GUARD FAIL");
        // The final step is the restart itself; the reloaded scene prints the
        // seed line, which the caller treats as proof it happened.
        Press(Key.Enter);
        await Wait(1.5);
        tree.Quit();
    }

    private void BuildWorld()
    {
        // First, so it is subscribed before anything can publish. It only
        // listens to the bus; nothing below needs a reference to it.
        _audio = new Audio.AudioDirector();
        AddChild(_audio);
        Audio.MusicDirector.BeginRun(this);

        _player = new Actors.Player();
        AddChild(_player);
        // PlayerHealth self-seeds its RNG if nobody supplies one, which would make
        // evasion rolls diverge between two runs of the same seed. Hand it the
        // run's generator so a seeded run is reproducible all the way down.
        _player.Health.Rng = Run.Rng;

        _camera = new Actors.PlayerCamera();
        AddChild(_camera);

        _enemies = new Actors.EnemyManager();
        AddChild(_enemies);
        _enemies.Configure(Catalog.Enemies, Run.Rng);

        _weapons = new Combat.WeaponSystem();
        AddChild(_weapons);

        _inventory = new Progression.Inventory(_player.Stats);
        _offers = new Progression.OfferPool(Meta.MetaContent.OfferItems(Catalog.Items), Meta.MetaContent.OfferWeapons(Catalog.Weapons), Run.Rng);
        // Tomes and the reroll/banish/skip charges; both publish themselves as Current.
        _ = new Progression.LevelUpChoices(_offers, _weapons, _inventory, _player.Stats, new Progression.TomeBook(_player.Stats));

        // Damage numbers and pickups are pooled nodes that need a parent in the
        // world, so they are children of the root rather than of the player.
        AddChild(new UI.DamagePopup());

        var ui = new CanvasLayer { Name = "UI" };
        AddChild(ui);
        _hud = new UI.Hud();
        ui.AddChild(_hud);
        _levelUp = new UI.LevelUpPanel();
        ui.AddChild(_levelUp);
        _results = new UI.ResultsScreen();
        ui.AddChild(_results);
        // Last, so the sheet draws over the level-up panel: checking your build
        // before choosing a card is the main reason to open it.
        var sheet = new UI.StatsPanel(_player, _inventory, _weapons, Run);
        ui.AddChild(sheet);
        // Esc / P: pause with the sheet held open and the menu buttons under it.
        ui.AddChild(new UI.PauseMenu(sheet, _levelUp, _results));

        // Everyone starts with one weapon; a run with no way to fight is not a
        // run, and the first level-up should be a choice rather than a rescue.
        _weapons.Add(Meta.RunMeta.StartingWeapon());

        // Attached for the whole run (the price ladder belongs to the run);
        // each map populates it when the stage director builds that map.
        ChestSpawner.Attach(this, Run.Seed);
        // Merchant, microwave and breakable pots: same lifetime and populate hook.
        Interactables.Attach(this, Run.Seed);
        PerfProbe.Attach(this);
        // --give=id,id: start holding these weapons at max level (effect checks).
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--give="))
                foreach (string id in arg["--give=".Length..].Split(','))
                    if (Catalog.WeaponById(id) is { } def) _weapons.DevMax(def);
                    else if (Tomes.ById(id) is { } tome && Progression.TomeBook.Current is { } book) while (book.Add(tome)) { }
        // Map shrines (charge, challenge, statue, greed); same lifetime as chests.
        ShrineSpawner.Attach(this, Run.Seed);

        if (Screenshot.Auto)
        {
            var bot = new Autopilot(_player);
            _player.MoveOverride = bot.Think;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Run.Over) return;

        Run.Tick(dt);
        // Before the enemy director: a phase change clears the field and sets
        // the new multipliers, and the spawns this frame should already see them.
        _stages.Tick(dt);
        if (Run.Over) return;
        using (Prof.Scope("Director")) _enemies.Tick(dt, Run.Seconds);
        using (Prof.Scope("Weapons")) _weapons.Tick(dt);
    }

    private void OnLevelGained(LevelGained e)
    {
        var choices = _offers.Roll(3, _weapons, _inventory, _player.Stats);
        if (choices.Count == 0) return;

        if (Screenshot.Auto)
        {
            // Never show the panel in autopilot runs: it pauses the tree waiting
            // for a click, so the run would stop at level 2 and every system past
            // it would go unverified while the log still looked healthy.
            Autopilot.ResolveLevelUp(choices, Run.Rng, Choose);
            return;
        }
        _levelUp.Show(choices, Choose);
    }

    private void Choose(Progression.Offer offer)
    {
        if (offer.Weapon is not null) _weapons.Add(offer.Weapon, offer.Upgrade);
        else if (offer.Item is not null) _inventory.Add(offer.Item);
        else if (offer.Tome is not null) Progression.TomeBook.Current?.Add(offer.Tome);
        else offer.Apply(_player, _inventory);
    }

    private void OnRunEnded(RunEnded e)
    {
        // MarkOver, not Finish: this handler is reacting to an announcement that
        // has already been made. Finish would publish it a second time and show
        // the results screen twice.
        Run.MarkOver(e.Survived);
        _results.Show(Run, _inventory, _weapons);
    }

    /// <summary>One machine-readable line per run, consumed by tests/smoke.ps1.
    /// Printed on the way out so a run that crashes first prints nothing, which
    /// is the correct signal rather than a misleading partial success.</summary>
    public string Summary() =>
        $"SUMMARY t={Run.Clock} kills={Run.Kills} level={_inventory.Level} " +
        $"xp={_inventory.Xp} gold={_inventory.Gold} alive={_enemies.AliveCount} " +
        $"weapons={_weapons.Weapons.Count} items={_inventory.TotalStacks} " +
        $"hp={_player.Health.Current:0}/{_player.Health.Max:0} seed={Run.Seed} " +
        $"jumps={_player.Jumps} airjumps={_player.AirJumpsUsed} dashes={_player.Dashes} " +
        $"slides={_player.Slides} slams={_player.Slams} slamhits={_player.SlamHits} " +
        $"peakspeed={_player.PeakSpeed:0.0} {ChestSpawner.Instance?.SummaryFields} " +
        $"phase={Run.Phase} phases={Run.PhaseCount} stage={Run.StageId} phasestate={_stages.State} " +
        $"{Meta.RunRewards.SummaryFields} " +
        $"{Interactables.Instance?.SummaryFields} " +
        $"{ShrineSpawner.Instance?.SummaryFields}";

    private async System.Threading.Tasks.Task RunScreenshot()
    {
        var timer = GetTree().CreateTimer(Screenshot.AfterSeconds);
        await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
        await Screenshot.Capture(GetTree(), this, Screenshot.Path!);
        GD.Print(Summary());

        // Autopilot runs end the run for real before quitting, after SUMMARY is
        // printed so its numbers are untouched. A 30-second capture never reaches
        // the timer and rarely dies, so without this the run-end path — results
        // screen, the RunEnded listeners, the closing sting — would never execute
        // in any automated run.
        if (Screenshot.Auto && !Run.Over) Run.Finish(survived: _player.IsAlive);
        GD.Print(_audio.Report());
        GetTree().Quit();
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<LevelGained>(OnLevelGained);
        EventBus.Unsubscribe<RunEnded>(OnRunEnded);
        Run?.Dispose();
        EventBus.Reset();
        Instance = null;
    }
}
