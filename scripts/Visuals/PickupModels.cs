using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Visuals;

/// <summary>One normalized imported mesh per drop kind, reused by the existing pool.</summary>
public static class PickupModels
{
    private static readonly Dictionary<PickupKind, ArrayMesh> Cache = new();

    public static ArrayMesh? Get(PickupKind kind)
    {
        if (kind == PickupKind.Health) return null;
        if (Cache.TryGetValue(kind, out ArrayMesh? mesh)) return mesh;
        string name = kind == PickupKind.Gold ? "gold_ingots" : "mineral";
        ArrayMesh? source = PropFactory.KitMesh($"res://assets/quaternius/pickups/{name}.glb");
        if (source is null) return null;
        Aabb bounds = source.GetAabb();
        float span = Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, bounds.Size.Z));
        if (span <= 0f) return null;
        float scale = (kind == PickupKind.Gold ? 0.48f : 0.4f) / span;
        Vector3 origin = new(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z);
        var transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), origin * scale);
        mesh = new ArrayMesh();
        for (int surface = 0; surface < source.GetSurfaceCount(); surface++)
        {
            using var builder = new SurfaceTool();
            builder.AppendFrom(source, surface, transform);
            builder.Commit(mesh);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, source.SurfaceGetMaterial(surface));
        }
        Cache[kind] = mesh;
        return mesh;
    }
}
