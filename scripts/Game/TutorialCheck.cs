using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Meta;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>
/// <c>--check-tutorial=&lt;dir&gt;</c>: an automated run never gets the Classic
/// tutorial, so this first proves that, then adds one by hand and plays it with
/// injected pad and key events. Prints TUTORIAL lines and PASS/FAIL, with a
/// capture of a step for each device (add <c>--lang=ar</c> for the Arabic).
/// </summary>
public static class TutorialCheck
{
    public static string? Requested()
        => System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--check-tutorial="))?["--check-tutorial=".Length..];

    public static async Task Run(Main main, string dir)
    {
        SceneTree tree = main.GetTree();
        bool ok = true;
        void Expect(string what, bool cond) { GD.Print($"TUTORIAL {(cond ? "ok  " : "FAIL")} {what}"); ok &= cond; }
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        void Btn(JoyButton b, bool down) => Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = b, Pressed = down, Device = 0 });
        async Task Tap(JoyButton b) { Btn(b, true); await Wait(0.1); Btn(b, false); await Wait(0.1); }
        void Stick(float y) => Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = y, Device = 0 });

        Screenshot.Invulnerable = true;
        await Wait(1.0);
        Expect("an automated run has none", main.FindChildren("*", "", true, false).OfType<ClassicTutorial>().FirstOrDefault() is null);
        Expect("first Classic run qualifies", ClassicTutorial.Eligible(false, false, "classic", 0));
        Expect("automated runs do not", !ClassicTutorial.Eligible(true, false, "classic", 0));
        Expect("co-op does not", !ClassicTutorial.Eligible(false, true, "classic", 0));
        Expect("expeditions do not", !ClassicTutorial.Eligible(false, false, "city", 0));
        Expect("a second run does not", !ClassicTutorial.Eligible(false, false, "classic", 1));

        var tour = new ClassicTutorial();
        (main.GetNode("AwarenessUI") ?? main).AddChild(tour);
        await Wait(4.0);   // past the map's title card
        Expect("playing it records it", Profile.GetMeta(ClassicTutorial.SeenKey) != 0);
        ClassicTutorial.Replay();
        Expect("Settings can ask for it again", ClassicTutorial.Eligible(false, false, "classic", Profile.GetMeta(ClassicTutorial.SeenKey)));

        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F12, PhysicalKeycode = Key.F12, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F12, PhysicalKeycode = Key.F12, Pressed = false });
        await Wait(0.2);
        Expect($"step 1, keys: {tour.LineText}", tour.StepIndex == 0 && tour.LineText.Contains('W'));
        await Screenshot.Capture(tree, main, System.IO.Path.Combine(dir, $"tutorial-keys-{Loc.Language}.png"));

        await Tap(JoyButton.LeftStick);
        await Wait(0.2);
        Expect($"step 1, pad: {tour.LineText}", tour.StepIndex == 0 && tour.LineText != "");
        await Screenshot.Capture(tree, main, System.IO.Path.Combine(dir, $"tutorial-pad-{Loc.Language}.png"));
        Stick(-1f);
        LevelUpPanel hand = main.FindChildren("*", "", true, false).OfType<LevelUpPanel>().First();
        for (int i = 0; i < 40 && tour.StepIndex == 0; i++) { hand.Cancel(); await Wait(0.1); }   // the run's kills level up too
        Stick(0f);
        Expect($"walking moves on (now step {tour.StepIndex + 1})", tour.StepIndex >= 1);
        Expect($"skip names the pad: {tour.SkipText}", tour.SkipText.Contains(Keybinds.Hint("help_toggle")));
        Expect("the run was not paused for it", !tree.Paused);
        await Tap(JoyButton.DpadUp);
        await Wait(0.2);
        Expect("the pad's help button skips it", tour.StepIndex == -1 && !tour.Visible);

        Screenshot.Invulnerable = false;
        GD.Print(ok ? "TUTORIAL PASS" : "TUTORIAL FAIL");
        tree.Quit();
    }
}
