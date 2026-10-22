using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Stats;

namespace Wavebreak.Game;

/// <summary>
/// A crude bot that plays the game, used for verification.
///
/// It exists because the two questions that matter most — "does a run actually
/// progress?" and "can you survive if you play reasonably?" — cannot be answered
/// by a build log or by a screenshot of the first fifteen seconds. Standing
/// still, the player is dead in a minute and every later system (level 8
/// weapons, elites, the win condition) is never reached.
///
/// It is not meant to be good. It kites the nearest threat and drifts back
/// toward the middle, which is roughly the floor of competent play — so if the
/// bot survives ten minutes the balance is too soft, and if it dies in thirty
/// seconds something is broken.
/// </summary>
public sealed class Autopilot
{
    private readonly Player _player;
    private float _wanderPhase;

    // Move-verb timing. The bot uses every verb on a rough rhythm, so a headless
    // run exercises dash, slide, the air jump and the slam through the real
    // controller -- if any of them is broken, the summary counters say so.
    private float _clock;
    private float _nextHop = 1.5f;
    private float _airJumpAt = -1f;
    private float _slamAt = -1f;
    private float _nextSlide = 3.2f;
    private float _slideJumpAt = -1f;
    private int _hopCount;
    private float _nextDash = 2.1f;

    // Orb seeking. The weight is relative to the unit flee vector: above 1 the
    // bot will walk toward an orb with an enemy still in view, which it has to
    // or it never collects anything in a crowded arena.
    private const float SeekRange = 20f;
    private const float SeekWeight = 1.6f;

    public Autopilot(Player player) => _player = player;

    public Vector3 Think()
    {
        _clock += (float)_player.GetPhysicsProcessDeltaTime();

        Vector3 me = _player.GlobalPosition;
        Vector3 away = Vector3.Zero;
        int considered = 0;
        float nearest = float.MaxValue;

        EnemyManager? em = EnemyManager.Instance;
        if (em is not null)
        {
            // Weight by inverse distance so the closest enemy dominates the
            // decision, rather than being outvoted by a distant crowd.
            foreach (Enemy e in em.Alive.Take(40))
            {
                Vector3 d = me - e.GlobalPosition;
                d.Y = 0f;
                float dist = d.Length();
                if (dist < 0.01f || dist > 16f) continue;
                if (dist < nearest) nearest = dist;
                away += d / (dist * dist);
                considered++;
            }
        }

        Vector3 wish = considered > 0 ? away.Normalized() : Vector3.Zero;

        // Walk toward the experience when nothing is on top of us. Pure kiting
        // runs away from exactly where kills drop their orbs, so a fleeing bot
        // ends a 30s run at level 1 and never reaches the level-up path at all.
        wish += SeekPickup(me, nearest);

        // Bias back toward the middle. Without this the bot backs itself into
        // the arena wall and dies there every time, which measures the wall
        // rather than the balance.
        Vector3 toCentre = -me with { Y = 0f };
        float edge = toCentre.Length() / Arena.Radius;
        if (edge > 0.55f)
            wish += toCentre.Normalized() * (edge - 0.55f) * 3f;

        // A slow orbit keeps it moving when nothing is near, so pickups get
        // collected and the run does not stall at zero xp.
        _wanderPhase += 0.02f;
        wish += new Vector3(Mathf.Cos(_wanderPhase), 0f, Mathf.Sin(_wanderPhase)) * 0.35f;

        // Fight the phase boss at mid range instead of fleeing it. Weapons aim at
        // whatever is nearest, so a bot that kites the boss along with the crowd
        // never makes it the nearest thing, and the phase sits in overtime for
        // ever: the first verification run spent six minutes with the boss at 61%.
        if (Main.Instance?.Stages.BossPosition is Vector3 boss)
        {
            Vector3 toBoss = boss - me;
            toBoss.Y = 0f;
            float d = toBoss.Length();
            if (d > 0.01f)
            {
                // Close enough that the boss is usually the nearest target:
                // the crowd presses in to touching distance, so a boss held at
                // 7 m is almost never what the weapons pick.
                float pull = d > 4.5f ? 2.2f : d < 2.8f ? -1.2f : 0f;
                // Circle while in the band, so its swing is dodged rather than eaten.
                Vector3 orbit = new Vector3(-toBoss.Z, 0f, toBoss.X) / d * 0.6f;
                wish = wish * 0.5f + toBoss / d * pull + orbit;
            }
        }

        // An open portal overrides everything but a point-blank threat. Without
        // this the bot orbits the centre forever with the way onward open, and an
        // --auto run never sees a second map -- so nothing past phase one would
        // ever be verified.
        if (Main.Instance?.Stages.PortalPosition is Vector3 portal)
        {
            Vector3 toPortal = portal - me;
            toPortal.Y = 0f;
            if (toPortal.LengthSquared() > 0.01f)
            {
                float flee = considered > 0 && nearest < 2f ? 0.6f : 0.15f;
                Vector3 avoid = considered > 0 ? away.Normalized() * flee : Vector3.Zero;
                wish = toPortal.Normalized() + avoid;
            }
        }

        wish = wish.LengthSquared() > 0.0001f ? wish.Normalized() : Vector3.Zero;
        UseVerbs(nearest);
        return wish;
    }

    /// <summary>
    /// A pull toward the nearest orb that is not already flying in, scaled down
    /// to nothing as an enemy closes. Fleeing always wins at touching distance —
    /// the bot trades health for xp only when the trade is free, which is what a
    /// cautious human does and no more. It does not path, rank orbs by value, or
    /// look at what stands between it and the orb; that is the crude part.
    /// </summary>
    private Vector3 SeekPickup(Vector3 me, float nearestEnemy)
    {
        // Orbs inside pickup range are already chasing the player, so steering
        // toward them wastes the frame; look past them to the next one.
        float magnet = _player.Stats.Get(Stat.PickupRange) * 0.9f;
        Vector3 best = Vector3.Zero;
        float bestDist = SeekRange;

        foreach (Pickup p in Pickup.Active)
        {
            if (p.IsPooled || !GodotObject.IsInstanceValid(p)) continue;
            Vector3 d = p.GlobalPosition - me;
            d.Y = 0f;
            float dist = d.Length();
            if (dist <= magnet || dist >= bestDist) continue;
            best = d;
            bestDist = dist;
        }

        if (best == Vector3.Zero) return Vector3.Zero;

        // 0 with an enemy at 1.5m, full weight from 4m out.
        float safety = Mathf.Clamp((nearestEnemy - 1.5f) / 2.5f, 0f, 1f);
        return best / bestDist * SeekWeight * safety;
    }

    private void UseVerbs(float nearest)
    {
        // Panic button when something is in touching distance; otherwise dash on
        // a slow rhythm anyway, so the verb is exercised even in a quiet run.
        if (_player.DashCooldownRemaining <= 0f && (nearest < 2.6f || _clock >= _nextDash))
        {
            _player.RequestDash();
            _nextDash = _clock + 5.3f;
        }

        // Hop rhythm: jump, air-jump near the apex, and every other hop slam
        // back down -- into a crowd if there is one nearby.
        if (_clock >= _nextHop && _player.IsOnFloor() && !_player.IsSliding)
        {
            _player.RequestJump();
            _hopCount++;
            _airJumpAt = _clock + 0.45f;
            _slamAt = _hopCount % 2 == 0 ? _clock + 0.9f : -1f;
            _nextHop = _clock + 2.6f;
        }
        if (_airJumpAt > 0f && _clock >= _airJumpAt)
        {
            _player.RequestJump();
            _airJumpAt = -1f;
        }
        if (_slamAt > 0f && _clock >= _slamAt)
        {
            _player.RequestSlide();
            _slamAt = -1f;
        }

        // Slide while moving, then hop out of it to carry the speed.
        if (_clock >= _nextSlide && _player.IsOnFloor() && !_player.IsDashing)
        {
            _player.RequestSlide();
            _slideJumpAt = _clock + 0.45f;
            _nextSlide = _clock + 4.1f;
        }
        if (_slideJumpAt > 0f && _clock >= _slideJumpAt)
        {
            if (_player.IsSliding) _player.RequestJump();
            _slideJumpAt = -1f;
        }
    }
}
