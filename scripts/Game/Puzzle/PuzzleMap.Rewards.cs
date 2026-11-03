using System.Collections.Generic;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Core;

namespace Wavebreak.Game.Puzzle;

/// <summary>
/// The reward vault: when the last room is solved the tier is known, each
/// machine rolls its own player's pedestals (<see cref="PuzzleRewards"/>) and
/// stands them in the vault, and the player claims one with interact. Leaving
/// without claiming takes the first; a skip pays its consolation on the way
/// out. Either way <see cref="PuzzleFinished"/> is published once.
/// </summary>
public sealed partial class PuzzleMap
{
    private List<PuzzleRewardOffer>? _offers;
    private readonly List<Node3D> _pedestals = new();
    private bool _claimed;
    private bool _announced;

    /// <summary>This player's prizes, once the vault has opened.</summary>
    public IReadOnlyList<PuzzleRewardOffer>? Offers => _offers;
    public bool Claimed => _claimed;

    private void SyncRewards(float dt)
    {
        foreach (Node3D p in _pedestals)
            if (IsInstanceValid(p) && p.GetChildCount() > 1 && p.GetChild(1) is Node3D prize) prize.RotateY(dt * 1.2f);
        if (!State.Finished || _offers is not null) return;
        RewardTier tier = State.Tier;
        if (Plan.Gauntlet)
        {
            // The Puzzle Gauntlet has no fight to equip for: the medal is the prize.
            _offers = new List<PuzzleRewardOffer>();
            AudioDirector.PlayPuzzle(PuzzleSound.Reward);
            Announce(tier);
            return;
        }
        var rng = new Rng(Plan.Seed ^ 0x9E3E_A4D5UL ^ (ulong)(Net.Coop.InRun ? Net.NetSession.Instance?.LocalSlot ?? 0 : 0) * 0x2545F491UL);
        _offers = PuzzleRewards.Roll(tier, rng);
        PuzzleRewards.GrantBonus(tier);
        BuildPedestals();
        AudioDirector.PlayPuzzle(PuzzleSound.Reward);
        Announce(tier);
    }

    private void Announce(RewardTier tier)
    {
        if (_announced) return;
        _announced = true;
        GD.Print($"[puzzle] finished tier={tier} t={State.SolveSeconds:0.0}s hints={State.Hints} offers={_offers?.Count ?? 0}");
        EventBus.Publish(new PuzzleFinished((int)tier, State.SolveSeconds, State.Hints, tier == RewardTier.Skipped));
    }

    private void BuildPedestals()
    {
        if (_offers is null) return;
        RoomPlan vault = Plan.Vault;
        int s = vault.EntrySide;
        var toEntry = new Vector3(RoomPlan.Dx[s], 0f, RoomPlan.Dz[s]);
        var across = new Vector3(Mathf.Abs(RoomPlan.Dz[s]), 0f, Mathf.Abs(RoomPlan.Dx[s]));
        for (int i = 0; i < _offers.Count; i++)
        {
            float x = (i - (_offers.Count - 1) * 0.5f) * 4.2f;
            Vector3 at = vault.Centre + toEntry * 1.5f + across * x;
            var root = new Node3D { Name = $"Pedestal{i}", Position = at };
            _roomRoots[^1].AddChild(root);
            root.AddChild(Mesh(new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.85f, Height = 1.0f, RadialSegments = 12 }, Vector3.Up * 0.5f, Flat(WallCap)));
            PuzzleRewardOffer offer = _offers[i];
            Node3D prize = Prize(offer);
            prize.Position = Vector3.Up * 1.7f;
            root.AddChild(prize);
            root.AddChild(new Label3D
            {
                Text = Meta.Loc.T(offer.Title) + "\n" + Meta.Loc.T(offer.Detail),
                FontSize = 36,
                PixelSize = 0.01f,
                OutlineSize = 10,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = Vector3.Up * 3.0f,
                Modulate = new Color(1f, 0.97f, 0.9f),
                OutlineModulate = new Color(0.12f, 0.12f, 0.18f),
            });
            root.AddChild(new OmniLight3D { Position = Vector3.Up * 2f, LightColor = offer.Tint, LightEnergy = 1.2f, OmniRange = 4f, ShadowEnabled = false });
            _pedestals.Add(root);
        }
    }

    /// <summary>A shape per kind of prize: a chest, a crystal, a blade, a ring of arrows.</summary>
    private Node3D Prize(PuzzleRewardOffer offer)
    {
        var n = new Node3D();
        var glow = Glow(offer.Tint, 1.8f);
        switch (offer.Kind)
        {
            case PuzzleRewardKind.EpicChest:
            case PuzzleRewardKind.RareChest:
            case PuzzleRewardKind.Chest:
                n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.9f, 0.55f, 0.6f) }, Vector3.Zero, Flat(Bronze)));
                n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.94f, 0.12f, 0.64f) }, Vector3.Up * 0.12f, glow));
                var lid = Mesh(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 0.9f, RadialSegments = 10 }, Vector3.Up * 0.3f, Flat(BronzeDark));
                lid.Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f);
                n.AddChild(lid);
                break;
            case PuzzleRewardKind.Catalyst:
                n.AddChild(Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.4f, Height = 0.6f, RadialSegments = 6 }, Vector3.Up * 0.3f, glow));
                n.AddChild(Mesh(new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0f, Height = 0.6f, RadialSegments = 6 }, Vector3.Down * 0.3f, glow));
                break;
            case PuzzleRewardKind.WeaponPick:
                n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.14f, 1.1f, 0.05f) }, Vector3.Up * 0.25f, Flat(new Color(0.9f, 0.94f, 1f))));
                n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.6f, 0.1f, 0.12f) }, Vector3.Down * 0.3f, glow));
                n.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.12f, 0.35f, 0.12f) }, Vector3.Down * 0.5f, Flat(BronzeDark)));
                break;
            default:
                var ring = Mesh(new TorusMesh { InnerRadius = 0.3f, OuterRadius = 0.44f, Rings = 20, RingSegments = 6 }, Vector3.Zero, glow);
                ring.Rotation = new Vector3(Mathf.Pi * 0.5f, 0f, 0f);
                n.AddChild(ring);
                var head = Mesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.18f, Height = 0.32f, RadialSegments = 3 }, new Vector3(0.38f, 0.1f, 0f), glow);
                n.AddChild(head);
                break;
        }
        return n;
    }

    /// <summary>Where pedestal <paramref name="i"/> stands (the autopilot).</summary>
    public Vector3? PedestalAt(int i) => i >= 0 && i < _pedestals.Count ? _pedestals[i].GlobalPosition : null;

    /// <summary>Take prize <paramref name="i"/>: granted to this player, the
    /// other pedestals go dark.</summary>
    public void Claim(int i)
    {
        if (_claimed || _offers is null || i < 0 || i >= _offers.Count) return;
        _claimed = true;
        string said = PuzzleRewards.Grant(_offers[i]);
        Vector3 at = i < _pedestals.Count ? _pedestals[i].GlobalPosition : Plan.Vault.Centre;
        EventBus.Publish(new PopupRequested(at + Vector3.Up * 3.6f, Meta.Loc.T(said), _offers[i].Tint, 1.7f));
        AudioDirector.PlayPuzzle(PuzzleSound.Solved);
        for (int k = 0; k < _pedestals.Count; k++)
            if (k != i && IsInstanceValid(_pedestals[k])) _pedestals[k].QueueFree();
    }

    /// <summary>On leaving the map (every machine): an unclaimed prize is
    /// taken for you, and a skip pays its consolation.</summary>
    public void GrantOnLeave()
    {
        if (Plan.Gauntlet)
        {
            if (State.Finished) SyncRewards(0f);
            else if (!_announced) Announce(RewardTier.Skipped);
            return;
        }
        if (State.Finished)
        {
            SyncRewards(0f);
            if (!_claimed && _offers is { Count: > 0 }) Claim(0);
            return;
        }
        if (_announced) return;
        PuzzleRewards.GrantBonus(RewardTier.Skipped);
        Announce(RewardTier.Skipped);
    }
}
