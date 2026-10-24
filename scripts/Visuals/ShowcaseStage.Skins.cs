using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Visuals;

/// <summary>
/// <c>--art-showcase=skins:&lt;characterId&gt; --shot=x.png</c>: every skin of one
/// character side by side, faced to camera, in the colours the run would
/// wear (<see cref="SkinDef.Paint"/>), so a skin that does not actually
/// repaint the model shows up at a glance.
/// </summary>
public sealed partial class ShowcaseStage
{
    private void Skins(PropTheme t, string what)
    {
        string id = what["skins:".Length..];
        CharacterDef c = Characters.ById(id) ?? Characters.Default;
        Floor(t, 30f);
        int n = c.Skins.Count;
        const float dx = 1.9f;
        for (int i = 0; i < n; i++)
        {
            SkinDef s = c.Skins[i];
            float x = (i - (n - 1) * 0.5f) * dx;
            var holder = new Node3D { Position = new Vector3(x, 0f, 0f), Rotation = new Vector3(0f, Mathf.Pi + 0.35f, 0f) };
            AddChild(holder);
            holder.AddChild(PlayerLooks.Build(s.LookId, s.Paint));
            Label(s.Name, new Vector3(x, 2.0f, 0f), 0.28f);
        }
        Label(c.DisplayName, new Vector3(0f, 2.6f, 0f), 0.6f);
        Camera(new Vector3(0f, 2.2f, 4.2f + n * 0.75f), new Vector3(0f, 0.9f, 0f), 50f);
    }
}
