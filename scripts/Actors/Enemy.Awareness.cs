using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Net;

namespace Wavebreak.Actors;

/// <summary>
/// Who hit this enemy last, for the scoreboard and the boss-kill toast.
///
/// Only the machine that owns enemy health tracks it: the host in co-op, this
/// machine in single-player. A client's hit reaches the host through
/// <c>NetSession.Hits</c>, which sets <see cref="NetHitter"/> to the sender
/// for the length of that one <see cref="TakeDamage"/> call; every other hit
/// on the host (its own weapons, burns, thorns) is the host's.
/// </summary>
public sealed partial class Enemy
{
    /// <summary>Peer id of the last player to damage this enemy (0 = nobody yet).</summary>
    internal long LastHitter;

    /// <summary>Host: set while a client's forwarded hit is being applied.</summary>
    internal static long NetHitter;

    private void NoteDamage(float amount)
    {
        LastHitter = NetHitter != 0 ? NetHitter : Coop.LocalId;
        RunScoreboard.NoteHit(LastHitter, amount);
    }

    private void NoteDeath()
    {
        long killer = LastHitter != 0 ? LastHitter : Coop.LocalId;
        LastHitter = 0;
        RunScoreboard.NoteKill(killer, IsBoss);
        if (!IsBoss || _def is null) return;

        if (Coop.IsHost) NetSession.Instance?.SendBossSlain(killer, _def.DisplayName);
        else if (!Coop.Active) EventBus.Publish(new BossDefeated(killer, NetSession.PlayerName, _def.DisplayName));
    }
}
