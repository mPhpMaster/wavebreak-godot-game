using System.Linq;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Visuals;

/// <summary>
/// --art-showcase=cute: the cute wave up close, two rows of four plus the
/// mimic's pot disguise (eyes peeking).
/// --art-showcase=cute-&lt;id&gt;: one of them as a portrait, three-quarter on;
/// the mimic's portrait stands its disguise beside it.
/// </summary>
public sealed partial class ShowcaseStage
{
    private void Cute(PropTheme t, string what)
    {
        BehaviourModels.EnsureRegistered();
        Floor(t, 24f);
        string[] ids = Catalog.CuteEnemyIds;
        string? single = what.StartsWith("cute-") ? what["cute-".Length..] : null;

        if (single is not null && Catalog.EnemyById(single) is { } one)
        {
            CuteActor(one, new Vector3(single == "crock_mimic" ? -0.7f : 0f, 0f, 0f), 0.45f, walking: false);
            if (single == "crock_mimic")
            {
                Node3D pot = CuteModels.MimicDisguise(one.Height, out Node3D eyes);
                eyes.Visible = true;
                pot.Position = new Vector3(0.9f, 0f, 0.2f);
                pot.Rotation = new Vector3(0f, 0.3f, 0f);
                AddChild(pot);
            }
            float lift = one.Hover * 0.7f;
            float h = Mathf.Max(0.8f, one.Height);
            Camera(new Vector3(0f, lift + h * 0.95f + 0.4f, h * 2.1f + 0.9f), new Vector3(0f, lift + h * 0.5f, 0f), 45f);
            Label(one.DisplayName, new Vector3(0f, lift + h + 0.55f, 0f), 0.5f);
            return;
        }

        var front = ids.Take(4).ToArray();
        var back = ids.Skip(4).ToArray();
        void RowOf(string[] row, float z, float spacing)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (Catalog.EnemyById(row[i]) is not { } def) continue;
                float x = (i - (row.Length - 1) * 0.5f) * spacing;
                CuteActor(def, new Vector3(x, 0f, z), 0.3f - x * 0.06f, walking: i % 2 == 1);
                Label(def.DisplayName, new Vector3(x, def.Height + def.Hover * 0.7f + 0.45f, z), 0.38f);
            }
        }
        RowOf(front, 1.6f, 1.9f);
        RowOf(back, -2.4f, 2.5f);

        // The mimic as it first appears: a pot, eyes peeking over the rim.
        Node3D disguise = CuteModels.MimicDisguise(0.95f, out Node3D peek);
        peek.Visible = true;
        disguise.Position = new Vector3(4.2f, 0f, 1.6f);
        disguise.Rotation = new Vector3(0f, -0.3f, 0f);
        AddChild(disguise);
        Label("(a pot?)", new Vector3(4.2f, 1.35f, 1.6f), 0.38f);

        Camera(new Vector3(0.4f, 4.2f, 9.2f), new Vector3(0.4f, 0.6f, -0.6f), 48f);
    }

    private void CuteActor(EnemyDef def, Vector3 at, float yaw, bool walking)
    {
        CharacterRig rig = Actor(EnemyVisuals.SpecFor(def), at + new Vector3(0f, def.Hover * 0.7f, 0f), true, yaw,
                                 walking ? new Vector3(0f, 0f, 1.6f) : Vector3.Zero);
        _ = rig;
    }
}
