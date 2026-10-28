using System.Threading.Tasks;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>Progress reflects completed startup stages, never an elapsed-time estimate.</summary>
public sealed partial class RunLoadingPanel : CanvasLayer
{
    public int StepsCompleted { get; private set; }
    private readonly Label _status = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ProgressBar _progress = new() { ShowPercentage = true, CustomMinimumSize = new Vector2(480, 18) };
    public override void _Ready()
    {
        Name = "RunLoading";
        Layer = 110;
        ProcessMode = ProcessModeEnum.Always;
        var backdrop = new ColorRect { Color = new Color(.035f, .05f, .085f), MouseFilter = Control.MouseFilterEnum.Stop };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        backdrop.AddChild(center);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 22);
        center.AddChild(box);
        var title = new Label { Text = "WAVEBREAK", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 36);
        box.AddChild(title);
        box.AddChild(_status);
        box.AddChild(_progress);
    }
    public void ShowFailure()
    {
        _status.Text = Loc.T("Loading failed. Return to the menu and try again.");
        _progress.Hide(); Input.MouseMode = Input.MouseModeEnum.Visible;
        var back = new Button { Text = Loc.T("BACK") };
        _status.GetParent().AddChild(back);
        back.Pressed += () => { GetTree().Paused = false; GetTree().ChangeSceneToFile(MainMenu.TitleScene); };
        back.GrabFocus();
    }

    public async Task Step(string status, float complete)
    {
        StepsCompleted++;
        _status.Text = Loc.T(status);
        _progress.Value = complete;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (Mathf.IsEqualApprox(complete, 35))
            foreach (string arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--loading-shot="))
                    await Game.Screenshot.Capture(GetTree(), this, arg["--loading-shot=".Length..]);
    }
}


