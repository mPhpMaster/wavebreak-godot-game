using Godot;
using Wavebreak.Combat;

namespace Wavebreak.Net;

/// <summary>A teammate with evolved weapons wears a gold ring at their feet that
/// widens with each one (Net/NetSession.Evolutions.cs).</summary>
public sealed partial class RemotePlayer
{
    private MeshInstance3D? _evolvedRing;

    public int EvolvedShown { get; private set; }

    internal void ShowEvolved(int count)
    {
        if (count == EvolvedShown) return;
        EvolvedShown = count;
        if (_evolvedRing is null)
        {
            _evolvedRing = new MeshInstance3D
            {
                Name = "EvolvedRing",
                Mesh = CombatVisuals.Disc,
                MaterialOverride = CombatVisuals.Glow(new Color(1f, 0.82f, 0.28f, 0.28f), additive: true),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0f, 0.06f, 0f),
            };
            AddChild(_evolvedRing);
        }
        float r = 0.8f + 0.15f * count;
        _evolvedRing.Scale = new Vector3(r, 1f, r);
        _evolvedRing.Visible = count > 0;
    }
}
