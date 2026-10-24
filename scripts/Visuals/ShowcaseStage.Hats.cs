using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Visuals;

/// <summary>
/// <c>--art-showcase=hats:&lt;hatId&gt; --shot=x.png</c>: every character look in
/// a 7-wide grid wearing one hat, faced to camera and held still, so a hat that
/// sinks into or floats off a head shows up at a glance. One capture per hat.
/// </summary>
public sealed partial class ShowcaseStage
{
    private void Hats(PropTheme t, string what)
    {
        // hats:<hatId>[:<row>]: one row of seven at a time reads at a glance.
        string[] parts = what.Split(':');
        string hatId = parts.Length > 1 ? parts[1] : Meta.Hats.All[1].Id;
        int onlyRow = parts.Length > 2 && int.TryParse(parts[2], out int r) ? r : -1;
        Floor(t, 30f);

        const int perRow = 7;
        const float dx = 2.3f, dz = 3.0f;
        int n = Characters.All.Count;
        int rows = (n + perRow - 1) / perRow;
        for (int i = 0; i < n; i++)
        {
            CharacterDef c = Characters.All[i];
            if (onlyRow >= 0 && i / perRow != onlyRow) continue;
            float x = (i % perRow - (perRow - 1) * 0.5f) * dx;
            float z = onlyRow >= 0 ? 0f : (i / perRow) * -dz;
            // Models face -Z; turn them to the camera.
            var holder = new Node3D { Position = new Vector3(x, 0f, z), Rotation = new Vector3(0f, Mathf.Pi, 0f) };
            AddChild(holder);
            Node3D model = PlayerLooks.Build(c.LookId);
            holder.AddChild(model);
            Wear(model, hatId);
            Label(c.Id, new Vector3(x, 2.25f, z), 0.3f);
        }
        if (onlyRow >= 0) rows = 1;
        Label($"hat: {hatId}", new Vector3(0f, 2.8f, -(rows - 1) * dz * 0.5f), 0.8f);
        if (onlyRow >= 0) Camera(new Vector3(0f, 2.3f, 7.5f), new Vector3(0f, 1.2f, 0f), 50f);
        else Camera(new Vector3(0f, 4.2f, 9.5f), new Vector3(0f, 0.9f, -(rows - 1) * dz * 0.5f), 50f);
    }

    private static void Wear(Node n, string hatId)
    {
        if (n is CharacterRig rig) { rig.SetHat(hatId); return; }
        foreach (Node c in n.GetChildren()) Wear(c, hatId);
    }
}
