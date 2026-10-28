using Godot;

namespace Wavebreak.Combat.Weapons;

/// <summary>Which sound a signature weapon wants. Published on the bus so
/// combat holds no reference to audio (<c>Audio/AudioDirector.Heroes.cs</c>).</summary>
public enum SignatureSound { SpyglassCast, SpyglassBullseye, SpyglassRicochet, AnchorThrow, AnchorLand, AnchorHaul, KiteWhip, KiteZap }

public readonly record struct SignatureCue(SignatureSound Sound, Vector3 Position);
