using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// Late join and the world snapshot.
///
/// <b>Late join.</b> A session stays open during the run (public lobbies: to
/// anyone; invite-only: to whoever is invited; up to 4 players). A player who
/// says Hello mid-run gets a free slot and <c>JoinRun</c> (the run's seed and
/// difficulty), loads the run scene like everyone did at the start, and says
/// Loaded. The host then sends it a <see cref="SendWorldSnapshot">world
/// snapshot</see> and its own <c>Go</c>; nobody else waits.
///
/// The newcomer arrives next to the host with a fresh character, then is
/// levelled to the team's average level (rounded down) with every card picked
/// by the player's own auto-pick rule (Settings > Gameplay; best rarity by
/// default), so they are useful at once without a minute of card menus.
///
/// <b>The world snapshot</b> (host to one peer, reliable, one message): the
/// phase index and state, the run clock, the boss's id, the portal and swarm
/// timers, the endless loop, every enemy (id, def, transform, health), every
/// orb and every chest of the map (and whether it is open), plus the run
/// snapshot (NetSession.Migration.cs). A late joiner builds its world from it;
/// a survivor reconnecting after a host migration lines up the puppets it
/// already has (keeps matching ids, removes the rest, adds the missing).
/// </summary>
public sealed partial class NetSession
{
    // World snapshot header layout.
    private const int WsKind = 0, WsPhase = 1, WsState = 2, WsSeconds = 3, WsPhaseSeconds = 4, WsBoss = 5,
                      WsPortal = 6, WsSwarm = 7, WsLoop = 8, WsLevel = 9, WsHostX = 10, WsHostY = 11, WsHostZ = 12,
                      WsEnemies = 13, WsPickups = 14, WsChests = 15, WsRunSnap = 16;
    private const int WsHeader = WsRunSnap + SnapLength;
    private const int WsEnemyStride = 8, WsPickupStride = 6, WsChestStride = 8;

    /// <summary>This machine joined a run already in progress.</summary>
    public bool LateJoined { get; private set; }

    /// <summary>World snapshots applied here (late join, reconnect).</summary>
    public int SnapshotsApplied { get; private set; }
    /// <summary>Puppets placed from the last snapshot.</summary>
    public int SnapshotEnemies { get; private set; }
    /// <summary>Players who joined mid-run (counted on the host).</summary>
    public int LateJoins { get; private set; }
    /// <summary>Levels granted to catch up with the team.</summary>
    public int CatchUpLevels { get; private set; }

    /// <summary>Can a new player join the run now?</summary>
    private bool CanLateJoin(out string reason)
    {
        reason = "";
        if (Coop.Roster.Count >= Coop.MaxPlayers) { reason = Loc.T("That game is full."); return false; }
        if (_main is null || !IsInstanceValid(_main) || !_go)
        {
            reason = Loc.T("That game is starting. Try again in a moment.");
            return false;
        }
        if (_main.Run.Over || _main.Stages.State == StageDirector.PhaseState.Victory)
        {
            reason = Loc.T("That game is ending. Try again in a moment.");
            return false;
        }
        return true;
    }

    /// <summary>Host: a new player is in the roster mid-run: give them the run.</summary>
    private void StartLateJoin(CoopPeer p)
    {
        LateJoins++;
        AddRemote(p);
        PeakPlayers = Mathf.Max(PeakPlayers, Coop.Roster.Count);
        string difficulty = Coop.Endless ? "endless:" + Coop.DifficultyId : Coop.DifficultyId;
        GD.Print($"[coop] {p.Name} joins the run in progress (slot {p.Slot})");
        Popup(Loc.F("{0} joined the game", p.Name), new Color(0.55f, 1f, 0.65f));
        RpcId(p.Id, MethodName.JoinRun, (long)Coop.Seed, difficulty);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void JoinRun(long seed, string difficultyId)
    {
        if (!Coop.IsClient || Coop.InRun) return;
        LateJoined = true;
        GD.Print("[coop] joining a run in progress");
        StartRun(seed, difficultyId);
    }

    // ------------------------------------------------------------ host side

    /// <summary>Host: the whole world, to one peer.</summary>
    private void SendWorldSnapshot(long peer, bool lateJoin)
    {
        if (_main is null || !IsInstanceValid(_main)) return;
        FlushEvents();
        var d = new List<float>(WsHeader + 256);
        for (int i = 0; i < WsHeader; i++) d.Add(0f);
        StageDirector st = _main.Stages;
        d[WsKind] = lateJoin ? 1f : 0f;
        d[WsPhase] = st.PhaseIndex;
        d[WsState] = (int)st.State;
        d[WsSeconds] = _main.Run.Seconds;
        d[WsPhaseSeconds] = _main.Run.PhaseSeconds;
        d[WsBoss] = st.State == StageDirector.PhaseState.Boss && EnemyManager.Instance?.ActiveBoss is { } boss ? boss.NetId : 0;
        d[WsPortal] = Mathf.Max(0f, st.PortalSecondsLeft);
        d[WsSwarm] = st.Swarm?.Seconds ?? 0f;
        d[WsLoop] = EndlessMode.Loop;
        d[WsLevel] = TeamLevel(peer);
        Vector3 at = Player.Instance is { } me && IsInstanceValid(me) ? me.GlobalPosition : Arena.Centre;
        d[WsHostX] = at.X; d[WsHostY] = at.Y; d[WsHostZ] = at.Z;

        // The run snapshot, as RunSnap carries it.
        var snap = new List<float>(SnapLength) { _nextEnemyId, _nextPickupId, _nextChestId };
        if (EnemyManager.Instance is { } em) em.WriteDirectorState(snap);
        else for (int i = 0; i < EnemyManager.DirectorStateLength; i++) snap.Add(0f);
        st.WriteHostTimers(snap);
        snap.Add(EndlessMode.Loop);
        for (int i = 0; i < SnapLength && i < snap.Count; i++) d[WsRunSnap + i] = snap[i];

        int enemies = 0;
        if (EnemyManager.Instance is { } mgr)
        {
            IReadOnlyList<Enemy> alive = mgr.AliveList;
            for (int i = 0; i < alive.Count; i++)
            {
                Enemy e = alive[i];
                if (!e.IsAlive || e.NetId <= 0) continue;
                Vector3 p = e.GlobalPosition;
                d.Add(e.NetId); d.Add(e.PoolIndex); d.Add(p.X); d.Add(p.Y); d.Add(p.Z);
                d.Add(e.Rotation.Y); d.Add(e.MaxHealth); d.Add(e.Health);
                enemies++;
            }
        }
        int before = d.Count;
        Pickup.WriteSnapshot(d);
        int pickups = (d.Count - before) / WsPickupStride;
        int chests = 0;
        foreach (KeyValuePair<int, Chest> kv in _chests)
        {
            Chest c = kv.Value;
            if (!IsInstanceValid(c) || c.IsQueuedForDeletion() || !c.IsInsideTree()) continue;
            Vector3 p = c.GlobalPosition;
            d.Add(kv.Key); d.Add((int)c.Tier); d.Add(c.IsFree ? 1f : 0f); d.Add(p.X); d.Add(p.Y); d.Add(p.Z);
            d.Add(c.Rotation.Y); d.Add(c.IsOpen || _claimedChests.Contains(kv.Key) ? 1f : 0f);
            chests++;
        }
        d[WsEnemies] = enemies;
        d[WsPickups] = pickups;
        d[WsChests] = chests;
        GD.Print($"[coop] world snapshot to {Coop.NameOf(peer)}: phase {st.PhaseIndex + 1} {st.State}, " +
                 $"{enemies} enemies, {pickups} orbs, {chests} chests{(lateJoin ? $", level {d[WsLevel]}" : "")}");
        RpcId(peer, MethodName.WorldSnap, d.ToArray());
    }

    /// <summary>The team's average level (rounded down), without <paramref name="except"/>.</summary>
    private static int TeamLevel(long except)
    {
        RunScoreboard.RefreshLocal();
        int sum = 0, n = 0;
        foreach (CoopPeer p in Coop.Roster)
        {
            if (p.Id == except) continue;
            sum += Mathf.Max(1, RunScoreboard.Line(p.Id).Level);
            n++;
        }
        return n == 0 ? 1 : Mathf.Max(1, sum / n);
    }

    // ------------------------------------------------------------ client side

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void WorldSnap(float[] d)
    {
        if (!Coop.IsClient || _main is null || !IsInstanceValid(_main) || d.Length < WsHeader) return;
        bool late = d[WsKind] > 0.5f;
        int enemies = (int)d[WsEnemies], pickups = (int)d[WsPickups], chests = (int)d[WsChests];
        int o = WsHeader;
        if (o + enemies * WsEnemyStride + pickups * WsPickupStride + chests * WsChestStride > d.Length) return;
        StageDirector st = _main.Stages;
        var state = (StageDirector.PhaseState)(int)d[WsState];

        // 1. The map (a change clears the field, so first).
        st.CatchUpMap((int)d[WsPhase], state);
        _main.Run.SyncClock(d[WsSeconds], d[WsPhaseSeconds]);

        // 2. Enemies: keep matching puppets, drop the rest, add the missing.
        var ids = new HashSet<int>();
        for (int i = 0; i < enemies; i++) ids.Add((int)d[o + i * WsEnemyStride]);
        if (EnemyManager.Instance is { } em)
        {
            IReadOnlyList<Enemy> alive = em.AliveList;
            for (int i = 0; i < alive.Count; i++)
                if (alive[i].IsAlive && !ids.Contains(alive[i].NetId)) alive[i].Despawn();
        }
        var keep = new Dictionary<int, Enemy>();
        foreach (KeyValuePair<int, Enemy> kv in _enemies)
            if (ids.Contains(kv.Key) && IsInstanceValid(kv.Value) && kv.Value.NetId == kv.Key && !kv.Value.IsDead) keep[kv.Key] = kv.Value;
        _enemies.Clear();
        foreach (KeyValuePair<int, Enemy> kv in keep) _enemies[kv.Key] = kv.Value;
        int placed = 0;
        for (int i = 0; i < enemies; i++, o += WsEnemyStride)
        {
            int id = (int)d[o];
            var at = new Vector3(d[o + 2], d[o + 3], d[o + 4]);
            if (_enemies.TryGetValue(id, out Enemy? have))
            {
                have.NetSnapshot(at, d[o + 5], d[o + 7]);
                continue;
            }
            Enemy? e = EnemyManager.Instance?.SpawnPuppet((int)d[o + 1], id, at, d[o + 5], d[o + 6], d[o + 7]);
            if (e is null) continue;
            _enemies[id] = e;
            EnemiesSynced++;
            placed++;
        }
        SnapshotEnemies = enemies;

        // 3. Orbs.
        var orbIds = new HashSet<int>();
        for (int i = 0; i < pickups; i++) orbIds.Add((int)d[o + i * WsPickupStride]);
        Pickup.KeepOnly(orbIds);
        for (int i = 0; i < pickups; i++, o += WsPickupStride)
        {
            Pickup.SnapshotSpawn((int)d[o], (PickupKind)(int)d[o + 1], new Vector3(d[o + 2], d[o + 3], d[o + 4]), d[o + 5]);
            PickupsSynced++;
        }

        // 4. Chests (placed by the host; open ones show as taken).
        if (ChestSpawner.Instance is { } spawner)
            for (int i = 0; i < chests; i++, o += WsChestStride)
            {
                int id = (int)d[o];
                if (!_chests.TryGetValue(id, out Chest? c) || !IsInstanceValid(c))
                {
                    c = spawner.NetSpawn(id, (ChestTier)(int)d[o + 1], d[o + 2] > 0.5f, new Vector3(d[o + 3], d[o + 4], d[o + 5]), d[o + 6]);
                    if (c is null) continue;
                    _chests[id] = c;
                    ChestsSynced++;
                }
                if (d[o + 7] > 0.5f && !c.IsOpen) spawner.NetTakenBy(c, "", new Color(0.7f, 0.7f, 0.75f));
            }

        // 5. The boss, the open portal, the swarm; the endless loop; the director.
        int bossId = (int)d[WsBoss];
        Enemy? bossPuppet = bossId > 0 && _enemies.TryGetValue(bossId, out Enemy? b) ? b : null;
        st.CatchUpState(state, bossPuppet, d[WsPortal], d[WsSwarm]);
        EndlessMode.NetSetLoop((int)d[WsLoop]);
        var snap = new float[SnapLength];
        Array.Copy(d, WsRunSnap, snap, 0, SnapLength);
        _hostSnap = snap;
        EnemyManager.Instance?.ReadDirectorState(snap, SnapDirector);

        SnapshotsApplied++;
        bool wasMigrating = _migrating;
        _migrating = false;
        if (wasMigrating)
        {
            Status = Loc.T("Back in the game.");
            ResyncSeconds = Now - _migrationStartedAt;
            GD.Print($"[coop] resynced with the new host {ResyncSeconds:0.0} s after losing the old one: {enemies} enemies ({placed} new), {pickups} orbs, {chests} chests");
        }

        // 6. A newcomer: next to the team, at the team's level.
        if (late && Player.Instance is { } me && IsInstanceValid(me))
        {
            var hostAt = new Vector3(d[WsHostX], d[WsHostY], d[WsHostZ]);
            me.Teleport(st.ClampToPlay(hostAt) + Coop.SpawnOffset);
            PlayerCamera.Instance?.SnapToTarget();
            int target = (int)d[WsLevel];
            if (Progression.Inventory.Current is { } inv && target > inv.Level)
            {
                int before = inv.Level;
                Main.CatchUpPicks += target - before;
                inv.CatchUpTo(target);
                Main.CatchUpPicks = 0;
                CatchUpLevels = inv.Level - before;
                GD.Print($"[coop] caught up from level {before} to {inv.Level} with auto-picked cards");
            }
            GD.Print($"[coop] joined mid-run: phase {st.PhaseIndex + 1} {st.State}, {enemies} enemies, t={_main.Run.Clock}");
        }
        Changed?.Invoke();
    }
}
