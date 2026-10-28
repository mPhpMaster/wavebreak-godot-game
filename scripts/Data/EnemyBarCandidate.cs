using Wavebreak.Actors;

namespace Wavebreak.Data;

public readonly record struct EnemyBarCandidate(Enemy Enemy, float DistanceSquared);
