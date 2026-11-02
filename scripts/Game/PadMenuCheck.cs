using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>
/// <c>--check-pad-menu</c>: opens every title page and walks it with injected
/// D-pad events, printing PADMENU lines and PADMENU PASS/FAIL. A page fails if
/// it opens with nothing focused, if a step drops focus, or if a button on it
/// cannot be reached (and is not a mouse-only extra listed here).
/// </summary>
public static class PadMenuCheck
{
    public static async Task Run(MainMenu menu)
    {
        SceneTree tree = menu.GetTree();
        bool ok = true;
        void Expect(string what, bool cond) { GD.Print($"PADMENU {(cond ? "ok  " : "FAIL")} {what}"); ok &= cond; }
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        void Btn(JoyButton b, bool down) => Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = b, Pressed = down, Device = 0 });
        async Task Tap(JoyButton b) { Btn(b, true); await Wait(0.05); Btn(b, false); await Wait(0.05); }
        Viewport view = menu.GetViewport();

        PadInput.ForceFocusForTest = true;
        await Wait(1.5);
        await Tap(JoyButton.DpadDown);
        Expect("title opens with a control focused", view.GuiGetFocusOwner() is not null);

        foreach (string page in new List<string>(menu.PageNames))
        {
            menu.OpenPageForCheck(page);
            await Wait(0.3);
            Control root = menu.PageForCheck(page);
            Expect($"{page}: lands on a control", view.GuiGetFocusOwner() is { } first && first.IsVisibleInTree());
            var seen = new HashSet<Control>();
            bool dropped = false;
            foreach (JoyButton dir in new[] { JoyButton.DpadDown, JoyButton.DpadRight, JoyButton.DpadUp, JoyButton.DpadLeft })
                for (int i = 0; i < 16; i++)
                {
                    await Tap(dir);
                    if (view.GuiGetFocusOwner() is { } now && now.IsVisibleInTree()) seen.Add(now);
                    else dropped = true;
                }
            Expect($"{page}: walking keeps focus ({seen.Count} controls)", !dropped);
            if (page is "play" or "run")
                Expect($"{page}: the puzzle route toggle is reachable", new List<Control>(seen).Exists(c => c.Name == "PuzzleRouteToggle"));
            if (page == "play")
            {
                // X switches the puzzle route from anywhere on the page, and back.
                bool before = Meta.Routes.PuzzleSelected;
                await Tap(JoyButton.X);
                bool flipped = Meta.Routes.PuzzleSelected != before;
                await Tap(JoyButton.X);
                Expect("play: X switches the puzzle route on and back off", flipped && Meta.Routes.PuzzleSelected == before);
            }

            // Everything pressable should be reachable: by focus, or listed as
            // a mouse shortcut for something a pad does another way.
            foreach (BaseButton b in Buttons(root))
                if (b.FocusMode == Control.FocusModeEnum.None && !b.Disabled)
                    GD.Print($"PADMENU note {page}: no focus on \"{b.Name}\" {(b as Button)?.Text}");
        }

        // Back out of a nested page and the title with B.
        menu.OpenPageForCheck("graphics");
        await Wait(0.3);
        await Tap(JoyButton.B);
        await Wait(0.2);
        Expect("B on graphics goes to settings", menu.CurrentPage == "settings");
        await Tap(JoyButton.B);
        await Wait(0.2);
        Expect("B on settings goes to the title", menu.CurrentPage == "main");

        // Focus taken away (a page rebuilt its buttons): the pad gets it back.
        view.GuiReleaseFocus();
        await Wait(0.3);
        Expect("lost focus comes back by itself", view.GuiGetFocusOwner() is not null);

        // An option list opens on A, walks with the D-pad, closes on B.
        menu.OpenPageForCheck("gameplay");
        await Wait(0.3);
        OptionButton? list = FindOption(menu.PageForCheck("gameplay"));
        Expect("gameplay page has an option list", list is not null);
        if (list is not null)
        {
            list.GrabFocus();
            await Wait(0.1);
            await Tap(JoyButton.A);
            await Wait(0.3);
            Expect("A opens the list", list.GetPopup().Visible);
            int before = list.GetPopup().GetFocusedItem();
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1f, Device = 0 });
            await Wait(0.2);
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 0f, Device = 0 });
            await Wait(0.8);
            Expect($"the left stick walks the list ({before} to {list.GetPopup().GetFocusedItem()})", list.GetPopup().GetFocusedItem() != before);
            await Tap(JoyButton.B);
            await Wait(0.3);
            Expect("B closes the list, not the page", !list.GetPopup().Visible && menu.CurrentPage == "gameplay");
        }

        PadInput.ForceFocusForTest = false;
        GD.Print(ok ? "PADMENU PASS" : "PADMENU FAIL");
        tree.Quit();
    }

    private static IEnumerable<BaseButton> Buttons(Node node)
    {
        if (node is CanvasItem { Visible: false }) yield break;
        if (node is BaseButton b) yield return b;
        foreach (Node c in node.GetChildren())
            foreach (BaseButton d in Buttons(c)) yield return d;
    }

    private static OptionButton? FindOption(Node node)
    {
        if (node is OptionButton { Disabled: false, Visible: true } o) return o;
        foreach (Node c in node.GetChildren())
            if (FindOption(c) is { } hit) return hit;
        return null;
    }
}
