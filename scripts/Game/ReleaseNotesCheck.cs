using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Meta;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>Uses an in-memory profile; never acknowledges notes on a player's profile.</summary>
public sealed partial class ReleaseNotesCheck : Node
{
    public override void _Ready() => _ = Run();
    private async Task Run()
    {
        try
        {
            Profile.EnsureLoaded();
            Require(Profile.Path is null, "isolated in-memory profile");
            var notes = ReleaseNotes.LoadCurrent();
            Require(notes is not null && notes.Version == ReleaseNotes.CurrentVersion, "current release has valid bundled notes");
            Require(ReleaseNotes.Unread(notes!.Version), "new version is initially unread");
            var panel = new WhatsNewPanel(notes, () => ReleaseNotes.Acknowledge(notes.Version));
            AddChild(panel);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var close = ExpeditionMenuCheck.Button(panel, "LET'S PLAY");
            Require(close is not null, "modal offers keyboard-focusable dismissal");
            close!.EmitSignal(Button.SignalName.Pressed);
            Require(!ReleaseNotes.Unread(notes.Version), "dismissal acknowledges the installed version");
            Require(ReleaseNotes.Unread(notes.Version + "-next"), "a different update remains unread");
            panel.QueueFree();
            AddChild(new WhatsNewPanel(notes, () => { }));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(ExpeditionMenuCheck.Button(this, "LET'S PLAY") is not null, "acknowledged notes remain available to reopen");
            GD.Print("RELEASE NOTES PASS"); GetTree().Quit();
        }
        catch (Exception error) { GD.Print("RELEASE NOTES FAIL " + error); GetTree().Quit(1); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        GD.Print("RELEASE NOTES OK " + message);
    }
}
