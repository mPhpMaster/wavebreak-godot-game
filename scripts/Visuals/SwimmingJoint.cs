using Godot;

namespace Wavebreak.Visuals;

internal readonly record struct SwimmingJoint(Skeleton3D Skeleton, int Bone, PartRole Role);
