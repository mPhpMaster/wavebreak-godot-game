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
    public static bool SeedRequested { get; private set; }
    public static bool Requested => Path is not null;

    /// <summary>--auto: drive the player with the autopilot and take level-up
    /// choices automatically. Without it a headless run stops dead at the first
    /// level-up, because the panel pauses the tree waiting for a click that is
    /// never coming — so every check past level 2 silently never runs.</summary>
    public static bool Auto { get; private set; }

    /// <summary>--show-stats: hold the Tab character sheet open, so a capture
    /// can verify it without a key press.</summary>
    public static bool ShowStats { get; private set; }

    /// <summary>--invulnerable: the player cannot die. For long verification
    /// runs that need to reach late-game content the autopilot would not
    /// survive to; never meaningful for balance numbers.</summary>
    public static bool Invulnerable { get; set; }

    public static void ParseArgs()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot="))
                Path = arg["--shot=".Length..];
            else if (arg.StartsWith("--shot-after="))
                AfterSeconds = arg["--shot-after=".Length..].ToFloat();
            else if (arg.StartsWith("--seed="))
            {
                if (ulong.TryParse(arg["--seed=".Length..], out ulong seed))
                { Seed = seed; SeedRequested = true; }
            }
            else if (arg == "--auto")
                Auto = true;
            else if (arg == "--show-stats")
                ShowStats = true;
            else if (arg == "--invulnerable")
                Invulnerable = true;
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
        if (Meta.Loc.IsArabic && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--latin-report") >= 0)
            LatinReport(tree.Root, path);
    }

    /// <summary>--latin-report (with --lang=ar): after each capture, print every
    /// visible label, button or 3D label whose text still has a Latin word in
    /// it, so a translation gap shows up as a line instead of a squint at a PNG.
    /// Key names and short tokens (E, Esc, x1.5, SHIFT) are expected and pass.</summary>
    private static void LatinReport(Node root, string shot)
    {
        var word = new System.Text.RegularExpressions.Regex(@"[A-Za-z]{3,}");
        var allowed = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "WAVEBREAK", "SHIFT", "Esc", "Enter", "Space", "Ctrl", "Tab", "Alt", "Mouse", "Backspace", "dps",
        };
        void Walk(Node n)
        {
            string? text = n switch
            {
                Label { Visible: true } l when l.IsVisibleInTree() => l.Text,
                Button { Visible: true } b when b.IsVisibleInTree() => b.Text,
                RichTextLabel { Visible: true } r when r.IsVisibleInTree() => r.GetParsedText(),
                Label3D { Visible: true } l3 when l3.IsVisibleInTree() => l3.Text,
                _ => null,
            };
            // A whole-key text is translated by the engine at draw time.
            if (!string.IsNullOrEmpty(text) && n.AutoTranslateMode != Node.AutoTranslateModeEnum.Disabled)
                text = TranslationServer.Translate(text);
            if (!string.IsNullOrEmpty(text))
                foreach (System.Text.RegularExpressions.Match m in word.Matches(text))
                    if (!allowed.Contains(m.Value))
                    {
                        GD.Print($"LATIN {System.IO.Path.GetFileName(shot)} {n.Name}: {text.Replace('\n', '|')}");
                        break;
                    }
            foreach (Node c in n.GetChildren()) Walk(c);
        }
        Walk(root);
    }
}
