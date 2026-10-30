using System.Collections.Generic;
using Godot;

namespace Wavebreak.Net;

/// <summary>
/// Automated refusal checks (tests/coop.ps1).
///
///   --coop-expect-refusal     the session is meant to be turned away: the host
///                             passes when it refuses a peer, the client when
///                             the refusal reaches it and names both sides
///   --coop-fake-version=V     pose as another game version (<see cref="CoopBuild"/>)
///   --coop-fake-content=H     pose as another content hash
///   --coop-rejoin-token=T     client: show this token when reconnecting after a
///                             host migration, instead of the one it was issued
///   --coop-expect-rejoin-refused  the new host: a survivor's claim must be
///                             refused and its slot stay untaken
///   --coop-forge-hits-at=S    client: at S seconds send what a broken or hostile
///                             client would (<see cref="StepForgeHits"/>)
///   --coop-expect-rejects=N   host: exactly N hit records must be dropped
///                             (0 when not given: real play must never be)
/// </summary>
public sealed partial class NetSession
{
    private bool _testExpectRefusal, _testExpectRejoinRefused;
    private string? _testRejoinToken;
    private int _testExpectRejects;
    private float _testForgeHitsAt = -1f;

    /// <summary>Host: a peer's build was refused.</summary>
    private void TestBuildRefused()
    {
        if (!_testMode || _testDone || !_testExpectRefusal) return;
        TestRefusalEnd($"COOP PASS role=host refused=1 version={CoopBuild.Version} content={CoopBuild.Content}");
    }

    /// <summary>Client: the session failed with <paramref name="reason"/>.
    /// True when this run expected that and has reported.</summary>
    private bool TestRefusalSeen(string reason)
    {
        if (!_testExpectRefusal) return false;
        bool named = _testRejoinToken is not null ? reason == Meta.Loc.T("That place is kept for another player.")
                     : reason.Contains(CoopBuild.Version) && reason != Meta.Loc.T("Different game version.");
        TestRefusalEnd($"COOP {(named ? "PASS" : "FAIL")} role=client refused=1 notice=\"{Coop.Notice}\"");
        return true;
    }

    /// <summary>Forged hit batches over the real RPC: four bad ids, then a live
    /// enemy hit with NaN and with absurd numbers, then a batch cut mid-record
    /// (one rejected record) and one too big to be real. That is
    /// 4 + 1 + (MaxHitsPerSecond + 1) dropped records; the host must carry on.</summary>
    private void StepForgeHits(float t)
    {
        if (_testForgeHitsAt < 0f || t < _testForgeHitsAt || !Coop.IsClient) return;
        int live = -1;
        foreach (int id in _enemies.Keys) { live = id; break; }
        if (live < 0) return;
        _testForgeHitsAt = -1f;
        GD.Print($"[coop-test] forging hits at t={t:0.0} on enemy {live}");
        float nan = float.NaN;
        var d = new List<float>();
        foreach (float badId in new[] { nan, -5f, 1.5f, 1e9f }) d.AddRange(new[] { badId, 10f, 0f, 0f, 0f, 0f, 0f, 0f, 0f });
        d.AddRange(new[] { live, nan, nan, nan, nan, nan, nan, nan, nan });
        d.AddRange(new[] { live, float.PositiveInfinity, 1e30f, 1e30f, 1e30f, 1e30f, 1e30f, 1e9f, 1e30f });
        d.AddRange(new[] { live, 1e30f, 1e30f, 1e30f, 1e30f, 1e30f, 1e30f, 1e9f, 1e30f });
        RpcId(1, MethodName.Hits, d.ToArray());
        RpcId(1, MethodName.Hits, new float[HitStride + 1]);
        RpcId(1, MethodName.Hits, new float[(HitGuard.MaxHitsPerSecond + 1) * HitStride]);
    }

    private void TestRefusalEnd(string line)
    {
        _testDone = true;
        GD.Print(line);
        SceneTree tree = GetTree();
        // The host stays long enough for the refusal to leave.
        tree.CreateTimer(1.5, processAlways: true).Timeout += () => tree.Quit();
    }
}
