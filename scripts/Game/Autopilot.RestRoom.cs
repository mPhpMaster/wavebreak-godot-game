using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Game;

/// <summary>
/// The bot in the rest room: shop (the dearest thing it can afford, through
/// <see cref="Interactables.AutopilotSteer"/> like on the maps), use the
/// microwave if it can pay, rest at the campfire, then walk into the CONTINUE
/// portal and stand there. After <see cref="RestErrandSeconds"/> it stops
/// shopping and heads for the portal regardless, so an unattended run can
/// never stall in the room.
/// </summary>
public sealed partial class Autopilot
{
    private const float RestErrandSeconds = 25f;

    private RestRoom? _restRoom;
    private float _restClock;

    /// <summary>Test hook (the swap meet check, Net/NetSession.Test.Trade.cs):
    /// when set and it returns a wish, the bot follows it instead of its
    /// errands, so it stands at the stall or in the portal on cue.</summary>
    public static System.Func<Vector3, Vector3?>? RestSteer { get; set; }

    private Vector3 RestRoomThink(RestRoom room, Vector3 me)
    {
        float dt = (float)_player.GetPhysicsProcessDeltaTime();
        if (!ReferenceEquals(room, _restRoom))
        {
            _restRoom = room;
            _restClock = 0f;
        }
        _restClock += dt;
        if (RestSteer?.Invoke(me) is { } steer) return steer;

        if (_restClock < RestErrandSeconds && Interactables.Instance is { } hub)
        {
            Vector3 errand = hub.AutopilotSteer(me, float.MaxValue, dt);
            if (hub.BotBusy) return Vector3.Zero;
            if (errand.LengthSquared() > 0.0001f) return errand.Normalized();
        }

        // Done: into the portal, and stay on its centre.
        Vector3 to = room.ExitPosition - me;
        to.Y = 0f;
        return to.Length() > 0.6f ? to.Normalized() : Vector3.Zero;
    }
}
