using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Meta;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class ExpeditionMenuRunCheck : Node
{
    public override void _Ready() => _ = Run();
    private async Task Run()
    {
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        bool city = Launch.MenuExpeditionCheck!.StartsWith("city");
        bool valid = ExpeditionMenuCheck.Phase == 1 && Arena.Current.Id == (city ? "bellspire" : "lumenreef")
            && Profile.SelectedCharacter == ExpeditionMenuCheck.ExpectedHero
            && RunMeta.Character.Id == Profile.SelectedCharacter
            && DifficultyTiers.Active.Id == ExpeditionMenuCheck.Difficulty
            && Routes.Active.Id == (city ? "skyline-run" : "reef-dive");
        GD.Print($"EXPEDITION MENU {(valid ? "OK" : "FAIL")} direct start map={Arena.Current.Id} character={Profile.SelectedCharacter} route={Routes.Active.Id}");
        if (!valid) { GetTree().Quit(1); return; }
        ExpeditionMenuCheck.Phase = 2;
        GetTree().ChangeSceneToFile(MainMenu.TitleScene);
    }
}
