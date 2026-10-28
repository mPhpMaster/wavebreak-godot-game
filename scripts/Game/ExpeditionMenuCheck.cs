using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Meta;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>Exercises the actual buttons and scene transitions using an in-memory profile.</summary>
public sealed partial class ExpeditionMenuCheck : Node
{
    private readonly MainMenu _menu;
    internal static int Phase;
    internal static string Difficulty = "normal", ExpectedHero = "", ClassicHero = "", ClassicRoute = "classic";
    public ExpeditionMenuCheck(MainMenu menu) => _menu = menu;
    public override void _Ready() => _ = Run();
    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    internal static Button? Button(Node root, string english)
    {
        if (root is Button b && b.Text == Loc.T(english)) return b;
        foreach (Node child in root.GetChildren())
            if (Button(child, english) is { } found) return found;
        return null;
    }
    private static void Require(bool condition, string message)
    {
        GD.Print($"EXPEDITION MENU {(condition ? "OK" : "FAIL")} {message}");
        if (!condition) throw new InvalidOperationException(message);
    }

    private async Task Run()
    {
        try
        {
            await Wait(0.3f);
            Require(_menu.CurrentPage == "main", "title opens normally");
            Button(_menu, "SPECIAL MAPS: REEF & CITY")!.EmitSignal(Godot.Button.SignalName.Pressed);
            Require(_menu.CurrentPage == "expeditions", "obvious main-menu entry opens both map cards");
            Require(Button(_menu, "START CITY EXPEDITION") is not null && Button(_menu, "START REEF EXPEDITION") is not null,
                "both direct-start buttons exist");
            Require(Characters.ById("nori")!.IsAvailable && Characters.ById("vesper")!.IsAvailable,
                "both heroes available in fresh and loaded profiles");
            if (Phase == 2)
            {
                Button(_menu, "CLASSIC PLAY & CHARACTER DETAILS")!.EmitSignal(Godot.Button.SignalName.Pressed);
                Require(_menu.CurrentPage == "play" && Routes.Selected.Id == ClassicRoute && Profile.SelectedCharacter == ClassicHero, "return to classic selection works");
                GD.Print("EXPEDITION MENU PASS"); GetTree().Quit(); return;
            }
            ClassicHero = Profile.SelectedCharacter;
            ClassicRoute = Routes.Selected.Id is "reef-dive" or "skyline-run" ? "classic" : Routes.Selected.Id;
            if (Launch.MenuExpeditionCheck!.Contains("direct"))
            {
                bool directCity = Launch.MenuExpeditionCheck.StartsWith("city");
                ExpectedHero = directCity ? "vesper" : "nori";
                Difficulty = DifficultyTiers.Selected.Id; Phase = 1;
                Button(_menu, directCity ? "START CITY EXPEDITION" : "START REEF EXPEDITION")!
                    .EmitSignal(Godot.Button.SignalName.Pressed);
                return;
            }
            Button(_menu, "Bellspire Ward")!.EmitSignal(Godot.Button.SignalName.Pressed);
            Require(Profile.SelectedCharacter == "vesper", "city card selects Vesper immediately");
            await Wait(.08f);
            Require(ReefCheckHelpers.Find<Visuals.CharacterRig>(_menu)?.Spec.Id.Contains("vesper") == true, "selected hero preview matches Vesper");
            await Capture("city");
            var next = (Godot.Button)_menu.FindChild("ExpeditionHeroNext", true, false);
            next.EmitSignal(Godot.Button.SignalName.Pressed);
            string manual = Profile.SelectedCharacter;
            _menu.OpenExpeditions(false);
            Require(manual != "vesper" && Profile.SelectedCharacter == manual, "manual override survives UI refresh");
            Require(Net.NetSession.LocalProfile().CharacterId == manual, "co-op identity follows selected override");
            Button(_menu, "Lumenreef Basin")!.EmitSignal(Godot.Button.SignalName.Pressed);
            Require(Profile.SelectedCharacter == "nori", "switching to reef selects Nori immediately");
            await Capture("reef");
            bool city = Launch.MenuExpeditionCheck!.StartsWith("city");
            if (city) Button(_menu, "Bellspire Ward")!.EmitSignal(Godot.Button.SignalName.Pressed);
            if (Launch.MenuExpeditionCheck.Contains("override")) next.EmitSignal(Godot.Button.SignalName.Pressed);
            ExpectedHero = Profile.SelectedCharacter;
            Difficulty = DifficultyTiers.Selected.Id;
            Phase = 1;
            Button(_menu, Launch.MenuExpeditionCheck!.StartsWith("city") ? "START CITY EXPEDITION" : "START REEF EXPEDITION")!
                .EmitSignal(Godot.Button.SignalName.Pressed);
        }
        catch (Exception e) { GD.Print("EXPEDITION MENU FAIL " + e); GetTree().Quit(1); }
    }

    private async Task Capture(string map)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--menu-review="))
            {
                await Wait(.3f);
                await Screenshot.Capture(GetTree(), this, arg["--menu-review=".Length..] + "-" + map + ".png");
            }
    }
}
