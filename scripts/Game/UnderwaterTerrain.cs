using Godot;
using System;
using System.Collections.Generic;
using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>Continuous relief shared by the collision mesh, dressing and swim limits.</summary>
public static class UnderwaterTerrain
{
    public const float SurfaceHeight = WaterSurfaceHeight.BaseHeight;
    public const float WaterCeiling = WaterSurfaceHeight.BaseHeight - 2f;
    private const int Cells = 112;

    public static float Height(float x, float z, float radius)
    {
        float distance = Mathf.Sqrt(x * x + z * z);
        float centre = Mathf.SmoothStep(16f, 31f, distance);
        float hill = 18f * Gaussian(x + 48f, z + 15f, 25f, 43f)
            + 14f * Gaussian(x - 63f, z - 36f, 30f, 25f)
            + 11f * Gaussian(x + 20f, z - 78f, 40f, 23f);
        float valley = -18f * Gaussian(x - 20f, z + 64f, 48f, 23f)
            - 11f * Gaussian(x - 69f, z + 4f, 17f, 49f);
        float folds = 2.8f * Mathf.Sin(x * 0.065f) * Mathf.Cos(z * 0.047f);
        float ground = (hill + valley + folds) * centre;
        float angle = Mathf.Atan2(z, x);
        float cliffStart = radius - 26f + Mathf.Sin(angle * 5f) * 3f;
        float cliff = Mathf.SmoothStep(cliffStart, radius - 5f, distance);
        float peak = 110f + 35f * Mathf.Pow(Mathf.Sin(angle * 7f + 0.7f), 2f)
            + 2.8f * Mathf.Sin(x * 0.31f) * Mathf.Cos(z * 0.27f);
        return Mathf.Lerp(ground, peak, cliff);
    }

    private static float Gaussian(float x, float z, float width, float length)
        => Mathf.Exp(-(x * x / (width * width) + z * z / (length * length)));

    public static float Height(Vector3 at) => Height(at.X, at.Z, Arena.Current.InRadius);

    public static bool LevelPatch(Vector3 at, float radius)
    {
        float y = Height(at);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f;
            var edge = at + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            if (Mathf.Abs(Height(edge) - y) > 0.75f) return false;
        }
        return true;
    }

    public static ArrayMesh Build(float radius) => Build(radius, PerformanceScenario.Reference);

    internal static ArrayMesh Build(float radius, bool reference)
    {
        using var timing = Prof.Scope("Startup.reefTerrain");
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        float extent = radius + 8f;
        float step = extent * 2f / Cells;
        Vector3 RawPoint(int x, int z)
        {
            float px = -extent + x * step, pz = -extent + z * step;
            return new Vector3(px, Height(px, pz, radius), pz);
        }
        TerrainGridVertex[]? grid = reference ? null : new TerrainGridVertex[(Cells + 1) * (Cells + 1)];
        if (grid is not null)
            for (int z = 0; z <= Cells; z++)
                for (int x = 0; x <= Cells; x++)
                {
                    Vector3 p = RawPoint(x, z);
                    grid[z * (Cells + 1) + x] = new TerrainGridVertex(p, ReefCavePath.TerrainDistance(p));
                }
        TerrainGridVertex Point(int x, int z) => grid is null
            ? new TerrainGridVertex(RawPoint(x, z), float.NaN) : grid[z * (Cells + 1) + x];
        void Vertex(Vector3 p)
        {
            surface.SetUV(new Vector2(p.X, p.Z) * 0.04f);
            surface.AddVertex(p);
        }
        Vector3 Intersection(Vector3 from, Vector3 to, float start)
        {
            float lo = 0, hi = 1;
            for (int j = 0; j < 12; j++)
            {
                float mid = (lo + hi) * .5f;
                if ((ReefCavePath.TerrainDistance(from.Lerp(to, mid)) >= 0) == (start >= 0)) lo = mid;
                else hi = mid;
            }
            return from.Lerp(to, (lo + hi) * .5f);
        }
        void ReferenceTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            var polygon = new List<Vector3>(4);
            Vector3[] triangle = { a, b, c };
            for (int i = 0; i < 3; i++)
            {
                Vector3 from = triangle[i], to = triangle[(i + 1) % 3];
                float start = ReefCavePath.TerrainDistance(from), end = ReefCavePath.TerrainDistance(to);
                if (start >= 0) polygon.Add(from);
                if ((start >= 0) == (end >= 0)) continue;
                polygon.Add(Intersection(from, to, start));
            }
            for (int i = 1; i + 1 < polygon.Count; i++) { Vertex(polygon[0]); Vertex(polygon[i]); Vertex(polygon[i + 1]); }
        }
        void Triangle(TerrainGridVertex a, TerrainGridVertex b, TerrainGridVertex c)
        {
            if (reference) { ReferenceTriangle(a.Position, b.Position, c.Position); return; }
            ReadOnlySpan<TerrainGridVertex> triangle = stackalloc TerrainGridVertex[] { a, b, c };
            Span<Vector3> polygon = stackalloc Vector3[6];
            int count = 0;
            for (int i = 0; i < 3; i++)
            {
                TerrainGridVertex from = triangle[i], to = triangle[(i + 1) % 3];
                if (from.CaveDistance >= 0) polygon[count++] = from.Position;
                if ((from.CaveDistance >= 0) == (to.CaveDistance >= 0)) continue;
                polygon[count++] = Intersection(from.Position, to.Position, from.CaveDistance);
            }
            for (int i = 1; i + 1 < count; i++) { Vertex(polygon[0]); Vertex(polygon[i]); Vertex(polygon[i + 1]); }
        }
        for (int z = 0; z < Cells; z++)
            for (int x = 0; x < Cells; x++)
            {
                TerrainGridVertex a = Point(x, z), b = Point(x + 1, z);
                TerrainGridVertex c = Point(x, z + 1), d = Point(x + 1, z + 1);
                Triangle(a, b, c); Triangle(b, d, c);
            }
        surface.GenerateNormals();
        return surface.Commit();
    }

    public static void GroundProps(Node3D props)
    {
        foreach (Node child in props.GetChildren())
        {
            if (child is not MultiMeshInstance3D { Multimesh: { } batch }) continue;
            for (int i = 0; i < batch.InstanceCount; i++)
            {
                Transform3D t = batch.GetInstanceTransform(i);
                Vector3 at = t.Origin;
                t.Origin = at with { Y = at.Y + Height(at) };
                batch.SetInstanceTransform(i, t);
            }
        }
    }
}
