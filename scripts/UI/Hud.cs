using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Progression;

namespace Wavebreak.UI;

/// <summary>
/// The always-on readout: health, shield, experience, level, clock, kills, gold,
/// the weapons currently owned, and the phase: which map, how long until its
/// boss, and where the portal is once it opens.
///
/// Built from <see cref="Control"/> nodes in code rather than from a scene. At
/// this size that is a straight win — the whole layout is one readable file with
/// its constants next to the things they position, and it cannot silently break
/// because someone dragged a node in the editor. If the HUD ever grows art, this
/// is the file to replace with a <c>.tscn</c>; nothing else refers to its
/// internals.
///
/// Everything countable is accumulated from <see cref="EventBus"/> rather than
/// read off the systems that own it. That is not tidiness: it means the HUD
/// holds no reference to the inventory, the weapon system or the director, so
/// none of them can be refactored into breaking it, and a headless run can
/// publish the same events to assert on the same numbers.
///
/// Three values are read directly each frame instead, because they are
/// continuous and deliberately have no event: health — which regeneration and
/// shield recharge move every frame, and which <see cref="PlayerHealth"/>
/// batches notifications for on purpose — the run clock, and the dash cooldown.
/// Sampling a few floats is the honest implementation; a "health changed" event
/// sixty times a second would not be.
/// </summary>
public sealed partial class Hud : Control
{
    // ------------------------------------------------------------- geometry
    // Authored against 1280x720. Anchors do the scaling, so these are margins
    // and sizes rather than positions on a fixed grid.

    private const float Margin = 18f;
    private const float XpBarHeight = 16f;
    private const float HealthBarWidth = 300f;
    private const float HealthBarHeight = 26f;
    private const float ShieldBarHeight = 8f;
    private const float RowHeight = 22f;
    private const float DashBarWidth = 180f;
    private const float DashBarHeight = 10f;
    private const float DashBarBottom = 44f;

    // -------------------------------------------------------------- palette

    private static readonly Color Ink = new(0.93f, 0.95f, 0.98f);
    private static readonly Color Outline = new(0.02f, 0.03f, 0.05f, 0.9f);
    private static readonly Color Trough = new(0.05f, 0.06f, 0.09f, 0.82f);
    private static readonly Color HealthFull = new(0.32f, 0.82f, 0.42f);
    private static readonly Color HealthLow = new(0.94f, 0.26f, 0.26f);
    private static readonly Color ShieldTint = new(0.55f, 0.80f, 1.00f);
    private static readonly Color XpTint = new(0.42f, 0.86f, 1.00f);
    private static readonly Color GoldTint = new(1.00f, 0.82f, 0.32f);
    private static readonly Color KillTint = new(1.00f, 0.62f, 0.56f);
    private static readonly Color LevelTint = new(0.80f, 0.72f, 1.00f);
    private static readonly Color PhaseTint = new(0.78f, 0.84f, 0.95f);
    private static readonly Color BossTint = new(1.00f, 0.36f, 0.32f);
    private static readonly Color PortalTint = new(0.55f, 1.00f, 0.92f);

    // Charging is a dim slate so the meter recedes while there is nothing to do
    // with it; ready is the brightest cool tone on the HUD, because "you can
    // dash now" is the one state the player needs to catch in peripheral vision.
    private static readonly Color DashCharging = new(0.40f, 0.48f, 0.60f);
    private static readonly Color DashReady = new(0.62f, 0.98f, 1.00f);

    /// <summary>Health fraction below which the bar starts pulsing. Set at a
    /// third rather than the more usual quarter because a single late-game hit
    /// routinely removes more than a quarter of the pool, and a warning that only
    /// appears after the hit that kills you is not a warning.</summary>
    private const float LowHealth = 0.34f;

    // ------------------------------------------------------------ bus state

    private int _level = 1;
    private int _xpTotal;
    private int _gold;
    private int _kills;
    private int _elites;

    private readonly Dictionary<string, Label> _weaponRows = new();
    private readonly List<string> _weaponOrder = new();

    // ------------------------------------------------------------- widgets

    private ColorRect _xpFill = null!;
    private Label _levelLabel = null!;
    private Label _clockLabel = null!;
    private Label _killLabel = null!;
    private Label _goldLabel = null!;
    private Label _healthLabel = null!;
    private ColorRect _healthFill = null!;
    private ColorRect _shieldTrough = null!;
    private ColorRect _shieldFill = null!;
    private VBoxContainer _weaponList = null!;
    private ColorRect _dashHalo = null!;
    private ColorRect _dashFill = null!;
    private Label _dashLabel = null!;
    private Label _phaseLabel = null!;
    private Label _phaseStatus = null!;
    private Control _card = null!;
    private Label _cardTitle = null!;
    private Label _cardSubtitle = null!;

    // ------------------------------------------------------------- runtime

    private PlayerHealth? _health;
    private RunState? _run;

    /// <summary>Displayed health fraction, chased toward the real one. The bar
    /// lags the value by a few frames on purpose: an instant drop is hard to read
    /// as a quantity, and the slide is what tells the player *how much* a hit
    /// took rather than merely that it landed.</summary>
    private float _shownHealth = 1f;

    private float _shownXp;
    private float _hurtFlash;
    private float _time;
    private int _lastClockSecond = -1;

    /// <summary>1 the frame the dash comes back, decaying to 0. Drives the halo
    /// and the white flash on the meter.</summary>
    private float _dashFlash;
    private bool _dashWasReady = true;

    /// <summary>Last countdown shown, in tenths of a second, so the label is only
    /// re-shaped when the digits change — same reasoning as the clock.</summary>
    private int _lastDashTenths = -1;

    /// <summary>Seconds left on the title card, counting down. The card is
    /// faded by this rather than by a tween so a pause (level-up) holds it.</summary>
    private float _cardTime;
    private const float CardSeconds = 3.6f;
    private string _lastStatus = "";
    private bool _subscribed;

    // ------------------------------------------------------------ lifecycle

    public override void _Ready()
    {
        Name = "Hud";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // The HUD is a readout, never a target. Without this it eats clicks meant
        // for the level-up cards underneath the pointer.
        MouseFilter = MouseFilterEnum.Ignore;

        BuildXpBar();
        BuildTopLeft();
        BuildClock();
        BuildTopRight();
        BuildWeaponList();
        BuildDashMeter();
        BuildPhaseReadout();
        BuildTitleCard();
        AddChild(new PortalMarker());

        Subscribe();
        RefreshXpBar();
        RefreshCounters();
    }

    public override void _ExitTree() => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;

        EventBus.Subscribe<XpGained>(OnXpGained);
        EventBus.Subscribe<LevelGained>(OnLevelGained);
        EventBus.Subscribe<GoldGained>(OnGoldGained);
        EventBus.Subscribe<EnemyKilled>(OnEnemyKilled);
        EventBus.Subscribe<WeaponAcquired>(OnWeaponAcquired);
        EventBus.Subscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Subscribe<PhaseStarted>(OnPhaseStarted);
        EventBus.Subscribe<PortalOpened>(OnPortalOpened);
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;

        EventBus.Unsubscribe<XpGained>(OnXpGained);
        EventBus.Unsubscribe<LevelGained>(OnLevelGained);
        EventBus.Unsubscribe<GoldGained>(OnGoldGained);
        EventBus.Unsubscribe<EnemyKilled>(OnEnemyKilled);
        EventBus.Unsubscribe<WeaponAcquired>(OnWeaponAcquired);
        EventBus.Unsubscribe<PlayerDamaged>(OnPlayerDamaged);
        EventBus.Unsubscribe<PhaseStarted>(OnPhaseStarted);
        EventBus.Unsubscribe<PortalOpened>(OnPortalOpened);
    }

    // ---------------------------------------------------------------- build

    private void BuildXpBar()
    {
        // Full-width strip along the very top. Experience is the thing the player
        // checks most often and least deliberately, so it gets the one piece of
        // screen edge that can be read without looking away from the character.
        ColorRect trough = Stretch(new ColorRect { Color = Trough }, 0f, XpBarHeight);
        AddChild(trough);

        _xpFill = new ColorRect { Color = XpTint, MouseFilter = MouseFilterEnum.Ignore };
        _xpFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _xpFill.AnchorRight = 0f;
        _xpFill.OffsetRight = 0f;
        trough.AddChild(_xpFill);

        // A thin bright lip under the bar. Without it the strip merges with a
        // pale sky box and the fill edge stops being findable.
        AddChild(Stretch(new ColorRect { Color = new Color(0f, 0f, 0f, 0.5f) }, XpBarHeight, 2f));
    }

    private void BuildTopLeft()
    {
        float y = XpBarHeight + 12f;

        _levelLabel = Text("LV 1", 24, LevelTint);
        Place(_levelLabel, Margin, y, 120f, 30f);
        AddChild(_levelLabel);

        y += 30f;

        var healthTrough = new ColorRect { Color = Trough, MouseFilter = MouseFilterEnum.Ignore };
        Place(healthTrough, Margin, y, HealthBarWidth, HealthBarHeight);
        AddChild(healthTrough);

        _healthFill = new ColorRect { Color = HealthFull, MouseFilter = MouseFilterEnum.Ignore };
        _healthFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _healthFill.AnchorRight = 1f;
        healthTrough.AddChild(_healthFill);

        _healthLabel = Text("100 / 100", 15, Ink, HorizontalAlignment.Center);
        _healthLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _healthLabel.VerticalAlignment = VerticalAlignment.Center;
        healthTrough.AddChild(_healthLabel);

        y += HealthBarHeight + 3f;

        // Shield gets its own thin bar rather than sharing the health bar. A
        // shield drawn as a segment of the same bar makes the health number and
        // the bar length disagree, which is worse than one extra line.
        _shieldTrough = new ColorRect { Color = Trough, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        Place(_shieldTrough, Margin, y, HealthBarWidth, ShieldBarHeight);
        AddChild(_shieldTrough);

        _shieldFill = new ColorRect { Color = ShieldTint, MouseFilter = MouseFilterEnum.Ignore };
        _shieldFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _shieldFill.AnchorRight = 1f;
        _shieldTrough.AddChild(_shieldFill);
    }

    private void BuildClock()
    {
        _clockLabel = Text("00:00", 40, Ink, HorizontalAlignment.Center);
        _clockLabel.AnchorLeft = 0f;
        _clockLabel.AnchorRight = 1f;
        _clockLabel.OffsetLeft = 0f;
        _clockLabel.OffsetRight = 0f;
        _clockLabel.AnchorTop = 0f;
        _clockLabel.AnchorBottom = 0f;
        _clockLabel.OffsetTop = XpBarHeight + 8f;
        _clockLabel.OffsetBottom = XpBarHeight + 58f;
        AddChild(_clockLabel);
    }

    private void BuildTopRight()
    {
        float y = XpBarHeight + 12f;

        _killLabel = Text("0 KILLS", 20, KillTint, HorizontalAlignment.Right);
        PlaceRight(_killLabel, Margin, y, 220f, 26f);
        AddChild(_killLabel);

        _goldLabel = Text("0 GOLD", 20, GoldTint, HorizontalAlignment.Right);
        PlaceRight(_goldLabel, Margin, y + 26f, 220f, 26f);
        AddChild(_goldLabel);
    }

    private void BuildWeaponList()
    {
        _weaponList = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _weaponList.AddThemeConstantOverride("separation", 2);
        Place(_weaponList, Margin, XpBarHeight + 12f + 30f + HealthBarHeight + ShieldBarHeight + 14f,
              HealthBarWidth, RowHeight * 10f);
        AddChild(_weaponList);
    }

    private void BuildDashMeter()
    {
        // Bottom-centre, under the character, rather than beside the health bar.
        // A cooldown is checked at the moment of deciding to act, and at that
        // moment the eyes are on the player model in the middle of the screen —
        // a meter in the corner means looking away exactly when it matters.
        float top = -(DashBarBottom + DashBarHeight);

        // The halo sits behind the trough and only shows during the ready flash:
        // a bar that briefly grows a glow reads as "something happened" even out
        // of the corner of the eye, where a colour change alone is easy to miss.
        _dashHalo = new ColorRect { Color = DashReady with { A = 0f }, MouseFilter = MouseFilterEnum.Ignore };
        PlaceBottomCentre(_dashHalo, top - 4f, DashBarWidth + 8f, DashBarHeight + 8f);
        AddChild(_dashHalo);

        var trough = new ColorRect { Color = Trough, MouseFilter = MouseFilterEnum.Ignore };
        PlaceBottomCentre(trough, top, DashBarWidth, DashBarHeight);
        AddChild(trough);

        _dashFill = new ColorRect { Color = DashReady, MouseFilter = MouseFilterEnum.Ignore };
        _dashFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _dashFill.AnchorRight = 1f;
        trough.AddChild(_dashFill);

        // The key is named on the meter itself. Dash is the verb new players
        // forget they have, and a meter that does not say which key it belongs
        // to teaches nothing.
        _dashLabel = Text("DASH  [SHIFT]  READY", 15, DashReady, HorizontalAlignment.Center);
        PlaceBottomCentre(_dashLabel, top - 24f, DashBarWidth + 120f, 22f);
        AddChild(_dashLabel);
    }

    /// <summary>
    /// Two lines under the clock: which phase and map, and what the phase is
    /// doing - counting down to the boss, fighting it, or waiting at the portal.
    /// Under the clock because that is where the eye already goes to ask "how
    /// long until something happens".
    /// </summary>
    private void BuildPhaseReadout()
    {
        _phaseLabel = Text("", 16, PhaseTint, HorizontalAlignment.Center);
        StretchCentre(_phaseLabel, XpBarHeight + 56f, 22f);
        AddChild(_phaseLabel);

        _phaseStatus = Text("", 18, Ink, HorizontalAlignment.Center);
        StretchCentre(_phaseStatus, XpBarHeight + 78f, 24f);
        AddChild(_phaseStatus);
    }

    /// <summary>The big centred card shown when a map is entered. Upper third
    /// rather than dead centre, so it never covers the player model.</summary>
    private void BuildTitleCard()
    {
        _card = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _card.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_card);

        _cardTitle = Text("", 52, Ink, HorizontalAlignment.Center);
        _cardTitle.AddThemeConstantOverride("outline_size", 10);
        _cardTitle.AnchorLeft = 0f; _cardTitle.AnchorRight = 1f;
        _cardTitle.AnchorTop = 0.22f; _cardTitle.AnchorBottom = 0.22f;
        _cardTitle.OffsetTop = 0f; _cardTitle.OffsetBottom = 64f;
        _card.AddChild(_cardTitle);

        _cardSubtitle = Text("", 22, PhaseTint, HorizontalAlignment.Center);
        _cardSubtitle.AnchorLeft = 0f; _cardSubtitle.AnchorRight = 1f;
        _cardSubtitle.AnchorTop = 0.22f; _cardSubtitle.AnchorBottom = 0.22f;
        _cardSubtitle.OffsetTop = 66f; _cardSubtitle.OffsetBottom = 96f;
        _card.AddChild(_cardSubtitle);
    }

    private void ShowCard(string title, string subtitle, Color tint)
    {
        _cardTitle.Text = title;
        _cardTitle.AddThemeColorOverride("font_color", tint);
        _cardSubtitle.Text = subtitle;
        _cardTime = CardSeconds;
        _card.Visible = true;
    }

    // --------------------------------------------------------------- events

    private void OnPhaseStarted(PhaseStarted e)
    {
        _phaseLabel.Text = $"PHASE {e.Phase}/{e.PhaseCount}  —  {e.StageName.ToUpperInvariant()}";
        ShowCard($"PHASE {e.Phase}  ·  {e.StageName.ToUpperInvariant()}", e.Tagline, Ink);
    }

    private void OnPortalOpened(PortalOpened e)
        => ShowCard("PORTAL OPEN", "Follow the light to the next map", PortalTint);

    /// <summary>
    /// Tracks the lifetime experience total the bar is derived from.
    ///
    /// The bar is computed from the total through <see cref="XpCurve"/> rather
    /// than by counting deltas since the last level. Counting is the version that
    /// looks correct and is not: a level-up carries the surplus forward, so an orb
    /// that grants two levels at once leaves a counter reading zero while the real
    /// progress is a third of the way along. Deriving from the total cannot drift
    /// because there is only one number.
    /// </summary>
    private void OnXpGained(XpGained e)
    {
        _xpTotal = e.Total;
        RefreshXpBar();
    }

    private void OnLevelGained(LevelGained e)
    {
        _level = e.Level;
        RefreshXpBar();
    }

    private void OnGoldGained(GoldGained e)
    {
        _gold = e.Total;
        RefreshCounters();
    }

    private void OnEnemyKilled(EnemyKilled e)
    {
        _kills++;
        if (e.WasElite) _elites++;
        RefreshCounters();
    }

    private void OnPlayerDamaged(PlayerDamaged e)
    {
        // Nullified hits — dodges, i-frames — must not flash the bar. Telling the
        // player they were hurt when they were not is the fastest way to make a
        // defensive stat feel like it does nothing.
        if (e.Info is { Nullified: false, Amount: > 0f })
            _hurtFlash = 1f;
    }

    /// <summary>
    /// Adds or re-levels a weapon row.
    ///
    /// Rows are created once and their text rewritten afterwards. A weapon can be
    /// levelled a dozen times in a run, and rebuilding the list each time would
    /// churn a Label and its theme overrides for a string change.
    /// </summary>
    private void OnWeaponAcquired(WeaponAcquired e)
    {
        if (string.IsNullOrEmpty(e.Id)) return;

        WeaponDef? def = Catalog.WeaponById(e.Id);
        string name = def?.DisplayName ?? e.Id;

        if (!_weaponRows.TryGetValue(e.Id, out Label? row))
        {
            row = Text(name, 16, Ink);
            row.CustomMinimumSize = new Vector2(0f, RowHeight);
            // Tinted to the weapon's own colour so the list matches what the
            // player sees coming out of themselves.
            if (def is not null) row.AddThemeColorOverride("font_color", def.Tint);
            _weaponList.AddChild(row);
            _weaponRows[e.Id] = row;
            _weaponOrder.Add(e.Id);
        }

        bool maxed = def is not null && e.NewLevel >= def.MaxLevel;
        row.Text = maxed ? $"{name}   MAX" : $"{name}   Lv {e.NewLevel}";
    }

    // -------------------------------------------------------------- refresh

    private void RefreshXpBar()
    {
        _shownXp = XpCurve.ProgressToNext(_xpTotal);

        // Apply a drop here rather than leaving it to the easing in _Process.
        // A level-up pauses the tree, so _Process does not run again until the
        // player has chosen — and the bar would sit at "almost full" underneath
        // the level-up screen, which is precisely the wrong thing to be showing.
        if (_shownXp < _xpFill.AnchorRight) _xpFill.AnchorRight = _shownXp;

        // The level and the numbers live on one line so the bar itself can stay a
        // bare strip. Reading "how far to the next choice" is what the bar is for;
        // the digits are for the player who wants to know whether to push.
        _levelLabel.Text = $"LV {_level}   {XpCurve.XpIntoLevel(_xpTotal)} / {XpCurve.XpForLevel(_level)}";
    }

    private void RefreshCounters()
    {
        // Elites are worth calling out separately: they are the reason a run
        // either has items or does not, and "34 kills" hides whether four of them
        // were the ones that mattered.
        _killLabel.Text = _elites > 0 ? $"{_kills} KILLS  ({_elites}★)" : $"{_kills} KILLS";
        _goldLabel.Text = $"{_gold} GOLD";
    }

    // ----------------------------------------------------------------- loop

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;

        UpdateClock();
        UpdateHealth(dt);
        UpdateDash(dt);
        UpdatePhase(dt);

        // The bar eases toward the target rather than snapping — exponential
        // smoothing, so the rate is frame-rate independent; a fixed lerp per frame
        // is a different animation at 30fps and at 144.
        //
        // Only ever eases *upward*; a drop is applied the moment it happens, in
        // RefreshXpBar. Easing a level-up backwards is a full-width slide that
        // reads as losing progress rather than gaining a level.
        if (_shownXp > _xpFill.AnchorRight)
            _xpFill.AnchorRight = Mathf.Lerp(_xpFill.AnchorRight, _shownXp, 1f - Mathf.Exp(-11f * dt));

        if (_hurtFlash > 0f)
            _hurtFlash = Mathf.Max(0f, _hurtFlash - dt * 3.2f);
    }

    private void UpdateClock()
    {
        _run ??= Main.Instance?.Run;
        if (_run is null) return;

        // Only touch the label when the displayed second actually changes.
        // Assigning a string to a Label re-shapes its text every time, which is
        // real work to do sixty times a second for a value that changes once.
        int second = (int)_run.Seconds;
        if (second == _lastClockSecond) return;

        _lastClockSecond = second;
        _clockLabel.Text = _run.Clock;
    }

    /// <summary>
    /// Sampled like the clock: the countdown and the boss's health are
    /// continuous, and the label is only rewritten when its text changes.
    /// </summary>
    private void UpdatePhase(float dt)
    {
        if (_cardTime > 0f)
        {
            _cardTime -= dt;
            // Quick fade in, hold, slow fade out.
            float shown = CardSeconds - _cardTime;
            float alpha = Mathf.Min(Mathf.Clamp(shown / 0.25f, 0f, 1f), Mathf.Clamp(_cardTime / 0.9f, 0f, 1f));
            _card.Modulate = new Color(1f, 1f, 1f, alpha);
            if (_cardTime <= 0f) _card.Visible = false;
        }

        StageDirector? stages = Main.Instance?.Stages;
        if (stages is null) return;

        string status;
        Color tint = Ink;
        switch (stages.State)
        {
            case StageDirector.PhaseState.Waves:
                status = $"BOSS IN {RunState.Format(Mathf.Ceil(stages.SecondsToBoss))}";
                break;
            case StageDirector.PhaseState.Boss:
                float hp = stages.BossHealth;
                string name = stages.BossName.ToUpperInvariant();
                status = hp >= 0f ? $"BOSS  {name}  {Mathf.CeilToInt(hp * 100f)}%" : $"BOSS  {name}";
                // Overtime is said out loud: pressure that rises silently feels
                // like a balance bug rather than a consequence.
                if (stages.OvertimeSeconds > 0f)
                    status += $"   OVERTIME +{Mathf.RoundToInt(stages.OvertimeSeconds / 30f * 12f)}%";
                tint = BossTint;
                break;
            case StageDirector.PhaseState.PortalOpen:
                status = "PORTAL OPEN  —  GO";
                tint = PortalTint;
                break;
            default:
                status = "VICTORY";
                tint = PortalTint;
                break;
        }

        if (status == _lastStatus) return;
        _lastStatus = status;
        _phaseStatus.Text = status;
        _phaseStatus.AddThemeColorOverride("font_color", tint);
    }

    private void UpdateHealth(float dt)
    {
        if (_health is null || !IsInstanceValid(_health))
        {
            _health = Player.Instance?.Health;
            if (_health is null) return;
        }

        float max = _health.Max;
        float current = _health.Current;
        float target = max <= 0f ? 0f : Mathf.Clamp(current / max, 0f, 1f);

        // Losing health slides; gaining it snaps. A regeneration tick that eases
        // in looks like lag, whereas a hit that eases out is the only way to see
        // how big the hit was.
        _shownHealth = target < _shownHealth
            ? Mathf.Lerp(_shownHealth, target, 1f - Mathf.Exp(-9f * dt))
            : target;

        _healthFill.AnchorRight = _shownHealth;

        // Colour is driven by the *real* fraction, not the animated one, so the
        // warning arrives with the hit rather than after the bar has finished
        // catching up.
        Color colour = HealthLow.Lerp(HealthFull, Mathf.Clamp(target / LowHealth, 0f, 1f));

        if (target <= LowHealth)
        {
            // 4 Hz pulse. Fast enough to register as an alarm, slow enough not to
            // be the brightest thing on a screen that is already busy.
            float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 8f);
            colour = colour.Lerp(Colors.White, pulse * 0.35f);
        }

        _healthFill.Color = colour.Lerp(Colors.White, _hurtFlash * 0.6f);

        float shieldMax = _health.ShieldMax;
        bool hasShield = shieldMax > 0.01f;
        _shieldTrough.Visible = hasShield;
        if (hasShield)
            _shieldFill.AnchorRight = Mathf.Clamp(_health.Shield / shieldMax, 0f, 1f);

        _healthLabel.Text = shieldMax > 0.01f && _health.Shield >= 1f
            ? $"{Mathf.CeilToInt(current)} / {Mathf.RoundToInt(max)}  +{Mathf.CeilToInt(_health.Shield)}"
            : $"{Mathf.CeilToInt(current)} / {Mathf.RoundToInt(max)}";
    }

    /// <summary>
    /// Sampled each frame, like health: the cooldown is a continuous countdown
    /// with no event, and adding one would mean the controller publishing sixty
    /// times a second so that one bar can read a float.
    /// </summary>
    private void UpdateDash(float dt)
    {
        Player? player = Player.Instance;
        if (player is null || !IsInstanceValid(player)) return;

        float total = Mathf.Max(0.05f, player.DashCooldownTotal);
        float remaining = Mathf.Max(0f, player.DashCooldownRemaining);
        bool ready = remaining <= 0f;

        if (ready && !_dashWasReady) _dashFlash = 1f;
        _dashWasReady = ready;
        if (_dashFlash > 0f) _dashFlash = Mathf.Max(0f, _dashFlash - dt * 2.5f);

        // Fills as it recharges — an emptying bar would read as "dash is
        // draining", which is the opposite of what is happening.
        _dashFill.AnchorRight = ready ? 1f : Mathf.Clamp(1f - remaining / total, 0f, 1f);

        Color colour = ready ? DashReady : DashCharging;
        if (player.IsDashing) colour = DashReady.Lerp(Colors.White, 0.5f);
        _dashFill.Color = colour.Lerp(Colors.White, _dashFlash * 0.7f);
        _dashHalo.Color = DashReady with { A = _dashFlash * 0.55f };

        // Countdown in tenths: whole seconds are too coarse for a cooldown that
        // is itself only a few seconds long, and hundredths are noise.
        int tenths = ready ? 0 : Mathf.CeilToInt(remaining * 10f);
        if (tenths == _lastDashTenths) return;

        _lastDashTenths = tenths;
        _dashLabel.Text = ready ? "DASH  [SHIFT]  READY" : $"DASH  [SHIFT]  {tenths / 10f:0.0}s";
        _dashLabel.AddThemeColorOverride("font_color", ready ? DashReady : DashCharging.Lerp(Ink, 0.4f));
    }

    // -------------------------------------------------------------- helpers

    /// <summary>A label with the HUD's outline treatment. The outline is not
    /// decoration: the HUD is drawn over a lit 3D scene whose brightness the UI
    /// does not control, and unoutlined text disappears the moment an explosion
    /// happens behind it.</summary>
    private static Label Text(string text, int size, Color color,
                              HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Outline);
        label.AddThemeConstantOverride("outline_size", 6);
        return label;
    }

    /// <summary>Pin a control to the top-left corner at an explicit rectangle.</summary>
    private static void Place(Control control, float left, float top, float width, float height)
    {
        control.AnchorLeft = 0f;
        control.AnchorRight = 0f;
        control.AnchorTop = 0f;
        control.AnchorBottom = 0f;
        control.OffsetLeft = left;
        control.OffsetRight = left + width;
        control.OffsetTop = top;
        control.OffsetBottom = top + height;
    }

    /// <summary>Pin a control to the top-right corner, <paramref name="right"/>
    /// pixels in from the edge.</summary>
    private static void PlaceRight(Control control, float right, float top, float width, float height)
    {
        control.AnchorLeft = 1f;
        control.AnchorRight = 1f;
        control.AnchorTop = 0f;
        control.AnchorBottom = 0f;
        control.OffsetLeft = -(right + width);
        control.OffsetRight = -right;
        control.OffsetTop = top;
        control.OffsetBottom = top + height;
    }

    /// <summary>Centre a control horizontally against the bottom edge;
    /// <paramref name="top"/> is negative, measured up from the bottom.</summary>
    private static void PlaceBottomCentre(Control control, float top, float width, float height)
    {
        control.AnchorLeft = 0.5f;
        control.AnchorRight = 0.5f;
        control.AnchorTop = 1f;
        control.AnchorBottom = 1f;
        control.OffsetLeft = -width * 0.5f;
        control.OffsetRight = width * 0.5f;
        control.OffsetTop = top;
        control.OffsetBottom = top + height;
    }

    /// <summary>Full-width band at a fixed offset from the top, for centred text.</summary>
    private static void StretchCentre(Control control, float top, float height)
    {
        control.AnchorLeft = 0f;
        control.AnchorRight = 1f;
        control.AnchorTop = 0f;
        control.AnchorBottom = 0f;
        control.OffsetLeft = 0f;
        control.OffsetRight = 0f;
        control.OffsetTop = top;
        control.OffsetBottom = top + height;
    }

    /// <summary>Stretch a control across the full width at a given vertical band.</summary>
    private static T Stretch<T>(T control, float top, float height) where T : Control
    {
        control.MouseFilter = MouseFilterEnum.Ignore;
        control.AnchorLeft = 0f;
        control.AnchorRight = 1f;
        control.AnchorTop = 0f;
        control.AnchorBottom = 0f;
        control.OffsetLeft = 0f;
        control.OffsetRight = 0f;
        control.OffsetTop = top;
        control.OffsetBottom = top + height;
        return control;
    }

    // ------------------------------------------------------------ inspection
    // Read by tests and the results screen rather than by the game itself.

    /// <summary>Kills counted from the bus this run.</summary>
    public int Kills => _kills;

    /// <summary>Elite and boss kills counted from the bus this run.</summary>
    public int Elites => _elites;

    /// <summary>Gold, as last reported by <see cref="GoldGained"/>.</summary>
    public int Gold => _gold;

    /// <summary>Current level, as last reported by <see cref="LevelGained"/>.</summary>
    public int Level => _level;

    /// <summary>Weapon ids in the order they were first acquired.</summary>
    public IReadOnlyList<string> WeaponIds => _weaponOrder;
}
