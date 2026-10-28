using System;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

/// <summary>Licensed Quaternius cave guardian; unrelated to the expedition's Leviathan boss.</summary>
public static class ReefGuardianModels
{
    public const string Id = "reef_cave_shark";
    public const string Path = "res://assets/models/quaternius/animated-fish/Shark.fbx";
    private static ModelSpec? _spec;
    public static ModelSpec For(EnemyDef def)
    {
        if (_spec is not null) return _spec;
        if (ResourceLoader.Load<PackedScene>(Path) is not { } scene) return ReefMarineModels.For(def);
        Node3D sample = scene.Instantiate<Node3D>();
        AnimationPlayer? animations = FindAnimations(sample);
        string? swim = null;
        if (animations is not null)
            foreach (string clip in animations.GetAnimationList())
                if (clip != "RESET" && (swim is null || clip.Contains("swim", StringComparison.OrdinalIgnoreCase))) swim = clip;
        sample.Free();
        var spec = ModelSpec.FromScene(scene, def.Height, id: "guardian:quaternius-shark");
        spec.PreserveSceneRootTransform = true;
        spec.NativeSwimmingClip = true;
        if (swim is not null)
            foreach (AnimSlot slot in new[] { AnimSlot.Idle, AnimSlot.Walk, AnimSlot.Run, AnimSlot.Dash, AnimSlot.Jump, AnimSlot.Fall })
                spec.Clips[slot] = swim;
        spec.LodDistance = 65;
        _spec = spec; return spec;
    }
    private static AnimationPlayer? FindAnimations(Node root)
    {
        if (root is AnimationPlayer animation) return animation;
        foreach (Node child in root.GetChildren())
            if (FindAnimations(child) is { } found) return found;
        return null;
    }
}

