using Godot;

namespace Wavebreak.Game;

public static class ReefCheckHelpers
{
    public static T? Find<T>(Node root) where T : Node
    {
        if (root is T typed) return typed;
        foreach (Node child in root.GetChildren())
            if (Find<T>(child) is { } found) return found;
        return null;
    }
}
