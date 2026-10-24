using System.Threading.Tasks;
using Godot;

namespace Wavebreak.Game;

/// <summary>
/// <c>--results-shot=&lt;png&gt;</c>: play for <c>--results-after=N</c> seconds
/// (default 8; use with <c>--auto --invulnerable</c> and <c>--give=...</c> for a
/// big build), end the run, photograph the results screen, quit.
/// <c>--results-win</c> ends it as survived instead of a death. Add
/// <c>--lang=ar</c> and <c>--resolution 1920x1080</c> for the other layouts.
/// </summary>
public sealed partial class Main
{
    private async Task ResultsShot(string path)
    {
        SceneTree tree = GetTree();
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        float after = 8f;
        bool win = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--results-after=")) after = arg["--results-after=".Length..].ToFloat();
            else if (arg == "--results-win") win = true;
        }
        await Wait(after);
        if (win) Run.Finish(survived: true);
        else
        {
            Screenshot.Invulnerable = false;
            _player.Health.Kill();
        }
        for (int i = 0; i < 40 && !_results.IsShown; i++) await Wait(0.25);
        await Wait(1.5);
        await Screenshot.Capture(tree, this, path);
        GD.Print($"RESULTSSHOT done mouse={Input.MouseMode}");
        tree.Quit();
    }
}
