using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Data;
using Wavebreak.Progression;

namespace Wavebreak.Actors;

/// <summary>
/// A thing in the world the player walks up to and presses interact at: the
/// shady merchant's stall, the microwave. Like <see cref="Chest"/> it is
/// passive — <see cref="Game.Interactables"/> decides reach, price and what
/// happens — and like a chest it has no collider (found by distance), a bobbing
/// emissive marker for the long-range tell, and a small light.
/// </summary>
public partial class Station : Node3D
{
    public const float InteractRadius = 2.7f;
    public const float InteractHeight = 1.8f;

    /// <summary>Footprint half-extent, for placement.</summary>
    public virtual float FootprintRadius => 1.4f;

    protected Node3D Marker = null!;
    protected OmniLight3D Light = null!;
    private float _phase;
    private float _markerHeight;

    /// <summary>Where prompts and popups are anchored.</summary>
    public Vector3 Top => GlobalPosition + Vector3.Up * 2.3f;

    /// <summary>False once a station has nothing left to give (microwave used,
    /// merchant sold out). The marker and light go out with it.</summary>
    public virtual bool Usable => true;

    public virtual Color Tint => Colors.White;

    public virtual string Verb => "use";

    public bool InReach(Vector3 at)
    {
        Vector3 d = GlobalPosition - at;
        float dy = MathF.Abs(d.Y);
        d.Y = 0f;
        return d.LengthSquared() <= InteractRadius * InteractRadius && dy <= InteractHeight;
    }

    protected void BuildMarker(float height)
    {
        _markerHeight = height;
        Marker = new Node3D { Name = "Marker", Position = new Vector3(0f, height, 0f) };
        AddChild(Marker);
        Marker.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.26f, Height = 0.7f, RadialSegments = 4, Rings = 2 },
            MaterialOverride = Glow(Tint, 2.4f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        Light = new OmniLight3D
        {
            Position = new Vector3(0f, 1.4f, 0f),
            LightColor = Tint,
            LightEnergy = 1.1f,
            OmniRange = 5f,
            ShadowEnabled = false,
        };
        AddChild(Light);
    }

    /// <summary>Called when <see cref="Usable"/> may have changed.</summary>
    public void Refresh()
    {
        if (Marker is not null) Marker.Visible = Usable;
        if (Light is not null) Light.LightEnergy = Usable ? 1.1f : 0.15f;
    }

    public override void _Process(double delta)
    {
        if (Marker is null || !Marker.Visible) return;
        _phase += (float)delta;
        Marker.Position = new Vector3(0f, _markerHeight + MathF.Sin(_phase * 2.2f) * 0.12f, 0f);
        Marker.RotateY((float)delta * 1.6f);
    }

    // ------------------------------------------------------------ building

    protected MeshInstance3D Part(Mesh mesh, Vector3 at, Material mat, Node3D? parent = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = mat };
        (parent ?? this).AddChild(mi);
        return mi;
    }

    protected static StandardMaterial3D Flat(Color c, float rough = 0.8f) => new() { AlbedoColor = c, Roughness = rough };

    protected static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    /// <summary>Instantiates an imported model scaled to <paramref name="height"/>
    /// metres with its feet on y = 0, playing an idle clip when it has one.
    /// Null when the file is missing.</summary>
    protected Node3D? Model(string path, float height, Vector3 at, float yaw)
    {
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene) return null;
        if (scene.Instantiate() is not Node3D root) return null;

        Aabb? box = null;
        void Walk(Node n, Transform3D xf)
        {
            Transform3D here = n is Node3D n3 ? xf * n3.Transform : xf;
            if (n is MeshInstance3D { Mesh: not null } mi)
            {
                Aabb b = here * mi.GetAabb();
                box = box is { } acc ? acc.Merge(b) : b;
            }
            foreach (Node c in n.GetChildren()) Walk(c, here);
        }
        foreach (Node c in root.GetChildren()) Walk(c, Transform3D.Identity);

        float h = box is { } bb ? MathF.Max(0.05f, bb.Size.Y) : 1f;
        float s = height / h;
        float floor = box is { } fb ? fb.Position.Y * s : 0f;
        root.Scale = Vector3.One * s;
        root.Position = at - new Vector3(0f, floor, 0f);
        root.Rotation = new Vector3(0f, yaw, 0f);
        AddChild(root);

        AnimationPlayer? anim = FindAnim(root);
        if (anim is not null)
            foreach (string clip in anim.GetAnimationList())
                if (clip.Contains("idle", StringComparison.OrdinalIgnoreCase))
                {
                    anim.Play(clip);
                    break;
                }
        return root;
    }

    private static AnimationPlayer? FindAnim(Node n)
    {
        if (n is AnimationPlayer a) return a;
        foreach (Node c in n.GetChildren())
            if (FindAnim(c) is { } found) return found;
        return null;
    }
}
