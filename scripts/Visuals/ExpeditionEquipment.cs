using Godot;

namespace Wavebreak.Visuals;

internal static class ExpeditionEquipment
{
    internal static void Add(MeshKit hand, Vector3 grip, float scale, bool fish, int side)
    {
        if (fish)
        {
            // Nori's separate weapon node is held by both hands through rig IK.
            return;
        }
        hand.Box(new Transform3D(Basis.Identity, grip), new Vector3(.045f, .045f, .13f) * scale, new Color(.16f, .2f, .24f));
        hand.Box(new Transform3D(Basis.Identity, grip + Vector3.Forward * (.08f * scale)),
            new Vector3(.16f, .025f, .03f) * scale, new Color(.5f, .6f, .65f));
        hand.Box(new Transform3D(Basis.Identity, grip + Vector3.Forward * (.34f * scale)),
            new Vector3(.045f, .018f, .49f) * scale, new Color(.8f, .9f, .96f));
        hand.Cone(grip + Vector3.Forward * (.57f * scale), grip + Vector3.Forward * (.67f * scale),
            .026f * scale, new Color(.8f, .9f, .96f));
    }
}
