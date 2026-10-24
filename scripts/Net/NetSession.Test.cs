using System;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Net;

/// <summary>
/// The automated co-op check: two (or more) game processes on one machine,
/// one hosting and one joining, both on autopilot.
///
///     host:   -- --coop-host=7777 --auto [--coop-players=2] [--coop-seconds=60] [--coop-shot=host.png]
///     client: -- --coop-join=127.0.0.1:7777 --auto [--coop-seconds=60] [--coop-shot=client.png]
///
/// The host starts the run as soon as everyone has joined. The client goes
/// down on purpose at 20 s (the host's bot walks over and revives it), both
/// bots huddle for a screenshot near the end, then the client leaves and the
/// host checks it survived the departure. Each side prints one line:
///
///     COOP PASS role=host players=2 enemies_synced=... hits=... ...
///     COOP FAIL role=client reason=...
///
/// tests/coop.ps1 launches both and checks both lines.
/// </summary>
public sealed partial class NetSession
{
    private bool _testMode;
    private string _testRole = "";
    private int _testPlayers = 2;
    private float _testSeconds = 60f;
    private string? _testShot;
    private bool _testLobbyOnly, _testShootersSent;
    private int _testExpectPhase;
    private string? _testDownShot;   // --coop-down-shot: a frame while the client is down (both sides)
    private double _testDownSeen;
    private bool _testReadySent, _testDowned, _testShotTaken, _testDone, _testClientLeft;
    private double _testStartedAt;
    private float _testDownAt = 20f;

    // Host-leaves scenario: the host quits at --coop-quit-at=S; a client
    // given --coop-expect-host-leave must land on the title's CO-OP page with
    // the message, which it reports from there (the session node is gone).
    private float _testHostQuitAt = -1f;
    private bool _testExpectHostLeave;
    private static bool _autoStarted;
    private static bool _awaitTitle;
    private static string? _titleShot;

    /// <summary>True when the command line asks for an automated co-op session.</summary>
    public static bool AutoRequested()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--coop-host") || arg.StartsWith("--coop-join=")) return true;
        return false;
    }

    /// <summary>The automated session was already started in this process
    /// (the title is being shown again after it ended).</summary>
    public static bool AutoStarted => _autoStarted;

    /// <summary>The title scene came back after the session ended. In the
    /// host-leaves test that is the expected outcome: report it and quit.</summary>
    public static void TitleShown(Node menu, string page)
    {
        if (!_awaitTitle) return;
        _awaitTitle = false;
        SceneTree tree = menu.GetTree();
        tree.CreateTimer(2.0, processAlways: true).Timeout += async () =>
        {
            if (_titleShot is not null) await Screenshot.Capture(tree, menu, _titleShot);
            bool ok = page == "coop" && !string.IsNullOrEmpty(Coop.Notice) && !Coop.Active;
            GD.Print($"COOP {(ok ? "PASS" : "FAIL")} role=client host_left=1 page={page} session_closed={(!Coop.Active ? 1 : 0)} " +
                     $"notice=\"{Coop.Notice}\"");
            tree.Quit();
        };
    }

    /// <summary>Begin the automated session (called by the title scene).</summary>
    public static void StartAuto(SceneTree tree)
    {
        _autoStarted = true;
        NetSession s = Ensure(tree);
        s._testMode = true;
        s._testStartedAt = Time.GetTicksMsec() / 1000.0;
        string? join = null;
        int port = Coop.DefaultPort;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--coop-host"))
            {
                s._testRole = "host";
                if (arg.StartsWith("--coop-host=") && int.TryParse(arg["--coop-host=".Length..], out int p)) port = p;
            }
            else if (arg.StartsWith("--coop-join=")) { s._testRole = "client"; join = arg["--coop-join=".Length..]; }
            else if (arg.StartsWith("--coop-players=")) s._testPlayers = Math.Clamp(arg["--coop-players=".Length..].ToInt(), 1, Coop.MaxPlayers);
            else if (arg.StartsWith("--coop-seconds=")) s._testSeconds = Mathf.Max(30f, arg["--coop-seconds=".Length..].ToFloat());
            else if (arg.StartsWith("--coop-shot=")) s._testShot = arg["--coop-shot=".Length..];
            else if (arg.StartsWith("--coop-name=")) PlayerName = CleanName(arg["--coop-name=".Length..]);
            else if (arg.StartsWith("--coop-down-at=")) s._testDownAt = arg["--coop-down-at=".Length..].ToFloat();
            else if (arg == "--coop-lobby-only") s._testLobbyOnly = true;   // stay in the lobby (lobby screenshots)
            else if (arg.StartsWith("--coop-quit-at=")) s._testHostQuitAt = arg["--coop-quit-at=".Length..].ToFloat();
            else if (arg == "--coop-expect-host-leave") s._testExpectHostLeave = true;
            else if (arg.StartsWith("--coop-expect-phase=")) s._testExpectPhase = arg["--coop-expect-phase=".Length..].ToInt();
            else if (arg.StartsWith("--coop-down-shot=")) s._testDownShot = arg["--coop-down-shot=".Length..];
            else if (arg == "--coop-down-before-rest") s._testDownBeforeRest = s._testDownBeforeRestAsked = true;
            else if (arg == "--coop-expect-restroom") s._testExpectRest = true;
            else if (arg.StartsWith("--coop-rest-shot=")) s._testRestShot = arg["--coop-rest-shot=".Length..];
        }
        if (PlayerName == DefaultName()) PlayerName = s._testRole == "host" ? "Host" : "Guest";
        GD.Print($"[coop-test] role={s._testRole} players={s._testPlayers} seconds={s._testSeconds}");
        if (s._testRole == "host") s.Host(port);
        else s.Join(join ?? "127.0.0.1:7777");
    }

    private void ProcessTest(float dt)
    {
        if (!_testMode || _testDone) return;
        double wall = Time.GetTicksMsec() / 1000.0 - _testStartedAt;
        if (wall > _testSeconds + 120)
        {
            TestResult(false, "timed out");
            return;
        }

        if (!Coop.InRun)
        {
            if (Coop.IsHost && !_testLobbyOnly && Coop.Roster.Count >= _testPlayers && Coop.Roster.TrueForAll(p => p.Ready)) StartGame();
            if (Coop.IsClient && !_testReadySent && Coop.LocalPeer is not null)
            {
                _testReadySent = true;
                SetReady(true);
            }
            return;
        }
        if (!_go || _main is null || !IsInstanceValid(_main)) return;

        float t = _main.Run.Seconds;
        Player? me = Player.Instance;
        TestRestRoom((float)GetProcessDeltaTime());

        if (Coop.IsHost && _testHostQuitAt > 0f)
        {
            if (t < _testHostQuitAt) return;
            _testDone = true;
            bool ok = PeakPlayers >= _testPlayers && StatesReceived >= 50;
            GD.Print($"COOP {(ok ? "PASS" : "FAIL")} role=host quit_early=1 players={PeakPlayers} states_rx={StatesReceived} " +
                     $"enemies_synced={EnemiesSynced} t={_main.Run.Clock}");
            GetTree().Quit();
            return;
        }
        if (_testExpectHostLeave) return;   // wait for the host to go

        // Ranged enemies next to the host, so mirrored enemy shots are exercised
        // inside a minute (the director would not field one this early).
        if (Coop.IsHost && !_testShootersSent && t >= 10f && me is not null && EnemyManager.Instance is { } em)
        {
            _testShootersSent = true;
            foreach (Data.EnemyDef def in Data.Catalog.Enemies)
            {
                if (def.Behavior != Data.EnemyBehavior.Shooter || def.IsBoss) continue;
                for (int k = 0; k < 3; k++)
                    em.SpawnAt(def, me.GlobalPosition + new Vector3(Mathf.Cos(k * 2.1f), 0f, Mathf.Sin(k * 2.1f)) * 9f);
                GD.Print($"[coop-test] spawned 3 x {def.Id} to test mirrored shots");
                break;
            }
        }

        if (Coop.IsClient && !_testDowned && t >= _testDownAt && me is not null && me.IsAlive)
        {
            _testDowned = true;
            GD.Print($"[coop-test] going down on purpose at t={t:0.0}");
            me.Health.Kill();
        }

        // One frame while the client is down: the host sees the ring and the
        // tag over it, the client the revive line.
        bool someoneDown = me is { IsAlive: false };
        foreach (RemotePlayer r in _remotes.Values) someoneDown |= IsInstanceValid(r) && r.Down;
        if (_testDownShot is not null && someoneDown)
        {
            _testDownSeen += GetProcessDeltaTime();
            if (_testDownSeen >= 1.5)
            {
                string path = _testDownShot;
                _testDownShot = null;
                _ = Screenshot.Capture(GetTree(), this, path);
            }
        }

        if (!_testShotTaken && t >= _testSeconds - 8f && _testShot is not null)
        {
            _testShotTaken = true;
            _ = Screenshot.Capture(GetTree(), this, _testShot);
        }

        if (Coop.IsClient && t >= _testSeconds - 5f)
        {
            TestResult(true, "");
            return;
        }

        if (Coop.IsHost && (t >= _testSeconds || (_testClientLeft && t >= _testSeconds - 3f)))
            TestResult(true, "");
    }

    /// <summary>Host: the client left; the run must carry on without it.</summary>
    private void TestNoteLeft() => _testClientLeft = true;

    private void TestFail(string reason)
    {
        if (_testMode && !_testDone) TestResult(false, reason);
    }

    private void TestHostLeft()
    {
        if (!_testMode || _testDone || !Coop.IsClient) return;
        if (_testExpectHostLeave)
        {
            // Expected: the title must come back on the CO-OP page (TitleShown).
            _testDone = true;
            _awaitTitle = true;
            _titleShot = _testShot;
            GD.Print($"[coop-test] host left as expected; enemies_synced={EnemiesSynced} states_rx={StatesReceived}");
            return;
        }
        TestResult(false, "the host left before the test finished");
    }

    private void TestResult(bool reachedEnd, string reason)
    {
        _testDone = true;
        Player? me = Player.Instance;
        int level = Progression.Inventory.Current?.Level ?? 0;
        int kills = _main?.Run.Kills ?? 0;
        int phase = _main?.Run.Phase ?? 0;
        string role = string.IsNullOrEmpty(_testRole) ? (Coop.IsHost ? "host" : "client") : _testRole;
        string line;
        var problems = new System.Collections.Generic.List<string>();
        if (!reachedEnd) problems.Add(reason);

        if (role == "host")
        {
            if (PeakPlayers < _testPlayers) problems.Add($"only {PeakPlayers} players joined");
            if (EnemiesSynced == 0) problems.Add("no enemies were sent");
            if (HitsApplied == 0) problems.Add("no client hit reached the host");
            if (StatesReceived < 100) problems.Add($"only {StatesReceived} player states received");
            if (reachedEnd && !_testClientLeft) problems.Add("the client's departure was not seen");
            if (AttacksMirrored == 0) problems.Add("no enemy attack was mirrored");
            if (_testExpectPhase > 0 && phase < _testExpectPhase) problems.Add($"only reached phase {phase}");
            int rests = _main?.Stages.RestRoomsVisited ?? 0;
            string restExit = _main?.Stages.RestExitReason ?? "";
            if (_testExpectRest && rests == 0) problems.Add("never entered the rest room");
            if (_testExpectRest && rests > 0 && restExit != "all") problems.Add($"left the rest room by '{restExit}', not with everyone in the portal");
            line = $"role=host rest_rooms={rests} rest_exit={(restExit.Length > 0 ? restExit : "-")} players={PeakPlayers} enemies_synced={EnemiesSynced} hits={HitsApplied} kills={kills} phase={phase} " +
                   $"pickups={PickupsSynced} remote_collects={RemoteCollects} chests={ChestsSynced} attacks={AttacksMirrored} " +
                   $"states_rx={StatesReceived} hurts_sent={HurtsSent} client_left={(_testClientLeft ? 1 : 0)} " +
                   $"remotes_now={_remotes.Count} level={level} t={_main?.Run.Clock ?? "-"}";
        }
        else
        {
            int downs = me?.Health.Downs ?? 0, revives = me?.Health.Revives ?? 0;
            if (EnemiesSynced == 0) problems.Add("no enemies arrived");
            if (EnemyStatesReceived < 50) problems.Add($"only {EnemyStatesReceived} enemy state packets");
            if (HitsQueued == 0) problems.Add("no hits sent");
            if (PuppetKills == 0) problems.Add("no kill was confirmed by the host");
            if (StatesReceived < 100) problems.Add($"only {StatesReceived} host states received");
            if (PickupsSynced == 0) problems.Add("no drops arrived");
            if (_testDowned && revives == 0) problems.Add("went down and was never revived");
            if (AttacksReceived == 0) problems.Add("no mirrored enemy attack arrived");
            if (_testExpectPhase > 0 && (phase < _testExpectPhase || BossesSeen == 0)) problems.Add($"phase {phase}, bosses seen {BossesSeen}");
            if (_testExpectRest && RestRoomsSeen == 0) problems.Add("the host never took this player into the rest room");
            if (_testExpectRest && RestRoomsSeen > 0 && phase < 2) problems.Add("still in the rest room / not on map 2");
            int restShops = Game.Interactables.Instance?.MerchantBuys ?? 0;
            if (_testExpectRest && RestRoomsSeen > 0 && Game.Interactables.Instance?.Rests == 0) problems.Add("never rested at the campfire");
            if (_testExpectRest && _testDownBeforeRestAsked && RestRevives == 0) problems.Add("arrived downed and was not revived");
            line = $"role=client rest_rooms={RestRoomsSeen} rest_revives={RestRevives} merchant_buys={restShops} players={Math.Max(PeakPlayers, Coop.Roster.Count)} phase={phase} bosses_seen={BossesSeen} " +
                   $"attacks_rx={AttacksReceived} enemies_synced={EnemiesSynced} hits={HitsQueued} " +
                   $"confirmed_kills={PuppetKills} kills={kills} pickups={PickupsSynced} xp_shared={SharedXp} chests={ChestsSynced} " +
                   $"states_rx={StatesReceived} enemy_states={EnemyStatesReceived} hurts_rx={HurtsReceived} " +
                   $"downed={downs} revived={revives} host_visible={(_remotes.Count > 0 ? 1 : 0)} level={level} t={_main?.Run.Clock ?? "-"}";
        }

        line += " " + AwarenessTestCheck(role, problems);   // NetSession.Awareness.cs
        GD.Print(problems.Count == 0 ? $"COOP PASS {line}" : $"COOP FAIL {line} reason=\"{string.Join("; ", problems)}\"");
        if (_main is not null && IsInstanceValid(_main)) GD.Print(_main.Summary());

        // The client leaves first; the host waits for it, then quits.
        SceneTree tree = GetTree();
        tree.CreateTimer(Coop.IsHost ? 1.0 : 0.5, processAlways: true).Timeout += () => tree.Quit();
    }

    // ------------------------------------------------------------ autopilot

    /// <summary>
    /// Co-op steering for the bot: revive a downed teammate first, keep within
    /// shouting distance of the team, and (in the automated test) huddle in
    /// front of the host's camera before the screenshots so both players are
    /// in both frames. Returns the adjusted wish.
    /// </summary>
    public Vector3 AutopilotSteer(Vector3 me, Vector3 wish)
    {
        if (!Coop.InRun || _remotes.Count == 0) return wish;

        RemotePlayer? nearest = null;
        float nearestSq = float.MaxValue;
        foreach (RemotePlayer r in _remotes.Values)
        {
            if (!IsInstanceValid(r) || !r.HasState) continue;
            Vector3 d = r.GlobalPosition - me;
            d.Y = 0f;
            if (r.Down)
            {
                // A downed teammate comes first: stand on them.
                return d.Length() > 1.2f ? d.Normalized() * 3f : Vector3.Zero;
            }
            if (d.LengthSquared() < nearestSq) { nearestSq = d.LengthSquared(); nearest = r; }
        }
        if (nearest is null) return wish;

        if (_testMode && Coop.IsClient && _main is not null && _main.Run.Seconds >= _testSeconds - 15f)
        {
            // Stand a little behind and beside the host along this camera's
            // view: the host is then ahead of this camera, and (cameras on the
            // same heading) this player is between the host and theirs.
            float yaw = PlayerCamera.Instance?.Yaw ?? nearest.CameraYaw;
            var fwd = new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
            var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
            Vector3 spot = nearest.GlobalPosition - fwd * 2.6f + right * 1.2f - me;
            spot.Y = 0f;
            return spot.Length() > 0.6f ? spot.Normalized() * 3f : Vector3.Zero;
        }

        float dist = Mathf.Sqrt(nearestSq);
        if (dist > 14f)
        {
            Vector3 to = (nearest.GlobalPosition - me) with { Y = 0f };
            return wish + to.Normalized() * Mathf.Min(3f, (dist - 14f) * 0.3f + 0.8f);
        }
        return wish;
    }
}
