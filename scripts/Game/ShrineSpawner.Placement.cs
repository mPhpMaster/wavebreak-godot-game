using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Progression;
using Wavebreak.Stats;
using Wavebreak.UI;

namespace Wavebreak.Game;

public sealed partial class ShrineSpawner
{
    public static IReadOnlyList<MapShrine> Populate(Node3D map, Rng rng, float arenaRadius, StageDef stage)
    {
        var placed = new List<MapShrine>();
        if (map is null || !GodotObject.IsInstanceValid(map)) return placed;
        ShrineSpawner? self = Instance;
        self?.ForgetMap();

        var kinds = new List<MapShrineKind>();
        void AddKind(MapShrineKind k, int n) { for (int i = 0; i < Math.Max(0, n); i++) kinds.Add(k); }
        AddKind(MapShrineKind.Charge, stage.ChargeShrines);
        AddKind(MapShrineKind.Challenge, stage.ChallengeShrines);
        AddKind(MapShrineKind.Statue, stage.StatueShrines);
        AddKind(MapShrineKind.Greed, stage.GreedShrines);

        Arena? arena = map as Arena;
        float outer = arenaRadius * 0.76f;
        float inner = Math.Max(stage.ClearRadius + 8f, 18f);
        var spots = new List<Vector3>();

        foreach (MapShrineKind kind in kinds)
        {
            Vector3? found = null;
            for (int attempt = 0; attempt < 60 && found is null; attempt++)
            {
                float d = Mathf.Lerp(inner, outer, MathF.Sqrt(rng.Float()));
                Vector3 c = rng.OnUnitCircleXZ() * d;
                if (arena is not null) c = arena.ClearSpotNear(c, kind == MapShrineKind.Charge ? MapShrines.ChargeRadius + 0.5f : 2f);
                c.Y = stage.Underwater ? UnderwaterTerrain.Height(c) : 0f;
                float flat = new Vector2(c.X, c.Z).Length();
                if (flat < inner * 0.8f || flat > arenaRadius * 0.8f) continue;
                float spacing = attempt < 40 ? MapShrines.MinSpacing : MapShrines.MinSpacing * 0.5f;
                bool crowded = false;
                foreach (Vector3 s in spots)
                    if (new Vector2(s.X - c.X, s.Z - c.Z).Length() < spacing) { crowded = true; break; }
                if (!crowded) found = c;
            }
            if (found is not { } at)
            {
                GD.Print($"[shrine] no spot for {kind}; skipped");
                continue;
            }

            spots.Add(at);
            var shrine = new MapShrine(kind)
            {
                Position = at,
                Rotation = new Vector3(0f, rng.Range(0f, Mathf.Tau), 0f),
            };
            map.AddChild(shrine);
            if (stage.Underwater)
            {
                if (kind == MapShrineKind.Statue || kind == MapShrineKind.Greed)
                    shrine.GlobalPosition = Visuals.ReefPlacement.Ledges[kind == MapShrineKind.Statue ? 0 : 2] + Vector3.Up * 0.04f;
                else Visuals.ReefPlacement.Ground(shrine, MapShrines.ChargeRadius + 0.5f);
            }
            placed.Add(shrine);
            GD.Print($"[shrine] placed {kind} at=({at.X:0.0},{at.Z:0.0})");
        }

        if (self is not null)
        {
            self._shrines.AddRange(placed);
            self.Placed += placed.Count;
        }
        return placed;
    }

    /// <summary>A map change: drop the old shrines and end any running trial
    /// (its guardians are cleared with the field).</summary>
    private void ForgetMap()
    {
        if (_trial != Trial.None) EndTrial(false, quiet: true);
        _shrines.Clear();
        _focus = null;
        _prompt?.HideNow();
    }

    // ------------------------------------------------------------- API

    /// <summary>Press interact on the next frame, for the autopilot.</summary>
}
