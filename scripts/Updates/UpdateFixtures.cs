using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Updates;

internal sealed class UpdateFixtureSource : IUpdateSource
{
    private readonly string _mode;
    internal UpdateFixtureSource(string mode) => _mode = mode;
    public Task<ReleaseResponse> LatestAsync(UpdateCacheEntry? cache, CancellationToken cancel)
    {
        if (_mode == "failed") throw new HttpRequestException("Fixture offline");
        return Task.FromResult(new ReleaseResponse(UpdateCheck.Metadata(new string('a', 64)), null));
    }
    public async Task DownloadAsync(UpdateAsset asset, string destination, IProgress<long> progress, CancellationToken cancel)
    { progress.Report(16); await Task.Delay(Timeout.Infinite, cancel); }
}
public static class UpdateFixtures
{
    public static bool Requested => Launch.MenuShot is not null && OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--update-fixture="));
    public static UpdateCoordinator Create()
    {
        string mode = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--update-fixture="))[17..];
        string stage = UpdatePaths.CreateStage();
        string root = Path.Combine(stage, "fixture-install");
        Directory.CreateDirectory(Path.Combine(root, "data_Wavebreak_windows_x86_64"));
        foreach (string relative in new[] { "Wavebreak.exe", "Wavebreak.pck", "data_Wavebreak_windows_x86_64/Wavebreak.dll" })
            File.WriteAllText(Path.Combine(root, relative), "fixture only");
        var updates = new UpdateCoordinator(new("1.3.3", mode == "development" ? UpdateInstallKind.Development : UpdateInstallKind.Portable,
            root, Path.Combine(root, "Wavebreak.exe"), 1, 1), new UpdateFixtureSource(mode), new UpdateCheckHandoff());
        if (mode == "downloading") _ = DownloadFixture(updates);
        return updates;
    }
    private static async Task DownloadFixture(UpdateCoordinator updates)
    { await updates.StartupCheckAsync(); await updates.DownloadAsync(false, false, "en"); }
}
