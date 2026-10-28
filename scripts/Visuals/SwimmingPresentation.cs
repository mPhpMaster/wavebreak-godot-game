using Godot;

namespace Wavebreak.Visuals;

public static class SwimmingPresentation
{
    public static float TargetPitch(Vector3 velocity)
    {
        if (velocity.LengthSquared() < 0.16f) return -Mathf.Pi * 0.5f;
        return -Mathf.Atan2(new Vector2(velocity.X, velocity.Z).Length(), velocity.Y);
    }

    public static void Apply(CharacterRig rig, bool swimming, Vector3 velocity, float time, bool breaching = false, float delta = 1f / 60f)
    {
        rig.Swimming = swimming && !breaching;
        if (!swimming)
        {
            rig.Rotation = Vector3.Zero;
            rig.Position = Vector3.Zero;
            return;
        }
        float target = TargetPitch(velocity);
        if (breaching && new Vector2(velocity.X, velocity.Z).Length() < 1f) target = 0f;
        float rate = velocity.LengthSquared() > 0.16f ? 10f : 4f;
        float pitch = Mathf.LerpAngle(rig.Rotation.X, target, 1f - Mathf.Exp(-rate * Mathf.Max(0f, delta)));
        rig.Rotation = new Vector3(pitch, 0f, Mathf.Sin(time * 2.5f) * 0.035f);
        float centre = rig.Spec.Height * 0.5f;
        rig.Position = Vector3.Up * centre - rig.Basis * (Vector3.Up * centre);
    }
}
