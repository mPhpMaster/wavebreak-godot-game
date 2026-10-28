using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>Static spatial batches let the renderer cull hidden districts independently.</summary>
public static class SpatialBatches
{
    public static void Build(Node3D parent, Mesh mesh, IReadOnlyList<(Transform3D X, Color C)> instances,
        Material? material, string name, float cell, float range, bool shadows, IReadOnlyList<Color>? custom = null)
    {
        bool monolithic = Game.Arena.IsCity && !Game.PerformanceScenario.Reference
            && name is "Houses" or "Roofs" or "Trims";
        var groups = new Dictionary<Vector3I, List<int>>();
        for (int i = 0; i < instances.Count; i++)
        {
            Vector3 p = instances[i].X.Origin;
            var key = monolithic ? Vector3I.Zero : new Vector3I(Mathf.FloorToInt(p.X / cell), Mathf.FloorToInt(p.Y / cell), Mathf.FloorToInt(p.Z / cell));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>();
            list.Add(i);
        }
        foreach (var pair in groups)
        {
            Vector3 center = Vector3.Zero;
            foreach (int index in pair.Value) center += instances[index].X.Origin;
            center /= pair.Value.Count;
            var batch = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true, UseCustomData = custom is not null, Mesh = mesh, InstanceCount = pair.Value.Count };
            for (int i = 0; i < pair.Value.Count; i++)
            {
                var value = instances[pair.Value[i]];
                Transform3D local = value.X; local.Origin -= center;
                batch.SetInstanceTransform(i, local); batch.SetInstanceColor(i, value.C);
                if (custom is not null) batch.SetInstanceCustomData(i, custom[pair.Value[i]]);
            }
            parent.AddChild(new MultiMeshInstance3D { Name = name + "_" + pair.Key,
                Position = center, Multimesh = batch, MaterialOverride = material, VisibilityRangeEnd = monolithic ? 0f : range,
                VisibilityRangeEndMargin = 15f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    public static void SplitStatic(Node3D parent, float cell, float range)
    {
        foreach (Node node in parent.GetChildren())
        {
            if (node is not MultiMeshInstance3D { Multimesh: { } source } old) continue;
            List<Color>? custom = source.UseCustomData ? new List<Color>(source.InstanceCount) : null;
            var values = new List<(Transform3D, Color)>(source.InstanceCount);
            for (int i = 0; i < source.InstanceCount; i++)
            {
                values.Add((old.Transform * source.GetInstanceTransform(i), source.UseColors ? source.GetInstanceColor(i) : Colors.White));
                custom?.Add(source.GetInstanceCustomData(i));
            }
            Build(parent, source.Mesh, values, old.MaterialOverride, old.Name, cell, range,
                old.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off, custom);
            parent.RemoveChild(old); old.QueueFree();
        }
    }
}
