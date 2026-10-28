using Godot;
using Wavebreak.Combat;
using Wavebreak.Game;
using Wavebreak.Visuals;

namespace Wavebreak.Actors;

public sealed partial class Enemy
{
    public bool IsCityGiant => CityGiantModels.IsGiant(_def);
    public float CityAttackRemaining => _cityWindup;
    private float _cityWait = 3f;
    private float _cityWindup;
    private Vector3 _cityTarget;
    private bool _cityAerial;
    private readonly Data.CityPathState _cityPath = new();
    private GroundDecal? _cityDisc;
    private MeshInstance3D? _citySphere;

    public Vector3 ClosestCore(Vector3 from)
    {
        float h = _def?.Height ?? 26f;
        Vector3 forward = -GlobalBasis.Z;
        Vector3 middle = GlobalPosition + Vector3.Up * (h * 0.65f);
        Vector3 a = middle + forward * (h * 0.102f);
        Vector3 b = middle - forward * (h * 0.102f);
        return from.DistanceSquaredTo(a) < from.DistanceSquaredTo(b) ? a : b;
    }

    private void ResetCityGiant()
    {
        _cityWait = 3f; _cityWindup = 0f; _cityPath.Clear();
        if (_cityDisc is not null) _cityDisc.Visible = false;
        if (_citySphere is not null) _citySphere.Visible = false;
    }

    private bool StepCityGiant(float dt, Vector3 target, IDamageable? player)
    {
        if (!IsCityGiant) return false;
        _cityWait -= dt;
        if (_cityWindup > 0f)
        {
            _cityWindup = Mathf.Max(0f, _cityWindup - dt);
            DrawCityAttack();
            if (_cityWindup <= 0f)
            {
                HitCityTarget(player, target);
                if (Net.Coop.IsHost && Net.NetSession.Instance is { } session)
                    foreach (Net.RemotePlayer remote in session.Remotes.Values)
                        if (remote.IsAlive && !ReferenceEquals(remote, player)) HitCityTarget(remote, remote.GlobalPosition);
                if (_cityDisc is not null) _cityDisc.Visible = false;
                if (_citySphere is not null) _citySphere.Visible = false;
                _rig?.PlayAttack();
                _cityWait = 3.8f;
            }
            Velocity = new Vector3(0f, IsOnFloor() ? 0f : Velocity.Y - Gravity * dt, 0f);
            MoveAndSlide();
            return true;
        }
        Vector3 difference = target - GlobalPosition;
        Vector3 flat = difference with { Y = 0f };
        if (_cityWait <= 0f && player is { IsAlive: true } && flat.Length() < 32f && difference.Y < (_def?.Height ?? 26f) + 5f
            && GrappleAnchors.Clear(this, AimPoint, target + Vector3.Up))
        {
            _cityAerial = difference.Y > 6f;
            _cityTarget = _cityAerial ? target + Vector3.Up : target with { Y = GlobalPosition.Y };
            _cityWindup = 1.7f;
            Net.NetSession.Instance?.SendCityTell(this, _cityTarget, _cityAerial, _cityWindup);
            _rig?.PlayAttackWindup(1.7f);
            DrawCityAttack();
        }
        Vector3 goal = CityNavigation.Next(GlobalPosition, target, _cityPath, dt);
        Vector3 direction = (goal - GlobalPosition) with { Y = 0f };
        direction = direction.LengthSquared() > 0.01f ? direction.Normalized() : Vector3.Zero;
        Vector3 desired = flat.Length() > 11f && !StatusHeld && _cityWindup <= 0f ? direction * _speed : Vector3.Zero;
        // No stepping through buildings, even at long distances.
        Velocity = new Vector3(desired.X, IsOnFloor() ? 0f : Velocity.Y - Gravity * dt, desired.Z);
        MoveAndSlide();
        if (direction.LengthSquared() > 0.1f)
            Rotation = Rotation with { Y = Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(direction.X, direction.Z), dt * 1.5f) };
        return true;
    }

    private void HitCityTarget(IDamageable? target, Vector3 feet)
    {
        if (target is not { IsAlive: true }) return;
        bool hit;
        if (_cityAerial) hit = (feet + Vector3.Up).DistanceTo(_cityTarget) <= 5f;
        else hit = Mathf.Abs(feet.Y - _cityTarget.Y) < 3f
            && new Vector2(feet.X - _cityTarget.X, feet.Z - _cityTarget.Z).Length() <= 12f;
        if (!hit || !GrappleAnchors.Clear(this, _cityTarget + Vector3.Up * 0.3f, feet + Vector3.Up)) return;
        EnemyAttacks.Hit(target, _contactDamage, (feet - GlobalPosition).Normalized(), 3f,
            _cityAerial ? "city_sweep" : "city_stomp");
    }

    internal void ReceiveCityTell(Vector3 target, bool aerial, float remaining)
    {
        _cityTarget = target; _cityAerial = aerial; _cityWindup = remaining;
        _rig?.PlayAttackWindup(remaining);
        DrawCityAttack();
    }

    private void StepCityPuppet(float dt)
    {
        if (!IsCityGiant || _cityWindup <= 0f) return;
        _cityWindup = Mathf.Max(0f, _cityWindup - dt);
        DrawCityAttack();
        if (_cityWindup <= 0f) _rig?.PlayAttack();
    }

    private void DrawCityAttack()
    {
        if (_cityDisc is null)
        {
            _cityDisc = new GroundDecal { Name = "WardenStomp", TopLevel = true };
            AddChild(_cityDisc);
            _citySphere = new MeshInstance3D
            {
                Name = "WardenSweep", TopLevel = true,
                Mesh = new SphereMesh { Radius = 5f, Height = 10f },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.19f, 0.08f, 0.25f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_citySphere);
        }
        _cityDisc.Visible = !_cityAerial && _cityWindup > 0f;
        _citySphere!.Visible = _cityAerial && _cityWindup > 0f;
        if (_cityAerial) _citySphere.GlobalPosition = _cityTarget;
        else _cityDisc.SetDisc(_cityTarget, 12f, 1f - _cityWindup / 1.7f, new Color(1f, 0.28f, 0.08f));
    }
}
