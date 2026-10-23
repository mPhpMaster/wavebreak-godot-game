using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Stats;

namespace Wavebreak.Data;

/// <summary>Map shrine checks for <c>--validate-content</c>.</summary>
public static partial class ContentAudit
{
    private static void AuditShrines()
    {
        // Stage data.
        foreach (StageDef stage in Stages.Run)
        {
            int total = stage.ChargeShrines + stage.ChallengeShrines + stage.StatueShrines + stage.GreedShrines;
            Check(stage.ChargeShrines >= 0 && stage.ChallengeShrines >= 0 && stage.StatueShrines >= 0 && stage.GreedShrines >= 0,
                  $"shrines: {stage.Id} has a negative shrine count");
            Check(total is > 0 and <= 14, $"shrines: {stage.Id} places {total} shrines (expected 1-14)");
        }

        // Boon table: distinct ids, real values, readable text, exact removal.
        var ids = new HashSet<string>();
        var sheet = new StatSheet();
        var probe = new object();
        foreach (ShrineBoonDef def in MapShrines.Boons)
        {
            Check(ids.Add(def.Id), $"shrines: duplicate boon id {def.Id}");
            Check(def.Base > 0f, $"shrines: boon {def.Id} has no value");
            foreach (Rarity r in new[] { Rarity.Common, Rarity.Rare, Rarity.Legendary })
            {
                var boon = new ShrineBoon(def, r, def.Base * MapShrines.TierScale(r));
                Check(boon.Effect.Length > 0 && boon.Title.Length > 0, $"shrines: boon {def.Id} has no text");
                float before = sheet.Get(def.Stat);
                sheet.Add(def.Stat, def.Op, boon.Value, probe);
                Check(!Mathf.IsEqualApprox(sheet.Get(def.Stat), before), $"shrines: boon {def.Id} at {r} changes nothing");
                sheet.RemoveAllFrom(probe);
                Check(Mathf.IsEqualApprox(sheet.Get(def.Stat), before), $"shrines: boon {def.Id} leaves residue after removal");
            }
        }

        // Rolls: distinct stats at every luck, and the floor is respected.
        var rng = new Rng(0x5A1E5A0D17UL);
        foreach (float luck in new[] { -0.95f, 0f, 2f, 8f })
            for (int i = 0; i < 40; i++)
            {
                List<ShrineBoon> roll = MapShrines.RollBoons(rng, MapShrines.ChargeChoices, luck);
                var stats = new HashSet<Stat>();
                foreach (ShrineBoon b in roll) stats.Add(b.Stat);
                Check(roll.Count == MapShrines.ChargeChoices && stats.Count == roll.Count,
                      $"shrines: charge roll at luck {luck} gave {roll.Count} boons on {stats.Count} stats");
                ShrineBoon one = MapShrines.RollBoons(rng, 1, luck, Rarity.Uncommon)[0];
                Check(one.Rarity >= Rarity.Uncommon, $"shrines: statue roll below its floor ({one.Rarity})");
            }

        // Greed: a stack makes enemies stronger and pays, and removes exactly.
        var greed = new object();
        float scaling = sheet.Get(Stat.EnemyScaling), gold = sheet.Get(Stat.GoldGain);
        foreach (var (stat, op, value) in MapShrines.GreedStack) sheet.Add(stat, op, value, greed);
        Check(sheet.Get(Stat.EnemyScaling) > scaling && sheet.Get(Stat.GoldGain) > gold, "shrines: greed stack has no effect");
        sheet.RemoveAllFrom(greed);
        Check(Mathf.IsEqualApprox(sheet.Get(Stat.EnemyScaling), scaling), "shrines: greed stack leaves residue");

        // Statue ladder climbs; challenge numbers are sane.
        for (int n = 0; n < 12; n++)
            Check(MapShrines.StatuePrice(n + 1) > MapShrines.StatuePrice(n), $"shrines: statue price does not climb at {n}");
        Check(MapShrines.SlaughterTarget(0f) > 0 && MapShrines.SlaughterTarget(3600f) == MapShrines.SlaughterMax,
              "shrines: slaughter target out of range");
        bool anyElite = false;
        foreach (EnemyDef e in Catalog.Enemies) anyElite |= e is { IsElite: true, IsBoss: false };
        Check(anyElite, "shrines: no elite for the guardian challenge (falls back to the kill count)");

        // Placement on the live map: every requested shrine stood, apart, inside.
        if (ShrineSpawner.Instance is { } spawner)
        {
            StageDef stage = Arena.Current;
            int want = stage.ChargeShrines + stage.ChallengeShrines + stage.StatueShrines + stage.GreedShrines;
            var live = new List<MapShrine>();
            foreach (MapShrine s in spawner.Shrines) if (GodotObject.IsInstanceValid(s)) live.Add(s);
            Check(live.Count == want, $"shrines: {live.Count}/{want} placed on {stage.Id}");
            for (int i = 0; i < live.Count; i++)
            {
                Vector3 p = live[i].GlobalPosition;
                Check(new Vector2(p.X, p.Z).Length() < stage.InRadius - 2f, $"shrines: {live[i].Kind} outside the walls");
                for (int j = i + 1; j < live.Count; j++)
                {
                    Vector3 q = live[j].GlobalPosition;
                    Check(new Vector2(p.X - q.X, p.Z - q.Z).Length() >= MapShrines.MinSpacing * 0.5f,
                          $"shrines: {live[i].Kind} and {live[j].Kind} overlap");
                }
            }
            Line($"shrines placed={live.Count} on {stage.Id} summary: {spawner.SummaryFields}");
        }
        else Check(false, "shrines: no ShrineSpawner in the run");
    }
}
