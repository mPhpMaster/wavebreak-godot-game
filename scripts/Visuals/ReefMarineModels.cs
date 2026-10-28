using System.Collections.Generic;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

/// <summary>Original marine silhouettes shared by every reef spawn path, including summons and bosses.</summary>
public static class ReefMarineModels
{
    private static readonly Dictionary<string, ModelSpec> Cache = new();

    public static ModelSpec For(EnemyDef def)
    {
        string key = def.Id + (def.IsElite ? "+elite" : "") + (def.IsBoss ? "+boss" : "");
        if (Cache.TryGetValue(key, out ModelSpec? cached)) return cached;
        int kind = def.IsBoss ? 4 : def.Id.Contains("crab") || def.Id.Contains("shell") || def.Id.Contains("carapace") ? 2
            : def.Id.Contains("eel") || def.Id.Contains("leech") ? 1
            : def.Id.Contains("jelly") || def.Id.Contains("polyp") || def.Id.Contains("eye") ? 3 : 0;
        var spec = new ModelSpec { Id = "reef:" + key, Height = def.Height, Body = BodyType.Flyer,
            Primary = def.Tint, Accent = new Color(0.4f, 0.95f, 1), Face = false,
            Crown = def.IsElite || def.IsBoss, Aura = def.IsElite || def.IsBoss, EyeGlow = 1.5f,
            LodDistance = def.IsBoss ? 0 : 40, Custom = body => Build(body, kind) };
        Cache[key] = spec; return spec;
    }

    private static void Build(CustomBody body, int kind)
    {
        float h = body.H;
        MeshKit kit = body.Kit;
        Color color = body.Spec.Primary;
        Vector3 center = Vector3.Up * h * 0.5f;
        if (kind == 3)
        {
            kit.Ellipsoid(new Transform3D(Basis.Identity, center + Vector3.Up * h * 0.12f), new Vector3(h * 0.5f, h * 0.3f, h * 0.5f), color, 0.2f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.Tau / 8;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * h * 0.32f;
                kit.Tube(center + p, p * 1.25f, h * 0.035f, h * 0.012f, body.Spec.Accent, 0.35f);
            }
        }
        else if (kind == 2)
        {
            kit.Ellipsoid(new Transform3D(Basis.Identity, center), new Vector3(h * 0.6f, h * 0.3f, h * 0.45f), color);
            for (int side = -1; side <= 1; side += 2)
            {
                var legs = new MeshKit();
                for (int i = 0; i < 4; i++)
                {
                    Vector3 end = new(side * h * 0.9f, -h * 0.35f, (i - 1.5f) * h * 0.3f);
                    legs.Tube(Vector3.Zero, end, h * 0.055f, h * 0.025f, color.Lightened(0.15f));
                }
                body.Fin(side, center, legs);
                kit.Sphere(center + new Vector3(side * h * 0.65f, 0, -h * 0.6f), h * 0.18f, color.Lightened(0.2f));
            }
        }
        else
        {
            float length = kind == 1 ? 1.3f : kind == 4 ? 0.9f : 0.65f;
            kit.Ellipsoid(new Transform3D(Basis.Identity, center), new Vector3(h * (kind == 4 ? 0.45f : 0.3f), h * 0.27f, h * length), color);
            for (int side = -1; side <= 1; side += 2)
            {
                var fin = new MeshKit();
                fin.Cone(Vector3.Zero, new Vector3(side * h * (kind == 4 ? 1.5f : 0.65f), -h * 0.08f, h * 0.3f), h * 0.21f, color.Lightened(0.15f), 0, 4);
                body.Fin(side, center + new Vector3(side * h * 0.2f, 0, 0), fin);
            }
            kit.Cone(center + Vector3.Back * h * length, center + Vector3.Back * h * (length + 0.5f), h * 0.3f, color, 0, 4);
            if (kind == 4)
                for (int i = 0; i < 5; i++) kit.Cone(center + new Vector3(0, h * 0.2f, (i - 2) * h * 0.3f),
                    center + new Vector3(0, h * 0.5f, (i - 2) * h * 0.3f), h * 0.12f, body.Spec.Accent, 0.5f);
        }
        foreach (int side in new[] { -1, 1 })
            kit.Sphere(center + new Vector3(side * h * 0.18f, h * 0.1f, -h * 0.4f), h * 0.065f, new Color(1, 0.95f, 0.6f), 1);
    }
}
