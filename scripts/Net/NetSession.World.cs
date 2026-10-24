using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Progression;

namespace Wavebreak.Net;

/// <summary>
/// The run, world side. The host is the authority on everything that is not a
/// player: it runs the enemy director and AI, owns enemy health, rolls drops,
/// places chests and drives the phase flow. Clients see puppets and send
/// their hits.
///
/// Traffic, all through this one node:
///   Ev (reliable, batched per frame)  enemy spawn/gone, pickup spawn/gone,
///                                     mirrored enemy shots and blasts
///   EState (unreliable, 15 Hz)        enemy positions, facing, health
///   Hits (client -> host, batched)    weapon hits and crowd control on enemies
///   chest / stage / payout RPCs       reliable, sent after a flush of Ev so
///                                     ordering holds
/// </summary>
public sealed partial class NetSession
{
    private const float EnemyStateInterval = 1f / 15f;
    private const int EnemiesPerPacket = 50;

    // Ev record types and their lengths (type included).
    private const int EvEnemySpawn = 1, EvEnemyGone = 2, EvPickupSpawn = 3, EvPickupGone = 4, EvAttack = 5;
    private static int EvLength(int type) => type switch
    {
        EvEnemySpawn => 9,
        EvEnemyGone => 6,
        EvPickupSpawn => 10,
        EvPickupGone => 2,
        EvAttack => 17,
        _ => 1,
    };

    private const int HitStride = 9;

    private readonly List<float> _ev = new();
    private readonly List<float> _hits = new();
    private readonly List<float> _state = new();
    private readonly Dictionary<int, Enemy> _enemies = new();     // host: own; client: puppets
    private readonly Dictionary<int, Chest> _chests = new();
    private readonly HashSet<int> _claimedChests = new();
    private int _nextEnemyId = 1, _nextPickupId = 1, _nextChestId = 1;
    private float _enemyStateTimer;

    // Counters for the automated test and the log.
    public int EnemiesSynced { get; private set; }
    public int HitsQueued { get; private set; }
    public int HitsApplied { get; private set; }
    public int PuppetKills { get; private set; }
    public int PickupsSynced { get; private set; }
    public int ChestsSynced { get; private set; }
    public int SharedXp { get; private set; }
    public int RemoteCollects { get; private set; }
    public int AttacksMirrored { get; private set; }
    public int EnemyStatesReceived { get; private set; }
    public int AttacksReceived { get; private set; }
    public int BossesSeen { get; private set; }
    public int PhasesSeen { get; private set; }

    private void ResetWorldState()
    {
        _ev.Clear();
        _hits.Clear();
        _enemies.Clear();
        _chests.Clear();
        _claimedChests.Clear();
    }

    private void FlushEvents()
    {
        if (_ev.Count == 0) return;
        if (Coop.IsHost && Multiplayer.GetPeers().Length > 0) Rpc(MethodName.Ev, _ev.ToArray());
        _ev.Clear();
    }

    private void FlushHits()
    {
        if (_hits.Count == 0) return;
        if (Coop.IsClient) RpcId(1, MethodName.Hits, _hits.ToArray());
        _hits.Clear();
    }

    private void StepHostWorld(float dt)
    {
        _enemyStateTimer -= dt;
        if (_enemyStateTimer > 0f || EnemyManager.Instance is not { } em) return;
        _enemyStateTimer = EnemyStateInterval;
        if (Multiplayer.GetPeers().Length == 0) return;

        _state.Clear();
        IReadOnlyList<Enemy> alive = em.AliveList;
        for (int i = 0; i < alive.Count; i++)
        {
            Enemy e = alive[i];
            if (!e.IsAlive || e.NetId <= 0) continue;
            Vector3 p = e.GlobalPosition;
            _state.Add(e.NetId); _state.Add(p.X); _state.Add(p.Y); _state.Add(p.Z);
            _state.Add(e.Rotation.Y); _state.Add(e.Health);
            if (_state.Count >= EnemiesPerPacket * 6)
            {
                Rpc(MethodName.EState, _state.ToArray());
                _state.Clear();
            }
        }
        if (_state.Count > 0) Rpc(MethodName.EState, _state.ToArray());
    }

    // ================================================================ enemies

    /// <summary>Host: an enemy entered the field.</summary>
    public void OnEnemySpawned(Enemy e)
    {
        e.NetId = _nextEnemyId++;
        _enemies[e.NetId] = e;
        Vector3 p = e.GlobalPosition;
        _ev.Add(EvEnemySpawn); _ev.Add(e.NetId); _ev.Add(e.PoolIndex);
        _ev.Add(p.X); _ev.Add(p.Y); _ev.Add(p.Z); _ev.Add(e.Rotation.Y);
        _ev.Add(e.MaxHealth); _ev.Add(e.Health);
        EnemiesSynced++;
    }

    /// <summary>Host: an enemy died (<paramref name="killed"/>) or was removed.</summary>
    public void OnEnemyGone(Enemy e, bool killed)
    {
        _enemies.Remove(e.NetId);
        Vector3 p = e.GlobalPosition + new Vector3(0f, 0.35f, 0f);
        _ev.Add(EvEnemyGone); _ev.Add(e.NetId); _ev.Add(killed ? 1f : 0f);
        _ev.Add(p.X); _ev.Add(p.Y); _ev.Add(p.Z);
    }

    /// <summary>Host: an enemy attack to repeat on every client (see EnemyAttacks).</summary>
    public void MirrorAttack(int kind, Vector3 a, Vector3 b, float p0, float p1, float p2, float p3, float p4, Color tint)
    {
        _ev.Add(EvAttack); _ev.Add(kind);
        _ev.Add(a.X); _ev.Add(a.Y); _ev.Add(a.Z);
        _ev.Add(b.X); _ev.Add(b.Y); _ev.Add(b.Z);
        _ev.Add(p0); _ev.Add(p1); _ev.Add(p2); _ev.Add(p3); _ev.Add(p4);
        _ev.Add(tint.R); _ev.Add(tint.G); _ev.Add(tint.B); _ev.Add(tint.A);
        AttacksMirrored++;
    }

    /// <summary>Host: a wave banner ("ELITE WAVE", a boss's name) for everyone.</summary>
    public void SendBanner(string text, Color color, float scale)
    {
        FlushEvents();
        Rpc(MethodName.Banner, text, color, scale);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Banner(string text, Color color, float scale)
    {
        if (Player.Instance is not { } me || !IsInstanceValid(me)) return;
        EventBus.Publish(new PopupRequested(me.GlobalPosition + new Vector3(0f, 3.2f, 0f), text, color, scale));
    }

    /// <summary>Client: this player's weapon hit puppet <paramref name="id"/>.</summary>
    public void QueueHit(int id, float amount, float knockback, Vector3 direction)
    {
        _hits.Add(id); _hits.Add(amount); _hits.Add(knockback); _hits.Add(direction.X); _hits.Add(direction.Z);
        _hits.Add(0f); _hits.Add(0f); _hits.Add(0f); _hits.Add(0f);
        HitsQueued++;
    }

    /// <summary>Client: crowd control this player's weapon put on a puppet.</summary>
    public void QueueCc(int id, float stun, float chill, int freezeAt, float freezeSeconds)
    {
        _hits.Add(id); _hits.Add(0f); _hits.Add(0f); _hits.Add(0f); _hits.Add(0f);
        _hits.Add(stun); _hits.Add(chill); _hits.Add(freezeAt); _hits.Add(freezeSeconds);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hits(float[] d)
    {
        if (!Coop.IsHost) return;
        // Every hit in this batch is the sender's: kill credit and the
        // scoreboard read it (Enemy.Awareness.cs).
        Enemy.NetHitter = Multiplayer.GetRemoteSenderId();
        try { ApplyHits(d); }
        finally { Enemy.NetHitter = 0; }
    }

    private void ApplyHits(float[] d)
    {
        // Applied straight to the enemy: no EnemyDamaged is published here, so
        // the host's own lifesteal and on-hit items never fire off someone
        // else's hit (the client already ran its own).
        for (int i = 0; i + HitStride <= d.Length; i += HitStride)
        {
            if (!_enemies.TryGetValue((int)d[i], out Enemy? e) || !IsInstanceValid(e) || !e.IsAlive) continue;
            float amount = d[i + 1];
            if (amount > 0f)
            {
                e.TakeDamage(new DamageInfo
                {
                    Amount = amount,
                    Knockback = d[i + 2],
                    Direction = new Vector3(d[i + 3], 0f, d[i + 4]),
                    CanCrit = false,
                    ProcCoefficient = 0f,
                    Source = "coop",
                });
                HitsApplied++;
            }
            if (!e.IsAlive) continue;
            if (d[i + 5] > 0f) e.ApplyStun(d[i + 5]);
            if (d[i + 6] > 0f) e.ApplyChill(d[i + 6], (int)d[i + 7], d[i + 8]);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 1)]
    private void EState(float[] d)
    {
        if (!Coop.IsClient) return;
        EnemyStatesReceived++;
        for (int i = 0; i + 6 <= d.Length; i += 6)
            if (_enemies.TryGetValue((int)d[i], out Enemy? e) && IsInstanceValid(e) && e.NetId == (int)d[i])
                e.NetSnapshot(new Vector3(d[i + 1], d[i + 2], d[i + 3]), d[i + 4], d[i + 5]);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Ev(float[] d)
    {
        if (!Coop.IsClient || _main is null) return;
        int i = 0;
        while (i < d.Length)
        {
            int type = (int)d[i];
            int len = EvLength(type);
            if (len <= 1 || i + len > d.Length) break;
            switch (type)
            {
                case EvEnemySpawn: ClientEnemySpawn(d, i + 1); break;
                case EvEnemyGone: ClientEnemyGone(d, i + 1); break;
                case EvPickupSpawn:
                    Pickup.SpawnNet((int)d[i + 1], (PickupKind)(int)d[i + 2], new Vector3(d[i + 3], d[i + 4], d[i + 5]),
                                    d[i + 6], new Vector3(d[i + 7], d[i + 8], d[i + 9]));
                    PickupsSynced++;
                    break;
                case EvPickupGone: Pickup.NetGone((int)d[i + 1]); break;
                case EvAttack: ClientAttack(d, i + 1); break;
            }
            i += len;
        }
    }

    private void ClientEnemySpawn(float[] d, int o)
    {
        int id = (int)d[o];
        if (EnemyManager.Instance is not { } em) return;
        Enemy? e = em.SpawnPuppet((int)d[o + 1], id, new Vector3(d[o + 2], d[o + 3], d[o + 4]), d[o + 5], d[o + 6], d[o + 7]);
        if (e is null) return;
        _enemies[id] = e;
        EnemiesSynced++;
    }

    private void ClientEnemyGone(float[] d, int o)
    {
        int id = (int)d[o];
        if (!_enemies.Remove(id, out Enemy? e) || !IsInstanceValid(e) || e.NetId != id) return;
        if (d[o + 1] > 0.5f)
        {
            e.NetKill(new Vector3(d[o + 2], d[o + 3], d[o + 4]));
            PuppetKills++;
        }
        else e.Despawn();
    }

    private void ClientAttack(float[] d, int o)
    {
        AttacksReceived++;
        int kind = (int)d[o];
        var a = new Vector3(d[o + 1], d[o + 2], d[o + 3]);
        var b = new Vector3(d[o + 4], d[o + 5], d[o + 6]);
        float p0 = d[o + 7], p1 = d[o + 8], p2 = d[o + 9], p3 = d[o + 10], p4 = d[o + 11];
        var tint = new Color(d[o + 12], d[o + 13], d[o + 14], d[o + 15]);
        switch (kind)
        {
            case 0: EnemyAttacks.Shoot(a, b, p0, p1, p2, p3, "enemy"); break;
            case 1: EnemyAttacks.Blast(a, p0, p1, p2, tint, "enemy"); break;
            case 2: EnemyAttacks.Shockwave(a, p0, p1, p2, p3, p4, "enemy"); break;
            case 3: EnemyAttacks.Flash(a, p0, tint); break;
        }
    }

    // ================================================================ pickups

    /// <summary>Host: an orb dropped.</summary>
    public void OnPickupSpawned(Pickup p, Vector3 launch)
    {
        p.NetId = _nextPickupId++;
        Vector3 at = p.GlobalPosition;
        _ev.Add(EvPickupSpawn); _ev.Add(p.NetId); _ev.Add((int)p.Kind);
        _ev.Add(at.X); _ev.Add(at.Y); _ev.Add(at.Z); _ev.Add(p.Value);
        _ev.Add(launch.X); _ev.Add(launch.Y); _ev.Add(launch.Z);
        PickupsSynced++;
    }

    /// <summary>Host: <paramref name="peer"/> collected <paramref name="p"/>.
    /// Experience goes to everyone; gold and health to the collector.</summary>
    public void HostCollected(Pickup p, long peer)
    {
        var (kind, value) = p.NetPayload;
        if (p.NetId > 0) { _ev.Add(EvPickupGone); _ev.Add(p.NetId); }

        if (kind == PickupKind.Xp)
        {
            int xp = Mathf.Max(1, Mathf.RoundToInt(value));
            if (peer != Coop.LocalId) Inventory.Current?.GrantXp(xp);   // the host's own pickup pays it in Collect
            FlushEvents();
            Rpc(MethodName.ShareXp, xp);
        }
        else if (peer != Coop.LocalId)
        {
            FlushEvents();
            RpcId(peer, MethodName.Payout, (int)kind, value);
        }
        if (peer != Coop.LocalId) RemoteCollects++;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ShareXp(int xp)
    {
        SharedXp += xp;
        Inventory.Current?.GrantXp(xp);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Payout(int kind, float value)
    {
        switch ((PickupKind)kind)
        {
            case PickupKind.Gold:
                int gold = Mathf.Max(1, Mathf.RoundToInt(value));
                Inventory.Current?.GrantGold(gold);
                if (Player.Instance is { } me && IsInstanceValid(me))
                    EventBus.Publish(new PopupRequested(me.GlobalPosition + Vector3.Up * 1.6f, $"+{gold}", new Color(1f, 0.72f, 0.12f), 0.75f));
                break;
            case PickupKind.Health:
                Player.Instance?.Health?.HealFraction(value);
                break;
        }
    }

    // ================================================================ chests

    /// <summary>Host: a chest was placed.</summary>
    public void OnChestSpawned(Chest c)
    {
        c.NetId = _nextChestId++;
        _chests[c.NetId] = c;
        FlushEvents();
        Rpc(MethodName.ChestPlaced, c.NetId, (int)c.Tier, c.IsFree, c.GlobalPosition, c.Rotation.Y);
        ChestsSynced++;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ChestPlaced(int id, int tier, bool free, Vector3 at, float yaw)
    {
        if (!Coop.IsClient || ChestSpawner.Instance is not { } spawner) return;
        Chest? c = spawner.NetSpawn(id, (ChestTier)tier, free, at, yaw);
        if (c is null) return;
        _chests[id] = c;
        ChestsSynced++;
    }

    /// <summary>Either side: this player pressed E at <paramref name="c"/>.</summary>
    public void AskChest(Chest c)
    {
        if (Coop.IsHost) ResolveChest(c.NetId, Coop.LocalId);
        else RpcId(1, MethodName.ChestAsk, c.NetId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ChestAsk(int id)
    {
        if (Coop.IsHost) ResolveChest(id, Multiplayer.GetRemoteSenderId());
    }

    /// <summary>Host: first come, first served.</summary>
    private void ResolveChest(int id, long requester)
    {
        ChestSpawner? spawner = ChestSpawner.Instance;
        bool ok = spawner is not null && _chests.TryGetValue(id, out Chest? c) && IsInstanceValid(c)
                  && !c.IsOpen && !_claimedChests.Contains(id);
        if (!ok)
        {
            if (requester == Coop.LocalId) { if (spawner is not null && _chests.TryGetValue(id, out Chest? mine) && IsInstanceValid(mine)) spawner.NetDenied(mine); }
            else RpcId(requester, MethodName.ChestAnswer, id, false);
            return;
        }
        _claimedChests.Add(id);
        Chest chest = _chests[id];
        FlushEvents();
        foreach (long peer in Multiplayer.GetPeers())
            if (peer != requester) RpcId(peer, MethodName.ChestTaken, id, requester);

        if (requester == Coop.LocalId) spawner!.NetGranted(chest);
        else
        {
            spawner!.NetTakenBy(chest, Coop.NameOf(requester), SlotColorOf(requester));
            RpcId(requester, MethodName.ChestAnswer, id, true);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ChestAnswer(int id, bool granted)
    {
        if (ChestSpawner.Instance is not { } spawner || !_chests.TryGetValue(id, out Chest? c) || !IsInstanceValid(c)) return;
        if (granted) spawner.NetGranted(c);
        else spawner.NetDenied(c);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ChestTaken(int id, long by)
    {
        if (ChestSpawner.Instance is not { } spawner || !_chests.TryGetValue(id, out Chest? c) || !IsInstanceValid(c)) return;
        spawner.NetTakenBy(c, Coop.NameOf(by), SlotColorOf(by));
    }

    private static Color SlotColorOf(long peer) => Coop.SlotColor(Coop.Peer(peer)?.Slot ?? 0);

    // ================================================================ stage

    /// <summary>Host: phase <paramref name="index"/> began (a new map).</summary>
    public void SendPhase(int index)
    {
        if (!_go) return;   // the opening map: every machine builds it itself
        FlushEvents();
        _chests.Clear();
        _claimedChests.Clear();
        Rpc(MethodName.Phase, index);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Phase(int index)
    {
        if (!Coop.IsClient || _main is null || !IsInstanceValid(_main)) return;
        _chests.Clear();
        PhasesSeen++;
        _main.Stages.NetPhase(index);
    }

    /// <summary>Host: the phase boss arrived.</summary>
    public void SendBoss(Enemy boss)
    {
        FlushEvents();
        Rpc(MethodName.Boss, boss.NetId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Boss(int id)
    {
        if (!Coop.IsClient || _main is null || !IsInstanceValid(_main)) return;
        BossesSeen++;
        _enemies.TryGetValue(id, out Enemy? boss);
        _main.Stages.NetBoss(boss is not null && IsInstanceValid(boss) ? boss : null, boss?.Def);
    }

    /// <summary>Host: the phase boss died.</summary>
    public void SendBossDown()
    {
        FlushEvents();
        Rpc(MethodName.BossDown);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BossDown()
    {
        if (!Coop.IsClient || _main is null || !IsInstanceValid(_main)) return;
        _main.Stages.NetBossDown();
    }
}
