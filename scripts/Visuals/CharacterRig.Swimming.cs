using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

public sealed partial class CharacterRig
{
    public bool Swimming { get; set; }
    public bool SwimmingAnimationActive => _swimWasActive;
    public int SwimmingJointCount => _swimJoints.Count + _parts.Length;
    public float SwimmingStroke => Mathf.Sin(_time * 2.5f);
    public bool LocomotionClipStopped => _player is null || !_player.IsPlaying();
    public int SwimmingAnimationFrames { get; private set; }
    private bool _swimWasActive;
    private bool _swimCollected;
    private readonly List<SwimmingJoint> _swimJoints = new();

    private void CollectSwimmingJoints(Node node)
    {
        if (node is Skeleton3D skeleton)
        {
            skeleton.ResetBonePoses();
            for (int i = 0; i < skeleton.GetBoneCount(); i++)
            {
                string name = skeleton.GetBoneName(i).ToString().ToLowerInvariant();
                bool left = name.Contains("left") || name.EndsWith("_l") || name.EndsWith(".l");
                bool right = name.Contains("right") || name.EndsWith("_r") || name.EndsWith(".r");
                if (!left && !right) continue;
                PartRole? role = name.Contains("arm") ? (left ? PartRole.ArmL : PartRole.ArmR)
                    : name.Contains("leg") ? (left ? PartRole.LegL : PartRole.LegR) : null;
                if (role is { } limb) _swimJoints.Add(new SwimmingJoint(skeleton, i, limb));
            }
        }
        foreach (Node child in node.GetChildren()) CollectSwimmingJoints(child);
    }

    private void PoseSwimming(float dt)
    {
        if (!_swimCollected)
        {
            CollectSwimmingJoints(_hips);
            _swimCollected = true;
        }
        _swimWasActive = true;
        SwimmingAnimationFrames++;
        // Imported walk/sprint clips must never advance while swimming.
        _player?.Stop(keepState: true);
        float stroke = SwimmingStroke;
        float sweep = Mathf.Cos(_time * 2.5f);
        float kick = Mathf.Sin(_time * 5f) * 0.22f;
        Vector3 RotationFor(PartRole role) => role switch
        {
            PartRole.ArmL => new Vector3(-1.35f + stroke * 0.8f, 0f, -0.65f - sweep * 0.5f),
            PartRole.ArmR => new Vector3(-1.35f + stroke * 0.8f, 0f, 0.65f + sweep * 0.5f),
            PartRole.LegL => new Vector3(kick, 0f, -0.09f),
            PartRole.LegR => new Vector3(-kick, 0f, 0.09f),
            PartRole.WingL => new Vector3(0f, 0f, -0.7f + stroke * 0.45f),
            PartRole.WingR => new Vector3(0f, 0f, 0.7f - stroke * 0.45f),
            _ => Vector3.Zero,
        };
        foreach (SwimmingJoint joint in _swimJoints)
            joint.Skeleton.SetBonePoseRotation(joint.Bone, Quaternion.FromEuler(RotationFor(joint.Role)));
        for (int i = 0; i < _parts.Length; i++)
        {
            ref Part part = ref _parts[i];
            Vector3 rotation = RotationFor(part.Def.Role);
            if (rotation != Vector3.Zero) Limb(ref part, rotation.X, rotation.Z, 0.15f);
        }
        // Small body bob; no running bounce, high-knee gait or landing squash.
        SetHips(0f, Mathf.Sin(_time * 2.5f) * 0.035f, 0f, 0f, sweep * 0.035f, 0f, 1f);
        ApplyOverlay(_flash > 0.05f ? ToonMaterials.FlashOverlay : _rimMaterial);
    }

    private void EndSwimmingAnimation()
    {
        _swimWasActive = false;
        _clip = "";
        foreach (SwimmingJoint joint in _swimJoints) joint.Skeleton.ResetBonePoses();
    }
}
