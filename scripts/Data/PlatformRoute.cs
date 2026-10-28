using Godot;

namespace Wavebreak.Data;

public readonly record struct PlatformRoute(Vector3 Centre, Vector3 RampSide, float HalfSize, float Height, float RampLength);
