using Godot;
using Wavebreak.Actors;
using Wavebreak.Net;
using Wavebreak.Stats;

namespace Wavebreak.Combat.Weapons;

/// <summary>
/// The two team perks. Each machine owns its own player's perks, so a buff a
/// teammate's perk gives *you* has to be applied on your machine:
///
///   * every machine tells the others its Crew Beacon and Lifeline levels
///     (<see cref="NetSession.SendPerks"/>, at once on a change and every
///     few seconds, so a late joiner or a migrated session catches up);
///   * Crew Beacon: this machine checks every teammate's puppet position
///     against that teammate's beacon radius and gives its own player the
///     best beacon in range (its own included; beacons do not stack);
///   * Lifeline: the downed player's machine counts its own revive
///     (<c>NetSession.StepRevive</c>), and reads the helper's Lifeline level
///     from <see cref="PerkNet"/> for the speed and the reach.
///
/// Single-player: the beacon buffs you, and Lifeline's surge heals you; there
/// is nobody to revive, which the card says.
/// </summary>
public sealed partial class PerkProcs
{
    private float _syncTimer;
    private bool _beaconFromTeam;

    /// <summary>Seconds this player spent inside a teammate's beacon (the co-op test reads it).</summary>
    public static float TeamBeaconSeconds { get; private set; }

    private void TickCoop(Player p, float dt)
    {
        if (Coop.InRun && NetSession.Instance is { } net && (_syncTimer -= dt) <= 0f)
        {
            _syncTimer = 3f;
            net.SendPerks(L(Beacon), L(Lifeline));
        }

        // Crew Beacon: the strongest beacon this player stands in.
        int own = p.IsAlive ? L(Beacon) : 0;
        float speed = own > 0 ? Beacon.V(1, own) : 0f, armour = own > 0 ? Beacon.V(2, own) : 0f;
        bool fromTeam = false;
        if (Coop.InRun && p.IsAlive && NetSession.Instance is { } session)
            foreach (RemotePlayer r in session.Remotes.Values)
            {
                if (!IsInstanceValid(r) || r.Down || !r.HasState) continue;
                int lv = PerkNet.BeaconOf(r.PeerId);
                if (lv <= 0) continue;
                Vector3 d = r.GlobalPosition - p.GlobalPosition;
                d.Y = 0f;
                if (d.Length() > Beacon.V(0, lv)) continue;
                if (Beacon.V(1, lv) <= speed) continue;
                speed = Beacon.V(1, lv);
                armour = Beacon.V(2, lv);
                fromTeam = true;
            }
        SetMod(p, Beacon, Stat.AttackSpeed, ModifierOp.Additive, speed / 100f);
        SetMod(p, Beacon, Stat.Armor, ModifierOp.Flat, armour);
        if (fromTeam)
        {
            TeamBeaconSeconds += dt;
            if (!_beaconFromTeam)
            {
                Popup(p.CenterPosition + Vector3.Up * 1.2f, "Crew Beacon", new Color(1f, 0.7f, 0.4f), 0.9f);
                Note(Beacon);
            }
        }
        else if (own > 0 && _clock - _beaconNoted > 5f) { _beaconNoted = _clock; Note(Beacon); }
        _beaconFromTeam = fromTeam;
    }

    private float _beaconNoted = -99f;
}
