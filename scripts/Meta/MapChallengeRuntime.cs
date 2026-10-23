using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.UI;

namespace Wavebreak.Meta;

/// <summary>
/// The live half of <see cref="MapChallenges"/>: watches the run for the
/// active challenge's goal, pays the reward the moment it is met, dresses the
/// challenge map for the visual twists (night, whiteout, ashfall), and shows a
/// small goal pill in the corner. One node per run; with no map challenge it
/// does nothing at all.
/// </summary>
public sealed partial class MapChallengeRuntime : Node
{
    private readonly Main _main;
    private MapChallengeDef? _def;
    private bool _done;
    private bool _onMap;
    private Label? _status;
    private PanelContainer? _pill;
    private OmniLight3D? _lantern;
    private float _poll;

    public MapChallengeRuntime(Main main) => _main = main;

    public override void _Ready()
    {
        Name = "MapChallengeRuntime";
        _def = MapChallenges.Active;
        if (_def is null) return;

        EventBus.Subscribe<PhaseStarted>(OnPhaseStarted);
        EventBus.Subscribe<PhaseBossArrived>(OnBossArrived);
        EventBus.Subscribe<PortalOpened>(OnPortalOpened);
        EventBus.Subscribe<FinalBossDefeated>(OnFinalBoss);
        BuildPill();
    }

    public override void _ExitTree()
    {
        if (_def is null) return;
        EventBus.Unsubscribe<PhaseStarted>(OnPhaseStarted);
        EventBus.Unsubscribe<PhaseBossArrived>(OnBossArrived);
        EventBus.Unsubscribe<PortalOpened>(OnPortalOpened);
        EventBus.Unsubscribe<FinalBossDefeated>(OnFinalBoss);
    }

    // ------------------------------------------------------------- events

    private void OnPhaseStarted(PhaseStarted e)
    {
        if (_def is null) return;
        _onMap = e.StageId == _def.MapId;
        if (_lantern is not null && IsInstanceValid(_lantern)) _lantern.QueueFree();
        _lantern = null;
        if (_onMap && _def.Twist is MapTwist.Night or MapTwist.Whiteout or MapTwist.Ashfall) CallDeferred(MethodName.DressMap);
    }

    private void OnBossArrived(PhaseBossArrived e)
    {
        if (_def is { Goal: MapGoal.Survive } && _onMap) Succeed();
    }

    private void OnPortalOpened(PortalOpened e)
    {
        if (_def is { Goal: MapGoal.Clear } && _onMap) Succeed();
    }

    private void OnFinalBoss(FinalBossDefeated e)
    {
        if (_def is null) return;
        // A one-map route: the final boss is the map's boss.
        if (_def.Goal != MapGoal.Swarm && _onMap) Succeed();
        else if (_def.Goal == MapGoal.Swarm && !e.SwarmFollows) Succeed();
    }

    public override void _Process(double delta)
    {
        if (_def is null) return;
        if (_lantern is not null && IsInstanceValid(_lantern) && Player.Instance is { } p && IsInstanceValid(p))
            _lantern.GlobalPosition = p.GlobalPosition + new Vector3(0f, 3.2f, 0f);

        _poll -= (float)delta;
        if (_poll > 0f) return;
        _poll = 0.25f;
        // Out of the way of the results screen.
        if (_pill is not null) _pill.Visible = !(_main.Run?.Over ?? false);
        if (!_done && _def.Goal == MapGoal.Swarm && _main.Stages?.Swarm is { } swarm && swarm.Seconds >= MapChallenges.SwarmSeconds)
            Succeed();
        RefreshPill();
    }

    private void Succeed()
    {
        if (_done || _def is null) return;
        if (_main.Run is { Over: true } && _def.Goal != MapGoal.Swarm) return;
        _done = true;
        string reward = MapChallenges.Complete(_def);
        string body = reward.Length > 0 ? reward : "Completed again. Nicely done.";
        Popup("MAP CHALLENGE COMPLETE!", CuteStyle.Mint);
        if (_main.GetNodeOrNull("MetaUI") is { } layer)
            foreach (Node n in layer.GetChildren())
                if (n is UnlockToast toast) toast.Enqueue($"{_def.Name}  ·  complete", body, CuteStyle.Mint);
        RefreshPill();
    }

    // ------------------------------------------------------------ dressing

    /// <summary>Night, whiteout and ashfall are light and fog: they go on the
    /// arena's own environment and sun once the map has been built.</summary>
    private void DressMap()
    {
        if (_def is null || _main.GetNodeOrNull("Arena") is not { } arena) return;
        foreach (Node child in arena.GetChildren())
        {
            if (child is WorldEnvironment { Environment: { } env })
            {
                switch (_def.Twist)
                {
                    case MapTwist.Night:
                        env.FogEnabled = true;
                        env.FogLightColor = new Color(0.16f, 0.12f, 0.30f);
                        env.FogDensity = Mathf.Max(env.FogDensity, 0.03f);
                        env.AmbientLightEnergy *= 0.5f;
                        env.AdjustmentBrightness = 0.8f;
                        if (env.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
                        {
                            sky.SkyTopColor = new Color(0.03f, 0.03f, 0.09f);
                            sky.SkyHorizonColor = new Color(0.12f, 0.08f, 0.22f);
                        }
                        break;
                    case MapTwist.Whiteout:
                        env.FogEnabled = true;
                        env.FogLightColor = new Color(0.92f, 0.95f, 1.00f);
                        env.FogDensity = Mathf.Max(env.FogDensity, 0.04f);
                        break;
                    case MapTwist.Ashfall:
                        env.FogEnabled = true;
                        env.FogLightColor = new Color(0.55f, 0.28f, 0.14f);
                        env.FogDensity = Mathf.Max(env.FogDensity, 0.032f);
                        break;
                }
            }
            else if (child is DirectionalLight3D sun && _def.Twist == MapTwist.Night)
            {
                sun.LightEnergy *= 0.28f;
                sun.LightColor = new Color(0.55f, 0.6f, 1f);
            }
        }
        if (_def.Twist == MapTwist.Night)
        {
            // A warm lantern over the player: night is for atmosphere, not for
            // losing track of yourself.
            _lantern = new OmniLight3D
            {
                Name = "ChallengeLantern",
                LightColor = new Color(1f, 0.82f, 0.55f),
                LightEnergy = 2.4f,
                OmniRange = 16f,
                ShadowEnabled = false,
            };
            _main.AddChild(_lantern);
        }
        GD.Print($"META map-twist dressed {_def.Twist}");
    }

    // ---------------------------------------------------------------- pill

    private void BuildPill()
    {
        var layer = new CanvasLayer { Name = "MapChallengeUI", Layer = 15 };
        AddChild(layer);
        _pill = CuteStyle.Panel(new Color(CuteStyle.Night, 0.82f), new Color(CuteStyle.Lavender, 0.9f), 16, 10);
        _pill.AnchorLeft = 1f; _pill.AnchorRight = 1f;
        _pill.OffsetLeft = -380f; _pill.OffsetRight = -16f; _pill.OffsetTop = 214f; _pill.OffsetBottom = 290f;
        layer.AddChild(_pill);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        _pill.AddChild(row);
        IconView icon = CuteStyle.Icon(new IconSpec("flag", CuteStyle.Butter), 40f, CuteStyle.Lavender);
        icon.Disc = false;
        icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(icon);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 0);
        row.AddChild(col);
        col.AddChild(CuteStyle.Text(_def!.Name, 16, CuteStyle.OnNight));
        _status = CuteStyle.Text("", 13, CuteStyle.OnNightDim);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_status);
        RefreshPill();
    }

    private void RefreshPill()
    {
        if (_status is null || _def is null) return;
        string goal = _def.GoalShort;
        if (_def.Goal == MapGoal.Swarm && _main.Stages?.Swarm is { } swarm)
            goal = $"Final Swarm {Mathf.Min(MapChallenges.SwarmSeconds, swarm.Seconds):0}/{MapChallenges.SwarmSeconds:0}s";
        _status.Text = _done ? "Complete!  " + (_def.Cosmetic != CosmeticKind.None ? "Reward unlocked" : $"+{_def.Pearls} {Pearls.Name}")
                             : $"{goal}  ·  {_def.RulesText}";
        _status.AddThemeColorOverride("font_color", _done ? CuteStyle.Mint : CuteStyle.OnNightDim);
    }

    private static void Popup(string text, Color color)
    {
        if (Player.Instance is { } p && IsInstanceValid(p))
            EventBus.Publish(new PopupRequested(p.GlobalPosition + new Vector3(0f, 4.2f, 0f), text, color, 1.5f));
    }
}
