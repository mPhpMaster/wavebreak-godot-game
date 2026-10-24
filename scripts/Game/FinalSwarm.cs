using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Core
{
    /// <summary>The last phase boss died: the run is won. Published once, at
    /// the moment of the kill, whether or not a Final Swarm follows.</summary>
    public readonly struct FinalBossDefeated
    {
        public readonly float Seconds;
        public readonly bool SwarmFollows;
        public FinalBossDefeated(float seconds, bool swarmFollows) { Seconds = seconds; SwarmFollows = swarmFollows; }
    }
}

namespace Wavebreak.Game
{
    /// <summary>
    /// The endless phase after the last boss.
    ///
    /// The run is already won when this starts — the boss kill is the win, and
    /// it is recorded the moment it happens (<see cref="FinalBossDefeated"/>).
    /// What follows is a score attack: the crowd returns at full size and every
    /// 30 seconds it gets denser, tougher and harder-hitting, without a cap, so
    /// it always ends in death. Time survived and kills made here pay out extra
    /// Pearls on the results screen.
    ///
    /// It only writes the director's existing phase knobs plus
    /// <see cref="EnemyManager.SpawnRateScale"/>; it never spawns anything itself.
    /// <c>--no-final-swarm</c> restores the old instant win.
    /// </summary>
    public sealed class FinalSwarm
    {
        /// <summary>One escalation step.</summary>
        public const float StepSeconds = 30f;

        // Per step. Health also compounds slightly so a strong build still meets a wall.
        private const float HealthPerStep = 0.15f;
        private const float HealthCompound = 1.10f;
        private const float DamagePerStep = 0.25f;
        // Damage compounds too: armour and dodge stack high enough that linear
        // growth never got through, leaving a strong build stuck forever.
        private const float DamageCompound = 1.09f;
        private const float SpawnBase = 1.5f;
        private const float SpawnPerStep = 0.35f;
        private const float SpeedPerStep = 0.02f;
        private const float SpeedCap = 1.3f;

        public static bool Enabled => Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-final-swarm") < 0;

        private readonly RunState _run;
        private readonly EnemyManager _enemies;
        private readonly StageDef _stage;
        private int _startKills;
        private int _lastStep = -1;

        /// <summary>Seconds survived in the swarm so far.</summary>
        public float Seconds { get; private set; }

        /// <summary>Kills made during the swarm.</summary>
        public int Kills => Math.Max(0, _run.Kills - _startKills);

        /// <summary>Escalation steps reached (one per 30 s).</summary>
        public float Steps => Seconds / StepSeconds;

        /// <summary>Extra enemy health over the last map's own scale, as a percentage, for the HUD.</summary>
        public int HealthBonusPercent => Mathf.RoundToInt((HealthFactor - 1f) * 100f);

        private float HealthFactor => (1f + HealthPerStep * Steps) * MathF.Pow(HealthCompound, Steps);

        public FinalSwarm(RunState run, EnemyManager enemies, StageDef stage)
        {
            _run = run;
            _enemies = enemies;
            _stage = stage;
        }

        public void Begin()
        {
            _startKills = _run.Kills;
            Seconds = 0f;
            Apply();
            Announce("FINAL SWARM", new Color(1f, 0.55f, 0.2f), 1.9f);
            GD.Print($"[stage] t={_run.Clock} final swarm begins (run won; endless escalation every {StepSeconds:0}s)");
        }

        /// <summary>Co-op: the host's swarm clock (a new host after migration
        /// carries on the escalation instead of starting it over).</summary>
        public void NetSetSeconds(float seconds)
        {
            Seconds = MathF.Max(0f, seconds);
            _lastStep = (int)Steps;
            Apply();
        }

        public void Tick(float dt)
        {
            Seconds += dt;
            Apply();
            int step = (int)Steps;
            if (step != _lastStep)
            {
                _lastStep = step;
                if (step > 0)
                {
                    Announce($"SWARM SURGE {step}", new Color(1f, 0.4f, 0.25f), 1.4f);
                    GD.Print($"[stage] t={_run.Clock} swarm surge {step}: hp x{HealthFactor:0.00}, " +
                             $"spawns x{SpawnBase + SpawnPerStep * step:0.00}, {Kills} swarm kills, {_enemies.AliveCount} alive");
                }
            }
        }

        private void Apply()
        {
            float steps = Steps;
            _enemies.LiveCapScale = 1f;
            _enemies.PhaseHealthScale = _stage.EnemyHealthScale * HealthFactor;
            _enemies.PhaseDamageScale = _stage.EnemyDamageScale * (1f + DamagePerStep * steps) * MathF.Pow(DamageCompound, steps);
            _enemies.PhaseSpeedScale = _stage.EnemySpeedScale * MathF.Min(1f + SpeedPerStep * steps, SpeedCap);
            _enemies.SpawnRateScale = SpawnBase + SpawnPerStep * steps;
        }

        private static void Announce(string text, Color color, float scale)
        {
            if (Player.Instance is not { } player || !GodotObject.IsInstanceValid(player)) return;
            EventBus.Publish(new PopupRequested(player.GlobalPosition + new Vector3(0f, 3.2f, 0f), text, color, scale));
        }
    }
}
