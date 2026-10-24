using System;
using Godot;

namespace Wavebreak.Actors;

/// <summary>
/// The rest room's campfire: a ring of stones, crossed logs and a bobbing,
/// flickering flame. Interact to rest, which restores health to full. It never
/// runs out — the room is safe, so resting twice is the same as resting once —
/// and <see cref="Game.Interactables"/> does the healing like any other station.
/// </summary>
public sealed partial class Campfire : Station
{
    public static readonly Color FireTint = new(1.0f, 0.62f, 0.25f);

    public override Color Tint => FireTint;
    public override string Verb => "rest by the campfire";
    public override float FootprintRadius => 1.1f;

    /// <summary>Times anyone rested here (each machine counts its own player).</summary>
    public int Rests { get; set; }

    private Node3D _flame = null!;
    private OmniLight3D _glow = null!;
    private float _t;

    public override void _Ready()
    {
        Name = "Campfire";
        var stone = Flat(new Color(0.62f, 0.60f, 0.58f));
        var stoneDark = Flat(new Color(0.48f, 0.46f, 0.47f));
        var log = Flat(new Color(0.50f, 0.32f, 0.19f));
        var logEnd = Flat(new Color(0.86f, 0.70f, 0.48f));

        // Ring of round stones.
        for (int i = 0; i < 9; i++)
        {
            float a = Mathf.Tau * i / 9f;
            Part(new SphereMesh { Radius = 0.22f, Height = 0.3f, RadialSegments = 8, Rings = 4 },
                 new Vector3(MathF.Cos(a) * 0.75f, 0.12f, MathF.Sin(a) * 0.75f), i % 2 == 0 ? stone : stoneDark);
        }

        // Three logs leaned into a little teepee.
        for (int i = 0; i < 3; i++)
        {
            float a = Mathf.Tau * i / 3f;
            var mi = Part(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.1f, Height = 0.9f, RadialSegments = 7 },
                          new Vector3(MathF.Cos(a) * 0.22f, 0.32f, MathF.Sin(a) * 0.22f), log);
            mi.LookAtFromPosition(mi.Position, new Vector3(0f, 0.75f, 0f), Vector3.Up);
            mi.RotateObjectLocal(Vector3.Right, Mathf.Pi * 0.5f);
        }
        // Two flat logs to sit on beside the fire.
        foreach (float side in new[] { -1f, 1f })
        {
            var seat = Part(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.18f, Height = 1.3f, RadialSegments = 10 },
                            new Vector3(side * 1.55f, 0.18f, 0.2f), log);
            seat.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
            foreach (float end in new[] { -0.655f, 0.655f })
                Part(new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.02f, RadialSegments = 10 },
                     new Vector3(0f, end, 0f), logEnd, seat);
        }

        // The flame: three stacked glowing cones in warm colours.
        _flame = new Node3D { Name = "Flame", Position = new Vector3(0f, 0.25f, 0f) };
        AddChild(_flame);
        Part(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.36f, Height = 0.8f, RadialSegments = 10 },
             new Vector3(0f, 0.4f, 0f), Glow(new Color(1f, 0.42f, 0.15f), 2.2f), _flame);
        Part(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.24f, Height = 0.62f, RadialSegments = 10 },
             new Vector3(0f, 0.38f, 0f), Glow(new Color(1f, 0.72f, 0.25f), 2.6f), _flame);
        Part(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.12f, Height = 0.4f, RadialSegments = 8 },
             new Vector3(0f, 0.3f, 0f), Glow(new Color(1f, 0.95f, 0.65f), 3f), _flame);

        _glow = new OmniLight3D
        {
            Position = new Vector3(0f, 1.1f, 0f),
            LightColor = FireTint,
            LightEnergy = 2.2f,
            OmniRange = 7f,
            ShadowEnabled = false,
        };
        AddChild(_glow);

        BuildMarker(2.4f);
        Refresh();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        _t += (float)delta;
        // Flicker: two sines out of step read as fire, one reads as a pulse.
        float f = 0.5f + 0.25f * MathF.Sin(_t * 9.1f) + 0.25f * MathF.Sin(_t * 13.7f + 1.3f);
        _flame.Scale = new Vector3(1f + f * 0.08f, 0.9f + f * 0.25f, 1f + f * 0.08f);
        _flame.Rotation = new Vector3(0f, _t * 1.4f, 0f);
        _glow.LightEnergy = 1.8f + f * 0.9f;
    }
}
