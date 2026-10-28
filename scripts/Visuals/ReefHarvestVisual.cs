using Godot;
using Wavebreak.Data;
using Wavebreak.Meta;

namespace Wavebreak.Visuals;

public sealed partial class ReefHarvestVisual : Node3D
{
    private readonly ReefHarvestDef _def;
    private Label3D _label = null!;
    private Node3D _growth = null!;
    public bool Spent { get; private set; }
    public ReefHarvestVisual(ReefHarvestDef def)
    { _def = def; Position = def.Position; Quaternion = new Quaternion(Vector3.Up, def.Normal); }

    public override void _Ready()
    {
        _growth = new Node3D(); AddChild(_growth);
        var kit = new MeshKit();
        for (int i = 0; i < 5; i++)
        {
            float angle = i * 2.4f;
            var start = new Vector3(Mathf.Cos(angle) * 0.35f, 0, Mathf.Sin(angle) * 0.35f);
            Vector3 tip = start + new Vector3(Mathf.Cos(angle) * 0.35f, 0.8f + (i % 3) * 0.35f, Mathf.Sin(angle) * 0.35f);
            if (_def.Crystal) kit.Cone(start, tip, 0.24f, new Color(0.3f, 0.95f, 1), 0.65f, 5);
            else
            {
                kit.Tube(start, tip, 0.08f, 0.03f, new Color(0.14f, 0.65f, 0.4f));
                kit.Ellipsoid(new Transform3D(Basis.Identity, tip), new Vector3(0.3f, 0.18f, 0.45f), new Color(0.55f, 0.95f, 0.48f));
            }
        }
        _growth.AddChild(new MeshInstance3D { Mesh = kit.ToMesh(), MaterialOverride = ToonMaterials.Prop });
        AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.65f, OuterRadius = 0.74f, Rings = 20, RingSegments = 5 },
            Position = Vector3.Up * 0.08f, MaterialOverride = Game.Arena.Flat(new Color(0.3f, 0.95f, 0.7f), 1) });
        _label = new Label3D { Position = Basis.Inverse() * (Vector3.Up * 2), FontSize = 32, PixelSize = 0.009f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 8, Visible = false };
        AddChild(_label);
    }

    public void Focus(bool show)
    {
        _label.Visible = show && !Spent;
        if (!show || Spent) return;
        string reward = _def.Reward == Actors.PickupKind.Xp ? "+6 XP" : _def.Reward == Actors.PickupKind.Gold ? "+4 gold" : "+5% health";
        string key = InputMap.ActionGetEvents("interact").Count > 0 ? InputMap.ActionGetEvents("interact")[0].AsText() : "E";
        _label.Text = Loc.F(_def.Crystal ? "{0}: harvest crystal ({1})" : "{0}: gather reef herb ({1})", key, Loc.T(reward));
    }

    public void Deplete(bool feedback)
    {
        if (Spent) return;
        Spent = true; _growth.Scale = Vector3.One * 0.15f; _label.Visible = feedback;
        if (!feedback) return;
        _label.Text = Loc.T("Harvested — reward released");
        var voice = new AudioStreamPlayer { Bus = Audio.AudioSettings.SfxBus,
            Stream = Audio.ReefAudioSynth.Stream(Audio.ReefAudioKind.Splash), VolumeDb = -26, PitchScale = 1.35f };
        AddChild(voice); voice.Play();
        var tween = CreateTween(); tween.TweenInterval(1.5); tween.TweenCallback(Callable.From(() => _label.Visible = false));
    }
}
