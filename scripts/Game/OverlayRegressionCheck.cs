using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class OverlayRegressionCheck : Node
{
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _ = Run();
    }

    private async Task Wait(float seconds) => await ToSignal(
        GetTree().CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);

    private static IEnumerable<Node> Walk(Node root)
    {
        foreach (Node child in root.GetChildren(includeInternal: true))
        { yield return child; foreach (Node descendant in Walk(child)) yield return descendant; }
    }

    private static void KeyEvent(Key key, bool pressed) => Input.ParseInputEvent(
        new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    private static void Require(bool condition, string label)
    {
        GD.Print($"OVERLAY {(condition ? "OK" : "FAIL")} {label}");
        if (!condition) throw new InvalidOperationException(label);
    }

    private async Task Run()
    {
        try
        {
            await Wait(.8f);
            Main.Instance!.SetProcess(false);
            EnemyManager enemies = EnemyManager.Instance!;
            enemies.ClearField(999); enemies.Quiet = true;
            PauseMenu pause = Walk(GetTree().Root).OfType<PauseMenu>().Single();
            KeyEvent(Key.Tab, true); await Wait(.15f);
            Require(ScoreboardPanel.Instance!.Visible && !pause.IsOpen && !StatsPanel.IsShown,
                "real Tab press shows only the scoreboard");
            Require(!Walk(ScoreboardPanel.Instance).OfType<OptionButton>().Any(), "all statistics share one table without dropdowns");
            KeyEvent(Key.Escape, true); KeyEvent(Key.Escape, false); await Wait(.15f);
            Require(pause.IsOpen && StatsPanel.IsShown && !ScoreboardPanel.Instance.Visible,
                "Escape owns pause even while Tab remains held");
            BuildInspectTile tile = Walk(GetTree().Root).OfType<BuildInspectTile>().First();
            ulong identity = tile.GetInstanceId();
            await Wait(1f);
            Require(GodotObject.IsInstanceValid(tile) && tile.GetInstanceId() == identity,
                "hover targets survive repeated stats refreshes");
            GodotObject tooltip = tile._MakeCustomTooltip(tile.TooltipText);
            Require(tooltip is BuildInspectTooltip { Content.Length: > 10 }, "build inspection contains live properties");
            tooltip.Free();
            if (DisplayServer.GetName() != "headless")
            {
                GetTree().Root.GuiEmbedSubwindows = true;
                Vector2 point = tile.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
                await Wait(1f);
                Require(Walk(GetTree().Root).OfType<BuildInspectTooltip>().Any(tip => tip.IsVisibleInTree()),
                    "native hover opens a tooltip while paused");
                foreach (string argument in OS.GetCmdlineUserArgs())
                    if (argument.StartsWith("--overlay-shot="))
                        await Screenshot.Capture(GetTree(), this, argument["--overlay-shot=".Length..]);
            }
            KeyEvent(Key.Tab, false); await Wait(.1f);
            Require(pause.IsOpen && StatsPanel.IsShown, "releasing Tab retains pause ownership");
            KeyEvent(Key.Escape, true); KeyEvent(Key.Escape, false); await Wait(.1f);
            Require(!pause.IsOpen && !StatsPanel.IsShown && !GetTree().Paused, "Escape resumes cleanly");
            Require(!Walk(GetTree().Root).OfType<BuildInspectTooltip>().Any(tip => tip.IsVisibleInTree()),
                "closing pause clears the tooltip");
            var tab = new InputEventKey { PhysicalKeycode = Key.Tab };
            InputMap.ActionAddEvent("pause", tab); InputMap.ActionAddEvent("stats", tab);
            KeyEvent(Key.Tab, true); await Wait(.1f);
            Require(ScoreboardPanel.Instance.Visible && !pause.IsOpen && !StatsPanel.IsShown,
                "old duplicate Tab bindings cannot open multiple overlays");
            KeyEvent(Key.Tab, false); InputMap.ActionEraseEvent("pause", tab); InputMap.ActionEraseEvent("stats", tab);
            foreach (EnemyDef marine in Catalog.Enemies.Where(def => def.UnderwaterOnly))
            {
                Require(!EnemyMapEligibility.Allows(marine, Arena.Current), marine.Id + " rejected on Thornmeadow");
                using var reef = new StageDef { Underwater = true };
                Require(EnemyMapEligibility.Allows(marine, reef), marine.Id + " allowed underwater");
                enemies.Quiet = false;
                Require(enemies.SpawnAt(marine, Vector3.Zero) is null && enemies.SpawnBoss(marine) is null,
                    marine.Id + " blocked through scripted and boss spawn APIs");
                int index = Catalog.Enemies.ToList().IndexOf(marine);
                Require(enemies.SpawnPuppet(index, 98765, Vector3.Zero, 0, 100, 100) is null,
                    marine.Id + " blocked through multiplayer puppet API");
                enemies.Quiet = true;
            }
            Require(Catalog.Enemies.Single(def => def.Id == "reef_cave_shark").DirectorSpawns == false,
                "cave shark remains a scripted guardian");
            GD.Print("OVERLAY REGRESSION PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("OVERLAY REGRESSION FAIL " + error); GetTree().Paused = false; GetTree().Quit(1); }
        finally { KeyEvent(Key.Tab, false); }
    }
}
