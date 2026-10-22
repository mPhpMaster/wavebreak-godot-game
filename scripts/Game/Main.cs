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

    public Actors.Player Player => _player;
    public Combat.WeaponSystem Weapons => _weapons;
    public Progression.Inventory Inventory => _inventory;
    public Actors.EnemyManager Enemies => _enemies;

    public override void _Ready()
    {
        Instance = this;
        Screenshot.ParseArgs();

        Run = new RunState(Screenshot.Requested ? Screenshot.Seed : (ulong)GD.Randi());
        GD.Print($"[wavebreak] seed {Run.Seed}");

        AddChild(new Arena());
        BuildWorld();

        EventBus.Subscribe<LevelGained>(OnLevelGained);
        EventBus.Subscribe<RunEnded>(OnRunEnded);

        if (Screenshot.Requested)
            _ = RunScreenshot();
    }

    private void BuildWorld()
    {
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
        _offers = new Progression.OfferPool(Catalog.Items, Catalog.Weapons, Run.Rng);

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

        // Everyone starts with one weapon; a run with no way to fight is not a
        // run, and the first level-up should be a choice rather than a rescue.
        _weapons.Add(Catalog.Weapons[0]);

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
        _enemies.Tick(dt, Run.Seconds);
        _weapons.Tick(dt);
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
            Choose(choices[Run.Rng.RangeInt(0, choices.Count)]);
            return;
        }
        _levelUp.Show(choices, Choose);
    }

    private void Choose(Progression.Offer offer)
    {
        if (offer.Weapon is not null) _weapons.Add(offer.Weapon);
        else if (offer.Item is not null) _inventory.Add(offer.Item);
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
        $"peakspeed={_player.PeakSpeed:0.0}";

    private async System.Threading.Tasks.Task RunScreenshot()
    {
        var timer = GetTree().CreateTimer(Screenshot.AfterSeconds);
        await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
        await Screenshot.Capture(GetTree(), this, Screenshot.Path!);
        GD.Print(Summary());
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
