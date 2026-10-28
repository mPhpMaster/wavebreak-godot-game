using Godot;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Stats;

namespace Wavebreak.Actors;

public sealed partial class Player
{
    private Audio.CoreImpactAudio? _coreImpact;
    private float _coreStrikeCooldown;
    public int CoreStrikes { get; private set; }
    public string CoreStrikeFeedback { get; private set; } = "";
    private float _coreFeedbackTime;

    private void StepCoreStrike(float dt)
    {
        _coreStrikeCooldown = Mathf.Max(0f, _coreStrikeCooldown - dt);
        _coreFeedbackTime -= dt;
        if (_coreFeedbackTime <= 0f) CoreStrikeFeedback = "";
    }

    private Audio.CoreImpactAudio CreateCoreImpact()
    {
        var audio = new Audio.CoreImpactAudio(); AddChild(audio); return audio;
    }

    internal bool TryCoreStrike()
    {
        if (!Arena.IsCity || !IsAlive || IsOnFloor() || Net.Coop.LocalHold || GetTree().Paused
            || _coreStrikeCooldown > 0f || _dashCooldown > 0f) return false;
        Enemy? chosen = null;
        float distance = 8f;
        if (EnemyManager.Instance is { } enemies)
            foreach (Enemy e in enemies.Alive)
            {
                if (!e.IsCityGiant) continue;
                Vector3 point = e.ClosestCore(CenterPosition);
                float d = CenterPosition.DistanceTo(point);
                if (d >= distance || !WorldSight.Clear(this, CenterPosition, point)) continue;
                distance = d;
                chosen = e;
            }
        if (chosen is null) return false;
        float speed = Velocity.Length();
        if (speed < 7f) return false;
        Vector3 direction = (chosen.ClosestCore(CenterPosition) - CenterPosition).Normalized();
        var hit = new DamageInfo
        {
            Amount = (160f + Mathf.Min(speed, 42f) * 14f) * Stats.GetMultiplier(Stat.Damage),
            Direction = direction, CanCrit = false, Source = "core_strike", ProcCoefficient = 0.5f,
        };
        chosen.TakeDamage(hit);
        if (hit.Nullified || hit.Amount <= 0f) return false;
        _coreStrikeCooldown = 0.65f;
        _coreFeedbackTime = 1.5f;
        Main.Instance?.Weapons.ReportHit(chosen, hit);
        PlayerCamera.Instance?.Shake(0.08f, 0.14f);
        _coreImpact ??= CreateCoreImpact();
        _coreImpact.Play();
        CoreStrikes++;
        CoreStrikeFeedback = "CORE HIT!";
        FlashHit(0.3f);
        return true;
    }
}

