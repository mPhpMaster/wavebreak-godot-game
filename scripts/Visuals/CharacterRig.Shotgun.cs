using Godot;

namespace Wavebreak.Visuals;

public sealed partial class CharacterRig
{
    private MeshInstance3D? _shotgun;
    private Vector3 _shotgunAim;
    private float _shotgunAimTime, _shotgunKick;
    public bool HoldsShotgun => Spec.Id == "expedition-nori";
    public Vector3 ShotgunMuzzle => _shotgun is null ? GlobalPosition : _shotgun.ToGlobal(ShotgunModel.Muzzle);
    public Vector3 ShotgunForward => _shotgun is null ? -GlobalBasis.Z : -_shotgun.GlobalBasis.Z.Normalized();
    public float ShotgunGripError { get; private set; }

    public void AimShotgun(Vector3 heading, bool fired = false)
    {
        if (!HoldsShotgun || !heading.IsFinite() || heading.LengthSquared() < .01f) return;
        _shotgunAim = heading.Normalized(); _shotgunAimTime = .65f;
        if (fired) _shotgunKick = 1;
        PoseShotgun(0);
    }

    private void PoseShotgun(float dt)
    {
        if (!HoldsShotgun || _dying) return;
        if (_shotgun is null)
        {
            _shotgun = new MeshInstance3D { Name = "TwoHandShotgun", Mesh = ShotgunModel.Mesh(),
                MaterialOverride = ToonMaterials.Character, Scale = Vector3.One * _model.Height,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _hips.AddChild(_shotgun);
        }
        _shotgunAimTime = Mathf.Max(0, _shotgunAimTime - dt);
        _shotgunKick = Mathf.MoveToward(_shotgunKick, 0, dt * 8);
        // Whole-body swimming pitch must not point the gun into the seabed.
        Vector3 worldForward = _shotgunAimTime > 0 ? _shotgunAim
            : Swimming ? (Velocity.LengthSquared() > .16f ? Velocity.Normalized() : GlobalBasis.Y.Normalized())
            : -GlobalBasis.Z.Normalized();
        Vector3 local = (_hips.GlobalBasis.Inverse() * worldForward).Normalized();
        Vector3 up = Mathf.Abs(local.Dot(Vector3.Up)) > .95f ? Vector3.Forward : Vector3.Up;
        Basis aim = Basis.LookingAt(local, up);
        Vector3 gripCentre = (ShotgunModel.RightGrip + ShotgunModel.LeftGrip) * .5f;
        _shotgun.Transform = new Transform3D(aim.Scaled(Vector3.One * _model.Height),
            (new Vector3(0, .205f, -.06f) - aim * gripCentre) * _model.Height
            - local * (_shotgunKick * .025f * _model.Height));
        ShotgunGripError = 0;
        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part arm = ref _parts[i];
            if (arm.Def.Role is not (PartRole.ArmL or PartRole.ArmR) || arm.Lower is null) continue;
            Vector3 grip = _shotgun.Transform * (arm.Def.Role == PartRole.ArmR ? ShotgunModel.RightGrip : ShotgunModel.LeftGrip);
            SolveShotgunArm(ref arm, grip);
        }
    }

    private void SolveShotgunArm(ref Part arm, Vector3 target)
    {
        Vector3 start = arm.Def.Pivot, reach = target - start;
        float upper = arm.Def.LowerPivot.Length(), lower = .17f * _model.Height;
        float distance = Mathf.Clamp(reach.Length(), Mathf.Abs(upper - lower) + .001f, (upper + lower) * .999f);
        Vector3 direction = reach.Normalized();
        float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
        Vector3 bend = Vector3.Right * arm.Def.Side + Vector3.Back * .35f;
        bend = (bend - direction * bend.Dot(direction)).Normalized();
        Vector3 elbow = start + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
        Basis upperBasis = new(new Quaternion(arm.Def.LowerPivot.Normalized(), (elbow - start).Normalized()));
        arm.Upper.Transform = new Transform3D(upperBasis, start);
        Vector3 lowerDirection = upperBasis.Inverse() * (target - elbow).Normalized();
        arm.Lower!.Transform = new Transform3D(new Basis(new Quaternion(Vector3.Down, lowerDirection)), arm.Def.LowerPivot);
        Vector3 hand = arm.Upper.Transform * (arm.Lower.Transform * (Vector3.Down * lower));
        ShotgunGripError = Mathf.Max(ShotgunGripError, hand.DistanceTo(target));
    }
}
