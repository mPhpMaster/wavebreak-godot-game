namespace Wavebreak.Net;

public sealed partial class RemotePlayer
{
    internal bool CoreStrikeAirborne => !_grounded;
    internal float CoreStrikeSpeed => _netVel.Length();
}
