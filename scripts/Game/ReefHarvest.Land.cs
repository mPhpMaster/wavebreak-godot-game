using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Game;

public sealed partial class ReefHarvest
{
    private void PopulateLand()
    {
        var rng = new Rng((ulong)Arena.Current.LayoutSeed ^ 0x6F48D29AUL);
        using var query = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Down, 1u);
        var excluded = new Godot.Collections.Array<Rid>();
        if (Player.Instance is { } player) excluded.Add(player.GetRid());
        foreach (Node node in GetParent().FindChildren("PropColliders", "StaticBody3D", true, false))
            if (node is StaticBody3D decoration) excluded.Add(decoration.GetRid());
        query.Exclude = excluded;
        for (int attempt = 0; attempt < 1200 && _defs.Count < 72; attempt++)
        {
            float angle = rng.Range(0, Mathf.Tau), radius = Mathf.Sqrt(rng.Float()) * (Arena.Current.InRadius - 7);
            if (radius < Arena.Current.ClearRadius + 8) continue;
            Vector3 at = new(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            query.From = at + Vector3.Up * 120; query.To = at + Vector3.Down * 50;
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count == 0) continue;
            Vector3 normal = hit["normal"].AsVector3(); at = hit["position"].AsVector3();
            if (normal.Y < .75f || at.Y > 12) continue;
            bool close = false;
            foreach (ReefHarvestDef existing in _defs)
                if (existing.Position.DistanceSquaredTo(at) < 16) { close = true; break; }
            if (!close) Add(at + normal * .04f, normal, (HarvestKind)(_defs.Count % 3));
        }
    }
}
