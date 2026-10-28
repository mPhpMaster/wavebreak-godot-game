using Godot;

namespace Wavebreak.Game;

internal static class CityRoofCheck
{
    internal static bool Matches(Arena arena)
    {
        bool wall = false, roof = false;
        // The dummy renderer does not retain MultiMesh instance buffers.
        if (DisplayServer.GetName() == "headless")
        {
            foreach (var instance in arena.CityWallInstances) Inspect(instance.X, true, ref wall, ref roof);
            foreach (var instance in arena.CityTrimInstances) Inspect(instance.X, false, ref wall, ref roof);
            return wall && roof;
        }
        foreach (Node node in arena.FindChildren("*", "MultiMeshInstance3D", true, false))
        {
            if (node is not MultiMeshInstance3D { Multimesh: { } mesh } batch) continue;
            for (int i = 0; i < mesh.InstanceCount; i++)
            {
                Transform3D transform = batch.GlobalTransform * mesh.GetInstanceTransform(i);
                if (batch.Name.ToString().StartsWith("Houses") || batch.Name.ToString().StartsWith("Trims"))
                    Inspect(transform, batch.Name.ToString().StartsWith("Houses"), ref wall, ref roof);
            }
        }
        return wall && roof;
    }
    private static void Inspect(Transform3D transform, bool house, ref bool wall, ref bool roof)
    {
        if (Mathf.Abs(transform.Origin.X - 18f) > .01f || Mathf.Abs(transform.Origin.Z - 22f) > .01f) return;
        float height = transform.Basis.Y.Length();
        float bottom = transform.Origin.Y - height * .5f, top = transform.Origin.Y + height * .5f;
        if (house) wall |= Mathf.Abs(bottom) < .01f && Mathf.Abs(top - 32.75f) < .01f;
        else if (Mathf.Abs(height - .25f) < .01f) roof |= Mathf.Abs(bottom - 32.75f) < .01f && Mathf.Abs(top - 33f) < .01f;
    }
}
