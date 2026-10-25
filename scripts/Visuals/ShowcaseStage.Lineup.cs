using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Visuals;

/// <summary>
/// <c>--art-showcase=lineup[:id,id,...]</c>: characters side by side in their
/// base looks, faced to camera at the same scale (default: the heroes trio
/// beside the five cube-pet crew, to prove the trio do not look like them).
/// <c>--art-showcase=poses:&lt;characterId&gt;</c>: one character in each verb
/// the rig animates (idle, walk, run, jump, dash, slide, attack windup), so a
/// hand-built body can be checked pose by pose in one frame.
/// </summary>
public sealed partial class ShowcaseStage
{
    private void Lineup(PropTheme t, string what)
    {
        string list = what.Contains(':') ? what[(what.IndexOf(':') + 1)..] : "sunny,biscuit,flick,pip,lumi,tilly,pebble,rosie";
        string[] ids = list.Split(',', System.StringSplitOptions.RemoveEmptyEntries);
        Floor(t, 30f);
        const float dx = 1.7f;
        for (int i = 0; i < ids.Length; i++)
        {
            CharacterDef c = Characters.ById(ids[i]) ?? Characters.Default;
            float x = (i - (ids.Length - 1) * 0.5f) * dx;
            Actor(PlayerLooks.Get(c.LookId), new Vector3(x, 0f, 0f), false, Mathf.Pi + 0.3f, Vector3.Zero);
            Label(c.Id, new Vector3(x, 2.05f, 0f), 0.3f);
        }
        Camera(new Vector3(0f, 1.5f, 1.2f + ids.Length * 1.05f), new Vector3(0f, 0.7f, 0f), 45f);
    }

    private void Poses(PropTheme t, string what)
    {
        CharacterDef c = Characters.ById(what["poses:".Length..]) ?? Characters.Default;
        ModelSpec spec = PlayerLooks.Get(c.LookId);
        Floor(t, 30f);
        (string name, Vector3 v, RigPose pose)[] poses =
        {
            ("idle", Vector3.Zero, RigPose.Locomotion),
            ("walk", new Vector3(0f, 0f, 2.2f), RigPose.Locomotion),
            ("run", new Vector3(0f, 0f, 7f), RigPose.Locomotion),
            ("jump", new Vector3(0f, 4f, 3f), RigPose.Air),
            ("dash", new Vector3(0f, 0f, 14f), RigPose.Dash),
            ("slide", new Vector3(0f, 0f, 8f), RigPose.Slide),
            ("windup", Vector3.Zero, RigPose.Locomotion),
        };
        const float dx = 1.6f;
        for (int i = 0; i < poses.Length; i++)
        {
            float x = (i - (poses.Length - 1) * 0.5f) * dx;
            // Side-on (facing +X... the camera's right), so limbs read in profile.
            CharacterRig r = Actor(spec, new Vector3(x, poses[i].pose == RigPose.Air ? 0.5f : 0f, 0f), false,
                                   -Mathf.Pi * 0.5f + 0.35f, poses[i].v, poses[i].pose);
            if (poses[i].name == "windup") _strikers.Add(r);
            Label(poses[i].name, new Vector3(x, 2.1f, 0f), 0.3f);
        }
        Label(c.DisplayName, new Vector3(0f, 2.6f, 0f), 0.5f);
        Camera(new Vector3(0f, 1.8f, 7.6f), new Vector3(0f, 0.8f, 0f), 50f);
    }
}
