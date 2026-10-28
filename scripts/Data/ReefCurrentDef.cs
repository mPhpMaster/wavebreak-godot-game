using Godot;

namespace Wavebreak.Data;

public readonly record struct ReefCurrentDef(Vector2 From, Vector2 To, float Radius, float Speed);
