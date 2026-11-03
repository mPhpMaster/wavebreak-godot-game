using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>
/// Orthographic side and top photographs of one creature for the guardian and
/// Leviathan checks. The player camera is switched off (not just outranked) and
/// the HUD hidden, so the capture can only show the portrait camera's view.
/// </summary>
internal static class CreaturePortrait
{
    internal static async Task Capture(Node owner, Enemy creature, CharacterRig rig, float size, string path)
    {
        SceneTree tree = owner.GetTree();
        Camera3D? playerCamera = owner.GetViewport().GetCamera3D();
        if (playerCamera is not null) playerCamera.Current = false;
        var hidden = new System.Collections.Generic.List<CanvasLayer>();
        foreach (Node node in tree.Root.FindChildren("*", "CanvasLayer", true, false))
            if (node is CanvasLayer { Visible: true } layer) { layer.Visible = false; hidden.Add(layer); }
        var camera = new Camera3D { Name = "CreaturePortrait", Projection = Camera3D.ProjectionType.Orthogonal, Size = size, Near = .05f, Far = size * 6 };
        tree.CurrentScene.AddChild(camera);
        Vector3 forward = -rig.GlobalBasis.Z with { Y = 0 };
        forward = forward.LengthSquared() > .001f ? forward.Normalized() : Vector3.Forward;
        Vector3 side = forward.Cross(Vector3.Up).Normalized();
        Vector3 centre = creature.GlobalPosition + Vector3.Up * (creature.Def?.Height ?? 2) * .5f;
        camera.MakeCurrent();
        GD.Print($"CREATURE facing={forward} centre={centre} camera={owner.GetViewport().GetCamera3D()?.Name}");
        // Side, from just outside the body so the reef fog stays thin: the nose points to screen right, the back is up.
        camera.GlobalPosition = centre + side * ((creature.Def?.Radius ?? 1) * 1.6f + 4);
        camera.LookAt(centre, Vector3.Up);
        await Wait(tree, .4f);
        await Screenshot.Capture(tree, owner, path);
        // Top: looking straight down, the nose points to screen top.
        camera.GlobalPosition = centre + Vector3.Up * ((creature.Def?.Height ?? 2) * .6f + 3); // under the water surface
        camera.LookAt(centre, forward);
        await Wait(tree, .3f);
        await Screenshot.Capture(tree, owner, path.EndsWith(".png") ? path[..^4] + ".top.png" : path + ".top.png");
        // Front three-quarter, from ahead, beside and above: the face, the back and one wing.
        camera.GlobalPosition = centre + (forward * 1.2f + side * .8f + Vector3.Up * .7f).Normalized() * ((creature.Def?.Radius ?? 1) * 2f + 4);
        camera.LookAt(centre, Vector3.Up);
        await Wait(tree, .3f);
        await Screenshot.Capture(tree, owner, path.EndsWith(".png") ? path[..^4] + ".front.png" : path + ".front.png");
        camera.QueueFree();
        foreach (CanvasLayer layer in hidden) if (GodotObject.IsInstanceValid(layer)) layer.Visible = true;
        if (playerCamera is not null && GodotObject.IsInstanceValid(playerCamera)) playerCamera.MakeCurrent();
    }

    private static async Task Wait(SceneTree tree, float time)
        => await tree.ToSignal(tree.CreateTimer(time, true, true), SceneTreeTimer.SignalName.Timeout);
}
