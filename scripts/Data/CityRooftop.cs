using Godot;

namespace Wavebreak.Data;

public readonly record struct CityRooftop(StaticBody3D Body, Vector3 Center, Vector2 Half, float Height, float RoofRise = 0);

