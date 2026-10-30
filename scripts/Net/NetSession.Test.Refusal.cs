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
/// </summary>
public sealed partial class NetSession
{
    private bool _testExpectRefusal;

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
        bool named = reason.Contains(CoopBuild.Version) && reason != Meta.Loc.T("Different game version.");
        TestRefusalEnd($"COOP {(named ? "PASS" : "FAIL")} role=client refused=1 notice=\"{Coop.Notice}\"");
        return true;
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
