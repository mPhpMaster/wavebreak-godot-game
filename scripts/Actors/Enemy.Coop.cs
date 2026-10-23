using System;
using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Actors;

/// <summary>
/// Co-op: an enemy on a client is a puppet of the host's enemy with the same
/// <see cref="NetId"/>. It does not think, steer or swing; it eases toward the
/// transforms the host sends, and a hit on it is predicted locally (flash,
/// number, health bar) and sent to the host, which owns the real health and
/// says when it dies. Single-player never sets <see cref="NetId"/>, so none of
/// this runs.
/// </summary>
public sealed partial class Enemy
{
    /// <summary>Session-wide id, assigned by the host when it spawns. 0 = not networked.</summary>
    internal int NetId;

    /// <summary>Dead or despawned (a puppet predicted dead by a local hit is
    /// not <see cref="IsAlive"/> but still stands until the host confirms).</summary>
    internal bool IsDead => _dead;

    private Vector3 _netPos;
    private Vector3 _netVel;
    private float _netYaw;
    private double _netAt = -1;
    private ulong _lastLocalHitMs;

    /// <summary>Client: set up as the host's enemy, with the host's health numbers.</summary>
    internal void ConfigurePuppet(EnemyDef def, int netId, Vector3 at, float yaw, float maxHealth, float health)
    {
        Configure(def, 1f, 1f, 1f);
        Spawn(at);
        NetId = netId;
        _maxHealth = MathF.Max(1f, maxHealth);
        _health = Mathf.Clamp(health, 0.01f, _maxHealth);
        Rotation = new Vector3(0f, yaw, 0f);
        _netPos = at;
        _netYaw = yaw;
        _netVel = Vector3.Zero;
        _netAt = Time.GetTicksMsec() / 1000.0;
    }

    /// <summary>Client: a state snapshot from the host.</summary>
    internal void NetSnapshot(Vector3 at, float yaw, float health)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        double gap = _netAt > 0 ? now - _netAt : 0;
        if (gap > 0.01 && gap < 0.5) _netVel = (at - _netPos) / (float)gap;
        else _netVel = Vector3.Zero;
        _netPos = at;
        _netYaw = yaw;
        _netAt = now;

        // A hit we just sent may not be in this number yet; keep the lower of
        // the two for a moment so the bar does not bounce back up.
        if (Time.GetTicksMsec() - _lastLocalHitMs < 350) _health = MathF.Min(_health, health);
        else _health = health;
        _health = Mathf.Clamp(_health, 0f, _maxHealth);
    }

    /// <summary>Client: one frame of a puppet. Statuses tick here so burns and
    /// poison applied by this player's weapons keep reporting damage.</summary>
    internal void StepPuppet(float dt)
    {
        if (_dead) return;
        UpdateFlash(dt);
        _aliveSeconds += dt;
        StepStatus(dt);
        if (_dead) return;

        float ahead = MathF.Min(0.15f, (float)(Time.GetTicksMsec() / 1000.0 - _netAt));
        Vector3 target = _netPos + _netVel * ahead;
        if (GlobalPosition.DistanceSquaredTo(target) > 36f) GlobalPosition = target;
        else GlobalPosition = GlobalPosition.Lerp(target, 1f - MathF.Exp(-14f * dt));
        Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, _netYaw, 1f - MathF.Exp(-12f * dt)), 0f);
    }

    /// <summary>Client: this player's weapon hit the puppet.</summary>
    private void PuppetHit(DamageInfo info)
    {
        float sent = MathF.Max(0f, info.Amount);
        if (sent <= 0f || _health <= 0f)
        {
            // Already predicted dead and waiting for the host to confirm.
            info.Nullified = true;
            info.Amount = 0f;
            return;
        }
        Net.NetSession.Instance?.QueueHit(NetId, sent, info.Knockback, info.Direction);

        float amount = MathF.Min(sent, _health);
        info.Amount = amount;
        _health -= amount;
        _lastLocalHitMs = Time.GetTicksMsec();
        _flashTimer = FlashDuration;
        _rig?.PlayHit();
    }

    /// <summary>Client: the host says this enemy died at <paramref name="at"/>.
    /// The kill is announced here too (kill count, sounds, on-kill items); drops
    /// do not happen here, the host spawns and sends them.</summary>
    internal void NetKill(Vector3 at)
    {
        if (_dead) return;
        _dead = true;
        _health = 0f;
        int xp = _def?.XpValue ?? 0;
        int gold = _def?.GoldValue ?? 0;
        GlobalPosition = at with { Y = at.Y - 0.35f };
        Visuals.EnemyVisuals.SpawnCorpse(_rig);
        EventBus.Publish(new EnemyKilled(at, xp, gold, IsElite || IsBoss));
        Deactivate();
        Despawned?.Invoke(this);
    }
}
