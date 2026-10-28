using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Core;
using Wavebreak.Meta;

namespace Wavebreak.Game;

/// <summary>Reachable cave passages and wreck, with host-owned rewards and guards.</summary>
public sealed partial class ReefLandmarks : Node3D
{
    private readonly Vector3[] _caves = new Vector3[ReefSites.Caves.Length];
    private readonly bool[] _caveRewarded = new bool[ReefSites.Caves.Length];
    private readonly bool[] _caveGuarded = new bool[ReefSites.Caves.Length];
    private Vector3 _wreck;
    private StaticBody3D _solids = null!;
    private bool _chestPlaced, _wreckGuarded;
    private readonly bool[] _treasures = new bool[3];
    private float _elapsed;
    public int CaveRewards { get; private set; }
    public int GuardsSpawned { get; private set; }
    public System.Collections.Generic.IReadOnlyList<Vector3> CaveCentres => _caves;
    public bool WreckChestPlaced => _chestPlaced;
    public Vector3 WreckRewardPosition => _wreck + new Vector3(0f, 0.15f, -10f);

    public override void _Ready()
    {
        Name = "ReefExploration";
        _solids = new StaticBody3D { Name = "ExplorationCollision", CollisionLayer = 1, CollisionMask = 1 };
        AddChild(_solids);
        ReefCaveGeometry.Build(this);
        _caves[0] = ReefCavePath.Points[48];
        Label("Lumen Caves", ReefCavePath.Entrance + Vector3.Up * 8f);
        _wreck = BuildWreck(ReefSites.Wreck);
    }

    public override void _PhysicsProcess(double delta)
    {
        _elapsed += (float)delta;
        if (Net.Coop.IsClient || _elapsed < 0.4f || Player.Instance is not { } player) return;
        if (!_chestPlaced && ChestSpawner.Instance is { } chests)
            _chestPlaced = chests.PlaceLandmarkChest(WreckRewardPosition, ChestTier.Gilded);
        if (ChestSpawner.Instance is { } treasure)
            for (int i = 0; i < _treasures.Length; i++)
                if (!_treasures[i])
                {
                    Vector3 site = i == 0 ? ReefCavePath.Points[70] + Vector3.Down * 5.5f
                        : Visuals.ReefPlacement.Ledges[i == 1 ? 1 : 3];
                    if (i == 0) site.Y = Visuals.ReefPlacement.SupportBelow(this, ReefCavePath.Points[70]) + 0.03f;
                    _treasures[i] = treasure.PlaceLandmarkChest(site, ChestTier.Gilded);
                }
        Vector3 at = player.CenterPosition;
        for (int i = 0; i < _caves.Length; i++)
        {
            if (!_caveGuarded[i] && at.DistanceSquaredTo(_caves[i]) < 30f * 30f)
            {
                bool first = SpawnGuard("brine_leech", ReefCavePath.Points[38]);
                bool second = SpawnGuard("silt_eel", ReefCavePath.Points[62]);
                _caveGuarded[i] = first || second;
            }
            if (!_caveRewarded[i] && at.DistanceSquaredTo(_caves[i] + Vector3.Up * 1.5f) < 4f * 4f)
            {
                for (int coin = 0; coin < 3; coin++)
                    Pickup.Spawn(PickupKind.Gold, _caves[i] + new Vector3((coin - 1) * 0.5f, 1f, 0f), 4f);
                _caveRewarded[i] = true;
                CaveRewards++;
                if (Net.Coop.IsHost) Net.NetSession.Instance?.SendReefCaveClaim();
                GD.Print($"REEF cave treasure claimed site={i} gold=12");
            }
        }
        if (!_wreckGuarded && at.DistanceSquaredTo(_wreck) < 38f * 38f)
        {
            _wreckGuarded = SpawnGuard("barnacle_hulk", _wreck + new Vector3(5f, 1f, -9f));
        }
    }

    private bool SpawnGuard(string id, Vector3 at)
    {
        if (Catalog.EnemyById(id) is not { } def || EnemyManager.Instance?.SpawnAt(def, at) is null) return false;
        GuardsSpawned++;
        return true;
    }

    public void ApplyCaveClaim()
    {
        _caveRewarded[0] = true;
        CaveRewards = 1;
    }

    private void Solid(Mesh mesh, Transform3D transform, Material material)
    {
        AddChild(new MeshInstance3D { Mesh = mesh, Transform = transform, MaterialOverride = material });
        _solids.AddChild(new CollisionShape3D { Shape = mesh.CreateConvexShape(), Transform = transform });
    }

    private void Label(string title, Vector3 at)
    {
        AddChild(new Label3D { Text = Loc.T(title), Position = at, FontSize = 48, PixelSize = 0.012f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = new Color(0.4f, 0.95f, 1f),
            OutlineSize = 12, NoDepthTest = false });
    }
}
