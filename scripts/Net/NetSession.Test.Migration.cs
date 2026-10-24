using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Game;

namespace Wavebreak.Net;

/// <summary>
/// Automated checks for host migration and late join (tests/coop.ps1).
///
///   --coop-expect-migration   a survivor: the host quits mid-run (its
///                             --coop-quit-at); this machine must carry on,
///                             as the new host or as a client of it. Checks:
///                             the migration happened, enemies kept moving,
///                             the run clock kept going, the phase advanced
///                             (--coop-expect-phase), and the other survivor
///                             (if any) came back (rejoins / resync).
///   --coop-toast-shot=PNG     a frame with the "is now the host" toast
///   --coop-no-migrate         the fallback: on host loss end the run with
///                             the results screen; checks it is on screen
///   --coop-late-join          this client joins a run in progress: it must
///                             get the snapshot (enemies, phase), arrive at
///                             the team's level, and play (hits, kills)
///   --coop-expect-late-join   the host: someone joined mid-run
/// </summary>
public sealed partial class NetSession
{
    private bool _testMigArgs, _testExpectMigration, _testNoMigrate, _testLateJoinClient, _testExpectLateJoin;
    private string? _testToastShot;
    private double _testToastAt = -1;
    private int _testPhaseAtMigration = -1;
    private int _testEnemyStatesAtMigration;
    private float _testEnemyTravel;     // metres enemies moved after the migration (sampled 1 Hz)
    private double _testTravelAt;
    private readonly Dictionary<ulong, Vector3> _testEnemyPos = new();
    private int _testHostPhaseSeen;
    // --coop-mark-file=P --coop-mark-at=S: write P when the run clock passes S
    // (tests/coop.ps1 starts the late joiner on it).
    private string? _testMarkFile;
    private float _testMarkAt = -1f;

    private void ParseMigrationTest()
    {
        if (_testMigArgs) return;
        _testMigArgs = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--coop-expect-migration") _testExpectMigration = true;
            else if (arg == "--coop-no-migrate") _testNoMigrate = true;
            else if (arg == "--coop-late-join") _testLateJoinClient = true;
            else if (arg == "--coop-expect-late-join") _testExpectLateJoin = true;
            else if (arg.StartsWith("--coop-toast-shot=")) _testToastShot = arg["--coop-toast-shot=".Length..];
            else if (arg.StartsWith("--coop-mark-file=")) _testMarkFile = arg["--coop-mark-file=".Length..];
            else if (arg.StartsWith("--coop-mark-at=")) _testMarkAt = arg["--coop-mark-at=".Length..].ToFloat();
        }
    }

    /// <summary>Per frame in a test run (from ProcessTest).</summary>
    private void StepMigrationTest()
    {
        ParseMigrationTest();
        if (_main is null || !IsInstanceValid(_main)) return;
        double now = Now;
        if (Migrations > 0 && _testPhaseAtMigration < 0)
        {
            _testPhaseAtMigration = _main.Run.Phase;
            _testEnemyStatesAtMigration = EnemyStatesReceived;
            _testTravelAt = now + 2.0;   // let the dust settle first
            if (_testToastShot is not null) _testToastAt = now + 1.5;
        }
        if (_testToastShot is not null && _testToastAt > 0 && now >= _testToastAt)
        {
            string path = _testToastShot;
            _testToastShot = null;
            _ = Screenshot.Capture(GetTree(), this, path);
        }
        if (_hostSnap.Length >= SnapLength) _testHostPhaseSeen = (int)_hostSnap[SnapStage] + 1;
        if (_testMarkFile is not null && _testMarkAt >= 0f && _main.Run.Seconds >= _testMarkAt)
        {
            using (var f = Godot.FileAccess.Open(_testMarkFile, Godot.FileAccess.ModeFlags.Write)) f?.StoreString($"{_main.Run.Seconds:0.0}");
            GD.Print($"[coop-test] mark written at t={_main.Run.Seconds:0.0}");
            _testMarkFile = null;
        }

        // Enemy travel after the migration: real enemies on the new host,
        // puppets following the new host's snapshots on the other survivor.
        if (_testPhaseAtMigration >= 0 && !_migrating && now >= _testTravelAt && EnemyManager.Instance is { } em)
        {
            _testTravelAt = now + 1.0;
            var seen = new Dictionary<ulong, Vector3>();
            IReadOnlyList<Enemy> alive = em.AliveList;
            for (int i = 0; i < alive.Count; i++)
            {
                Enemy e = alive[i];
                if (!e.IsAlive) continue;
                ulong key = e.GetInstanceId();
                Vector3 p = e.GlobalPosition;
                if (_testEnemyPos.TryGetValue(key, out Vector3 was))
                {
                    float d = new Vector2(p.X - was.X, p.Z - was.Z).Length();
                    if (d < 15f) _testEnemyTravel += d;   // a respawned pool body is not travel
                }
                seen[key] = p;
            }
            _testEnemyPos.Clear();
            foreach (KeyValuePair<ulong, Vector3> kv in seen) _testEnemyPos[kv.Key] = kv.Value;
        }
    }

    /// <summary>The quitting host goes at once, so the others notice now
    /// rather than after a transport timeout.</summary>
    private void TestHostQuit()
    {
        if (_peer is ENetMultiplayerPeer enet)
        {
            foreach (int id in Multiplayer.GetPeers()) enet.GetPeer(id)?.PeerDisconnectNow();
            enet.Host?.Flush();
        }
    }

    /// <summary>Fields and problems for a migration or late-join run, or null
    /// when this run is neither.</summary>
    private string? MigrationTestLine(string role, List<string> problems, int phase)
    {
        ParseMigrationTest();
        if (_testLateJoinClient)
        {
            if (!LateJoined) problems.Add("did not join as a late joiner");
            if (SnapshotsApplied == 0) problems.Add("no world snapshot arrived");
            if (EnemiesSynced == 0) problems.Add("no enemies arrived");
            if (EnemyStatesReceived < 50) problems.Add($"only {EnemyStatesReceived} enemy state packets");
            if (HitsQueued == 0) problems.Add("no hits sent");
            if (PuppetKills == 0) problems.Add("no kill was confirmed by the host");
            if (_testHostPhaseSeen > 0 && phase != _testHostPhaseSeen) problems.Add($"on phase {phase}, the host on {_testHostPhaseSeen}");
            int level = Progression.Inventory.Current?.Level ?? 0;
            if (level < 2) problems.Add($"still level {level} after joining");
            return $"role={role} late_join=1 snapshots={SnapshotsApplied} snapshot_enemies={SnapshotEnemies} catch_up_levels={CatchUpLevels} " +
                   $"level={level} phase={phase} host_phase={_testHostPhaseSeen} players={Coop.Roster.Count} enemies_synced={EnemiesSynced} " +
                   $"enemy_states={EnemyStatesReceived} hits={HitsQueued} confirmed_kills={PuppetKills} pickups={PickupsSynced} " +
                   $"states_rx={StatesReceived} remotes={_remotes.Count} t={_main?.Run.Clock ?? "-"}";
        }
        if (!_testExpectMigration) return null;

        float t = _main?.Run.Seconds ?? 0f;
        float after = MigratedAt >= 0f ? t - MigratedAt : 0f;
        string roleNow = Coop.IsHost ? "host" : "client";
        if (Migrations == 0) problems.Add("the host never left / no migration happened");
        else
        {
            if (after < 15f) problems.Add($"the run went on only {after:0} s after the host left");
            if (_testEnemyTravel < 5f) problems.Add($"enemies moved only {_testEnemyTravel:0.0} m after the migration");
            if (_testExpectPhase > 0 && phase < _testExpectPhase) problems.Add($"reached phase {phase}, wanted {_testExpectPhase}");
            if (_testPhaseAtMigration > 0 && _testExpectPhase > _testPhaseAtMigration && phase <= _testPhaseAtMigration)
                problems.Add("no phase progress after the migration");
            if (UI.CoopToast.Shown == 0 || !UI.CoopToast.LastText.Contains(LastOldHost)) problems.Add("no migration toast");
            if (Coop.IsHost)
            {
                if (!BecameHost) problems.Add("host now without having been elected");
                if (PeakPlayers > 2 && Rejoins == 0) problems.Add("the other survivor never reconnected");
            }
            else
            {
                if (SnapshotsApplied == 0) problems.Add("never resynced with the new host");
                if (EnemyStatesReceived - _testEnemyStatesAtMigration < 30) problems.Add($"only {EnemyStatesReceived - _testEnemyStatesAtMigration} enemy states from the new host");
                if (_remotes.Count == 0) problems.Add("cannot see the new host");
            }
        }
        if (MigrationFallback.Length > 0) problems.Add($"fell back: {MigrationFallback}");
        int rests = _main?.Stages.RestRoomsVisited ?? 0;
        return $"role={role} role_now={roleNow} migrated={Migrations} old_host=\"{LastOldHost}\" new_host=\"{LastNewHost}\" " +
               $"became_host={(BecameHost ? 1 : 0)} promoted={PromotedEnemies} rejoins={Rejoins} resyncs={SnapshotsApplied} resync_s={ResyncSeconds:0.0} " +
               $"migrated_at={MigratedAt:0.0} ran_after={after:0.0} enemy_travel={_testEnemyTravel:0} " +
               $"enemy_states_after={EnemyStatesReceived - _testEnemyStatesAtMigration} phase_at_migration={_testPhaseAtMigration} phase={phase} " +
               $"rest_rooms={rests} players_now={Coop.Roster.Count} remotes={_remotes.Count} alive={EnemyManager.Instance?.AliveCount ?? 0} " +
               $"kills={_main?.Run.Kills ?? 0} level={Progression.Inventory.Current?.Level ?? 0} t={_main?.Run.Clock ?? "-"} " +
               $"toast=\"{UI.CoopToast.LastText}\"";
    }

    /// <summary>--coop-no-migrate: the fallback ended the run; after a moment
    /// the results screen must be up. Reported from a tree timer (this node
    /// is gone by then).</summary>
    private void TestMigrationFallback()
    {
        ParseMigrationTest();
        if (!_testMode || _testDone) return;
        _testDone = true;
        SceneTree tree = GetTree();
        string? shot = _testShot;
        string reason = MigrationFallback;
        tree.CreateTimer(2.5, processAlways: true).Timeout += async () =>
        {
            Main? main = Main.Instance;
            bool results = main is not null && IsInstanceValid(main) && main.Results.Visible;
            bool over = main is not null && IsInstanceValid(main) && main.Run.Over;
            if (shot is not null) await Screenshot.Capture(tree, main is not null && IsInstanceValid(main) ? main : tree.Root, shot);
            bool ok = _testNoMigrateStatic && results && over && !Coop.Active;
            GD.Print($"COOP {(ok ? "PASS" : "FAIL")} role=client fallback=1 results_visible={(results ? 1 : 0)} run_over={(over ? 1 : 0)} " +
                     $"session_closed={(!Coop.Active ? 1 : 0)} notice=\"{Coop.Notice}\" reason=\"{reason}\"");
            tree.Quit();
        };
        _testNoMigrateStatic = _testNoMigrate;
    }

    private static bool _testNoMigrateStatic;
}
