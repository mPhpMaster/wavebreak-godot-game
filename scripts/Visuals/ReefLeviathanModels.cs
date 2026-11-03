using Godot;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

public static class ReefLeviathanModels
{
    public const string Id = "reef_leviathan";
    public const string Path = "res://assets/models/wertt/leviathan/leviathan-normalized.glb";
    // The import's body runs diagonally through its box: head toward (0.318, 0.789, 0.526),
    // the flat wing sheet's normal (0.404, -0.615, 0.677), both measured from the vertices.
    // These angles turn the head to -Z (forward) and that normal to +Y (back up, wings level).
    public const float Pitch = -2.3076f, Roll = 0.4162f, Yaw = 0.355f;
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
        spec.ScenePitch = Pitch; spec.SceneRoll = Roll; spec.SceneYaw = Yaw;
        spec.SceneAlbedoFloor = .55f;   // .8 washed the teal hide out to near white under the reef light
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


