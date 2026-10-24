using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Core;
using Wavebreak.Game;

namespace Wavebreak.Net;

/// <summary>
/// Team awareness traffic: the scoreboard and the boss-kill toast.
///
///   ScoreSelf  (client -> host, reliable, 1 Hz)   downs, level, damage taken, gold earned
///   ScoreSync  (host -> all, reliable, 1 Hz)      the merged table (Game/RunScoreboard)
///   RevivedBy  (client -> host, reliable)         who filled this player's revive
///   BossSlain  (host -> all + local, reliable)    who landed the killing blow on a boss
///
/// The host tracks the last hitter of every enemy (Actors/Enemy.Awareness.cs),
/// so the credit for a kill is the host's call, the same as the kill itself.
/// Minimap, name tags and the down alert need no traffic of their own: they
/// read the 20 Hz player state and the enemy snapshots already flowing.
/// </summary>
public sealed partial class NetSession
{
    private const float ScoreInterval = 1f;
    private float _scoreTimer;

    // Counters for the automated test.
    public int BossToasts { get; private set; }
    public long LastBossKiller { get; private set; }
    public string LastBossKillerName { get; private set; } = "";
    public int ScoreSyncsReceived { get; private set; }

    private void ProcessAwareness(float dt)
    {
        _scoreTimer -= dt;
        if (_scoreTimer <= 0f)
        {
            _scoreTimer = ScoreInterval;
            RunScoreboard.RefreshLocal();
            if (Coop.IsHost)
            {
                if (Multiplayer.GetPeers().Length > 0) Rpc(MethodName.ScoreSync, RunScoreboard.Pack());
            }
            else
            {
                (int downs, int level, int taken, int gold) = RunScoreboard.LocalSelf();
                RpcId(1, MethodName.ScoreSelf, downs, level, taken, gold);
            }
        }
        StepAwarenessTest();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ScoreSelf(int downs, int level, int taken, int gold)
    {
        if (!Coop.IsHost) return;
        RunScoreboard.ApplySelf(Multiplayer.GetRemoteSenderId(), downs, level, taken, gold);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ScoreSync(long[] packed)
    {
        if (!Coop.IsClient) return;
        ScoreSyncsReceived++;
        RunScoreboard.Apply(packed);
    }

    /// <summary>This (downed) player was just revived by <paramref name="helper"/>.</summary>
    private void CreditRevive(long helper)
    {
        if (helper == 0) return;
        if (Coop.IsHost) RunScoreboard.NoteRevive(helper);
        else RpcId(1, MethodName.RevivedBy, helper);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RevivedBy(long helper)
    {
        if (Coop.IsHost) RunScoreboard.NoteRevive(helper);
    }

    /// <summary>Host: a boss died to <paramref name="killer"/>'s hit. Everyone,
    /// the host included, shows the toast.</summary>
    public void SendBossSlain(long killer, string boss)
    {
        FlushEvents();
        Rpc(MethodName.BossSlain, killer, Coop.NameOf(killer), boss);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BossSlain(long killer, string killerName, string boss)
    {
        BossToasts++;
        LastBossKiller = killer;
        LastBossKillerName = killerName;
        GD.Print($"[coop] {killerName} ({killer}) defeated {boss}");
        EventBus.Publish(new BossDefeated(killer, killerName, boss));
        if (_testBossShot is not null) _testBossShotAt = Time.GetTicksMsec() / 1000.0 + 1.2;
    }

    // ================================================================ automated test
    //
    // --coop-check-hud (both sides): the client lands the killing blow on the
    // first boss through the normal hit path, and both sides then require the
    // toast to credit the client, a scoreboard with kills for both players,
    // and a down marker (host: the client's SOS on the minimap and the alert;
    // client: its own SOS). --coop-board-shot / --coop-boss-shot photograph
    // the scoreboard and the toast.

    private bool _testHudParsed, _testCheckHud;
    private string? _testBoardShot, _testBossShot, _testPingShot;
    private bool _testPinged;
    private double _testPingShotAt = -1;
    private double _testBossShotAt = -1, _testBoardShotAt = -1;
    private double _testNextBossHit;
    private string _testClientName = "";

    private void ParseAwarenessTest()
    {
        _testHudParsed = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--coop-check-hud") _testCheckHud = true;
            else if (arg.StartsWith("--coop-board-shot=")) _testBoardShot = arg["--coop-board-shot=".Length..];
            else if (arg.StartsWith("--coop-boss-shot=")) _testBossShot = arg["--coop-boss-shot=".Length..];
            else if (arg.StartsWith("--coop-ping-shot=")) _testPingShot = arg["--coop-ping-shot=".Length..];
        }
    }

    private void StepAwarenessTest()
    {
        if (!_testMode || _testDone || _main is null || !IsInstanceValid(_main)) return;
        if (!_testHudParsed) ParseAwarenessTest();
        double now = Time.GetTicksMsec() / 1000.0;
        float t = _main.Run.Seconds;

        if (Coop.IsHost)
            foreach (RemotePlayer r in _remotes.Values)
                if (IsInstanceValid(r)) _testClientName = r.DisplayName;

        // Client: once the boss is up, finish it with one big hit of ours.
        if (_testCheckHud && Coop.IsClient && BossToasts == 0 && now >= _testNextBossHit)
        {
            foreach (KeyValuePair<int, Enemy> kv in _enemies)
            {
                if (!IsInstanceValid(kv.Value) || !kv.Value.IsBoss || kv.Value.IsDead) continue;
                if (_testNextBossHit == 0) { _testNextBossHit = now + 1.5; break; }   // let it walk in first
                GD.Print($"[coop-test] client lands the killing blow on boss {kv.Key} at t={t:0.0}");
                QueueHit(kv.Key, 1e9f, 0f, Vector3.Zero);
                _testNextBossHit = now + 1.0;
                break;
            }
        }

        // Pings: at 32 s the client sends five in a burst (past its own
        // limit, straight to the host) in front of the host's camera; the host
        // must pass exactly three on. The host pings once at 34 s.
        if (_testCheckHud && !_testPinged && _remotes.Count > 0)
        {
            RemotePlayer? other = null;
            foreach (RemotePlayer r in _remotes.Values) if (IsInstanceValid(r) && r.HasState) other = r;
            if (other is not null && Coop.IsClient && t >= 32f)
            {
                _testPinged = true;
                var fwd = new Vector3(-Mathf.Sin(other.CameraYaw), 0f, -Mathf.Cos(other.CameraYaw));
                Vector3 at = other.GlobalPosition + fwd * 5f;
                GD.Print($"[coop-test] client pings 5 times at {at}");
                for (int i = 0; i < 5; i++) RpcId(1, MethodName.PingAsk, (int)PingKind.GoHere, at + Vector3.Right * i * 0.5f);
            }
            else if (other is not null && Coop.IsHost && t >= 34f && Player.Instance is { } hostMe)
            {
                _testPinged = true;
                UI.PingMarkers.Place(PingKind.Help, hostMe.GlobalPosition);
            }
        }
        if (_testPingShot is not null && _testPingShotAt < 0 && Pings.ShownFromOthers > 0) _testPingShotAt = now + 0.5;
        if (_testPingShot is not null && _testPingShotAt > 0 && now >= _testPingShotAt)
        {
            string path = _testPingShot;
            _testPingShot = null;
            _ = Screenshot.Capture(GetTree(), this, path);
        }

        if (_testBossShot is not null && _testBossShotAt > 0 && now >= _testBossShotAt)
        {
            string path = _testBossShot;
            _testBossShot = null;
            _ = Screenshot.Capture(GetTree(), this, path);
        }

        if (_testBoardShot is not null)
        {
            if (_testBoardShotAt < 0 && t >= _testSeconds - 14f)
            {
                UI.ScoreboardPanel.ForceShow = true;
                _testBoardShotAt = now + 0.8;
            }
            else if (_testBoardShotAt > 0 && now >= _testBoardShotAt)
            {
                string path = _testBoardShot;
                _testBoardShot = null;
                _ = CaptureBoard(path);
            }
        }
    }

    private async System.Threading.Tasks.Task CaptureBoard(string path)
    {
        await Screenshot.Capture(GetTree(), this, path);
        UI.ScoreboardPanel.ForceShow = false;
    }

    /// <summary>Extra checks and fields for the COOP line.</summary>
    private string AwarenessTestCheck(string role, List<string> problems)
    {
        if (!_testHudParsed) ParseAwarenessTest();
        RunScoreboard.RefreshLocal();
        IReadOnlyList<ScoreLine> lines = RunScoreboard.All;
        var kills = new List<string>();
        int downsTotal = 0, revivesTotal = 0;
        foreach (ScoreLine l in lines)
        {
            kills.Add($"{l.Name}:{l.Kills}/{l.BossKills}/{l.Downs}/{l.Revives}/{RunScoreboard.Short(l.DamageDealt)}");
            downsTotal += l.Downs;
            revivesTotal += l.Revives;
        }
        string fields = $"boss_toasts={BossToasts} boss_killer=\"{LastBossKillerName}\" board=[{string.Join(",", kills)}] " +
                        $"pings_from_clients={PingsFromClients} pings_shown={Pings.Shown} pings_from_others={Pings.ShownFromOthers} ping_drawn={UI.PingMarkers.RemoteDrawn} " +
                        $"board_syncs={ScoreSyncsReceived} down_markers={UI.Minimap.DownMarkersDrawn} down_alerts={UI.AwarenessAlerts.DownAlertsShown}";
        if (!_testCheckHud) return fields;

        if (BossToasts == 0) problems.Add("no boss toast");
        // The client may have left by now, so the host checks the name it was sent.
        else if (role == "host" ? LastBossKiller == Coop.LocalId || LastBossKillerName != _testClientName
                                : LastBossKiller != Coop.LocalId)
            problems.Add($"the boss kill was credited to {LastBossKillerName} ({LastBossKiller}), not the client");
        if (lines.Count < 2) problems.Add($"scoreboard has {lines.Count} lines");
        foreach (ScoreLine l in lines)
            if (l.Kills <= 0) problems.Add($"scoreboard shows no kills for {l.Name}");
        if (role == "client" && ScoreSyncsReceived == 0) problems.Add("no scoreboard sync arrived");
        if (UI.Minimap.DownMarkersDrawn == 0) problems.Add("no down marker on the minimap");
        if (Pings.ShownFromOthers == 0 || UI.PingMarkers.RemoteDrawn == 0) problems.Add("no ping from the other player was shown");
        if (role == "host" && PingsFromClients != Pings.Burst) problems.Add($"the host passed on {PingsFromClients} of the client's 5 burst pings, not {Pings.Burst}");
        if (role == "client" && Pings.Shown - Pings.ShownFromOthers == 0) problems.Add("the client's own ping did not come back from the host");
        if (role == "host")
        {
            if (UI.AwarenessAlerts.DownAlertsShown == 0) problems.Add("no down alert");
            if (downsTotal > 0 && revivesTotal == 0) problems.Add("a revive was not credited");
        }
        return fields;
    }
}
