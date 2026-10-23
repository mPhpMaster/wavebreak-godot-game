using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Progression;

namespace Wavebreak.Actors;

/// <summary>
/// A microwave on a crate. Interact, pick one held item, pay, and it comes back
/// with one more stack — once per microwave. Stack caps are respected: a maxed
/// item cannot be put in.
/// </summary>
public sealed partial class Microwave : Station
{
    public static readonly Color MicrowaveTint = new(0.35f, 0.95f, 0.85f);

    public bool Used { get; set; }

    public override Color Tint => MicrowaveTint;
    public override string Verb => "use microwave";
    public override bool Usable => !Used;
    public override float FootprintRadius => 1.0f;

    private MeshInstance3D? _window;

    public override void _Ready()
    {
        Name = "Microwave";
        Part(new BoxMesh { Size = new Vector3(1.0f, 0.8f, 0.8f) }, new Vector3(0f, 0.4f, 0f), Flat(new Color(0.45f, 0.32f, 0.2f)));
        Part(new BoxMesh { Size = new Vector3(0.95f, 0.55f, 0.65f) }, new Vector3(0f, 1.08f, 0f), Flat(new Color(0.86f, 0.87f, 0.88f), 0.4f));
        _window = Part(new BoxMesh { Size = new Vector3(0.55f, 0.36f, 0.02f) }, new Vector3(-0.12f, 1.08f, 0.33f),
                       Glow(MicrowaveTint.Darkened(0.3f), 0.8f));
        Part(new BoxMesh { Size = new Vector3(0.2f, 0.36f, 0.02f) }, new Vector3(0.33f, 1.08f, 0.33f), Flat(new Color(0.2f, 0.2f, 0.22f)));
        BuildMarker(2.2f);
        Refresh();
    }

    public void MarkUsed()
    {
        Used = true;
        if (_window is not null) _window.MaterialOverride = Flat(new Color(0.1f, 0.1f, 0.12f), 0.3f);
        Refresh();
    }
}
