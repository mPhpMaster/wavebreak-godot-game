using Godot;

namespace Wavebreak.Data;

public readonly record struct SwimmingIntent(Vector3 Direction, float SpeedScale, bool Reinforced);
