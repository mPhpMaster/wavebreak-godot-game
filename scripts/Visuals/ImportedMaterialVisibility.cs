using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

internal static class ImportedMaterialVisibility
{
    private static readonly Dictionary<(ulong, float), StandardMaterial3D> Cache = new();
    internal static void Apply(IEnumerable<GeometryInstance3D> geometry, float minimum)
    {
        foreach (GeometryInstance3D instance in geometry)
        {
            if (instance is not MeshInstance3D { Mesh: { } mesh } model) continue;
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D material) continue;
                Color color = material.AlbedoColor;
                float peak = Mathf.Max(color.R, Mathf.Max(color.G, color.B));
                if (peak < .001f) continue;
                var key = (material.GetInstanceId(), minimum);
                if (!Cache.TryGetValue(key, out StandardMaterial3D? visible))
                {
                    visible = (StandardMaterial3D)material.Duplicate();
                    float lift = Mathf.Max(1, minimum / peak);
                    visible.AlbedoColor = new Color(color.R * lift, color.G * lift, color.B * lift, color.A);
                    visible.EmissionEnabled = true;
                    visible.Emission = visible.AlbedoColor;
                    visible.EmissionEnergyMultiplier = .22f;
                    Cache[key] = visible;
                }
                model.SetSurfaceOverrideMaterial(surface, visible);
            }
        }
    }
}

