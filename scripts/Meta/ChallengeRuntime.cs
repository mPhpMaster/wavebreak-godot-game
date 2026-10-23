using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Stats;

namespace Wavebreak.Meta;

/// <summary>
/// The behaviour half of <see cref="Challenges"/>: the timed and per-frame
/// rules (Scalding Sand, Ebb Clock, Becalmed, Spring Tide, Sea Fret), the
/// records written when a map is cleared, and a small corner readout.
/// One node per run, added by <see cref="Challenges.Attach"/>; it always
/// exists so the clear/win records and the reset on exit have one home.
/// </summary>
public sealed partial class ChallengeRuntime : Node
{
    private const float MoltenStillAfter = 0.75f;
    private const float MoltenPulse = 0.5f;
    private const float MoltenFractionPerSecond = 0.08f;
    private const float SpeedBossSeconds = 75f;
    private const float PacifistSeconds = 45f;
    private const float SurgeEvery = 45f;
    private const float SurgeLength = 8f;
    private const float FogDensity = 0.045f;

    private static readonly object PacifistSource = new();

    private readonly Main _main;
    private float _still, _moltenPulse;
    private float _bossDeadline = -1f;
    private float _phaseStartedAt;
    private float _nextSurgeAt = SurgeEvery, _surgeEndsAt = -1f;
    private bool _pacifistBoost;
    private Label? _label;
    private float _labelIn;

    public ChallengeRuntime(Main main) => _main = main;

    public override void _Ready()
    {
        Name = "ChallengeRuntime";
        EventBus.Subscribe<PhaseStarted>(OnPhaseStarted);
        EventBus.Subscribe<PhaseBossArrived>(OnBossArrived);
        EventBus.Subscribe<PortalOpened>(OnPortalOpened);
        EventBus.Subscribe<FinalBossDefeated>(OnFinalBoss);

        if (Challenges.Active.Count == 0) return;
        var layer = new CanvasLayer { Name = "ChallengeUI", Layer = 15 };
        AddChild(layer);
        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 1f, AnchorRight = 1f, OffsetLeft = -460f, OffsetRight = -16f, OffsetTop = 96f, OffsetBottom = 200f,
        };
        _label.AddThemeFontSizeOverride("font_size", 15);
        _label.AddThemeColorOverride("font_color", new Color(0.55f, 0.9f, 1f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.03f, 0.05f, 0.9f));
        _label.AddThemeConstantOverride("outline_size", 5);
        layer.AddChild(_label);
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<PhaseStarted>(OnPhaseStarted);
        EventBus.Unsubscribe<PhaseBossArrived>(OnBossArrived);
        EventBus.Unsubscribe<PortalOpened>(OnPortalOpened);
        EventBus.Unsubscribe<FinalBossDefeated>(OnFinalBoss);
        // The timed flags must not outlive the run. The multipliers stay until
        // the next BeginRun: RunRewards may settle after this node has left.
        Challenges.WeaponsSilenced = false;
        Challenges.SurgeActive = false;
    }

    private float Now => _main.Run.Seconds;

    // ------------------------------------------------------------- events

    private void OnPhaseStarted(PhaseStarted e)
    {
        _phaseStartedAt = Now;
        _bossDeadline = -1f;
        if (Challenges.IsActive("fog")) Fog();
    }

    private void OnBossArrived(PhaseBossArrived e)
    {
        if (!Challenges.IsActive("speed")) return;
        _bossDeadline = Now + SpeedBossSeconds;
        Popup($"EBB CLOCK  ·  {SpeedBossSeconds:0}s", new Color(0.4f, 0.85f, 1f));
    }

    private void OnPortalOpened(PortalOpened e)
    {
        _bossDeadline = -1f;
        Challenges.RecordClear(_main.Run.StageId);
    }

    private void OnFinalBoss(FinalBossDefeated e)
    {
        _bossDeadline = -1f;
        Challenges.RecordClear(_main.Run.StageId);
        Challenges.RecordWin();
    }

    // -------------------------------------------------------------- ticks

    public override void _Process(double delta)
    {
        if (Challenges.Active.Count == 0 || _main.Run is null || _main.Run.Over) return;
        Player? p = _main.Player;
        if (p is null || !IsInstanceValid(p)) return;

        // Becalmed: weapons silent for the opening of every map.
        if (Challenges.IsActive("pacifist"))
        {
            bool silent = Now - _phaseStartedAt < PacifistSeconds;
            Challenges.WeaponsSilenced = silent;
            if (silent != _pacifistBoost)
            {
                _pacifistBoost = silent;
                if (silent) p.Stats.Add(Stat.MoveSpeed, ModifierOp.Additive, 0.15f, PacifistSource);
                else
                {
                    p.Stats.RemoveAllFrom(PacifistSource);
                    Popup("WEAPONS FREE", new Color(1f, 0.84f, 0.38f));
                }
            }
        }

        // Spring Tide: periodic surges on the run clock.
        if (Challenges.IsActive("surge"))
        {
            if (Challenges.SurgeActive && Now >= _surgeEndsAt) Challenges.SurgeActive = false;
            if (!Challenges.SurgeActive && Now >= _nextSurgeAt)
            {
                Challenges.SurgeActive = true;
                _surgeEndsAt = Now + SurgeLength;
                _nextSurgeAt = Now + SurgeEvery;
                Popup("SPRING TIDE", new Color(0.3f, 0.8f, 1f));
            }
        }

        // Ebb Clock: the boss must fall in time.
        if (_bossDeadline > 0f && Now >= _bossDeadline && p.Health.IsAlive && !(_main.Stages?.Won ?? false))
        {
            _bossDeadline = -1f;
            Popup("THE TIDE TOOK YOU", new Color(1f, 0.35f, 0.3f));
            GD.Print("META challenge speed: boss deadline missed");
            p.Health.Kill();
        }

        _labelIn -= (float)delta;
        if (_label is not null && _labelIn <= 0f)
        {
            _labelIn = 0.25f;
            string text = string.Join("  ·  ", Challenges.Active.Select(c => c.Name));
            if (_bossDeadline > 0f) text += $"\nEBB CLOCK {Mathf.CeilToInt(_bossDeadline - Now)}s";
            if (Challenges.WeaponsSilenced) text += $"\nBECALMED {Mathf.CeilToInt(PacifistSeconds - (Now - _phaseStartedAt))}s";
            if (Challenges.SurgeActive) text += "\nSPRING TIDE";
            _label.Text = text;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Challenges.IsActive("molten") || _main.Run is null || _main.Run.Over) return;
        Player? p = _main.Player;
        if (p is null || !IsInstanceValid(p) || !p.Health.IsAlive) return;
        float dt = (float)delta;

        // The arrival breather is safe ground: the title card is being read.
        if (Now - _phaseStartedAt < 3f || p.PlanarVelocity.Length() > 1.5f)
        {
            _still = 0f;
            _moltenPulse = 0f;
            return;
        }
        _still += dt;
        if (_still < MoltenStillAfter) return;
        _moltenPulse -= dt;
        if (_moltenPulse > 0f) return;
        _moltenPulse = MoltenPulse;
        p.Health.TakeDamage(new DamageInfo
        {
            Amount = p.Health.Max * MoltenFractionPerSecond * MoltenPulse,
            CanCrit = false,
            Source = "scalding sand",
        });
    }

    // ------------------------------------------------------------ helpers

    private void Fog()
    {
        if (_main.GetNodeOrNull("Arena") is not { } arena) return;
        foreach (Node child in arena.GetChildren())
            if (child is WorldEnvironment { Environment: { } env })
            {
                // Gameplay fog: GraphicsSettings keeps it (thinner) even with fog off.
                env.SetMeta(GraphicsSettings.GameplayFogMeta, FogDensity);
                GraphicsSettings.ApplyEnvironment(env);
            }
    }

    private static void Popup(string text, Color color)
    {
        if (Player.Instance is { } p && IsInstanceValid(p))
            EventBus.Publish(new PopupRequested(p.GlobalPosition + new Vector3(0f, 4f, 0f), text, color, 1.3f));
    }
}
