using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Net;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public sealed partial class CoopLeviathanCheck : Node
{
    private float _time;
    private bool _initialized, _positioned;
    public override void _PhysicsProcess(double delta)
    {
        if (NetSession.Instance is not { Started: true } session || Player.Instance is not { } player
            || session.Remotes.Count == 0 || session.Remotes.Values.Any(remote => !remote.HasState)) return;
        Main main = Main.Instance!;
        if (!_initialized)
        {
            main.SetProcess(false); EnemyManager.DevFrozen = true;
            player.MoveOverride = static () => Vector3.Zero;
            player.Teleport(new Vector3(Coop.IsHost ? -5 : 5, 42, 0));
            if (Coop.IsHost) main.Stages.DevBossNow();
            _initialized = true;
        }
        _time += (float)delta;
        Enemy? boss = main.Enemies.Alive.FirstOrDefault(enemy => enemy.IsBoss);
        if (boss is not null && !_positioned)
        {
            player.Teleport(boss.AimPoint + Vector3.Right * (Coop.IsHost ? 21 : -21) - Vector3.Up * .9f);
            _positioned = true;
        }
        if (boss is not null && _positioned) main.Weapons.Tick((float)delta);
        if (_time < 4) return;
        CharacterRig? rig = boss is null ? null : ReefCheckHelpers.Find<CharacterRig>(boss);
        CollisionShape3D? collider = boss is null ? null : ReefCheckHelpers.Find<CollisionShape3D>(boss);
        bool ok = boss?.Def is { Id: ReefLeviathanModels.Id, Height: >= 32, Radius: >= 15 }
            && rig?.Spec is { IsImported: true, Id: "boss:wertt-leviathan" }
            && Mathf.IsEqualApprox(rig.Spec.ScenePitch, -Mathf.Pi * .5f)
            && collider?.Shape is CapsuleShape3D { Height: >= 32, Radius: >= 15 }
            && boss.Health < boss.MaxHealth && player.ShotgunRecoilSpeed <= .801f;
        GD.Print($"COOP LEVIATHAN {(ok ? "PASS" : "FAIL")} role={Coop.Mode} id={boss?.Def?.Id} height={boss?.Def?.Height} radius={boss?.Def?.Radius} model={rig?.Spec.Id} hp={boss?.Health}/{boss?.MaxHealth}");
        SetPhysicsProcess(false); GetTree().CreateTimer(1).Timeout += () => GetTree().Quit(ok ? 0 : 1);
    }
}
