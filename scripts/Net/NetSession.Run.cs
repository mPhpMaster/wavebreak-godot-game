using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// The run, player side: loading in step, every player's transform and
/// health, enemy hits on remote players, down and revive, and the end.
///
/// Each player's body and health are simulated by the machine that plays them
/// (movement must not wait for a round trip), and sent to everyone 20 times a
/// second. Everything that is not a player's own — enemies, drops, chests,
/// the phase flow — is the host's.
/// </summary>
public sealed partial class NetSession
{
    private const float StateInterval = 0.05f;   // 20 Hz
    private const float ClockInterval = 0.5f;
    private const double LoadTimeout = 30.0;

    private readonly Dictionary<long, RemotePlayer> _remotes = new();
    private readonly HashSet<long> _loaded = new();
    private Main? _main;
    private bool _go;
    private double _loadStarted;
    private float _stateTimer;
    private float _clockTimer;
    private float _revive;
    private bool _runEndedHooked;

    public IReadOnlyDictionary<long, RemotePlayer> Remotes => _remotes;

    /// <summary>Local revive progress while down, 0..1.</summary>
    public float LocalRevive01 => Mathf.Clamp(_revive / Coop.ReviveSeconds, 0f, 1f);

    /// <summary>The run has started on every machine (or the wait timed out).</summary>
    public bool Started => _go;

    // Counters for the automated test and the log.
    public int StatesReceived { get; private set; }
    public int HurtsReceived { get; private set; }
    public int HurtsSent { get; private set; }
    public int PeakPlayers { get; private set; }

    private void BeginLoading()
    {
        _loaded.Clear();
        _go = false;
        _main = null;
        _loadStarted = Time.GetTicksMsec() / 1000.0;
        PeakPlayers = Mathf.Max(PeakPlayers, Coop.Roster.Count);
    }

    /// <summary>Called by <see cref="Main"/> once its world is built. Holds the
    /// world still until every player has loaded, so nobody starts behind.</summary>
    public void BeginRun(Main main)
    {
        _main = main;
        _revive = 0f;
        Game.Trade.TradeLedger.Reset();   // NetSession.Trade.cs: a new run holds no escrow
        Game.Trade.TradeView.Reset();
        Pickup.ResetNet();
        ResetWorldState();
        foreach (CoopPeer p in Coop.Roster)
            if (p.Id != Coop.LocalId) AddRemote(p);
        PeakPlayers = Mathf.Max(PeakPlayers, Coop.Roster.Count);

        var layer = new CanvasLayer { Name = "CoopUI", Layer = 4 };
        main.AddChild(layer);
        layer.AddChild(new UI.CoopHud());

        EventBus.Subscribe<RunEnded>(OnRunEnded);
        _runEndedHooked = true;

        GetTree().Paused = true;
        GD.Print($"[coop] run loaded as {(Coop.IsHost ? "host" : "client")} {Coop.LocalId}, waiting for the team");
        if (Coop.IsHost) MarkLoaded(1);
        else RpcId(1, MethodName.Loaded);
    }

    private void AddRemote(CoopPeer p)
    {
        if (_main is null || _remotes.ContainsKey(p.Id)) return;
        var r = new RemotePlayer(p);
        _main.AddChild(r);
        r.Place(Arena.Centre + Coop.SlotOffset(p.Slot) + Vector3.Up * 0.3f);
        _remotes[p.Id] = r;
    }

    /// <summary>Put every remote avatar on its arrival spot (new map).</summary>
    public void PlaceRemotes()
    {
        foreach (RemotePlayer r in _remotes.Values)
            if (IsInstanceValid(r)) r.Place(Arena.Centre + Coop.SlotOffset(r.Slot) + Vector3.Up * 0.3f);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Loaded()
    {
        if (Coop.IsHost) MarkLoaded(Multiplayer.GetRemoteSenderId());
    }

    private void MarkLoaded(long id)
    {
        if (_go)
        {
            // A late joiner (NetSession.Snapshot.cs): the world, then its own Go.
            _loaded.Add(id);
            GD.Print($"[coop] {Coop.NameOf(id)} loaded the run in progress");
            SendWorldSnapshot(id, lateJoin: true);
            RpcId(id, MethodName.Go);
            return;
        }
        _loaded.Add(id);
        GD.Print($"[coop] {Coop.NameOf(id)} loaded ({_loaded.Count}/{Coop.Roster.Count})");
        TryGo(force: false);
    }

    private void TryGo(bool force)
    {
        if (_go || _main is null) return;
        bool all = Coop.Roster.TrueForAll(p => _loaded.Contains(p.Id));
        if (!all && !force) return;
        FlushEvents();
        Rpc(MethodName.Go);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Go()
    {
        _go = true;
        GetTree().Paused = false;
        GD.Print("[coop] go");
        Changed?.Invoke();
    }

    /// <summary>Main left the tree: the session ends with the run.</summary>
    public void RunSceneExited()
    {
        if (Coop.Active) Leave();
    }

    private void EndRunState()
    {
        if (_runEndedHooked)
        {
            EventBus.Unsubscribe<RunEnded>(OnRunEnded);
            _runEndedHooked = false;
        }
        foreach (RemotePlayer r in _remotes.Values)
            if (IsInstanceValid(r)) r.QueueFree();
        _remotes.Clear();
        _main = null;
        _go = false;
        Pickup.ResetNet();
        ResetWorldState();
        Coop.InRun = false;
        Coop.ClearHold();
    }

    // ------------------------------------------------------------ per frame

    private void ProcessRun(float dt)
    {
        if (!Coop.InRun || _main is null || !IsInstanceValid(_main) || !_main.IsInsideTree()) return;

        if (!_go)
        {
            if (Coop.IsHost && Time.GetTicksMsec() / 1000.0 - _loadStarted > LoadTimeout)
            {
                GD.Print("[coop] not everyone loaded in time; starting anyway");
                TryGo(force: true);
            }
            return;
        }

        Coop.TickHold(dt);
        RunState run = _main.Run;
        Player? me = Player.Instance;

        // Between hosts (NetSession.Migration.cs): play on locally, send nothing.
        if (_migrating)
        {
            if (!run.Over && me is not null && IsInstanceValid(me)) StepRevive(me, dt);
            return;
        }

        _stateTimer -= dt;
        if (_stateTimer <= 0f && me is not null && IsInstanceValid(me))
        {
            _stateTimer = StateInterval;
            int flags = me.NetPoseFlags | (me.IsAlive ? 0 : 16);
            float cam = PlayerCamera.Instance?.Yaw ?? 0f;
            Rpc(MethodName.PState, me.GlobalPosition, me.FacingYaw, me.NetVelocity, flags,
                me.Health?.Fraction ?? 0f, me.Stats.Get(Stats.Stat.PickupRange), LocalRevive01, cam);
        }

        if (!run.Over && me is not null && IsInstanceValid(me)) StepRevive(me, dt);

        if (Coop.IsHost)
        {
            StepHostWorld(dt);
            StepRunSnapshot(dt);   // NetSession.Migration.cs
            _clockTimer -= dt;
            if (_clockTimer <= 0f)
            {
                _clockTimer = ClockInterval;
                Rpc(MethodName.Clock, run.Seconds, run.PhaseSeconds);
            }
            if (!run.Over && EveryoneDown(me))
            {
                GD.Print("[coop] everyone is down; run over");
                run.Finish(survived: _main.Stages.Won);
            }
        }
        ProcessAwareness(dt);   // scoreboard sync, awareness test (NetSession.Awareness.cs)
        FlushEvents();
        FlushHits();
    }

    private bool EveryoneDown(Player? me)
    {
        if (me is not null && IsInstanceValid(me) && me.IsAlive) return false;
        foreach (RemotePlayer r in _remotes.Values)
            if (IsInstanceValid(r) && !r.Down) return false;
        return true;
    }

    /// <summary>Down: a teammate standing close fills the revive; walking away
    /// lets it drain slowly.</summary>
    private void StepRevive(Player me, float dt)
    {
        if (me.IsAlive)
        {
            _revive = 0f;
            return;
        }
        bool helped = false;
        long helper = 0;
        foreach (RemotePlayer r in _remotes.Values)
        {
            if (!IsInstanceValid(r) || r.Down || !r.HasState) continue;
            Vector3 d = r.GlobalPosition - me.GlobalPosition;
            d.Y = 0f;
            if (d.LengthSquared() <= Coop.ReviveRadius * Coop.ReviveRadius) { helped = true; helper = r.PeerId; break; }
        }
        _revive = helped ? _revive + dt : Mathf.Max(0f, _revive - dt * 0.5f);
        if (_revive < Coop.ReviveSeconds) return;
        _revive = 0f;
        me.Health.Revive(Coop.ReviveHealth);
        CreditRevive(helper);   // scoreboard (NetSession.Awareness.cs)
    }

    // ------------------------------------------------------------ player state

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 1)]
    private void PState(Vector3 pos, float yaw, Vector3 vel, int flags, float health, float range, float revive, float cameraYaw)
    {
        long id = Multiplayer.GetRemoteSenderId();
        if (!_remotes.TryGetValue(id, out RemotePlayer? r) || !IsInstanceValid(r)) return;
        bool wasDown = r.Down;
        r.ApplyState(pos, yaw, vel, flags, health, range, revive, cameraYaw);
        StatesReceived++;
        if (r.Down != wasDown)
            Popup(Loc.F(r.Down ? "{0} is down!" : "{0} is back up!", r.DisplayName),
                  r.Down ? new Color(1f, 0.45f, 0.4f) : new Color(0.45f, 1f, 0.55f));
    }

    /// <summary>Host: an enemy hit a remote player; their machine resolves it.</summary>
    public void HurtPeer(long peer, DamageInfo info)
    {
        if (!Coop.IsHost || info.Amount <= 0f) return;
        HurtsSent++;
        RpcId(peer, MethodName.Hurt, info.Amount, info.Knockback, info.Direction, info.Source ?? "");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hurt(float amount, float knockback, Vector3 direction, string source)
    {
        HurtsReceived++;
        if (Player.Instance is not { } me || !IsInstanceValid(me)) return;
        me.TakeDamage(new DamageInfo
        {
            Amount = amount,
            Element = Element.Physical,
            CanCrit = false,
            Knockback = knockback,
            Direction = direction,
            ProcCoefficient = 1f,
            Source = source,
        });
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 1)]
    private void Clock(float seconds, float phaseSeconds)
    {
        if (Coop.IsClient) _main?.Run.SyncClock(seconds, phaseSeconds);
    }

    /// <summary>Host: living players for the enemy director to chase.</summary>
    public void FillTargets(List<(Node3D Node, IDamageable Hit)> into)
    {
        if (Player.Instance is { } me && IsInstanceValid(me) && me.IsAlive) into.Add((me, me));
        foreach (RemotePlayer r in _remotes.Values)
            if (IsInstanceValid(r) && r.IsAlive) into.Add((r, r));
    }

    /// <summary>Host: living players who can collect orbs, with their magnet range.</summary>
    public void FillCollectors(List<(long Peer, Vector3 Hip, float Range)> into)
    {
        if (Player.Instance is { } me && IsInstanceValid(me) && me.IsAlive)
            into.Add((Coop.LocalId, me.GlobalPosition + Vector3.Up * 0.9f, me.Stats.Get(Stats.Stat.PickupRange)));
        foreach (RemotePlayer r in _remotes.Values)
            if (IsInstanceValid(r) && r.IsAlive) into.Add((r.PeerId, r.GlobalPosition + Vector3.Up * 0.9f, r.PickupRange));
    }

    /// <summary>Host: does anyone on the team want a med-pack?</summary>
    public bool AnyPlayerBelow(float fraction)
    {
        if (Player.Instance is { } me && IsInstanceValid(me) && me.IsAlive && me.Health.Fraction < fraction) return true;
        foreach (RemotePlayer r in _remotes.Values)
            if (IsInstanceValid(r) && r.IsAlive && r.Health01 < fraction) return true;
        return false;
    }

    /// <summary>Host: a living remote player is standing in the portal.</summary>
    public bool AnyRemoteInside(Portal portal)
    {
        foreach (RemotePlayer r in _remotes.Values)
            if (IsInstanceValid(r) && r.IsAlive && portal.Contains(r.GlobalPosition)) return true;
        return false;
    }

    // ------------------------------------------------------------ leaving / ending

    /// <summary>Host: a client left mid-run. Their avatar goes; the run goes on.</summary>
    private void PeerLeftRun(long id, string name)
    {
        _loaded.Remove(id);
        if (_remotes.Remove(id, out RemotePlayer? r) && IsInstanceValid(r)) r.QueueFree();
        if (!Coop.InRun) return;
        TestNoteLeft();
        Popup(Loc.F("{0} left the game", name), new Color(1f, 0.8f, 0.4f));
        if (!_go) TryGo(force: false);
    }

    /// <summary>Client: the roster changed mid-run. Avatars are matched by
    /// slot, which survives a host migration (peer ids do not): a matching
    /// avatar takes the new id, a new player gets one (late join), and an
    /// avatar whose player is gone leaves.</summary>
    private void SyncRemotes()
    {
        if (!Coop.InRun) return;
        var bySlot = new Dictionary<int, RemotePlayer>();
        foreach (RemotePlayer r in _remotes.Values)
            if (IsInstanceValid(r)) bySlot[r.Slot] = r;
        var next = new Dictionary<long, RemotePlayer>();
        var added = new List<CoopPeer>();
        foreach (CoopPeer p in Coop.Roster)
        {
            if (p.Id == Coop.LocalId) continue;
            if (bySlot.Remove(p.Slot, out RemotePlayer? r))
            {
                if (r.PeerId != p.Id) RunScoreboard.Rekey(r.PeerId, p.Id);
                r.PeerId = p.Id;
                next[p.Id] = r;
            }
            else added.Add(p);
        }
        foreach (RemotePlayer gone in bySlot.Values)
        {
            Popup(Loc.F("{0} left the game", gone.DisplayName), new Color(1f, 0.8f, 0.4f));
            gone.QueueFree();
        }
        _remotes.Clear();
        foreach (KeyValuePair<long, RemotePlayer> kv in next) _remotes[kv.Key] = kv.Value;
        if (_main is null) return;
        foreach (CoopPeer p in added)
        {
            AddRemote(p);
            PeakPlayers = Mathf.Max(PeakPlayers, Coop.Roster.Count);
            if (_go) Popup(Loc.F("{0} joined the game", p.Name), new Color(0.55f, 1f, 0.65f));
        }
    }

    private void OnRunEnded(RunEnded e)
    {
        if (Coop.IsHost) Rpc(MethodName.RunOver, e.Survived);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RunOver(bool survived)
    {
        if (!Coop.IsClient || _main is null || !IsInstanceValid(_main)) return;
        GD.Print($"[coop] host ended the run (survived={survived})");
        _main.Run.Finish(survived);
    }

    private static void Popup(string text, Color color)
    {
        if (Player.Instance is not { } me || !IsInstanceValid(me)) return;
        EventBus.Publish(new PopupRequested(me.GlobalPosition + Vector3.Up * 3.4f, text, color, 1.2f));
    }
}
