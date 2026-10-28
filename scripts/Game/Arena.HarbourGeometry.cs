using System.Collections.Generic;
using Godot;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

public sealed partial class Arena
{
    private void House(Vector2 at, float yaw, float len, float depth, float height, int style,
                       bool sunken = false, bool warehouse = false, float baseY = 0f)
    {
        Color wallA = HouseWalls[style % HouseWalls.Length], wallB = HouseWalls[(style + 2) % HouseWalls.Length];
        Color roof = Roofs[style % Roofs.Length];
        if (warehouse) { wallA = new Color(0.62f, 0.50f, 0.42f); wallB = wallA.Darkened(0.08f); roof = new Color(0.36f, 0.40f, 0.46f); }
        if (sunken) { wallA = wallA.Lerp(Moss, 0.45f).Darkened(0.12f); wallB = wallB.Lerp(Moss, 0.5f).Darkened(0.16f); }

        Blocker(at, yaw, len, depth, height + baseY, MapBlockers.Shape.Box, wallA);
        AddBoxCollider(at, yaw, new Vector3(len, height, depth), baseY);

        var x = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
        float half = len * 0.5f;
        float lowH = sunken ? height * 0.78f : warehouse ? height : height - 2.5f;
        // Two halves: the full-height one and a lower neighbour.
        Vector2 c1 = at - x * (half * 0.5f), c2 = at + x * (half * 0.5f);
        _bodies.Add((BoxX(c1, yaw, baseY + height * 0.5f, new Vector3(half, height, depth)), wallA));
        _bodies.Add((BoxX(c2, yaw, baseY + lowH * 0.5f, new Vector3(half, lowH, depth - 0.4f)), wallB));
        if (!sunken)
        {
            float roofH = warehouse ? 2.2f : Mathf.Min(5f, depth * 0.45f);
            _roofs.Add((RoofX(c1, yaw, baseY + height, new Vector3(half + 0.6f, roofH, depth + 0.8f)), roof));
            _roofs.Add((RoofX(c2, yaw, baseY + lowH, new Vector3(half + 0.6f, roofH, depth + 0.4f)), roof.Darkened(0.1f)));
        }
        else
        {
            // Broken tops: a few leaning chunks instead of a roof, and a
            // waterline of weed at the foot.
            _bodies.Add((BoxX(c1 + x * 1.5f, yaw + 0.3f, baseY + height + 0.8f, new Vector3(half * 0.5f, 1.6f, depth * 0.6f)), wallA.Darkened(0.1f)));
            _trims.Add((BoxX(at, yaw, baseY + 0.45f, new Vector3(len + 0.3f, 0.9f, depth + 0.3f)), new Color(0.24f, 0.42f, 0.34f)));
        }

        // Windows: rows on both long faces, warm and lit, every 4 m.
        var z = new Vector2(-Mathf.Sin(yaw), Mathf.Cos(yaw));
        int cols = Mathf.Max(1, (int)(len / 4f));
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 face = at + z * (side * (depth * 0.5f + 0.06f));
            for (int c = 0; c < cols; c++)
            {
                float u = -half + (c + 0.5f) * len / cols;
                float topHere = u < 0f ? height : lowH;
                for (float y = 3.4f; y < topHere - 1.6f; y += 4f)
                {
                    if (sunken && (c + (int)y) % 3 == 0) continue;   // smashed
                    Color glow = sunken ? new Color(0.45f, 0.85f, 0.85f) : WindowWarm;
                    _windows.Add((BoxX(face + x * u, yaw, baseY + y, new Vector3(1.3f, 1.7f, 0.12f)), glow));
                }
            }
        }

        // A door on the long face nearer the square.
        int toward = (at + z).LengthSquared() < (at - z).LengthSquared() ? 1 : -1;
        Vector2 door = at + z * (toward * (depth * 0.5f + 0.1f)) - x * (half * 0.5f);
        _trims.Add((BoxX(door, yaw, baseY + 1.5f, new Vector3(2.0f, 3.0f, 0.2f)), warehouse ? new Color(0.30f, 0.36f, 0.42f) : DoorWood));
    }

    /// <summary>A rock wall from a to b: a box core and big kit rocks along its
    /// crest so the skyline is jagged rather than a slab.</summary>
    private void Cliff(Vector2 a, Vector2 b, float halfWidth, float height)
    {
        Vector2 d = b - a;
        float len = d.Length();
        float yaw = Mathf.Atan2(d.Y, d.X);
        Vector2 at = (a + b) * 0.5f;
        Blocker(at, yaw, len, halfWidth * 2f, height, MapBlockers.Shape.Box, Rock);
        AddBoxCollider(at, yaw, new Vector3(len, height, halfWidth * 2f), 0f);
        _bodies.Add((BoxX(at, yaw, height * 0.5f, new Vector3(len - 0.6f, height, halfWidth * 2f - 0.6f)), Rock));
        _trims.Add((BoxX(at, yaw, 0.6f, new Vector3(len + 0.4f, 1.2f, halfWidth * 2f + 0.6f)), Moss.Darkened(0.2f)));

        Vector2 dir = d / len;
        int n = Mathf.Max(2, (int)(len / 7f));
        for (int i = 0; i < n; i++)
        {
            Vector2 p = a + dir * ((i + 0.5f) * len / n);
            float s = halfWidth * _rng.RandfRange(0.95f, 1.25f);
            float y = height - halfWidth * _rng.RandfRange(0.3f, 0.9f);
            AddRock(new Vector3(p.X, y, p.Y), s, _rng.RandiRange(0, 3));
        }
    }

    /// <summary>A raised, walkable headland: a flat top with 20 degree slopes
    /// all round, like a big mound. Enemies walk up it; nothing blocks.</summary>
    private void Headland(Vector2 at, float top, float h)
    {
        float bottom = top + h / Mathf.Tan(Mathf.DegToRad(20f));
        _footprints.Add((at, bottom + 1f));
        var mesh = new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = h, RadialSegments = 40, Rings = 1 };
        AddSolid(mesh, new Transform3D(Basis.Identity, new Vector3(at.X, h * 0.5f, at.Y)), PropFactory.FloorMaterial(_theme));
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = top - 0.16f, OuterRadius = top + 0.16f, RingSegments = 48 },
            Position = new Vector3(at.X, h + 0.01f, at.Y),
            Scale = new Vector3(1f, 0.3f, 1f),
            MaterialOverride = Flat(_stage.Wall, emission: 0.45f),
        });
        // A grassy cap so the top reads as a place.
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = top - 0.4f, BottomRadius = top - 0.4f, Height = 0.05f, RadialSegments = 40, Rings = 1 },
            Position = new Vector3(at.X, h + 0.03f, at.Y),
            MaterialOverride = Flat(new Color(0.36f, 0.52f, 0.34f)),
        });
    }

    /// <summary>The lighthouse: banded tower, gallery, lit lantern room, cap,
    /// and a slow sweeping beam. Tall enough to be seen over the town, which
    /// is the point: it is the landmark you steer by.</summary>
    private void Lighthouse(Vector2 at, float radius, float height, float baseY)
    {
        Blocker(at, 0f, radius * 2f, radius * 2f, baseY + height, MapBlockers.Shape.Round, new Color(0.95f, 0.95f, 0.92f));
        _body.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = radius, Height = height + 2f },
            Position = new Vector3(at.X, baseY + height * 0.5f, at.Y),
        });
        int bands = 6;
        float bandH = height / bands;
        for (int i = 0; i < bands; i++)
        {
            float r0 = Mathf.Lerp(radius, radius * 0.72f, (float)i / bands);
            float r1 = Mathf.Lerp(radius, radius * 0.72f, (float)(i + 1) / bands);
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = r1, BottomRadius = r0, Height = bandH, RadialSegments = 24, Rings = 1 },
                Position = new Vector3(at.X, baseY + bandH * (i + 0.5f), at.Y),
                MaterialOverride = Flat(i % 2 == 0 ? new Color(0.96f, 0.95f, 0.92f) : new Color(0.90f, 0.30f, 0.30f)),
            });
        }
        float topY = baseY + height;
        float rt = radius * 0.72f;
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = rt + 1.2f, BottomRadius = rt + 1.2f, Height = 0.5f, RadialSegments = 24, Rings = 1 },
            Position = new Vector3(at.X, topY + 0.25f, at.Y),
            MaterialOverride = Flat(new Color(0.25f, 0.30f, 0.38f)),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = rt * 0.8f, BottomRadius = rt * 0.8f, Height = 3f, RadialSegments = 16, Rings = 1 },
            Position = new Vector3(at.X, topY + 2f, at.Y),
            MaterialOverride = Flat(new Color(1f, 0.92f, 0.55f), emission: 2.2f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.2f, BottomRadius = rt + 0.6f, Height = 2.6f, RadialSegments = 16, Rings = 1 },
            Position = new Vector3(at.X, topY + 4.8f, at.Y),
            MaterialOverride = Flat(new Color(0.90f, 0.30f, 0.30f)),
        });
        AddChild(new LighthouseBeam { Position = new Vector3(at.X, topY + 2f, at.Y) });
    }

    private void SeaStack(Vector2 at, float radius, float height)
    {
        Blocker(at, 0f, radius * 2f, radius * 2f, height, MapBlockers.Shape.Round, Rock);
        _body.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = radius, Height = height },
            Position = new Vector3(at.X, height * 0.5f, at.Y),
        });
        _rounds.Add((CylX(at, height * 0.5f, radius, height), Rock.Darkened(0.05f)));
        AddRock(new Vector3(at.X, height - radius * 0.4f, at.Y), radius * 1.15f, _rng.RandiRange(0, 3));
        // A tuft of green and a gull's-eye white cap.
        _rounds.Add((CylX(at, height + 0.2f, radius * 0.7f, 0.4f), new Color(0.40f, 0.58f, 0.40f)));
    }

    private void CoralSpire(Vector2 at, float radius, float height)
    {
        var tint = new Color(0.98f, 0.55f, 0.62f);
        Blocker(at, 0f, radius * 2f, radius * 2f, height, MapBlockers.Shape.Round, tint);
        _body.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = radius, Height = height },
            Position = new Vector3(at.X, height * 0.5f, at.Y),
        });
        // Three stacked tapering stalks with bulbs: a cute coral tower.
        for (int i = 0; i < 3; i++)
        {
            float h = height / 3f;
            float r = radius * (1f - i * 0.22f);
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = r * 0.78f, BottomRadius = r, Height = h, RadialSegments = 12, Rings = 1 },
                Position = new Vector3(at.X, h * (i + 0.5f), at.Y),
                MaterialOverride = Flat(tint.Lerp(new Color(1f, 0.75f, 0.55f), i * 0.3f), emission: 0.15f),
            });
        }
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius * 0.55f, Height = radius * 1.1f, RadialSegments = 12, Rings = 6 },
            Position = new Vector3(at.X, height + radius * 0.3f, at.Y),
            MaterialOverride = Flat(new Color(1f, 0.85f, 0.60f), emission: 0.6f),
        });
    }

    private void Ship(Vector2 at, float yaw, float len, float beam)
    {
        const float hullH = 7f;
        Blocker(at, yaw, len, beam, hullH, MapBlockers.Shape.Box, Hull);
        AddBoxCollider(at, yaw, new Vector3(len, hullH, beam), 0f);
        var x = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
        _bodies.Add((BoxX(at, yaw, hullH * 0.5f, new Vector3(len - 3f, hullH, beam)), Hull));
        // Bow and stern: narrower blocks on the ends.
        _bodies.Add((BoxX(at + x * (len * 0.5f - 1.5f), yaw, hullH * 0.55f, new Vector3(3f, hullH * 0.9f, beam * 0.6f)), Hull.Darkened(0.1f)));
        _bodies.Add((BoxX(at - x * (len * 0.5f - 1.5f), yaw, hullH * 0.5f, new Vector3(3f, hullH, beam * 0.85f)), Hull.Darkened(0.05f)));
        _trims.Add((BoxX(at, yaw, hullH + 0.1f, new Vector3(len - 3.2f, 0.2f, beam - 0.6f)), new Color(0.78f, 0.62f, 0.44f)));
        _trims.Add((BoxX(at, yaw, hullH * 0.62f, new Vector3(len - 2.8f, 0.6f, beam + 0.08f)), new Color(0.92f, 0.92f, 0.88f)));
        // Mast and a furled sail.
        foreach (float u in new[] { -0.2f, 0.25f })
        {
            Vector2 m = at + x * (len * u);
            _rounds.Add((CylX(m, hullH + 7f, 0.35f, 14f), new Color(0.55f, 0.40f, 0.28f)));
            _trims.Add((BoxX(m, yaw + Mathf.Pi / 2f, hullH + 9f, new Vector3(beam + 1f, 0.7f, 0.7f)), new Color(0.95f, 0.92f, 0.84f)));
        }
    }

    private void Crates(Vector2 at, float size, float height)
    {
        Blocker(at, 0.3f, size, size, height, MapBlockers.Shape.Box, new Color(0.70f, 0.52f, 0.34f));
        AddBoxCollider(at, 0.3f, new Vector3(size, height, size), 0f);
        float h = height / 2f;
        _bodies.Add((BoxX(at, 0.3f, h * 0.5f, new Vector3(size, h, size)), new Color(0.70f, 0.52f, 0.34f)));
        _bodies.Add((BoxX(at + new Vector2(0.4f, -0.3f), 0.55f, h * 1.5f, new Vector3(size * 0.8f, h, size * 0.8f)), new Color(0.62f, 0.46f, 0.30f)));
    }

    private void Lamp(Vector2 at)
    {
        _rounds.Add((CylX(at, 2f, 0.12f, 4f), new Color(0.22f, 0.26f, 0.32f)));
        _windows.Add((BoxX(at, 0f, 4.2f, new Vector3(0.55f, 0.6f, 0.55f)), WindowWarm));
    }

    // ============================================================== helpers

}
