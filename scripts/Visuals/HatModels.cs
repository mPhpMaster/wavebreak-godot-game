using System.Collections.Generic;
using Godot;

namespace Wavebreak.Visuals;

/// <summary>
/// Procedural hat meshes, by <see cref="Meta.HatDef.Id"/>. Built once each with
/// <see cref="MeshKit"/> (colour in the vertices, like every procedural model)
/// and shared. Units: head half-width = 1, origin on the crown of the head,
/// +Y up, front = -Z. A hat therefore sits from about y = -0.2 upward, and
/// <see cref="CharacterRig.SetHat"/> scales it to the head it lands on.
/// </summary>
public static partial class HatModels
{
    private static readonly Dictionary<string, Mesh?> Cache = new();

    public static Mesh? Get(string id)
    {
        if (Cache.TryGetValue(id, out Mesh? m) && (m is null || GodotObject.IsInstanceValid(m))) return m;
        var k = new MeshKit();
        bool ok = Build(id, k);
        m = ok && k.VertexCount > 0 ? k.ToMesh() : null;
        Cache[id] = m;
        return m;
    }

    private static Transform3D At(float x, float y, float z) => new(Basis.Identity, new Vector3(x, y, z));

    private static readonly Color White = new(0.95f, 0.95f, 0.93f);
    private static readonly Color Navy = new(0.12f, 0.18f, 0.38f);
    private static readonly Color Gold = new(1.00f, 0.80f, 0.30f);
    private static readonly Color Brass = new(0.85f, 0.62f, 0.25f);

    private static bool Build(string id, MeshKit k)
    {
        switch (id)
        {
            case "sailor_cap":
                k.Cylinder(At(0f, -0.18f, 0f), 0.80f, 0.86f, 0.38f, White, 0f, 14, new Color(1f, 1f, 1f));
                k.Cylinder(At(0f, -0.20f, 0f), 0.83f, 0.83f, 0.14f, Navy, 0f, 14);
                k.Sphere(new Vector3(0f, 0.22f, 0f), 0.12f, Navy);
                return true;

            case "tricorne":
            {
                Color felt = new(0.22f, 0.14f, 0.10f);
                k.Cylinder(At(0f, -0.12f, 0f), 1.15f, 1.10f, 0.08f, felt, 0f, 3);
                k.Cylinder(At(0f, -0.06f, 0f), 0.78f, 0.62f, 0.42f, felt, 0f, 10);
                for (int i = 0; i < 3; i++)
                {
                    float a = i / 3f * Mathf.Tau + Mathf.Pi / 2f;
                    k.Sphere(new Vector3(Mathf.Cos(a) * 1.08f, -0.02f, Mathf.Sin(a) * 1.08f), 0.09f, Gold, 0.4f);
                }
                return true;
            }

            case "souwester":
            {
                Color yellow = new(1.00f, 0.82f, 0.15f);
                k.Ellipsoid(At(0f, -0.05f, 0f), new Vector3(0.82f, 0.48f, 0.82f), yellow, 0f, 12, 7);
                k.Cylinder(At(0f, -0.22f, 0.12f), 1.12f, 1.05f, 0.06f, yellow.Darkened(0.1f), 0f, 14);
                return true;
            }

            case "kelp_wreath":
            {
                Color kelp = new(0.22f, 0.55f, 0.25f);
                for (int i = 0; i < 12; i++)
                {
                    float a = i / 12f * Mathf.Tau;
                    Vector3 p = new(Mathf.Cos(a) * 0.82f, -0.08f, Mathf.Sin(a) * 0.82f);
                    k.Ellipsoid(new Transform3D(new Basis(Vector3.Up, -a), p), new Vector3(0.18f, 0.12f, 0.26f), kelp.Lightened(i % 2 * 0.15f), 0f, 6, 4);
                    if (i % 3 == 0) k.Cone(p, p + new Vector3(Mathf.Cos(a) * 0.15f, 0.45f, Mathf.Sin(a) * 0.15f), 0.08f, kelp.Lightened(0.25f), 0f, 4);
                }
                return true;
            }

            case "starfish":
            {
                Color orange = new(1.00f, 0.50f, 0.25f);
                for (int i = 0; i < 5; i++)
                {
                    float a = i / 5f * Mathf.Tau;
                    Vector3 tip = new(Mathf.Sin(a) * 0.95f, 0.02f, -Mathf.Cos(a) * 0.95f);
                    k.Tube(new Vector3(0f, 0.06f, 0f), tip, 0.24f, 0.05f, orange, 0f, 6);
                }
                k.Sphere(new Vector3(0f, 0.1f, 0f), 0.26f, orange.Lightened(0.15f));
                return true;
            }

            case "shell_bonnet":
            {
                Color cream = new(0.98f, 0.88f, 0.78f);
                Color pink = new(1.00f, 0.62f, 0.62f);
                k.Ellipsoid(At(0f, -0.08f, 0.1f), new Vector3(0.88f, 0.45f, 0.82f), cream, 0f, 12, 6);
                for (int i = 0; i < 7; i++)
                {
                    float a = Mathf.Lerp(-1.2f, 1.2f, i / 6f);
                    Vector3 from = new(0f, 0.32f, 0.2f);
                    Vector3 to = new(Mathf.Sin(a) * 0.9f, -0.12f, 0.1f - Mathf.Cos(a) * 0.75f);
                    k.Tube(from, to, 0.05f, 0.04f, pink, 0f, 5);
                }
                return true;
            }

            case "pearl_tiara":
            {
                Color silver = new(0.82f, 0.85f, 0.92f);
                k.Cylinder(At(0f, -0.16f, 0f), 0.84f, 0.84f, 0.08f, silver, 0.2f, 16);
                for (int i = 0; i < 5; i++)
                {
                    float a = Mathf.Lerp(-0.9f, 0.9f, i / 4f);
                    float lift = 0.12f + (1f - Mathf.Abs(a) / 0.9f) * 0.22f;
                    Vector3 p = new(Mathf.Sin(a) * 0.84f, -0.1f + lift, -Mathf.Cos(a) * 0.84f);
                    k.Tube(new Vector3(p.X, -0.12f, p.Z), p, 0.03f, 0.03f, silver, 0.2f, 4);
                    k.Sphere(p, i == 2 ? 0.13f : 0.09f, new Color(1f, 0.98f, 0.95f), 0.6f);
                }
                return true;
            }

            case "captains_cap":
                k.Cylinder(At(0f, -0.2f, 0f), 0.82f, 0.95f, 0.36f, White, 0f, 14, new Color(1f, 1f, 1f));
                k.Cylinder(At(0f, -0.21f, 0f), 0.84f, 0.85f, 0.14f, new Color(0.08f, 0.08f, 0.1f), 0f, 14);
                k.Ellipsoid(At(0f, -0.2f, -0.78f), new Vector3(0.5f, 0.05f, 0.32f), new Color(0.08f, 0.08f, 0.1f), 0f, 10, 4);
                k.Sphere(new Vector3(0f, -0.08f, -0.84f), 0.11f, Gold, 0.6f);
                return true;

            case "lantern_hat":
            {
                Color iron = new(0.20f, 0.20f, 0.24f);
                k.Cylinder(At(0f, -0.18f, 0f), 0.7f, 0.62f, 0.2f, iron, 0f, 8);
                k.Cylinder(At(0f, 0.02f, 0f), 0.42f, 0.42f, 0.5f, new Color(1f, 0.85f, 0.4f), 2.2f, 8);
                for (int i = 0; i < 4; i++)
                {
                    float a = i / 4f * Mathf.Tau + 0.4f;
                    Vector3 b = new(Mathf.Cos(a) * 0.44f, 0.02f, Mathf.Sin(a) * 0.44f);
                    k.Tube(b, b + Vector3.Up * 0.5f, 0.04f, 0.04f, iron, 0f, 4, caps: false);
                }
                k.Cone(new Vector3(0f, 0.52f, 0f), new Vector3(0f, 0.9f, 0f), 0.56f, iron, 0f, 8);
                k.Sphere(new Vector3(0f, 0.95f, 0f), 0.08f, Brass);
                return true;
            }

            case "anchor_crest":
            {
                Color steel = new(0.45f, 0.50f, 0.58f);
                k.Cylinder(At(0f, -0.2f, 0f), 0.84f, 0.84f, 0.12f, Navy, 0f, 14);
                k.Box(new Vector3(0f, 0.42f, 0f), new Vector3(0.14f, 0.85f, 0.14f), steel);
                k.Box(new Vector3(0f, 0.68f, 0f), new Vector3(0.6f, 0.1f, 0.1f), steel);
                k.Tube(new Vector3(0f, 0.9f, 0f), new Vector3(0f, 0.98f, 0f), 0.13f, 0.13f, steel, 0f, 8);
                k.Tube(new Vector3(0f, 0.05f, 0f), new Vector3(-0.42f, 0.32f, 0f), 0.07f, 0.04f, steel, 0f, 5);
                k.Tube(new Vector3(0f, 0.05f, 0f), new Vector3(0.42f, 0.32f, 0f), 0.07f, 0.04f, steel, 0f, 5);
                k.Cone(new Vector3(-0.42f, 0.32f, 0f), new Vector3(-0.5f, 0.48f, 0f), 0.09f, steel, 0f, 4);
                k.Cone(new Vector3(0.42f, 0.32f, 0f), new Vector3(0.5f, 0.48f, 0f), 0.09f, steel, 0f, 4);
                return true;
            }

            case "coral_crown":
            {
                Color coral = new(1.00f, 0.45f, 0.45f);
                k.Cylinder(At(0f, -0.18f, 0f), 0.84f, 0.86f, 0.16f, coral.Darkened(0.2f), 0f, 14);
                for (int i = 0; i < 7; i++)
                {
                    float a = i / 7f * Mathf.Tau;
                    Vector3 b = new(Mathf.Cos(a) * 0.8f, -0.04f, Mathf.Sin(a) * 0.8f);
                    Vector3 t = b + new Vector3(Mathf.Cos(a) * 0.12f, 0.42f + (i % 2) * 0.14f, Mathf.Sin(a) * 0.12f);
                    k.Tube(b, t, 0.08f, 0.04f, coral, 0.2f, 5);
                    k.Tube(b.Lerp(t, 0.5f), b.Lerp(t, 0.5f) + new Vector3(-Mathf.Sin(a) * 0.16f, 0.2f, Mathf.Cos(a) * 0.16f), 0.04f, 0.025f, coral.Lightened(0.15f), 0.2f, 4);
                    k.Sphere(t, 0.06f, coral.Lightened(0.3f), 0.4f);
                }
                return true;
            }

            case "diving_helmet":
            {
                // Encloses the whole head: centre about one head-height down.
                k.Sphere(new Vector3(0f, -0.95f, 0f), 1.22f, Brass, 0f, 14, 9);
                k.Ellipsoid(new Transform3D(Basis.Identity, new Vector3(0f, -0.95f, -1.12f)), new Vector3(0.6f, 0.6f, 0.16f), new Color(0.25f, 0.55f, 0.75f), 0.8f, 12, 6);
                k.Cylinder(At(0f, -0.95f - 1.2f, 0f), 1.1f, 1.0f, 0.22f, Brass.Darkened(0.25f), 0f, 14);
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * Mathf.Tau;
                    k.Sphere(new Vector3(Mathf.Cos(a) * 0.7f, -0.95f + Mathf.Sin(a) * 0.7f, -1.05f), 0.07f, Brass.Lightened(0.3f));
                }
                return true;
            }

            case "gull_perch":
            {
                Color grey = new(0.62f, 0.66f, 0.72f);
                Color orange = new(1.00f, 0.62f, 0.15f);
                k.Ellipsoid(At(0f, 0.28f, 0.05f), new Vector3(0.28f, 0.24f, 0.45f), White, 0f, 10, 6);
                k.Sphere(new Vector3(0f, 0.5f, -0.32f), 0.18f, White);
                k.Cone(new Vector3(0f, 0.48f, -0.46f), new Vector3(0f, 0.45f, -0.72f), 0.06f, orange, 0f, 5);
                k.Sphere(new Vector3(0.1f, 0.55f, -0.44f), 0.03f, new Color(0.05f, 0.05f, 0.05f));
                k.Sphere(new Vector3(-0.1f, 0.55f, -0.44f), 0.03f, new Color(0.05f, 0.05f, 0.05f));
                k.Ellipsoid(new Transform3D(new Basis(Vector3.Back, 0.35f), new Vector3(-0.3f, 0.3f, 0.1f)), new Vector3(0.08f, 0.18f, 0.38f), grey, 0f, 6, 4);
                k.Ellipsoid(new Transform3D(new Basis(Vector3.Back, -0.35f), new Vector3(0.3f, 0.3f, 0.1f)), new Vector3(0.08f, 0.18f, 0.38f), grey, 0f, 6, 4);
                k.Tube(new Vector3(-0.1f, 0.08f, 0f), new Vector3(-0.1f, -0.06f, 0f), 0.03f, 0.03f, orange, 0f, 4);
                k.Tube(new Vector3(0.1f, 0.08f, 0f), new Vector3(0.1f, -0.06f, 0f), 0.03f, 0.03f, orange, 0f, 4);
                return true;
            }

            case "jellyfish_cap":
            {
                Color bell = new(0.95f, 0.55f, 0.95f);
                k.Ellipsoid(At(0f, 0f, 0f), new Vector3(0.95f, 0.55f, 0.95f), bell, 0.7f, 12, 7);
                for (int i = 0; i < 8; i++)
                {
                    float a = i / 8f * Mathf.Tau;
                    Vector3 b = new(Mathf.Cos(a) * 0.9f, -0.05f, Mathf.Sin(a) * 0.9f);
                    k.Tube(b, b + new Vector3(Mathf.Cos(a) * 0.15f, -0.55f - (i % 2) * 0.2f, Mathf.Sin(a) * 0.15f), 0.06f, 0.02f, bell.Lightened(0.2f), 0.9f, 4);
                }
                return true;
            }

            case "storm_cloud":
            {
                Color cloud = new(0.55f, 0.58f, 0.66f);
                k.Sphere(new Vector3(0f, 0.75f, 0f), 0.42f, cloud);
                k.Sphere(new Vector3(-0.42f, 0.62f, 0.05f), 0.32f, cloud.Lightened(0.08f));
                k.Sphere(new Vector3(0.42f, 0.64f, -0.05f), 0.34f, cloud.Darkened(0.05f));
                k.Sphere(new Vector3(0.1f, 0.65f, 0.38f), 0.3f, cloud);
                k.Sphere(new Vector3(-0.1f, 0.62f, -0.38f), 0.3f, cloud.Lightened(0.05f));
                k.Cone(new Vector3(0.15f, 0.4f, -0.1f), new Vector3(0.05f, 0.12f, -0.15f), 0.08f, new Color(1f, 0.95f, 0.4f), 2.5f, 4);
                return true;
            }
        }
        return BuildCute(id, k);   // Visuals/HatModels.Cute.cs
    }
}
