using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Data;

namespace Wavebreak.Game;

/// <summary>
/// Tide Totems (Actors/TideTotem): a map structure that, woken with E, pulls
/// every experience orb and coin on the map to whoever woke it. Once per
/// totem, then it sleeps (dimmed, cracked) until the map ends.
///
/// <b>How many.</b> By map size (<see cref="TotemCountFor"/>): one on the
/// smaller maps, two on the mid-sized ones, three on Stormwatch Harbour.
/// Never in the rest room: <see cref="UseRestRoom"/> places no totems.
///
/// <b>Free, once.</b> The totem costs nothing. It adds no experience or gold
/// to the run; it only fetches what is already lying on the floor, which is
/// what a Magnet item (bigger pickup range, all the time) does a little of
/// every second. Charging gold for gold would make it a sum to work out
/// rather than a decision; one use per totem and one to three per map keeps
/// it a "when" decision instead: wake it after a big fight or before the boss.
/// Med-packs are left alone, so a totem never stands in for finding a heal.
///
/// <b>Placement</b> follows the stations' rules (physics ground, flat, clear
/// of props, chests, blockers and other stations) plus the arena's terrain
/// footprints (<see cref="Arena.ClearSpotNear"/>, so off mounds and pools),
/// well outside every shrine's charge ring and inside the band the hidden
/// secret shrines use; spread out round the map, never near the start.
///
/// <b>Co-op</b> (NetSession.Totems.cs): the host places them and tells
/// everyone; E asks the host, which grants the first asker, latches every
/// networked orb to that player and tells everyone the totem is spent.
/// </summary>
public sealed partial class Interactables
{
    /// <summary>Totems on a map by its in-radius: under 135 m one, under
    /// 165 m two, else three.</summary>
    public static int TotemCountFor(StageDef stage) => stage.InRadius < 135f ? 1 : stage.InRadius < 165f ? 2 : 3;

    public const float TotemShrineClearance = 4f;
    public const float TotemStartClearance = 22f;

    /// <summary>The autopilot wakes a totem when at least this many orbs and
    /// coins lie further away than its pickup range.</summary>
    public const int BotTotemOrbs = 25;

    private int _wantTotems;
    private int _nextTotemId = 1;
    private AudioStreamPlayer3D? _totemSfx;
    private static AudioStreamWav? _totemSound;

    public int TotemsPlaced { get; private set; }
    /// <summary>Totems this machine's player woke.</summary>
    public int TotemUses { get; private set; }
    /// <summary>Orbs and coins this machine latched to a totem's caller.</summary>
    public int TotemOrbsPulled { get; private set; }

    /// <summary>Test switch: the autopilot leaves totems alone.</summary>
    public static bool BotTotemsOff;

    public IEnumerable<TideTotem> Totems => _stations.OfType<TideTotem>().Where(t => IsInstanceValid(t));

    public TideTotem? TotemById(int id)
    {
        foreach (Station s in _stations)
            if (s is TideTotem t && IsInstanceValid(t) && t.NetId == id) return t;
        return null;
    }

    // ------------------------------------------------------------ placement

    private void PlaceTotems(PhysicsDirectSpaceState3D space, Vector3 start)
    {
        foreach (int _ in TotemPlacementSteps(space, start)) { }
    }

    private IEnumerable<int> TotemPlacementSteps(PhysicsDirectSpaceState3D space, Vector3 start)
    {
        if (_wantTotems <= 0 || _map is null || Net.Coop.IsClient) yield break;   // the host's are sent to clients
        var rng = new Rng(Rng.Seed ^ 0x7071DE70UL ^ (ulong)_phase);
        Arena? arena = _map as Arena;
        float offset = rng.Range(0f, Mathf.Tau);
        int placed = 0;
        for (int i = 0; i < _wantTotems; i++)
        {
            Vector3? found = null;
            for (int attempt = 0; attempt < 80 && found is null; attempt++)
            {
                // Each totem in its own sector of the map, so they spread out.
                float sector = Mathf.Tau / _wantTotems;
                float a = attempt < 50 ? offset + sector * i + rng.Range(-0.4f, 0.4f) * sector : rng.Range(0f, Mathf.Tau);
                float d = _radius * rng.Range(0.3f, 0.76f);
                Vector3 c = _centre + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * d;
                if (arena is not null) c = arena.ClearSpotNear(c, 1.6f);
                if (!Validate(space, c, start, 1.0f, 3.4f, 0.9f, 2.5f, out Vector3 spot)) continue;
                if (Flat(spot).DistanceTo(Flat(start)) < TotemStartClearance || Flat(spot - _centre).Length() < TotemStartClearance) continue;
                if (Flat(spot - _centre).Length() > _radius * 0.8f) continue;
                if (NearStation(spot, attempt < 50 ? MathF.Max(30f, _radius * 0.45f) : StationSpacing)) continue;
                if (ChestSpawner.Instance?.Nearest(spot, StationChestClearance) is not null) continue;
                if (NearShrine(spot)) continue;
                if (!Level(space, spot, 1.0f)) continue;
                found = spot;
            }
            if (found is not { } at)
            {
                GD.Print("[totem] no valid spot found; totem dropped");
                yield return 0;
                continue;
            }
            Vector3 to = _centre - at; to.Y = 0f;
            AddTotem(new TideTotem(), at, to.LengthSquared() > 0.01f ? MathF.Atan2(to.X, to.Z) : 0f, 0, false);
            placed++;
            yield return 1;
        }
        GD.Print($"[totem] placed {placed}/{_wantTotems} phase={_phase}");
    }

    /// <summary>Clear of every map shrine's ring (charge rings with room to
    /// spare) and of the friendly shrines.</summary>
    internal static bool NearShrine(Vector3 p)
    {
        if (ShrineSpawner.Instance is { } shrines)
            foreach (MapShrine s in shrines.Shrines)
            {
                if (!IsInstanceValid(s)) continue;
                float need = (s.Kind == MapShrineKind.Charge ? MapShrines.ChargeRadius : 1.5f) + TotemShrineClearance;
                if (Flat(s.GlobalPosition).DistanceSquaredTo(Flat(p)) < need * need) return true;
            }
        if (CuteShrineSpawner.Instance is { } cute)
            foreach (CuteShrine s in cute.Shrines)
                if (IsInstanceValid(s) && Flat(s.GlobalPosition).DistanceSquaredTo(Flat(p)) < 25f) return true;
        return false;
    }

    /// <summary>Adds a totem to this map (placed, dev-spawned, or sent by the
    /// host). The host and single-player number it; the host tells clients.</summary>
    internal TideTotem? AddTotem(TideTotem totem, Vector3 at, float yaw, int id, bool used)
    {
        if (_map is null || !IsInstanceValid(_map)) { totem.Free(); return null; }
        if (id > 0 && TotemById(id) is { } existing)
        {
            totem.Free();
            if (used) existing.MarkUsed();
            return existing;
        }
        if (id <= 0)
        {
            foreach (TideTotem t in Totems) _nextTotemId = Math.Max(_nextTotemId, t.NetId + 1);
            id = _nextTotemId++;
        }
        totem.NetId = id;
        if (used) totem.MarkUsed();
        _map.AddChild(totem);
        totem.GlobalPosition = at;
        totem.Rotation = new Vector3(0f, yaw, 0f);
        if (Arena.Current.Underwater) Visuals.ReefActorSupport.Ground(totem, new Vector3(1.1f, 1f, 1.1f));
        _stations.Add(totem);
        StationsPlaced++;
        TotemsPlaced++;
        GD.Print($"[totem] placed #{id} at=({at.X:0.0},{at.Y:0.0},{at.Z:0.0}) phase={_phase}{(used ? " (asleep)" : "")}");
        if (Net.Coop.IsHost) Net.NetSession.Instance?.OnTotemPlaced(totem);
        return totem;
    }

    /// <summary>Developer panel: a totem at <paramref name="at"/>, synced in co-op.</summary>
    public TideTotem? SpawnTotemAt(Vector3 at)
    {
        if (Net.Coop.IsClient) return null;   // the host owns totems
        float yaw = 0f;
        if (Player.Instance is { } p && IsInstanceValid(p))
        {
            Vector3 to = p.GlobalPosition - at; to.Y = 0f;
            if (to.LengthSquared() > 0.01f) yaw = MathF.Atan2(to.X, to.Z);
        }
        return AddTotem(new TideTotem(), at, yaw, 0, false);
    }

    // ------------------------------------------------------------------ use

    /// <summary>E at a totem. Single-player: the tide comes at once. Co-op:
    /// ask the host (first asker wins).</summary>
    public bool UseTotem(TideTotem totem)
    {
        if (!IsInstanceValid(totem) || totem.Used || totem.NetPending) return false;
        if (Net.Coop.InRun)
        {
            totem.NetPending = true;
            Net.NetSession.Instance?.AskTotem(totem);
            return true;
        }
        FireTotem(totem, Net.Coop.LocalId);
        return true;
    }

    /// <summary>Runs on every machine when a totem is spent:
    /// <paramref name="peer"/> woke it. Marks it asleep, plays the whoosh,
    /// and latches the orbs this machine is in charge of (Pickup.Totem.cs).</summary>
    internal int FireTotem(TideTotem totem, long peer, List<int>? netIds = null)
    {
        if (!IsInstanceValid(totem)) return 0;
        totem.NetPending = false;
        if (totem.Used) return 0;
        totem.MarkUsed();
        bool mine = !Net.Coop.InRun || peer == Net.Coop.LocalId;
        int pulled = Actors.Pickup.CallTide(peer, netIds);
        TotemOrbsPulled += pulled;
        if (mine) TotemUses++;

        TideWhoosh.Play(_map ?? (Node)this, totem.GlobalPosition);
        PlayTotemSound(totem.GlobalPosition);
        string line = mine ? Meta.Loc.T("The tide answers!")
                           : Meta.Loc.F("{0} called the tide", Net.Coop.NameOf(peer));
        EventBus.Publish(new PopupRequested(totem.Top + Vector3.Up * 0.5f, line, TideTotem.TotemTint.Lerp(Colors.White, 0.35f), 1.4f));
        GD.Print($"[totem] #{totem.NetId} woken by {(mine ? "me" : $"peer {peer}")}: {pulled} orbs latched");
        return pulled;
    }

    /// <summary>Co-op: the host said no (already spent, or mid-migration).</summary>
    internal void TotemDenied(TideTotem totem)
    {
        if (!IsInstanceValid(totem)) return;
        totem.NetPending = false;
    }

    private void PlayTotemSound(Vector3 at)
    {
        _totemSound ??= Audio.SfxSynth.ToStream(Audio.SfxSynth.BuildTotem());
        if (_totemSfx is null || !IsInstanceValid(_totemSfx))
        {
            _totemSfx = new AudioStreamPlayer3D
            {
                Name = "TotemSfx",
                Bus = Audio.AudioSettings.SfxBus,
                VolumeDb = -4f,
                UnitSize = 14f,
                MaxDistance = 120f,
                Stream = _totemSound,
            };
            AddChild(_totemSfx);
        }
        _totemSfx.GlobalPosition = at + Vector3.Up * 1.5f;
        _totemSfx.Play();
    }

    // ------------------------------------------------------------ autopilot

    /// <summary>Worth waking: enough orbs lie out of reach.</summary>
    private bool BotWantsTotem(TideTotem t)
    {
        if (BotTotemsOff || t.Used || t.NetPending || Player.Instance is not { } p || !IsInstanceValid(p)) return false;
        float range = p.Stats.Get(Stats.Stat.PickupRange);
        return Actors.Pickup.LooseOrbs(p.GlobalPosition, range + 1f) >= BotTotemOrbs;
    }

    // ---------------------------------------------------------------- audit

    private static void TotemAudit(Action<bool, string> check)
    {
        foreach (StageDef s in Data.Stages.All)
        {
            int n = TotemCountFor(s);
            check(n is >= 1 and <= 3, $"stage {s.Id} wants {n} tide totems, outside 1-3");
        }
        check(Data.Stages.All.Any(s => s.Id == "stormwatch" && TotemCountFor(s) == 3), "Stormwatch Harbour (the biggest map) gets three tide totems");
        check(Data.Stages.All.Any(s => TotemCountFor(s) == 1), "the smallest map gets one tide totem");
    }
}
