using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Stats;

namespace Wavebreak.Game;

/// <summary>
/// Every pot, vase and crate on one map: where they stand, which are broken, and
/// what falls out.
///
/// <b>They are not targets.</b> Pots never enter <see cref="Targeting"/>'s list,
/// so no weapon can aim at one, and a homing shot never wastes itself on a vase
/// while an enemy walks up. Weapons break them incidentally: every area a weapon
/// tests (a shot's contact sphere, a blast, a swing, an aura tick) is reported
/// through <see cref="Targeting.AreaProbe"/>, and pots inside it break. Slams
/// break them through the <see cref="PlayerMoved"/> event, and a dash or slide
/// shoulders through any in its way.
///
/// <b>Cost is per map, not per pot.</b> The drawn pots are three MultiMeshes (one
/// per shape); breaking one collapses its instance to zero scale. Lookup is a
/// fixed grid of 4 m cells with a linked list per cell, so a probe — which runs
/// once per projectile per frame — is a handful of array reads and returns at
/// once when the map has no pots left. No pot has a collider, a node, or a
/// process callback of its own.
///
/// One instance per map, parented to the arena so it leaves with it.
/// </summary>
public sealed partial class Breakables : Node3D
{
    // -------------------------------------------------------------- tuning

    /// <summary>Hit radius added to a probe's own radius.</summary>
    public const float PotRadius = 0.45f;

    /// <summary>A probe further than this above or below a pot misses it, so a
    /// shot crossing a platform does not break pots on the floor beneath.</summary>
    public const float VerticalReach = 2.6f;

    /// <summary>Reach of a dash or slide shouldering through pots.</summary>
    public const float BodyReach = 0.8f;

    /// <summary>Slam radius before area scaling; matches the player's own slam.</summary>
    public const float SlamRadius = 4.2f;

    /// <summary>Drop odds per broken pot. Gold always; xp and heals are bonuses.</summary>
    public const float XpChance = 0.30f;
    public const float HealChance = 0.035f;
    public const float HealFraction = 0.08f;

    private const float CellSize = 4f;

    /// <summary>The map's pots, or null between maps.</summary>
    public static Breakables? Current { get; private set; }

    // --------------------------------------------------------------- state

    private struct Pot
    {
        public Vector3 Position;
        public int Variant;
        public int Instance;
        public bool Broken;
        public int Next;      // next pot in the same grid cell, -1 ends
    }

    private Pot[] _pots = Array.Empty<Pot>();
    private int _alive;
    private int[] _head = Array.Empty<int>();
    private int _gridSide;
    private float _gridOrigin;

    private readonly MultiMeshInstance3D?[] _multis = new MultiMeshInstance3D?[3];
    private readonly List<CpuParticles3D> _bursts = new();
    private int _nextBurst;

    private Rng _rng = new(0xB07UL);
    private int _phase = 1;

    public int Total => _pots.Length;
    public int Alive => _alive;
    public int Broken => _pots.Length - _alive;

    // ----------------------------------------------------------- lifecycle

    public override void _EnterTree()
    {
        Current = this;
        // Installed once; the static forwarder reads whichever map is current.
        Targeting.AreaProbe ??= ProbeCurrent;
        EventBus.Subscribe<PlayerMoved>(OnPlayerMoved);
    }

    public override void _ExitTree()
    {
        EventBus.Unsubscribe<PlayerMoved>(OnPlayerMoved);
        if (Current == this) Current = null;
    }

    /// <summary>
    /// Takes the placed spots and builds the drawn pots. Called once, by
    /// <see cref="Interactables"/>, after placement has found ground for them.
    /// </summary>
    public void Build(List<Vector3> spots, Rng rng, int phase, float arenaRadius)
    {
        Name = "Breakables";
        _rng = rng;
        _phase = Math.Max(1, phase);

        _pots = new Pot[spots.Count];
        _alive = spots.Count;

        // Grid over the whole map square.
        _gridOrigin = -(arenaRadius + 8f);
        _gridSide = Math.Max(1, (int)MathF.Ceiling((arenaRadius + 8f) * 2f / CellSize) + 1);
        _head = new int[_gridSide * _gridSide];
        Array.Fill(_head, -1);

        var transforms = new List<Transform3D>[3] { new(), new(), new() };
        for (int i = 0; i < spots.Count; i++)
        {
            int variant = _rng.RangeInt(0, 3);
            float scale = _rng.Range(0.9f, 1.15f);
            var basis = new Basis(Vector3.Up, _rng.Range(0f, Mathf.Tau)).Scaled(Vector3.One * scale);
            Vector3 local = ToLocal(spots[i]);
            transforms[variant].Add(new Transform3D(basis, local));

            _pots[i] = new Pot
            {
                Position = spots[i],
                Variant = variant,
                Instance = transforms[variant].Count - 1,
                Next = -1,
            };

            int cell = CellIndex(spots[i].X, spots[i].Z);
            if (cell >= 0)
            {
                _pots[i].Next = _head[cell];
                _head[cell] = i;
            }
        }

        for (int v = 0; v < 3; v++)
        {
            if (transforms[v].Count == 0) continue;
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = PotMesh(v),
                InstanceCount = transforms[v].Count,
            };
            for (int i = 0; i < transforms[v].Count; i++) mm.SetInstanceTransform(i, transforms[v][i]);
            var mmi = new MultiMeshInstance3D { Name = $"Pots{v}", Multimesh = mm };
            AddChild(mmi);
            _multis[v] = mmi;
        }

        for (int i = _bursts.Count; i < 6; i++) _bursts.Add(MakeBurst());
    }

    // -------------------------------------------------------------- probes

    private static void ProbeCurrent(Vector3 centre, float radius, Vector3 forward, float cosHalfArc)
    {
        Breakables? b = Current;
        if (b is null || b._alive == 0) return;
        b.Probe(centre, radius, forward, cosHalfArc);
    }

    /// <summary>Breaks every pot inside the area. <paramref name="cosHalfArc"/> of
    /// -1 means a full circle; otherwise only pots within the arc around
    /// <paramref name="forward"/> break (a swing breaks what is in front of it).</summary>
    public int Probe(Vector3 centre, float radius, Vector3 forward = default, float cosHalfArc = -1f)
    {
        if (_alive == 0 || !IsInsideTree()) return 0;

        float reach = radius + PotRadius;
        float reachSq = reach * reach;
        int x0 = CellCoord(centre.X - reach), x1 = CellCoord(centre.X + reach);
        int z0 = CellCoord(centre.Z - reach), z1 = CellCoord(centre.Z + reach);
        if (x1 < 0 || z1 < 0 || x0 >= _gridSide || z0 >= _gridSide) return 0;
        x0 = Math.Max(0, x0); z0 = Math.Max(0, z0);
        x1 = Math.Min(_gridSide - 1, x1); z1 = Math.Min(_gridSide - 1, z1);

        bool arc = cosHalfArc > -0.999f && forward.LengthSquared() > 0.0001f;
        int broke = 0;
        for (int x = x0; x <= x1; x++)
        {
            for (int z = z0; z <= z1; z++)
            {
                for (int i = _head[x * _gridSide + z]; i >= 0; i = _pots[i].Next)
                {
                    ref Pot pot = ref _pots[i];
                    if (pot.Broken) continue;
                    float dx = pot.Position.X - centre.X;
                    float dz = pot.Position.Z - centre.Z;
                    float sq = dx * dx + dz * dz;
                    if (sq > reachSq) continue;
                    if (MathF.Abs(pot.Position.Y + 0.4f - centre.Y) > VerticalReach) continue;
                    if (arc && sq > 0.09f)
                    {
                        float len = MathF.Sqrt(sq);
                        if ((dx * forward.X + dz * forward.Z) / len < cosHalfArc) continue;
                    }
                    Break(i);
                    broke++;
                }
            }
        }
        return broke;
    }

    /// <summary>Nearest unbroken pot within range, for the autopilot and checks.</summary>
    public Vector3? Nearest(Vector3 from, float maxDistance)
    {
        Vector3? best = null;
        float bestSq = maxDistance * maxDistance;
        for (int i = 0; i < _pots.Length; i++)
        {
            if (_pots[i].Broken) continue;
            Vector3 d = _pots[i].Position - from; d.Y = 0f;
            float sq = d.LengthSquared();
            if (sq >= bestSq) continue;
            bestSq = sq;
            best = _pots[i].Position;
        }
        return best;
    }

    private void OnPlayerMoved(PlayerMoved e)
    {
        if (e.Verb != MoveVerb.SlamImpact || _alive == 0) return;
        float area = Player.Instance is { } p && IsInstanceValid(p) ? p.Stats.Get(Stat.AreaSize) : 1f;
        Probe(e.Position, SlamRadius * MathF.Max(0.5f, area));
    }

    public override void _PhysicsProcess(double delta)
    {
        using var __prof = Wavebreak.Game.Prof.Scope("Breakables.phys");
        if (_alive == 0) return;
        if (Player.Instance is not { } p || !IsInstanceValid(p) || !p.IsAlive) return;
        if (p.IsDashing || p.IsSliding) Probe(p.GlobalPosition, BodyReach);
    }

    // --------------------------------------------------------------- break

    private void Break(int index)
    {
        ref Pot pot = ref _pots[index];
        if (pot.Broken) return;
        pot.Broken = true;
        _alive--;

        if (_multis[pot.Variant] is { } mmi && IsInstanceValid(mmi))
            mmi.Multimesh.SetInstanceTransform(pot.Instance,
                new Transform3D(Basis.Identity.Scaled(Vector3.Zero), ToLocal(pot.Position)));

        Burst(pot.Position + Vector3.Up * 0.45f, PotColor(pot.Variant));
        Drop(pot.Position + Vector3.Up * 0.5f);
        Interactables.Instance?.NotePotBroken();
    }

    /// <summary>
    /// Gold always (1-3, +2 per phase after the first), xp sometimes, a heal
    /// rarely. Small on purpose: thirty pots in phase one are worth about one
    /// merchant item, so breaking them is a side errand, not a farm.
    /// </summary>
    private void Drop(Vector3 at)
    {
        // Co-op: pots are each player's own (every machine has its set), so
        // their drops are too; nothing here is sent to the others.
        Pickup.Personal = true;
        try
        {
            int gold = _rng.RangeInt(1, 4) + (_phase - 1) * 2;
            Pickup.Spawn(PickupKind.Gold, at, gold, Launch());

            if (_rng.Chance(XpChance))
                Pickup.Spawn(PickupKind.Xp, at, 3 + 3 * (_phase - 1) + _rng.RangeInt(0, 3), Launch());

            if (_rng.Chance(HealChance))
                Pickup.Spawn(PickupKind.Health, at, HealFraction, Launch());
        }
        finally { Pickup.Personal = false; }
    }

    private Vector3 Launch()
        => _rng.OnUnitCircleXZ() * _rng.Range(0.8f, 2.2f) + Vector3.Up * _rng.Range(2.4f, 3.4f);

    // ------------------------------------------------------------- visuals

    private static readonly Color Clay = new(0.70f, 0.40f, 0.24f);
    private static readonly Color Wood = new(0.52f, 0.36f, 0.20f);
    private static readonly Color Stone = new(0.62f, 0.60f, 0.56f);

    private static Color PotColor(int variant) => variant switch
    {
        0 => Stone,
        1 => Wood,
        _ => Clay,
    };

    private static readonly Mesh?[] Meshes = new Mesh?[3];

    /// <summary>Built once per process and shared by every map.</summary>
    private static Mesh PotMesh(int variant)
    {
        if (Meshes[variant] is { } cached) return cached;
        Mesh mesh = variant switch
        {
            0 => Urn(),
            1 => Crate(),
            _ => Vase(),
        };
        Meshes[variant] = mesh;
        return mesh;
    }

    private static StandardMaterial3D Mat(Color c) => new() { AlbedoColor = c, Roughness = 0.85f };

    /// <summary>The Kenney graveyard urn, scaled to 0.9 m; a squat clay sphere
    /// when the asset is missing.</summary>
    private static Mesh Urn()
    {
        ArrayMesh? kit = Visuals.PropFactory.KitMesh("res://assets/kenney/graveyard-kit/urn-round.glb");
        if (kit is not null)
        {
            Aabb box = kit.GetAabb();
            float h = MathF.Max(0.05f, box.Size.Y);
            float s = 0.9f / h;
            // Re-bake at the target size with the base on y = 0, so the instance
            // transform is only position, yaw and the small random scale.
            var scaled = new ArrayMesh();
            var xf = new Transform3D(Basis.Identity.Scaled(Vector3.One * s), new Vector3(0f, -box.Position.Y * s, 0f));
            for (int surf = 0; surf < kit.GetSurfaceCount(); surf++)
            {
                var st = new SurfaceTool();
                st.AppendFrom(kit, surf, xf);
                st.Commit(scaled);
                scaled.SurfaceSetMaterial(scaled.GetSurfaceCount() - 1, kit.SurfaceGetMaterial(surf));
            }
            if (scaled.GetSurfaceCount() > 0) return scaled;
        }
        return Bake(new SphereMesh { Radius = 0.38f, Height = 0.8f, RadialSegments = 10, Rings = 6, Material = Mat(Stone) },
                    new Vector3(0f, 0.4f, 0f));
    }

    private static Mesh Crate()
        => Bake(new BoxMesh { Size = new Vector3(0.8f, 0.8f, 0.8f), Material = Mat(Wood) }, new Vector3(0f, 0.4f, 0f));

    private static Mesh Vase()
        => Bake(new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.32f, Height = 0.85f, RadialSegments = 10, Material = Mat(Clay) },
                new Vector3(0f, 0.425f, 0f));

    /// <summary>Moves a primitive so its base sits on y = 0.</summary>
    private static Mesh Bake(PrimitiveMesh prim, Vector3 offset)
    {
        var mesh = new ArrayMesh();
        var st = new SurfaceTool();
        st.AppendFrom(prim, 0, new Transform3D(Basis.Identity, offset));
        st.Commit(mesh);
        mesh.SurfaceSetMaterial(0, prim.Material);
        return mesh;
    }

    private CpuParticles3D MakeBurst()
    {
        var p = new CpuParticles3D
        {
            Emitting = false,
            OneShot = true,
            Amount = 10,
            Lifetime = 0.55f,
            Explosiveness = 1f,
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = 2.5f,
            InitialVelocityMax = 5f,
            Gravity = new Vector3(0f, -14f, 0f),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.2f,
            Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.1f, 0.14f) },
        };
        p.MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f };
        AddChild(p);
        return p;
    }

    private void Burst(Vector3 at, Color colour)
    {
        if (_bursts.Count == 0) return;
        CpuParticles3D p = _bursts[_nextBurst];
        _nextBurst = (_nextBurst + 1) % _bursts.Count;
        p.GlobalPosition = at;
        p.Color = colour;
        p.Restart();
        p.Emitting = true;
    }

    // ------------------------------------------------------------- helpers

    private int CellCoord(float v) => (int)MathF.Floor((v - _gridOrigin) / CellSize);

    private int CellIndex(float x, float z)
    {
        int cx = CellCoord(x), cz = CellCoord(z);
        if (cx < 0 || cz < 0 || cx >= _gridSide || cz >= _gridSide) return -1;
        return cx * _gridSide + cz;
    }
}
