using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.Stats;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>
/// Owns the map shrines: where they stand, who is in reach, and what each does.
///
///     ShrineSpawner.Populate(map, rng, arenaRadius, stage);
///
/// Four kinds, counts per map in <see cref="StageDef"/>, numbers in
/// <see cref="MapShrines"/>:
///
/// <b>Charge</b> — stand in the ring for <see cref="MapShrines.ChargeSeconds"/>;
/// leaving drains it. Full: pick one of three stat blessings for the run.
///
/// <b>Challenge</b> — E starts a timed trial (kill N in 30 s, or slay the
/// guardian elites in 40 s). Success pays like a gilded chest; failure pays
/// nothing. One trial at a time.
///
/// <b>Statue</b> — E buys a random blessing for gold; the price climbs with every
/// purchase in the run. A statue gives <see cref="MapShrines.StatueUsesPerShrine"/>.
///
/// <b>Greed</b> — E accepts a curse stack: enemies stronger and more numerous
/// (<see cref="Stat.EnemyScaling"/>) for more gold, XP and luck. Stacks.
///
/// Every stat change is a <see cref="StatSheet"/> modifier with a source object of
/// its own, so a grant can be inspected or removed exactly.
///
/// Like the chest spawner, it lives for the whole run (the statue price and greed
/// stacks belong to the run) and the shrines are children of the map, so a map
/// that frees itself frees its shrines.
/// </summary>
public sealed partial class ShrineSpawner : Node
{
    private const string InteractAction = "interact";

    /// <summary>The run's spawner, or null between runs.</summary>
    public static ShrineSpawner? Instance { get; private set; }

    /// <summary>Boon and challenge rolls. Forked from the run seed so shrines
    /// never shift the run's main stream.</summary>
    public Rng Rng { get; private set; }

    private readonly List<MapShrine> _shrines = new();
    private ShrinePrompt _prompt = null!;
    private ShrinePanel _panel = null!;
    private string _keyName = "E";
    private bool _hasAction;
    private bool _interactRequested;
    private MapShrine? _focus;

    // Run state.
    private int _statueBought;
    private readonly List<object> _greedSources = new();

    // Challenge.
    private enum Trial { None, Slaughter, Guardians }
    private Trial _trial;
    private MapShrine? _trialShrine;
    private float _trialLeft;
    private int _trialTarget;
    private int _trialKillsAt;
    private readonly List<Enemy> _guardians = new();
    private int _guardiansDown;

    // Run totals, for SUMMARY and the audit.
    public int Placed { get; private set; }
    public int Charged { get; private set; }
    public int Blessings { get; private set; }
    public int ChallengesWon { get; private set; }
    public int ChallengesFailed { get; private set; }
    public int StatueGoldSpent { get; private set; }
    public int StatueBought => _statueBought;
    public int GreedStacks => _greedSources.Count;

    public IReadOnlyList<MapShrine> Shrines => _shrines;

    public string SummaryFields =>
        $"shrines={Placed} shrinecharged={Charged} shrineblessings={Blessings} " +
        $"challengewon={ChallengesWon} challengefailed={ChallengesFailed} " +
        $"statues={_statueBought} statuegold={StatueGoldSpent} greed={GreedStacks}";

    /// <summary>What the challenge banner says, or null when no trial runs.</summary>
    public string? ChallengeText => _trial switch
    {
        Trial.Slaughter => Meta.Loc.F("CHALLENGE  ·  KILL {0}/{1}  ·  {2}s", Math.Min(KillsSinceStart, _trialTarget), _trialTarget, Mathf.CeilToInt(_trialLeft)),
        Trial.Guardians => Meta.Loc.F("CHALLENGE  ·  SLAY THE GUARDIANS {0}/{1}  ·  {2}s", _guardiansDown, _trialTarget, Mathf.CeilToInt(_trialLeft)),
        _ => null,
    };

    public float ChallengeSecondsLeft => _trial == Trial.None ? 0f : _trialLeft;

    public bool ChallengeRunning => _trial != Trial.None;

    private int KillsSinceStart => (Main.Instance?.Run.Kills ?? 0) - _trialKillsAt;

    public ShrineSpawner() => Rng = new Rng(0x5A1E5UL);

    public ShrineSpawner(Rng rng) => Rng = rng;

    /// <summary>Creates the run's spawner. Call once per run, then
    /// <see cref="Populate"/> per map.</summary>
    public static ShrineSpawner Attach(Node parent, ulong runSeed)
    {
        var spawner = new ShrineSpawner(new Rng(runSeed ^ 0x51A7E5C0FFEEUL));
        parent.AddChild(spawner);
        return spawner;
    }

    public override void _Ready()
    {
        Name = "Shrines";
        Instance = this;

        _prompt = new ShrinePrompt();
        AddChild(_prompt);

        // Own layer, so the shared HUD is not edited; drawn over it.
        var layer = new CanvasLayer { Name = "ShrineUI", Layer = 2 };
        AddChild(layer);
        layer.AddChild(new ShrineHud());
        _panel = new ShrinePanel();
        layer.AddChild(_panel);

        _hasAction = InputMap.HasAction(InteractAction);
        _keyName = KeyName();

        EventBus.Subscribe<RunEnded>(OnRunEnded);
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<RunEnded>(OnRunEnded);
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------ placement

    /// <summary>
    /// Places the stage's shrines under <paramref name="map"/>, scattered inside
    /// <paramref name="arenaRadius"/> on open floor. Uses the arena's terrain
    /// footprints (<see cref="Arena.ClearSpotNear"/>) to stay off mounds,
    /// platforms and ridges, keeps <see cref="MapShrines.MinSpacing"/> between
    /// shrines and stays inside the band the hidden secrets use (80–90%).
    /// Call it before the chest spawner populates, so chests treat the shrines
    /// as obstacles and stay out of the charge rings.
    /// </summary>
    public void RequestInteract() => _interactRequested = true;

    /// <summary>Nearest unspent shrine within range, optionally of one kind.</summary>
    public MapShrine? Nearest(Vector3 from, float maxDistance, MapShrineKind? kind = null)
    {
        MapShrine? best = null;
        float bestSq = maxDistance * maxDistance;
        foreach (MapShrine s in _shrines)
        {
            if (!Usable(s) || (kind is { } k && s.Kind != k)) continue;
            Vector3 d = s.GlobalPosition - from; d.Y = 0f;
            float sq = d.LengthSquared();
            if (sq >= bestSq) continue;
            best = s; bestSq = sq;
        }
        return best;
    }

    public static bool InRing(MapShrine s, Vector3 at)
    {
        Vector3 d = s.GlobalPosition - at;
        float dy = MathF.Abs(d.Y); d.Y = 0f;
        return d.LengthSquared() <= MapShrines.ChargeRadius * MapShrines.ChargeRadius && dy <= 2.5f;
    }

    public static bool InReach(MapShrine s, Vector3 at)
    {
        if (s.Kind == MapShrineKind.Charge) return InRing(s, at);
        Vector3 d = s.GlobalPosition - at;
        float dy = MathF.Abs(d.Y); d.Y = 0f;
        return d.LengthSquared() <= MapShrines.InteractRadius * MapShrines.InteractRadius && dy <= MapShrines.InteractHeight;
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _shrines.RemoveAll(s => !IsInstanceValid(s) || s.IsQueuedForDeletion());

        bool pressed = _interactRequested;
        _interactRequested = false;

        Player? player = Player.Instance;
        bool live = player is not null && IsInstanceValid(player) && player.IsAlive && Main.Instance?.Run.Over != true;
        Vector3 me = live ? player!.GlobalPosition : Vector3.Zero;

        if (_trial != Trial.None) TickTrial(dt);

        // Charge rings fill and drain, and the names show within 16 m.
        foreach (MapShrine s in _shrines)
        {
            if (s.Spent) continue;
            Vector3 d = s.GlobalPosition - me; d.Y = 0f;
            s.ShowName(live && d.LengthSquared() < 16f * 16f && !InReach(s, me));
            if (s.Kind != MapShrineKind.Charge) continue;
            bool inside = live && InRing(s, me);
            float rate = inside ? 1f / MapShrines.ChargeSeconds : -1f / MapShrines.ChargeDrainSeconds;
            if (inside || s.Charge > 0f) s.SetCharge(s.Charge + rate * dt);
            if (s.Charge >= 1f) CompleteCharge(s);
        }

        _focus = live ? Focus(me) : null;
        if (_focus is null) { _prompt.HideNow(); return; }

        ShowPrompt(_focus);
        if (pressed && _focus.Kind != MapShrineKind.Charge) Interact(_focus);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_hasAction || e is InputEventKey { Echo: true } || Net.Coop.LocalHold) return;
        if (!e.IsActionPressed(InteractAction)) return;
        if (_focus is null || _focus.Kind == MapShrineKind.Charge) return;
        _interactRequested = true;
        GetViewport().SetInputAsHandled();
    }

    private void ShowPrompt(MapShrine s)
    {
        int gold = Inventory.Current?.Gold ?? 0;
        string name = Meta.Loc.T(MapShrines.Name(s.Kind));
        switch (s.Kind)
        {
            case MapShrineKind.Charge:
                _prompt.ShowText(s.Top, name + "\n" + Meta.Loc.F("charging  {0:0}%", s.Charge * 100f), MapShrines.Tint(s.Kind).Lightened(0.4f));
                break;
            case MapShrineKind.Challenge:
                _prompt.ShowText(s.Top, s.Active
                        ? $"{name}\n{ChallengeText}"
                        : ChallengeRunning
                            ? name + "\n" + Meta.Loc.T("another challenge is running")
                            : Meta.Loc.F("{0} — start challenge\nwin: gilded reward · lose: nothing", _keyName),
                    ChallengeRunning && !s.Active ? ShrinePrompt.TooPoor : ShrinePrompt.Neutral);
                break;
            case MapShrineKind.Statue:
            {
                int price = MapShrines.StatuePrice(_statueBought);
                bool ok = gold >= price;
                int left = MapShrines.StatueUsesPerShrine - s.Uses;
                _prompt.ShowText(s.Top,
                    ok ? Meta.Loc.F("{0} — offer {1} gold for a blessing\n{2} left", _keyName, price, left)
                       : Meta.Loc.F("{0} — offer {1} gold  (need {2} more)", _keyName, price, price - gold),
                    ok ? ShrinePrompt.Neutral : ShrinePrompt.TooPoor);
                break;
            }
            default:
            {
                int left = MapShrines.GreedUsesPerShrine - s.Uses;
                _prompt.ShowText(s.Top, Meta.Loc.F("{0} — accept a curse  ({1} left)", _keyName, left) + "\n" + MapShrines.DescribeGreed(),
                    MapShrines.Tint(s.Kind).Lightened(0.45f));
                break;
            }
        }
    }

    private void Interact(MapShrine s)
    {
        switch (s.Kind)
        {
            case MapShrineKind.Challenge: StartTrial(s); break;
            case MapShrineKind.Statue: BuyStatue(s); break;
            case MapShrineKind.Greed: AcceptCurse(s); break;
        }
    }

    // --------------------------------------------------------------- charge

    private void CompleteCharge(MapShrine s)
    {
        s.SetCharge(1f);
        s.MarkSpent();
        s.Celebrate();
        Charged++;
        Publish(s, ShrineOutcome.Charged);

        Player? player = Player.Instance;
        float luck = player?.Stats.Get(Stat.Luck) ?? 0f;
        List<ShrineBoon> boons = MapShrines.RollBoons(Rng, MapShrines.ChargeChoices, luck);
        GD.Print($"[shrine] charge full t={Main.Instance?.Run.Seconds ?? 0f:0}s offers: " +
                 string.Join(" | ", boons.ConvertAll(b => $"{b.Title} {b.Effect} ({b.Rarity})")));

        Vector3 at = s.Top;
        if (Screenshot.Auto)
        {
            // Never pause an unattended run; pick from the shrine's own stream.
            Grant(boons[Rng.RangeInt(0, boons.Count)], MapShrineKind.Charge, at);
            return;
        }
        _panel.Show(boons, b => Grant(b, MapShrineKind.Charge, at));
    }

    /// <summary>Apply a blessing with a source tag of its own.</summary>
    private void Grant(ShrineBoon boon, MapShrineKind from, Vector3 at)
    {
        Player? player = Player.Instance;
        if (player is null || !IsInstanceValid(player)) return;
        var source = new ShrineGrant(from, boon);
        player.Stats.Add(boon.Stat, boon.Op, boon.Value, source);
        Blessings++;
        EventBus.Publish(new PopupRequested(at, $"{Meta.Loc.T(boon.Title)} {boon.Effect}", boon.Color, 1.4f));
        EventBus.Publish(new MapShrineUsed(from, ShrineOutcome.Blessed, at, $"{boon.Title} {boon.Effect}"));
        GD.Print($"[shrine] blessing {from}: {boon.Title} {boon.Effect} ({boon.Rarity})");
    }

    /// <summary>Source tag for one shrine grant. A class, so every grant is a
    /// distinct reference and <see cref="StatSheet.RemoveAllFrom"/> is exact.</summary>
    public sealed class ShrineGrant
    {
        public MapShrineKind From { get; }
        public ShrineBoon Boon { get; }
        public ShrineGrant(MapShrineKind from, ShrineBoon boon) { From = from; Boon = boon; }
        public override string ToString() => $"{MapShrines.Name(From)}: {Boon.Title}";
    }

    // --------------------------------------------------------------- statue

    private void BuyStatue(MapShrine s)
    {
        Inventory? inv = Inventory.Current;
        Player? player = Player.Instance;
        if (inv is null || player is null) return;

        int price = MapShrines.StatuePrice(_statueBought);
        if (!inv.SpendGold(price))
        {
            s.Deny();
            _prompt.Deny();
            Publish(s, ShrineOutcome.Denied, $"need {price}");
            return;
        }

        _statueBought++;
        StatueGoldSpent += price;
        s.Uses++;
        ShrineBoon boon = MapShrines.RollBoons(Rng, 1, player.Stats.Get(Stat.Luck), Rarity.Uncommon)[0];
        EventBus.Publish(new PopupRequested(s.Top + Vector3.Down * 0.6f, $"-{price} gold", new Color(1f, 0.78f, 0.25f)));
        Grant(boon, MapShrineKind.Statue, s.Top);
        s.Celebrate();
        if (s.Uses >= MapShrines.StatueUsesPerShrine) s.MarkSpent();
        GD.Print($"[shrine] statue bought price={price} next={MapShrines.StatuePrice(_statueBought)}");
    }

    // ---------------------------------------------------------------- greed

    private void AcceptCurse(MapShrine s)
    {
        Player? player = Player.Instance;
        if (player is null) return;
        var source = new GreedStack(_greedSources.Count + 1);
        foreach (var (stat, op, value) in MapShrines.GreedStack) player.Stats.Add(stat, op, value, source);
        _greedSources.Add(source);
        s.Uses++;
        s.Celebrate();
        if (s.Uses >= MapShrines.GreedUsesPerShrine) s.MarkSpent();

        Color tint = MapShrines.Tint(MapShrineKind.Greed);
        EventBus.Publish(new PopupRequested(s.Top, $"CURSED  x{GreedStacks}", tint.Lightened(0.3f), 1.6f));
        EventBus.Publish(new PopupRequested(s.Top + Vector3.Down * 0.7f, MapShrines.DescribeGreed(), tint, 1f));
        Publish(s, ShrineOutcome.Cursed, $"x{GreedStacks}");
        GD.Print($"[shrine] greed stack {GreedStacks}: enemy x{player.Stats.Get(Stat.EnemyScaling):0.00} " +
                 $"gold +{player.Stats.Get(Stat.GoldGain) * 100f:0}% luck {player.Stats.Get(Stat.Luck):0.00}");
    }

    /// <summary>Source tag for one greed stack.</summary>
    public sealed class GreedStack
    {
        public int Index { get; }
        public GreedStack(int index) => Index = index;
        public override string ToString() => $"Greed Altar x{Index}";
    }

    // ------------------------------------------------------------ challenge

    private void OnRunEnded(RunEnded e)
    {
        if (_trial != Trial.None) EndTrial(false, quiet: true);
    }

    // -------------------------------------------------------------- helpers

    private static bool Usable(MapShrine s) => IsInstanceValid(s) && !s.IsQueuedForDeletion() && !s.Spent;

    private MapShrine? Focus(Vector3 at)
    {
        MapShrine? best = null;
        float bestSq = float.MaxValue;
        foreach (MapShrine s in _shrines)
        {
            if (!Usable(s) || !InReach(s, at)) continue;
            float sq = (s.GlobalPosition - at).LengthSquared();
            if (sq < bestSq) { best = s; bestSq = sq; }
        }
        return best;
    }

    private static void Publish(MapShrine s, ShrineOutcome outcome, string detail = "")
        => EventBus.Publish(new MapShrineUsed(s.Kind, outcome, s.GlobalPosition, detail));

    private static void Publish(MapShrineKind kind, ShrineOutcome outcome, Vector3 at, string detail)
        => EventBus.Publish(new MapShrineUsed(kind, outcome, at, detail));

    private static string KeyName()
    {
        if (!InputMap.HasAction(InteractAction)) return "E";
        foreach (InputEvent ev in InputMap.ActionGetEvents(InteractAction))
        {
            if (ev is not InputEventKey k) continue;
            Key code = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            if (code != Key.None) return OS.GetKeycodeString(code);
        }
        return "E";
    }
}
