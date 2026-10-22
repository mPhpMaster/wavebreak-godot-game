using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// Entry point for the art verification modes. Hooked from Main with one line so
/// the game's own startup stays untouched:
///
///     --art-showcase=hero|enemies|props-&lt;theme&gt;   stage a scene instead of a run
///     --art-stress=N                                 run the game with N extra enemies
///     --art-theme=&lt;theme&gt;                            run the game in a PropTheme (demo only:
///                                                    redresses the current arena from outside)
///     --art-procedural                               procedural models instead of imported ones
///
/// Both combine with --shot/--shot-after: the showcase captures its own frame and
/// quits, the stress mode lets the normal run capture and prints a STRESS line.
/// </summary>
public static class ArtShowcase
{
    /// <summary>Returns true when a showcase has taken over the scene, in which
    /// case the caller must skip building the game world.</summary>
    public static bool Intercept(Node3D main)
    {
        string? showcase = null, theme = null;
        int stress = 0;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--art-showcase")) showcase = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : "all";
            else if (arg.StartsWith("--art-stress=")) stress = arg["--art-stress=".Length..].ToInt();
            else if (arg == "--art-procedural") ArtAssets.UseImported = false;
            else if (arg == "--art-off") ArtAssets.ModelsEnabled = false;
            else if (arg.StartsWith("--art-theme=")) theme = arg["--art-theme=".Length..];
        }

        // Until the phase system dresses each map itself, every run is dressed
        // in the meadow so the game is not played on the bare primitive arena.
        // --art-theme=none keeps the old look (for A/B comparisons).
        theme ??= "meadow";
        if (theme != "none") main.CallDeferred(Node.MethodName.AddChild, new ThemeDresser(theme));

        if (stress > 0)
        {
            float stop = Game.Screenshot.Requested ? Game.Screenshot.AfterSeconds - 0.3f : 20f;
            main.CallDeferred(Node.MethodName.AddChild, new ArtStress(stress, stop));
        }

        if (showcase is null) return false;
        // The run was never built, so Main has nothing to tick.
        // Deferred: Godot switches processing on for a script with _Process
        // after _Ready returns, which would undo a direct call made from inside it.
        main.CallDeferred(Node.MethodName.SetProcess, false);
        main.CallDeferred(Node.MethodName.SetPhysicsProcess, false);
        main.AddChild(new ShowcaseStage(showcase));
        return true;
    }
}
