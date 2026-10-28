using System.Collections.Generic;
using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public sealed partial class Arena : Node3D
{
    // ------------------------------------------------------------- dressing

    private void BuildDressing()
    {
        using var timing = Prof.Scope("Startup.dressing");
        if (!_stage.Underwater) AddChild(PropFactory.BuildFloor(_stage.Radius + FloorSkirt, _theme));
        var world = PropFactory.MakeWorldEnvironment(_theme);
        var sun = PropFactory.MakeSun(_theme);
        if (_stage.Layout == "city") world.Environment.FogDensity = 0.0025f;
        AddChild(world);
        AddChild(sun);

        // Seeded from the layout, not the run: the dressing is part of the place.
        var rng = new Core.Rng((ulong)_stage.LayoutSeed ^ 0xD2E55UL);
        Node3D props = PropFactory.Scatter(this, rng, _stage.InRadius - 3f, _theme, _stage.PropDensity * Meta.GraphicsSettings.PropDensityScale,
                                           clearRadius: _stage.ClearRadius);
        CullScatter(props);
        if (_stage.Underwater)
        {
            ReefFoundations.Ground(props);
            SpatialBatches.SplitStatic(props, 64f, 125f);
        }
        else if (_stage.Layout == "city") SpatialBatches.SplitStatic(props, 128f, 190f);
        if (_stage.Theme == "underwater")
        {
            var atmosphere = UnderwaterDressing.Build(_stage.InRadius, _stage.LayoutSeed);
            AddChild(atmosphere);
            var bubbles = atmosphere.GetNode<MultiMeshInstance3D>("VisibleBubbleStreams");
            AddChild(new ReefAtmosphere(world, sun, (ShaderMaterial)bubbles.MaterialOverride));
        }
        if (_stage.Underwater) { AddChild(new ReefCurrentVisuals()); AddChild(new ReefFishSchools()); }
        // The new sky and sun take the player's graphics settings at once.
        Meta.GraphicsSettings.ApplyScene(this);
    }

    /// <summary>
    /// Remove scatter instances standing inside terrain or landmarks. The scatter
    /// does not know about this map's footprints, and a tree growing through a
    /// platform top reads as a bug even though nothing collides with it.
    /// </summary>
    private void CullScatter(Node3D props)
    {
        foreach (Node child in props.GetChildren())
        {
            if (child is not MultiMeshInstance3D { Multimesh: { } mm }) continue;
            int count = mm.InstanceCount;
            var keep = new List<Transform3D>(count);
            var keepData = mm.UseCustomData ? new List<Color>(count) : null;
            for (int i = 0; i < count; i++)
            {
                Transform3D t = mm.GetInstanceTransform(i);
                if (Overlaps(new Vector2(t.Origin.X, t.Origin.Z), 0.6f, 0f)) continue;
                keep.Add(t);
                keepData?.Add(mm.GetInstanceCustomData(i));
            }
            if (keep.Count == count) continue;
            mm.InstanceCount = keep.Count;
            for (int i = 0; i < keep.Count; i++)
            {
                mm.SetInstanceTransform(i, keep[i]);
                if (keepData is not null) mm.SetInstanceCustomData(i, keepData[i]);
            }
        }
    }

    // -------------------------------------------------------------- helpers

    private static Vector3 Tangent(Vector3 radial) => new(-radial.Z, 0f, radial.X);

    /// <summary>A yaw-only basis whose local +X points along <paramref name="d"/>.
    /// Box and prism sizes here are authored with X as their long axis.</summary>
    private static Basis XAlong(Vector3 d) => new(Vector3.Up, Mathf.Atan2(-d.Z, d.X));

    /// <summary>Unshaded-ish flat material. Emission is used sparingly to make
    /// gameplay-relevant things read against the scenery.</summary>
    public static StandardMaterial3D Flat(Color color, float emission = 0f)
    {
        var m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f, Metallic = 0f };
        if (emission > 0f)
        {
            m.EmissionEnabled = true;
            m.Emission = color;
            m.EmissionEnergyMultiplier = emission;
        }
        return m;
    }

    /// <summary>Clamp a position inside the arena, used by spawning and knockback.</summary>
    public static Vector3 Clamp(Vector3 p, float margin = 2f) => ClampInside(p, margin, out _);

    /// <summary>
    /// How far <paramref name="p"/> is past the inner face of the nearest wall,
    /// in metres: negative inside, positive outside. Exact for every map shape,
    /// because it is measured against each edge of the current polygon rather
    /// than against a circle — on a hexagon a circle is either too small (the
    /// corners become unreachable) or too big (the corners leak).
    /// </summary>
    public static float EdgeExcess(Vector3 p)
    {
        int sides = Mathf.Max(3, Current.Sides);
        float worst = float.MinValue;
        for (int k = 0; k < sides; k++)
        {
            float a = Mathf.Tau * k / sides;
            worst = Mathf.Max(worst, p.X * Mathf.Cos(a) + p.Z * Mathf.Sin(a));
        }
        return worst - Current.InRadius;
    }

    /// <summary>
    /// Push <paramref name="p"/> back inside the current map's polygon, keeping
    /// <paramref name="margin"/> metres from every wall. <paramref name="normal"/>
    /// is the outward direction it was pushed back from (zero when it was
    /// already inside), so a caller can cancel only the outward part of a
    /// velocity and still slide along the wall — or into a corner.
    /// </summary>
    public static Vector3 ClampInside(Vector3 p, float margin, out Vector3 normal)
    {
        int sides = Mathf.Max(3, Current.Sides);
        float limit = Current.InRadius - margin;
        normal = Vector3.Zero;
        // Two passes: in a corner, correcting against one edge can leave the
        // point just past its neighbour. A convex polygon settles in two.
        for (int pass = 0; pass < 2; pass++)
            for (int k = 0; k < sides; k++)
            {
                float a = Mathf.Tau * k / sides;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float over = p.X * n.X + p.Z * n.Z - limit;
                if (over <= 0f) continue;
                p -= n * over;
                normal += n;
            }
        if (normal != Vector3.Zero) normal = normal.Normalized();
        return p;
    }
}
