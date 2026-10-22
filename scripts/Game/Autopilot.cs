using System.Linq;
using Godot;
using Wavebreak.Actors;

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

        wish = wish.LengthSquared() > 0.0001f ? wish.Normalized() : Vector3.Zero;
        UseVerbs(nearest);
        return wish;
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
