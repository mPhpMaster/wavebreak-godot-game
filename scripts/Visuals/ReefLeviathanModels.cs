using Godot;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

public static class ReefLeviathanModels
{
    public const string Id = "reef_leviathan";
    public const string Path = "res://assets/models/wertt/leviathan/leviathan-normalized.glb";
    private static ModelSpec? _spec;
    public static ModelSpec For(EnemyDef def)
    {
        if (_spec is not null) return _spec;
        PackedScene scene = ResourceLoader.Load<PackedScene>(Path)
            ?? throw new System.InvalidOperationException("The licensed Leviathan scene is missing: " + Path);
        Node3D sample = scene.Instantiate<Node3D>();
        AnimationPlayer? player = FindPlayer(sample);
        string? swim = null;
        if (player is not null)
            foreach (string clip in player.GetAnimationList()) if (clip != "RESET") { swim = clip; break; }
        sample.Free();
        var spec = ModelSpec.FromScene(scene, def.Height, id: "boss:wertt-leviathan");
        spec.PreserveSceneRootTransform = true;
        spec.NativeSwimmingClip = true;
        spec.ScenePitch = -Mathf.Pi * .5f;
        spec.SceneAlbedoFloor = .8f;
        if (swim is not null)
            foreach (AnimSlot slot in new[] { AnimSlot.Idle, AnimSlot.Walk, AnimSlot.Run, AnimSlot.Dash, AnimSlot.Jump, AnimSlot.Fall })
                spec.Clips[slot] = swim;
        _spec = spec; return spec;
    }
    private static AnimationPlayer? FindPlayer(Node node)
    {
        if (node is AnimationPlayer animation) return animation;
        foreach (Node child in node.GetChildren()) if (FindPlayer(child) is { } found) return found;
        return null;
    }
}


