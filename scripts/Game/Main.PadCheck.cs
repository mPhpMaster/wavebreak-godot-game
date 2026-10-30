using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Meta;
using Wavebreak.Net;
using Wavebreak.Progression;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>
/// <c>--check-pad</c>: plays a run with injected InputEventJoypadButton and
/// InputEventJoypadMotion events and prints PAD lines, ending in PAD PASS/FAIL.
/// No physical pad is involved: this proves the bindings, the panels and the
/// focus paths, not a real controller's axis ranges or button labels.
/// </summary>
public sealed partial class Main
{
    private async Task PadCheck()
    {
        try { await PadCheckSteps(); }
        catch (System.Exception e)
        {
            GD.Print($"PAD FAIL exception: {e}");
            GD.Print("PAD FAIL");
            GetTree().Quit();
        }
    }

    private async Task PadCheckSteps()
    {
        SceneTree tree = GetTree();
        bool ok = true;
        void Expect(string what, bool cond) { GD.Print($"PAD {(cond ? "ok  " : "FAIL")} {what}"); ok &= cond; }
        async Task Wait(double s) => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        void Btn(JoyButton b, bool down) => Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = b, Pressed = down, Device = 0 });
        void Axis(JoyAxis a, float v) => Input.ParseInputEvent(new InputEventJoypadMotion { Axis = a, AxisValue = v, Device = 0 });
        async Task Tap(JoyButton b, double hold = 0.08) { Btn(b, true); await Wait(hold); Btn(b, false); await Wait(0.08); }
        Control? Focus() => GetViewport().GuiGetFocusOwner();
        bool Inside(Control root) => Focus() is { } f && root.IsAncestorOf(f);

        PlayerCamera.ForceCaptureForTest = true;
        Screenshot.Invulnerable = true;
        await Wait(4.5);

        // ------------------------------------------------------------ bindings
        foreach (var (action, _) in Keybinds.Actions)
            if (!action.Contains("volume") && action != "mute")
                Expect($"{action} has a pad binding", Keybinds.PadEvent(action) is not null);
        Expect("ui_accept is on A", InputMap.ActionGetEvents("ui_accept").OfType<InputEventJoypadButton>().Any(b => b.ButtonIndex == JoyButton.A));
        Expect("ui_cancel is on B", InputMap.ActionGetEvents("ui_cancel").OfType<InputEventJoypadButton>().Any(b => b.ButtonIndex == JoyButton.B));
        var reserved = new System.Collections.Generic.HashSet<string>();
        foreach (var (action, _) in Keybinds.Actions)
            if (Keybinds.PadEvent(action) is { } ev) Expect($"{action}: no other action on {ev.AsText()}", reserved.Add(ev.AsText()));

        // -------------------------------------------------------------- device
        await Tap(JoyButton.DpadUp);
        Expect("a pad press marks the pad as the device in use", PadInput.UsingPad);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F12, PhysicalKeycode = Key.F12, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F12, PhysicalKeycode = Key.F12, Pressed = false });
        await Wait(0.1);
        Expect("a key hands it back", !PadInput.UsingPad);
        await Tap(JoyButton.DpadUp);

        // ---------------------------------------------------------- left stick
        Vector3 from = _player.GlobalPosition;
        Axis(JoyAxis.LeftY, -1f);
        await Wait(0.7);
        Axis(JoyAxis.LeftY, 0f);
        await Wait(0.2);
        Expect($"left stick walks the player ({from.DistanceTo(_player.GlobalPosition):0.0} m)", from.DistanceTo(_player.GlobalPosition) > 1f);
        Axis(JoyAxis.LeftX, 0.1f);
        await Wait(0.15);
        Expect("a stick inside the move dead zone is no input", Input.GetVector("move_left", "move_right", "move_forward", "move_back") == Vector2.Zero);
        Axis(JoyAxis.LeftX, 0f);

        // --------------------------------------------------------- right stick
        PlayerCamera cam = PlayerCamera.Instance!;
        async Task<float> Turned(float value)
        {
            float before = cam.Yaw;
            Axis(JoyAxis.RightX, value);
            await Wait(0.4);
            Axis(JoyAxis.RightX, 0f);
            await Wait(0.1);
            return Mathf.Abs(Mathf.Wrap(cam.Yaw - before, -Mathf.Pi, Mathf.Pi));
        }
        float slow = await Turned(1f);
        PadSettings.SetLookSensitivity(2f);
        float fast = await Turned(1f);
        PadSettings.SetLookSensitivity(1f);
        Expect($"right stick turns the camera ({slow:0.00} rad)", slow > 0.3f);
        Expect($"look speed scales it ({slow:0.00} to {fast:0.00})", fast > slow * 1.5f && fast < slow * 2.6f);
        Expect("inside the look dead zone nothing turns", await Turned(0.1f) < 0.001f);
        float pitch = cam.Pitch;
        Axis(JoyAxis.RightY, 1f);
        await Wait(0.3);
        Axis(JoyAxis.RightY, 0f);
        await Wait(0.1);
        float downward = cam.Pitch - pitch;
        PadSettings.SetInvertLook(true);
        Axis(JoyAxis.RightY, 1f);
        await Wait(0.3);
        Axis(JoyAxis.RightY, 0f);
        await Wait(0.1);
        PadSettings.SetInvertLook(false);
        Expect("invert flips vertical look", downward < 0f && cam.Pitch - pitch > downward);

        // -------------------------------------------------------- face buttons
        Btn(JoyButton.A, true);
        await Wait(0.12);
        Expect("A jumps", _player.Velocity.Y > 0.5f);
        Btn(JoyButton.A, false);
        await Wait(1.0);
        int dashes = _player.Dashes;
        _player.RefundDash(100f);
        await Tap(JoyButton.X);
        Expect("X dashes", _player.Dashes == dashes + 1);
        Btn(JoyButton.B, true);
        await Wait(0.15);
        Expect("B is slide", Input.IsActionPressed("slide"));
        Btn(JoyButton.B, false);
        Btn(JoyButton.LeftShoulder, true);
        Btn(JoyButton.RightShoulder, true);
        await Wait(0.1);
        Expect("LB and RB are the city hooks", Input.IsActionPressed("city_hook_left") && Input.IsActionPressed("city_hook_right"));
        Btn(JoyButton.LeftShoulder, false);
        Btn(JoyButton.RightShoulder, false);
        int pings = Pings.Active.Count;
        await Tap(JoyButton.RightStick);
        Expect("R3 pings", Pings.Active.Count == pings + 1);
        Btn(JoyButton.Back, true);
        await Wait(0.15);
        Expect("View holds the sheet and map open", StatsPanel.IsShown);
        Btn(JoyButton.Back, false);
        await Wait(0.15);
        Btn(JoyButton.DpadDown, true);
        await Wait(0.15);
        Expect("D-pad down holds the scoreboard", ScoreboardPanel.Requested);
        Btn(JoyButton.DpadDown, false);
        await Wait(0.15);
        float distance = cam.Distance;
        Btn(JoyButton.DpadRight, true);
        await Wait(0.3);
        Btn(JoyButton.DpadRight, false);
        await Wait(0.1);
        Expect($"D-pad right zooms in ({distance:0.0} to {cam.Distance:0.0})", cam.Distance < distance);

        // ---------------------------------------------------------- full hand
        LevelUpChoices choices = LevelUpChoices.Current!;
        int Owned() => _inventory.TotalStacks + _weapons.Weapons.Sum(w => w.Level)
            + (TomeBook.Current is { } book ? book.Held.Sum(t => book.LevelOf(t)) : 0);
        int owned = Owned();
        _levelUp.Show(_offers.Roll(3, _weapons, _inventory, _player.Stats), Choose);
        await Wait(0.05);
        await Tap(JoyButton.A);
        Expect("A inside the arm delay takes no card", _levelUp.IsOpen && Owned() == owned);
        await Wait(0.5);
        Control? before = Focus();
        Expect("the hand opens with a card focused", before is not null);
        await Tap(JoyButton.DpadRight);
        await Wait(0.1);
        Expect("D-pad moves between cards", Focus() != before && Focus() is not null);
        Axis(JoyAxis.LeftX, -1f);
        await Wait(0.2);
        Control? one = Focus();
        await Wait(0.05);
        Expect("one push of the stick is one step", one == before && Focus() == one);
        await Wait(0.45);
        Axis(JoyAxis.LeftX, 0f);
        await Wait(0.3);
        int rerolls = choices.RerollsLeft;
        for (float v = 0.3f; v <= 1f; v += 0.1f) { Axis(JoyAxis.TriggerLeft, v); await Wait(0.02); }
        Axis(JoyAxis.TriggerLeft, 0f);
        await Wait(0.4);
        Expect($"LT is one reroll however many motion events ({rerolls} to {choices.RerollsLeft})", rerolls <= 0 || choices.RerollsLeft == rerolls - 1);
        Axis(JoyAxis.TriggerRight, 1f);
        await Wait(0.05);
        Axis(JoyAxis.TriggerRight, 0f);
        await Wait(0.3);
        await Tap(JoyButton.B);
        await Wait(0.3);
        Expect("the hand stays open through a cancelled banish", _levelUp.IsOpen);
        await Tap(JoyButton.A, 0.1);
        await Wait(0.2);
        Expect("A takes the focused card", !_levelUp.IsOpen && Owned() > owned);

        // ------------------------------------------------------- compact hand
        GameplaySettings.ForceCompactForTest(true);
        owned = Owned();
        _levelUp.Show(_offers.Roll(3, _weapons, _inventory, _player.Stats), Choose);
        await Wait(0.6);
        Expect("the compact hand takes no focus", _levelUp.CompactOpen && Focus() is null);
        float yaw = cam.Yaw;
        Axis(JoyAxis.RightX, 1f);
        await Wait(0.2);
        Axis(JoyAxis.RightX, 0f);
        Expect("the stick still looks around over the compact hand", !Mathf.IsEqualApprox(cam.Yaw, yaw));
        await Tap(JoyButton.DpadLeft);
        await Wait(0.2);
        Expect("D-pad left takes the first compact card", !_levelUp.IsOpen && Owned() > owned);
        GameplaySettings.ForceCompactForTest(false);

        // --------------------------------------------------------------- pause
        PauseMenu pause = FindChildren("*", "", true, false).OfType<PauseMenu>().First();
        _levelUp.Cancel();   // the run's own kills level the player up in the background
        await Wait(0.1);
        await Tap(JoyButton.Start);
        await Wait(0.2);
        Expect("Start opens the pause menu", pause.IsOpen && tree.Paused && Focus() is Button);
        await Tap(JoyButton.DpadRight);
        await Wait(0.1);
        await Tap(JoyButton.A);
        await Wait(0.2);
        Expect("A on SETTINGS opens the page with focus in it", pause.SettingsShown && Focus() is not null);
        int steps = 0;
        var seen = new System.Collections.Generic.HashSet<Control>();
        for (int i = 0; i < 40; i++)
        {
            if (Focus() is { } f) seen.Add(f);
            else steps = -1000;
            await Tap(JoyButton.DpadDown, 0.04);
            steps++;
        }
        Expect($"walking settings never drops focus ({seen.Count} controls)", steps > 0 && seen.Count >= 8);
        await Tap(JoyButton.B);
        await Wait(0.15);
        Expect("B leaves settings, the menu stays", pause.IsOpen && !pause.SettingsShown && Focus() is Button);
        await Tap(JoyButton.B);
        await Wait(0.2);
        Expect("B resumes the run", !pause.IsOpen && !tree.Paused);

        // ------------------------------------------------------------ stations
        _inventory.DevSetGold(2000);
        foreach (ItemDef item in Catalog.Items.Take(6)) _inventory.Add(item);
        Vector3 Ahead() => _player.GlobalPosition + cam.FlatForward * 2f;
        Control Buy(Station s) => FindChildren("*", "", true, false).OfType<ModalPanel>().First(m => m.IsOpen);
        foreach (Station station in new Station[] { new Merchant(), new Microwave() })
        {
            string name = station.GetType().Name;
            if (Interactables.Instance?.SpawnStationAt(station, Ahead()) is not { } placed) { Expect($"{name} placed", false); continue; }
            await Wait(0.6);
            _levelUp.Cancel();
            await Wait(0.1);
            await Tap(JoyButton.Y);
            await Wait(0.5);
            Control panel = Buy(station);
            Expect($"Y opens the {name} with a button focused", Inside(panel) && Focus() is Button);
            await Tap(JoyButton.B);
            await Wait(0.2);
            Expect($"B leaves the {name}", !tree.Paused);
            placed.QueueFree();
            await Wait(0.3);
        }

        // -------------------------------------------------------------- shrine
        if (ShrineSpawner.Instance is { } shrines && _stages.DevArena is { } arena)
        {
            shrines.SpawnAt(MapShrineKind.Charge, arena, _player.GlobalPosition);
            ShrinePanel panel = FindChildren("*", "", true, false).OfType<ShrinePanel>().First();
            for (int i = 0; i < 120 && !panel.IsOpen; i++) { _levelUp.Cancel(); await Wait(0.25); }
            await Wait(0.6);
            Expect($"the shrine panel opens focused (open={panel.IsOpen} focus={Focus()?.GetPath()})", panel.IsOpen && Inside(panel));
            await Tap(JoyButton.A, 0.1);
            await Wait(0.3);
            Expect("A takes the boon", !panel.IsOpen);
        }

        // ------------------------------------------------------------- results
        Screenshot.Invulnerable = false;
        _player.Health.Kill();
        for (int i = 0; i < 40 && !_results.IsShown; i++) await Wait(0.25);
        await Wait(1.2);
        Expect("the results screen lands on RESTART for a pad", Focus() is Button && Inside(_results));
        await Tap(JoyButton.DpadRight);
        await Wait(0.1);
        Expect("D-pad reaches MAIN MENU", Focus() is Button && Inside(_results));

        PlayerCamera.ForceCaptureForTest = false;
        GD.Print(ok ? "PAD PASS" : "PAD FAIL");
        tree.Quit();
    }
}
