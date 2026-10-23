using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>
/// The "show FPS" counter: a small label in the top-right corner. It lives
/// under the root window rather than a scene, so it survives the title-to-run
/// scene change, and it is only created once someone turns it on.
/// </summary>
public sealed partial class FpsOverlay : CanvasLayer
{
    private static FpsOverlay? _instance;
    private Label _label = null!;
    private double _clock;

    public static bool Showing => _instance is { } i && IsInstanceValid(i) && i.Visible;

    public static void Sync(SceneTree tree)
    {
        bool want = GraphicsSettings.Current.ShowFps;
        if (_instance is null || !IsInstanceValid(_instance))
        {
            if (!want) return;
            _instance = new FpsOverlay();
            // The root may be busy adding the scene that asked.
            tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
        }
        _instance.Visible = want;
    }

    public override void _Ready()
    {
        Name = "FpsOverlay";
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
        _label = MainMenu.MakeLabel("", 14, new Color(0.75f, 1f, 0.75f));
        _label.HorizontalAlignment = HorizontalAlignment.Right;
        _label.AnchorLeft = 1f; _label.AnchorRight = 1f;
        _label.OffsetLeft = -110f; _label.OffsetRight = -8f;
        _label.OffsetTop = 4f; _label.OffsetBottom = 24f;
        AddChild(_label);
        Update();
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_clock < 0.25) return;
        _clock = 0;
        Update();
    }

    private void Update() => _label.Text = $"{Engine.GetFramesPerSecond():0} FPS";
}
