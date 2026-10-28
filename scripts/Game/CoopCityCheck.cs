using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Net;

namespace Wavebreak.Game;

/// <summary>Two real ENet processes: route, heroes, tether, tells and damage.</summary>
public sealed partial class CoopCityCheck : Node
{
    private float _clock;
    private int _step;
    private Enemy? _giant;
    private bool _strike, _targetHookStarted, _sawRemoteMovingTarget;
    private bool _sawRemoteHook;
    private bool _sawHeroes, _sawBoth, _sawBoost;
    private bool _boostFreeze;
    private double _waitingSince;
    private bool _transportReady;
    private float _strikeAt;
    private readonly CityCheckTarget _target = new() { Position = new Vector3(7f, 0f, -22f) };

    public override void _PhysicsProcess(double delta)
    {
        if (NetSession.Instance is not { Started: true } session || Player.Instance is not { } player
            || EnemyManager.Instance is not { } enemies) return;
        // Begin assertions only after both real transports have exchanged state.
        // A peer may still be finishing its first expensive world/fog frame after Loaded.
        if (!_transportReady && !session.Remotes.Values.Any(remote => remote.HasState))
        {
            double now = Time.GetTicksMsec() / 1000.0;
            if (_waitingSince == 0) _waitingSince = now;
            if (now - _waitingSince > 35)
            { GD.Print("COOP CITY FAIL no remote state after transport readiness timeout"); GetTree().Quit(1); }
            return;
        }
        _transportReady = true;
        float dt = (float)delta;
        _clock += dt;
        _sawHeroes |= Coop.Roster.Any(p => p.CharacterId == "nori") && Coop.Roster.Any(p => p.CharacterId == "vesper");
        _sawBoth |= session.Remotes.Values.Any(remote => remote.LeftHookAttached && remote.RightHookAttached);
        _sawBoost |= session.Remotes.Values.Any(remote => remote.HookBoosts > 0);
        _sawRemoteMovingTarget |= session.Remotes.Values.Any(remote => remote.GrapplingMovingTarget && remote.MovingHookError < .8f);
        _sawRemoteHook |= session.Remotes.Values.Any(remote => remote.Grappling);
        if (_step == 0 && _clock > 0.6f)
        {
            player.MoveOverride = static () => Vector3.Zero;
            player.Teleport(Vector3.Up * 0.1f);
            player.SetPhysicsProcess(false);
            EnemyManager.DevFrozen = true;
            if (Coop.IsHost)
            {
                enemies.ClearField(999f);
                _giant = enemies.SpawnAt(Catalog.EnemyById("city_gatewarden")!, new Vector3(0f, 0f, -22f));
            }
            _step = 1;
        }
        if (_step == 1 && _clock > 1f)
        {
            Vector3 from = player.CenterPosition;
            if (GrappleAnchors.Ray(player, from, (new Vector3(18f, 17f, -16f) - from).Normalized(), from, out Vector3 anchor))
                player.AttachHook(anchor);
            if (GrappleAnchors.Ray(player, from, (new Vector3(-18, 9, -16) - from).Normalized(), from, out Vector3 other))
                player.AttachHook(HookSide.Right, other);
            Input.ActionPress("city_hook_left"); Input.ActionPress("city_hook_right");
            player.RequestJump(); player.SetPhysicsProcess(true);
            _step = 2;
        }
        if (_step == 2 && _clock > 1.2f && !_boostFreeze)
        { player.SetPhysicsProcess(false); _boostFreeze = true; }
        if (Coop.IsHost && _giant is { IsAlive: true }) _giant.Step(dt, _target.Position, _target, Vector3.Zero);
        if (Coop.IsClient && _clock > 3.6f && _step == 2)
        {
            _giant = enemies.Alive.FirstOrDefault(enemy => enemy.Def?.Id == "city_gatewarden");
            if (_giant is not null)
            {
                Vector3 core = _giant.ClosestCore(new Vector3(0f, 17f, -15f));
                player.Teleport(core + Vector3.Back * 5f - Vector3.Up * 0.9f);
                Input.ActionRelease("city_hook_right");
                Vector3 center = player.CenterPosition;
                if (GrappleAnchors.Ray(player, center, (new Vector3(18f, 18f, -22f) - center).Normalized(), center, out Vector3 strikeAnchor))
                    player.AttachHook(strikeAnchor);
                player.RequestJump();
                player.SetPhysicsProcess(true);
                _step = 3;
            }
        }
        if (Coop.IsClient && _step == 3 && _clock > 3.8f)
        {
            _strikeAt = _clock;
            _step = 4;
        }
        if (Coop.IsClient && _step == 4 && _clock - _strikeAt > .08f)
        {
            GD.Print($"COOP CORE REQUEST speed={player.Velocity.Length():F2} grounded={player.IsOnFloor()} distance={player.CenterPosition.DistanceTo(_giant!.ClosestCore(player.CenterPosition)):F2} cooldown={player.DashCooldownRemaining:F2} alive={_giant.IsAlive}");
            player.RequestDash();
            _strikeAt = _clock;
            _step = 5;
        }
        if (Coop.IsClient && _step == 5 && _clock - _strikeAt > .12f)
        {
            _strike = player.CoreStrikes > 0;
            GD.Print($"COOP CORE RESULT strikes={player.CoreStrikes} speed={player.Velocity.Length():F2} cooldown={player.DashCooldownRemaining:F2}");
            player.SetPhysicsProcess(false);
            _step = 6;
        }
        if (!_targetHookStarted && _clock > 5.5f)
        {
            _giant ??= enemies.Alive.FirstOrDefault(enemy => enemy.Def?.Id == "city_gatewarden");
            if (_giant is { IsAlive: true })
            {
                player.ReleaseHook(); player.Teleport(_giant.GlobalPosition + new Vector3(0, 12, 13));
                Vector3 from = player.CenterPosition;
                if (GrappleAnchors.RayTarget(player, from, (_giant.GlobalPosition + Vector3.Up * 16 - from).Normalized(),
                    from, out Vector3 movingAnchor, out CollisionObject3D? body) && body == _giant)
                {
                    Input.ActionPress("city_hook_left"); Input.ActionRelease("city_hook_right");
                    player.AttachHook(movingAnchor); player.SetPhysicsProcess(true); _targetHookStarted = true;
                    GD.Print($"COOP MOVING START role={Coop.Mode} target={player.HookEnemyId(HookSide.Left)} attached={player.LeftHookAttached} anchor={movingAnchor}");
                }
            }
        }
        if (_clock < (Coop.IsHost ? 8.5f : 8f)) return;
        bool hurt = _giant is { IsAlive: true, HealthFraction: < 0.9f };
        bool heroes = _sawHeroes;
        bool tells = Coop.IsHost ? session.CityTellsSent > 0 : session.CityTellsReceived > 0;
        bool deathReset = true;
        if (Coop.IsHost) { player.Health.Kill(); deathReset = !player.Grappling; }
        bool pass = _sawRemoteMovingTarget && _sawBoth && _sawBoost && deathReset && Arena.IsCity && Coop.RouteId == "skyline-run" && heroes && _sawRemoteHook && hurt && tells
            && (Coop.IsHost ? session.AcceptedCoreStrikes > 0 : _strike);
        GD.Print($"COOP CITY {(pass ? "PASS" : "FAIL")} role={Coop.Mode} route={Coop.RouteId} heroes={heroes} "
            + $"hook={_sawRemoteHook} hurt={hurt} strike={_strike} accepted={session.AcceptedCoreStrikes} tells_tx={session.CityTellsSent} tells_rx={session.CityTellsReceived} death_reset={deathReset} both={_sawBoth} boost={_sawBoost} moving_target={_sawRemoteMovingTarget} local_target_started={_targetHookStarted} local_attached={player.LeftHookAttached} local_target={player.HookEnemyId(HookSide.Left)}");
        SetPhysicsProcess(false); GetTree().Quit(pass ? 0 : 1);
    }
}
