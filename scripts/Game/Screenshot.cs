using System.Threading.Tasks;
using Godot;

namespace Wavebreak.Game;

/// <summary>
/// Self-verification harness: run the game, let it settle, save a PNG, quit.
///
///     Godot.exe --path . -- --shot=out.png --shot-after=6 --seed=42
///
/// This exists because "it compiles" and "it works" are different claims, and
/// only one of them can be made from a build log. A captured frame after several
/// seconds of real simulation shows the arena, the player, live enemies, the HUD
/// and the damage numbers all at once — if any of those is broken, it is visible.
///
/// Note the double dash: everything after it is passed to the game rather than
/// consumed by the engine.
/// </summary>
public static class Screenshot
{
    public static string? Path { get; private set; }
    public static float AfterSeconds { get; private set; } = 5f;
    public static ulong Seed { get; private set; } = 1;
    public static bool Requested => Path is not null;

    /// <summary>--auto: drive the player with the autopilot and take level-up
    /// choices automatically. Without it a headless run stops dead at the first
    /// level-up, because the panel pauses the tree waiting for a click that is
    /// never coming — so every check past level 2 silently never runs.</summary>
    public static bool Auto { get; private set; }

    public static void ParseArgs()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot="))
                Path = arg["--shot=".Length..];
            else if (arg.StartsWith("--shot-after="))
                AfterSeconds = arg["--shot-after=".Length..].ToFloat();
            else if (arg.StartsWith("--seed="))
                Seed = (ulong)arg["--seed=".Length..].ToInt();
            else if (arg == "--auto")
                Auto = true;
        }
    }

    /// <summary>Capture after the current frame has finished drawing. Grabbing
    /// the viewport mid-frame yields a blank or half-drawn image.</summary>
    public static async Task Capture(SceneTree tree, Node node, string path)
    {
        await tree.ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        Image img = node.GetViewport().GetTexture().GetImage();
        Error err = img.SavePng(path);
        GD.Print(err == Error.Ok
            ? $"[shot] saved {path} ({img.GetWidth()}x{img.GetHeight()})"
            : $"[shot] FAILED to save {path}: {err}");
    }
}
