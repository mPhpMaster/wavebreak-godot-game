using System;
using Godot;

namespace Wavebreak.UI;

public sealed partial class BuildInspectTile : Control
{
    private readonly Func<string> _describe;
    private BuildInspectTooltip? _tooltip;

    public BuildInspectTile(Func<string> describe)
    {
        _describe = describe;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        TooltipText = describe();
    }

    public override GodotObject _MakeCustomTooltip(string forText)
    {
        CloseTooltip();
        _tooltip = new BuildInspectTooltip(_describe(), GetViewportRect().Size);
        return _tooltip;
    }

    internal void CloseTooltip()
    {
        if (_tooltip is not null && IsInstanceValid(_tooltip) && _tooltip.IsInsideTree())
        {
            Window window = _tooltip.GetWindow();
            if (window != GetTree().Root) window.Hide();
            _tooltip.Hide();
        }
        _tooltip = null;
    }

    public override void _ExitTree() => CloseTooltip();
}
