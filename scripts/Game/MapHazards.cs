using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Stats;

namespace Wavebreak.Game;

/// <summary>
/// A map's floor hazard: flat pools that slow the player (tide pools) and may
/// also gnaw at health (sunken graves). Built by <see cref="Arena"/> from the
/// stage's Hazard* fields and parented to it, so it leaves with the map.
///
/// No physics: pools are circles on a flat floor, and one distance test per
/// pool per physics frame is cheaper and more exact than an Area3D each. The
/// slow is a stat modifier tagged with this node, so leaving a pool, the map
/// changing, or the node being freed always removes exactly what was added.
/// </summary>
public sealed partial class MapHazards : Node3D
{
    /// <summary>Damage is applied in pulses rather than per frame, so it shows
    /// as a readable number and does not reset invulnerability every frame.</summary>
    private const float PulseSeconds = 0.5f;

    private readonly StageDef _stage;
    private readonly List<Vector3> _pools;
    private Player? _slowed;
    private float _pulse;

    /// <summary>Whether the player is standing in a pool right now (for HUD/tests).</summary>
    public static bool PlayerInside { get; private set; }

    public MapHazards(StageDef stage, List<Vector3> pools)
    {
        _stage = stage;
        _pools = pools;
    }

    public override void _Ready()
    {
        Name = "Hazards";
        if (_pools.Count == 0) return;

        Color c = _stage.HazardColor;
        var surface = new StandardMaterial3D
        {
            AlbedoColor = new Color(c.R, c.G, c.B, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.15f,
            Metallic = 0.2f,
            EmissionEnabled = true,
            Emission = c,
            EmissionEnergyMultiplier = 0.45f,
        };
        var rim = Arena.Flat(c.Lightened(0.25f), emission: 0.9f);
        float r = _stage.HazardRadius;
        var disc = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.06f, RadialSegments = 32, Rings = 1 };
        var ring = new TorusMesh { InnerRadius = r - 0.12f, OuterRadius = r + 0.12f, RingSegments = 40 };

        foreach (Vector3 at in _pools)
        {
            AddChild(new MeshInstance3D { Mesh = disc, Position = at + Vector3.Up * 0.03f, MaterialOverride = surface,
                                          CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            AddChild(new MeshInstance3D { Mesh = ring, Position = at + Vector3.Up * 0.05f, Scale = new Vector3(1f, 0.25f, 1f),
                                          MaterialOverride = rim, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("MapHazards.phys");
        Player? p = Player.Instance;
        bool inside = p is not null && IsInstanceValid(p) && p.Health.IsAlive && Inside(p.GlobalPosition);
        PlayerInside = inside;

        if (inside && _slowed is null)
        {
            _slowed = p;
            if (_stage.HazardSlow < 1f) p!.Stats.Add(Stat.MoveSpeed, ModifierOp.Multiplicative, _stage.HazardSlow, this);
            _pulse = PulseSeconds;
        }
        else if (!inside && _slowed is not null) ClearSlow();

        if (!inside || _stage.HazardDamage <= 0f) return;
        _pulse -= (float)delta;
        if (_pulse > 0f) return;
        _pulse += PulseSeconds;
        p!.Health.TakeDamage(new DamageInfo
        {
            Amount = p.Health.Max * _stage.HazardDamage * PulseSeconds,
            CanCrit = false,
            Source = _stage.HazardName,
        });
    }

    private bool Inside(Vector3 at)
    {
        // Only near the floor: jumping over a pool is how you cross it dry.
        if (at.Y > 1.2f) return false;
        float r2 = _stage.HazardRadius * _stage.HazardRadius;
        foreach (Vector3 pool in _pools)
        {
            float dx = at.X - pool.X, dz = at.Z - pool.Z;
            if (dx * dx + dz * dz < r2) return true;
        }
        return false;
    }

    private void ClearSlow()
    {
        if (_slowed is not null && IsInstanceValid(_slowed)) _slowed.Stats.RemoveAllFrom(this);
        _slowed = null;
    }

    public override void _ExitTree()
    {
        ClearSlow();
        PlayerInside = false;
    }
}
