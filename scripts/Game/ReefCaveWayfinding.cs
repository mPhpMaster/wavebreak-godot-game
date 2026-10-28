using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Game;

public static class ReefCaveWayfinding
{
    public static void Build(Node3D parent, Vector3[] path)
    {
        var exit = Arena.Flat(new Color(1, .68f, .22f), .9f);
        var entrance = Arena.Flat(new Color(.15f, .65f, .9f), .8f);
        for (int index = 6; index < path.Length - 3; index += 8)
        {
            Vector3 direction = (path[index + 2] - path[index]).Normalized();
            Vector3 at = path[index] + Vector3.Down * (ReefCavePath.Width(index) - 1);
            var marker = new MeshInstance3D
            {
                Mesh = new PrismMesh { Size = new Vector3(1.2f, .15f, 1.8f) },
                Position = at, MaterialOverride = exit,
            };
            parent.AddChild(marker);
            marker.LookAt(at + direction, Mathf.Abs(direction.Dot(Vector3.Up)) > .95f ? Vector3.Right : Vector3.Up);
            parent.AddChild(new OmniLight3D
            {
                Position = path[index], LightColor = new Color(.6f, .8f, .86f), OmniRange = 18,
                LightEnergy = .8f, ShadowEnabled = false,
            });
            if (index % 16 == 6)
                parent.AddChild(new Label3D
                {
                    Text = Loc.T("EXIT") + " →", Position = path[index] + Vector3.Up * 2,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 42, PixelSize = .015f,
                    Modulate = new Color(1, .75f, .3f), NoDepthTest = false,
                });
        }
        foreach (var portal in new[] { (path[0], entrance, "ENTRANCE"), (path[^1], exit, "EXIT") })
            parent.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 6.8f, OuterRadius = 7, Rings = 8, RingSegments = 32 },
                Position = portal.Item1, MaterialOverride = portal.Item2,
            });
    }
}
