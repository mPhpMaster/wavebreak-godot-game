using Godot;

namespace Wavebreak.Visuals;

public sealed partial class CharacterRig
{
    private void PoseMarine(float dt)
    {
        _player?.Stop(keepState: true);
        float beat = _time * (Swimming ? 3.5f : 6f);
        float planar = new Vector2(Velocity.X, Velocity.Z).Length();
        float pitch = Swimming ? -Mathf.Atan2(Velocity.Y, Mathf.Max(0.3f, planar)) : 0;
        SetHips(0, Swimming ? Mathf.Sin(beat) * 0.035f : 0, 0, pitch, Mathf.Sin(beat * 0.5f) * 0.04f, 0, 1);
        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part part = ref _parts[i];
            float flap = Mathf.Sin(beat + i * 0.7f) * (Swimming ? 0.32f : 0.12f);
            Limb(ref part, 0, flap * part.Def.Side, 0.08f);
        }
        if (Swimming) { _swimWasActive = true; SwimmingAnimationFrames++; }
    }
}
