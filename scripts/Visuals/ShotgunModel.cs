using Godot;

namespace Wavebreak.Visuals;

internal static class ShotgunModel
{
    private static Mesh? _mesh;
    internal static Vector3 RightGrip => new(.045f, -.08f, .04f);
    internal static Vector3 LeftGrip => new(-.015f, -.035f, -.18f);
    internal static Vector3 Muzzle => new(0, .015f, -.48f);

    internal static Mesh Mesh()
    {
        if (_mesh is not null && GodotObject.IsInstanceValid(_mesh)) return _mesh;
        var kit = new MeshKit();
        kit.Box(Transform3D.Identity, new Vector3(.13f, .09f, .24f), new Color(.2f, .3f, .35f));
        kit.Box(new Transform3D(Basis.FromEuler(new Vector3(.2f, 0, 0)), RightGrip), new Vector3(.055f, .14f, .07f), new Color(.28f, .18f, .1f));
        kit.Box(new Transform3D(Basis.Identity, LeftGrip), new Vector3(.13f, .07f, .09f), new Color(.28f, .18f, .1f));
        foreach (int side in new[] { -1, 1 })
        {
            var end = new Vector3(side * .031f, .015f, -.48f);
            kit.Tube(new Vector3(side * .031f, .015f, -.12f), end, .026f, .026f, Colors.Silver);
            kit.Sphere(end, .02f, new Color(.04f, .07f, .08f));
        }
        return _mesh = kit.ToMesh();
    }
}
