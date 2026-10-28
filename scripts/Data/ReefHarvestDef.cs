using Godot;
using Wavebreak.Actors;

namespace Wavebreak.Data;

public sealed record ReefHarvestDef(int Id, Vector3 Position, Vector3 Normal, bool Crystal, PickupKind Reward, float Value, HarvestKind Kind = HarvestKind.Plant);
