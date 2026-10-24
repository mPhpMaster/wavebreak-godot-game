using Godot;
using Steamworks;

namespace Wavebreak.Net;

/// <summary>Runs Steam callbacks every frame (paused or not), and shuts Steam
/// down with the game. Created by <see cref="SteamApp"/> once Steam has
/// started; single-player never has one.</summary>
public sealed partial class SteamPump : Node
{
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        if (SteamApp.Ready) SteamAPI.RunCallbacks();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete) SteamApp.Shutdown();
    }
}
