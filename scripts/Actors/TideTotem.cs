using System;
using Godot;

namespace Wavebreak.Actors;

/// <summary>
/// The Tide Totem: three carved sea faces stacked on a driftwood plinth (a
/// pufferfish at the bottom, a clam with a pearl in its mouth, a conch on
/// top), every face with glowing sea-glass eyes. Press E and it calls the
/// tide: every experience orb and coin on the map flies to whoever woke it
/// (<see cref="Game.Interactables"/>, Interactables.Totems.cs). It works once,
/// then sleeps: eyes shut to dark, the wood greys, two cracks show.
///
/// Built from primitives like the other stations (no imported model), so it
/// costs a handful of draws and always exists. Idle tells: the Station marker
/// bobbing above it, an aqua light, eyes that pulse, and a slow pulsing ring
/// of water on the ground.
/// </summary>
public sealed partial class TideTotem : Station
{
    public static readonly Color TotemTint = new(0.35f, 0.92f, 1.00f);
    private static readonly Color Driftwood = new(0.56f, 0.42f, 0.30f);
    private static readonly Color Puffer = new(0.30f, 0.66f, 0.74f);
    private static readonly Color PufferBelly = new(0.93f, 0.88f, 0.70f);
    private static readonly Color Clam = new(1.00f, 0.62f, 0.62f);
    private static readonly Color Conch = new(0.98f, 0.86f, 0.72f);
    private static readonly Color ConchLip = new(1.00f, 0.66f, 0.58f);
    private static readonly Color Ink = new(0.13f, 0.10f, 0.12f);
    private static readonly Color Asleep = new(0.42f, 0.42f, 0.44f);

    /// <summary>Session-wide id in co-op (host-assigned); single-player numbers
    /// them too, harmlessly.</summary>
    public int NetId { get; set; }

    public bool Used { get; private set; }

    /// <summary>Co-op: an ask is on its way to the host.</summary>
    public bool NetPending { get; set; }

    public override Color Tint => TotemTint;
    public override string Verb => "call the tide";
    public override bool Usable => !Used;
    public override float FootprintRadius => 1.0f;

    /// <summary>Prompts sit over the conch, not on the clam.</summary>
    public override Vector3 Top => GlobalPosition + Vector3.Up * 3.9f;

    private const float Height = 2.75f;
    /// <summary>The whole model is built at 2.75 m and shown 1.3x: about 3.6 m
    /// to the pearl, twice a hero's height.</summary>
    private const float Size = 1.3f;

    private readonly System.Collections.Generic.List<MeshInstance3D> _eyes = new();
    private readonly System.Collections.Generic.List<Node3D> _lids = new();
    private readonly System.Collections.Generic.List<(MeshInstance3D Mesh, Color Base)> _body = new();
    private Node3D? _cracks;
    private MeshInstance3D? _ring;
    private StandardMaterial3D? _eyeMat, _ringMat;
    private float _t;

    public override void _Ready()
    {
        Name = "TideTotem";
        _eyeMat = Glow(TotemTint, 3.0f);
        _ringMat = new StandardMaterial3D
        {
            AlbedoColor = TotemTint with { A = 0.35f },
            EmissionEnabled = true,
            Emission = TotemTint,
            EmissionEnergyMultiplier = 1.4f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        // Plinth: a driftwood drum with a ring of pebbles.
        Body(new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.74f, Height = 0.32f, RadialSegments = 14 }, new Vector3(0f, 0.16f, 0f), Driftwood);
        for (int i = 0; i < 7; i++)
        {
            float a = Mathf.Tau * i / 7f + 0.3f;
            Body(new SphereMesh { Radius = 0.13f, Height = 0.18f, RadialSegments = 8, Rings = 4 },
                 new Vector3(MathF.Cos(a) * 0.78f, 0.06f, MathF.Sin(a) * 0.78f), new Color(0.72f, 0.70f, 0.66f));
        }

        // 1. Pufferfish: round, cheeky, spines, little fins and pouty lips.
        Node3D puffer = Tier(0.78f);
        Body(new SphereMesh { Radius = 0.52f, Height = 0.98f, RadialSegments = 16, Rings = 8 }, Vector3.Zero, Puffer, puffer);
        Body(new SphereMesh { Radius = 0.40f, Height = 0.62f, RadialSegments = 12, Rings = 6 }, new Vector3(0f, -0.14f, 0.20f), PufferBelly, puffer);
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.Tau * i / 10f;
            var spine = Body(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.05f, Height = 0.16f, RadialSegments = 5 },
                             new Vector3(MathF.Cos(a) * 0.52f, 0.18f, MathF.Sin(a) * 0.52f), Puffer.Darkened(0.2f), puffer);
            spine.Rotation = new Vector3(MathF.Sin(a) * 1.1f, 0f, -MathF.Cos(a) * 1.1f);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            var fin = Body(new PrismMesh { Size = new Vector3(0.30f, 0.26f, 0.06f) }, new Vector3(side * 0.56f, 0f, 0f), Puffer.Lightened(0.25f), puffer);
            fin.Rotation = new Vector3(0f, 0f, side * -1.57f);
        }
        Body(new TorusMesh { InnerRadius = 0.04f, OuterRadius = 0.10f, Rings = 10, RingSegments = 6 }, new Vector3(0f, -0.06f, 0.50f), new Color(1f, 0.55f, 0.55f), puffer)
            .Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
        Eyes(puffer, new Vector3(0.21f, 0.12f, 0.42f), 0.15f);

        // 2. Clam: two pink shells, ribbed, holding a pearl.
        Node3D clam = Tier(1.52f);
        var lower = Body(new SphereMesh { Radius = 0.46f, Height = 0.46f, IsHemisphere = true, RadialSegments = 16, Rings = 6 }, new Vector3(0f, 0.02f, 0f), Clam, clam);
        lower.Rotation = new Vector3(Mathf.Pi, 0f, 0f);
        Body(new SphereMesh { Radius = 0.46f, Height = 0.40f, IsHemisphere = true, RadialSegments = 16, Rings = 6 }, new Vector3(0f, 0.06f, -0.04f), Clam.Lightened(0.12f), clam)
            .Rotation = new Vector3(-0.22f, 0f, 0f);
        for (int i = -3; i <= 3; i++)
        {
            var rib = Body(new BoxMesh { Size = new Vector3(0.04f, 0.36f, 0.05f) }, new Vector3(i * 0.11f, 0.20f, 0.30f), Clam.Darkened(0.18f), clam);
            rib.Rotation = new Vector3(-0.6f, 0f, i * -0.16f);
        }
        Body(new SphereMesh { Radius = 0.10f, Height = 0.20f, RadialSegments = 10, Rings = 5 }, new Vector3(0f, 0.04f, 0.32f), new Color(0.96f, 0.97f, 1.0f), clam);
        Eyes(clam, new Vector3(0.18f, 0.20f, 0.36f), 0.12f);

        // 3. Conch crown: a spiral cone with a pink lip, a tiny face and a pearl tip.
        Node3D conch = Tier(2.06f);
        Body(new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.36f, Height = 0.70f, RadialSegments = 12 }, new Vector3(0f, 0.30f, 0f), Conch, conch);
        for (int i = 0; i < 3; i++)
            Body(new TorusMesh { InnerRadius = 0.30f - i * 0.09f, OuterRadius = 0.36f - i * 0.09f, Rings = 12, RingSegments = 5 },
                 new Vector3(0f, 0.05f + i * 0.18f, 0f), Conch.Darkened(0.12f), conch);
        Body(new SphereMesh { Radius = 0.22f, Height = 0.30f, RadialSegments = 10, Rings = 5 }, new Vector3(0f, 0.06f, 0.20f), ConchLip, conch);
        Eyes(conch, new Vector3(0.12f, 0.24f, 0.25f), 0.095f);
        var tip = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 8, Rings = 4 }, Position = new Vector3(0f, 0.68f, 0f), MaterialOverride = _eyeMat };
        conch.AddChild(tip);
        _eyes.Add(tip);

        // Cracks, shown once asleep.
        _cracks = new Node3D { Name = "Cracks", Visible = false };
        AddChild(_cracks);
        var crack = Flat(Ink);
        foreach ((Vector3 at, float roll) in new[] { (new Vector3(0.12f, 0.92f, 0.50f), 0.5f), (new Vector3(-0.10f, 1.62f, 0.40f), -0.7f), (new Vector3(0.05f, 2.25f, 0.30f), 0.3f) })
        {
            var c = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.34f, 0.03f) }, Position = at, MaterialOverride = crack, Rotation = new Vector3(0f, 0f, roll) };
            _cracks.AddChild(c);
        }

        // A ring of water on the ground, pulsing slowly.
        _ring = new MeshInstance3D
        {
            Name = "TideRing",
            Mesh = new TorusMesh { InnerRadius = 1.05f, OuterRadius = 1.22f, Rings = 32, RingSegments = 4 },
            Position = new Vector3(0f, 0.04f, 0f),
            Scale = new Vector3(1f, 0.15f, 1f),
            MaterialOverride = _ringMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ring);

        BuildMarker(Height + 0.75f);
        // Taller than everyone, so it reads across a field.
        Scale = Vector3.One * Size;
        Refresh();
        if (Used) Sleep();
    }

    private Node3D Tier(float y)
    {
        var n = new Node3D { Position = new Vector3(0f, y, 0f) };
        AddChild(n);
        return n;
    }

    private MeshInstance3D Body(Mesh mesh, Vector3 at, Color c, Node3D? parent = null)
    {
        MeshInstance3D mi = Part(mesh, at, Flat(c, 0.7f), parent);
        _body.Add((mi, c));
        return mi;
    }

    /// <summary>Two eyes on the front of a face and two on the back: a totem
    /// is walked up to from every side.</summary>
    private void Eyes(Node3D tier, Vector3 front, float r)
    {
        foreach (float face in new[] { 1f, -1f })
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 at = front with { Z = front.Z * face };
            var white = Part(new SphereMesh { Radius = r * 1.25f, Height = r * 2.5f, RadialSegments = 10, Rings = 5 },
                             new Vector3(at.X * side, at.Y, at.Z - r * 0.25f * face), Flat(Ink), tier);
            white.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _lids.Add(white);
            var eye = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 10, Rings = 5 },
                Position = new Vector3(at.X * side, at.Y, at.Z),
                MaterialOverride = _eyeMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            tier.AddChild(eye);
            _eyes.Add(eye);
            // A white sparkle in each eye: cute reads from here.
            var sparkle = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = r * 0.32f, Height = r * 0.64f, RadialSegments = 6, Rings = 3 },
                Position = new Vector3(at.X * side + r * 0.35f * face, at.Y + r * 0.35f, at.Z + r * 0.7f * face),
                MaterialOverride = Glow(Colors.White, 2f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            tier.AddChild(sparkle);
            _lids.Add(sparkle);
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (Used || _eyeMat is null || _ringMat is null || _ring is null) return;
        _t += (float)delta;
        float pulse = 0.5f + 0.5f * MathF.Sin(_t * 2.4f);
        _eyeMat.EmissionEnergyMultiplier = 2.2f + 1.6f * pulse;
        _ringMat.AlbedoColor = TotemTint with { A = 0.18f + 0.22f * pulse };
        float s = 1f + 0.08f * pulse;
        _ring.Scale = new Vector3(s, 0.15f, s);
    }

    /// <summary>Used up: dormant for good (no reset; the next map has new ones).</summary>
    public void MarkUsed()
    {
        if (Used) return;
        Used = true;
        if (IsInsideTree()) Sleep();
    }

    private void Sleep()
    {
        foreach ((MeshInstance3D mesh, Color c) in _body)
            if (IsInstanceValid(mesh)) mesh.MaterialOverride = Flat(c.Lerp(Asleep, 0.65f).Darkened(0.15f), 0.9f);
        // Eyes shut: each one squashed to a sleepy line, sparkles gone.
        var shut = Flat(Ink.Lightened(0.1f), 0.9f);
        foreach (MeshInstance3D eye in _eyes)
            if (IsInstanceValid(eye)) { eye.MaterialOverride = shut; eye.Scale = new Vector3(1.15f, 0.22f, 1f); }
        foreach (Node3D lid in _lids)
            if (IsInstanceValid(lid)) lid.Visible = false;
        if (_cracks is not null) _cracks.Visible = true;
        if (_ring is not null) _ring.Visible = false;
        Refresh();
    }
}
