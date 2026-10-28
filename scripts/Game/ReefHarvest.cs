using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Data;
using Wavebreak.Visuals;

namespace Wavebreak.Game;

/// <summary>Deterministic resource positions; only the host releases networked rewards.</summary>
public sealed partial class ReefHarvest : Node3D
{
    public static ReefHarvest? Instance { get; private set; }
    private readonly List<ReefHarvestDef> _defs = new();
    private readonly List<ReefHarvestVisual> _visuals = new();
    private readonly HashSet<int> _depleted = new();
    public int Count => _defs.Count;
    public IReadOnlyList<ReefHarvestDef> Definitions => _defs;
    public int[] SpentIds { get { var ids = new int[_depleted.Count]; _depleted.CopyTo(ids); return ids; } }

    public override void _Ready()
    {
        Instance = this;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Count != 0) return;
        for (int i = 0; i < 28; i++)
        {
            float angle = i * 2.39996f;
            Vector3 at = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (25 + i % 7 * 11);
            at.Y = UnderwaterTerrain.Height(at);
            Add(at);
        }
        foreach (Vector3 ledge in ReefPlacement.Ledges)
        { Add(ledge + new Vector3(3, 0, 2)); Add(ledge + new Vector3(-3, 0, -2)); }
        for (int i = 0; i < 8; i++)
        {
            Vector3 radial = new(Mathf.Cos(0.8f + i * 0.65f), 0, Mathf.Sin(0.8f + i * 0.65f));
            float lo = Arena.Current.InRadius - 30, hi = Arena.Current.InRadius - 6;
            for (int j = 0; j < 20; j++)
            {
                float mid = (lo + hi) * 0.5f;
                if (UnderwaterTerrain.Height(radial * mid) < 32 + i % 3 * 8) lo = mid; else hi = mid;
            }
            Vector3 at = radial * ((lo + hi) * 0.5f); at.Y = UnderwaterTerrain.Height(at);
            float dx = (UnderwaterTerrain.Height(at + Vector3.Right * 0.5f) - UnderwaterTerrain.Height(at - Vector3.Right * 0.5f));
            float dz = (UnderwaterTerrain.Height(at + Vector3.Back * 0.5f) - UnderwaterTerrain.Height(at - Vector3.Back * 0.5f));
            Vector3 normal = new Vector3(-dx, 1, -dz).Normalized();
            var query = PhysicsRayQueryParameters3D.Create(at + normal * 6, at - normal * 6, 1);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.TryGetValue("position", out Variant support))
            { at = support.AsVector3(); normal = ((Vector3)hit["normal"]).Normalized(); }
            Add(at + normal * 0.05f, normal);
        }
        for (int i = 16; i < ReefCavePath.Points.Length - 12; i += 10)
        {
            Vector3 site = ReefCavePath.Points[i];
            site.Y = ReefPlacement.SupportBelow(this, site) + 0.03f;
            Add(site);
        }
        SetPhysicsProcess(false);
    }

    private void Add(Vector3 at, Vector3 normal = default)
    {
        if (normal == Vector3.Zero) normal = Vector3.Up;
        int id = _defs.Count;
        bool crystal = id % 2 == 0;
        PickupKind reward = crystal ? (id % 4 == 0 ? PickupKind.Xp : PickupKind.Gold) : PickupKind.Health;
        var def = new ReefHarvestDef(id, at, normal, crystal, reward, reward == PickupKind.Xp ? 6 : reward == PickupKind.Gold ? 4 : 0.05f);
        _defs.Add(def);
        var visual = new ReefHarvestVisual(def); _visuals.Add(visual); AddChild(visual);
        if (_depleted.Contains(id)) visual.Deplete(false);
    }

    public override void _Process(double delta)
    {
        if (Player.Instance is not { IsAlive: true } player) return;
        int focus = -1; float best = 3.5f * 3.5f;
        for (int i = 0; i < _defs.Count; i++)
        {
            float distance = player.CenterPosition.DistanceSquaredTo(_defs[i].Position + _defs[i].Normal);
            if (!_visuals[i].Spent && distance < best) { best = distance; focus = i; }
        }
        for (int i = 0; i < _visuals.Count; i++) _visuals[i].Focus(i == focus);
        if (focus < 0 || Net.Coop.LocalHold || !Input.IsActionJustPressed("interact")) return;
        if (Net.Coop.InRun) Net.NetSession.Instance?.AskReefHarvest(focus);
        else TryHarvest(focus, player.CenterPosition);
    }

    public bool TryHarvest(int id, Vector3 player)
    {
        if (Net.Coop.IsClient || id < 0 || id >= Count || _visuals[id].Spent
            || player.DistanceSquaredTo(_defs[id].Position + _defs[id].Normal) > 4.5f * 4.5f) return false;
        ReefHarvestDef def = _defs[id];
        ApplySpent(id, true);
        Pickup.Spawn(def.Reward, def.Position + def.Normal * 1.4f + Vector3.Up * 0.3f, def.Value);
        return true;
    }

    public void ApplySpent(int id, bool feedback)
    {
        if (id < 0 || id >= 128) return;
        _depleted.Add(id);
        if (id < Count) _visuals[id].Deplete(feedback);
    }

    public override void _ExitTree() { if (Instance == this) Instance = null; }
}
