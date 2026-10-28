using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Wavebreak.Updates;

public sealed partial class UpdateCheck : Node
{
    public static bool Requested => OS.GetCmdlineUserArgs().Contains("--check-updater");
    private int _checks;
    public override void _Ready() => _ = Run();
    private void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++; GD.Print("UPDATER OK " + label);
    }
    private async Task Reject(Func<Task> action, string label)
    {
        try { await action(); } catch (Exception error) when (error is UpdateException or OperationCanceledException or InvalidDataException)
        { Check(true, label); return; }
        Check(false, label);
    }
    internal static string Metadata(string hash, long size = 32) => JsonSerializer.Serialize(new
    {
        tag_name = "v1.3.4", draft = false, prerelease = false,
        html_url = UpdatePolicy.ReleasesUrl + "/tag/v1.3.4",
        assets = new[] { new { name = "Wavebreak-1.3.4-windows.zip", size,
            digest = "sha256:" + hash, state = "uploaded", content_type = "application/zip",
            browser_download_url = UpdatePolicy.ReleasesUrl + "/download/v1.3.4/Wavebreak-1.3.4-windows.zip" } },
    });
    private async Task Run()
    {
        string stage = UpdatePaths.CreateStage();
        try
        {
            Check(SemanticVersion.TryParse("1.10.0", out var a) && SemanticVersion.TryParse("1.9.9", out var b) && a.CompareTo(b) > 0, "numeric semantic comparison");
            Check(SemanticVersion.TryParse("v1.3.4", out a) && SemanticVersion.TryParse("1.3.4-rc.10", out b) && a.CompareTo(b) > 0, "stable outranks prerelease");
            Check(!SemanticVersion.TryParse("1.03.4", out _) && !SemanticVersion.TryParse("1.3.4-01", out _), "leading zero rejection");
            Check(SemanticVersion.TryParse("1.3.4+abc", out a) && SemanticVersion.TryParse("1.3.4+def", out b) && a.CompareTo(b) == 0, "metadata never causes an upgrade");
            string hash = Convert.ToHexString(SHA256.HashData(new byte[32])).ToLowerInvariant();
            string json = Metadata(hash);
            UpdateRelease release = UpdatePolicy.Parse(json);
            Check(release.Portable is not null && release.Installer is null, "exact official asset selection");
            await Reject(() => Task.FromResult(UpdatePolicy.Parse(json.Replace("github.com/", "evil.example/"))), "foreign release rejection");
            await Reject(() => Task.FromResult(UpdatePolicy.Parse(json.Replace("\"prerelease\":false", "\"prerelease\":true"))), "prerelease rejection");
            Check(!UpdatePolicy.AllowedAssetRedirect(new Uri("http://objects.githubusercontent.com/x"))
                && !UpdatePolicy.AllowedAssetRedirect(new Uri("https://evil.example/x")), "HTTPS redirect allowlist");
            foreach (string unsafePath in new[] { "../escape", "/root", "x:ads", "CON.txt", "a/../b", ".git/config", "profile.cfg", "x. " })
                await Reject(() => Task.FromResult(PortableUpdatePackage.SafeRelative(unsafePath)), "reject path " + unsafePath);
            string zip = Path.Combine(stage, "bad.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntry("../escape");
            await Reject(() => PortableUpdatePackage.ExtractAsync(zip, Path.Combine(stage, "payload"), CancellationToken.None), "zip traversal rejection before writes");
            var handler = new UpdateCheckHandler { Respond = _ => new(HttpStatusCode.OK) { Content = new StringContent(json) } };
            using var source = new GitHubUpdateSource(handler);
            Check((await source.LatestAsync(null, CancellationToken.None)).Json == json, "bounded metadata fetch through HTTP fixture");
            handler.Respond = _ => new(HttpStatusCode.NotModified);
            Check((await source.LatestAsync(new(DateTimeOffset.UtcNow, "\"test\"", json), CancellationToken.None)).Json == json, "304 cache reuse");
            handler.Respond = _ => new(HttpStatusCode.Forbidden);
            await Reject(() => source.LatestAsync(null, CancellationToken.None), "rate limit recovery path");
            handler.Respond = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[32]) };
            await source.DownloadAsync(release.Portable!, Path.Combine(stage, "valid.bin"), new Progress<long>(), CancellationToken.None);
            Check(File.ReadAllBytes(Path.Combine(stage, "valid.bin")).Length == 32, "streamed size and SHA256 verification");
            await Reject(() => source.DownloadAsync(release.Portable! with { Sha256 = new string('a', 64) }, Path.Combine(stage, "bad.bin"), new Progress<long>(), CancellationToken.None), "checksum mismatch rejection");
            handler.Respond = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[12]) };
            await Reject(() => source.DownloadAsync(release.Portable!, Path.Combine(stage, "short.bin"), new Progress<long>(), CancellationToken.None), "truncated download rejection");
            handler.Respond = _ => new(HttpStatusCode.OK) { Content = new StringContent(json) };
            var handoff = new UpdateCheckHandoff();
            var coordinator = new UpdateCoordinator(new("1.3.3", UpdateInstallKind.Development, stage, "", 1, 1), source, handoff);
            await coordinator.StartupCheckAsync();
            Check(coordinator.Snapshot.Phase == UpdatePhase.Available && handoff.Calls == 0, "startup checks never install");
            await coordinator.DownloadAsync(true, true, "en");
            Check(handoff.Calls == 0 && coordinator.Snapshot.Phase == UpdatePhase.Available, "development tree installation blocked");
            var newer = new UpdateCoordinator(new("1.10.0", UpdateInstallKind.Portable, stage, "", 1, 1), source, handoff);
            await newer.CheckAsync();
            Check(newer.Snapshot.Phase == UpdatePhase.Current, "no downgrade offer");
            await CheckPrepared(stage, handler, source);
            Check(Game.GrappleWind.Intensity(true, true, true, 60f, 1f) == 1f
                && Game.GrappleWind.Intensity(true, true, true, 18f, 1f) == 0f
                && Game.GrappleWind.Intensity(true, true, true, 60f, 0f) == 0f
                && Game.GrappleWind.Intensity(false, true, true, 60f, 1f) == 0f, "speed effect thresholds and quality setting");
            if (OperatingSystem.IsWindows()) await CheckArabic(stage);
            GD.Print($"UPDATER PASS checks={_checks}"); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("UPDATER FAIL " + error); GetTree().Quit(1); }
        finally { UpdatePaths.Cleanup(stage); }
    }
    private async Task CheckArabic(string stage)
    {
        WindowsUpdateHandoff.WriteHelpers(stage);
        byte[] bytes = File.ReadAllBytes(Path.Combine(stage, "UpdateProgress.ps1"));
        Check(bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "helper scripts have UTF8 BOM for Windows PowerShell 5.1");
        string probe = Path.Combine(stage, "probe.ps1"), output = Path.Combine(stage, "arabic.txt");
        File.WriteAllText(probe, "param($Module,$Output)\n. $Module\nInitialize-UpdateProgress $true\n[IO.File]::WriteAllText($Output,$script:UpdateForm.Controls[0].Text,[Text.Encoding]::UTF8)\n$script:UpdateForm.Dispose()\n", Encoding.ASCII);
        var start = new ProcessStartInfo(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in new[] { "-STA", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", probe, "-Module", Path.Combine(stage, "UpdateProgress.ps1"), "-Output", output }) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Check(process.ExitCode == 0 && File.ReadAllText(output).Contains("جارٍ"), "actual Windows PowerShell preserves Arabic in hidden progress form");
    }
    private async Task CheckPrepared(string stage, UpdateCheckHandler handler, GitHubUpdateSource source)
    {
        string zip = Path.Combine(stage, "complete.zip");
        byte[] executable = new byte[96]; executable[0] = 77; executable[1] = 90; executable[60] = 64;
        executable[64] = 80; executable[65] = 69;
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            foreach (string name in new[] { "Wavebreak.exe", "Wavebreak.pck", "data_Wavebreak_windows_x86_64/Wavebreak.dll", "steam_api64.dll", "steam_appid.txt" })
            {
                using Stream file = archive.CreateEntry(name).Open();
                file.Write(name.EndsWith(".exe") ? executable : Encoding.ASCII.GetBytes(name.EndsWith(".pck") ? "GDPCfixture" : "fixture"));
            }
        byte[] bytes = File.ReadAllBytes(zip);
        string json = Metadata(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
        handler.Respond = request => new(HttpStatusCode.OK) { Content = request.RequestUri == UpdatePolicy.LatestApi
            ? new StringContent(json) : new ByteArrayContent(bytes) };
        string root = Path.Combine(stage, "fake-portable");
        Directory.CreateDirectory(Path.Combine(root, "data_Wavebreak_windows_x86_64"));
        foreach (string name in new[] { "Wavebreak.exe", "Wavebreak.pck", "data_Wavebreak_windows_x86_64/Wavebreak.dll" })
            File.WriteAllText(Path.Combine(root, name), "old fixture");
        var handoff = new UpdateCheckHandoff();
        var context = new UpdateContext("1.3.3", UpdateInstallKind.Portable, root, Path.Combine(root, "Wavebreak.exe"), 1, 1);
        var coordinator = new UpdateCoordinator(context, source, handoff);
        bool quit = false; coordinator.QuitRequested += () => quit = true;
        await coordinator.CheckAsync(); await coordinator.DownloadAsync(false, false, "ar");
        Check(coordinator.HasPrepared && coordinator.Snapshot.Phase == UpdatePhase.Ready && handoff.Calls == 0, "complete package prepares without implicit installation");
        await coordinator.InstallAsync(false, "ar");
        Check(!quit && handoff.Calls == 1 && coordinator.Snapshot.Phase == UpdatePhase.Failed && coordinator.HasPrepared, "failed handoff keeps game open and verified payload retryable");
        await coordinator.InstallAsync(false, "ar");
        Check(!quit && handoff.Calls == 2, "verified handoff retry never exits without acknowledgement");
        handoff.Result = true; handoff.BeforeReturn = coordinator.Cancel;
        await coordinator.InstallAsync(false, "ar");
        Check(!quit && coordinator.Snapshot.Phase == UpdatePhase.Cancelled
            && File.Exists(Path.Combine(handoff.Stage, "cancel.request")), "cancellation at helper acknowledgement prevents quit and cancels the helper");
        var cancelled = new UpdateCoordinator(context, new UpdateFixtureSource("downloading"), handoff);
        await cancelled.CheckAsync();
        Task download = cancelled.DownloadAsync(true, false, "en");
        await Task.Delay(20); cancelled.Cancel(); await download;
        Check(cancelled.Snapshot.Phase == UpdatePhase.Cancelled && handoff.Calls == 3, "cancelled download cannot reach automatic handoff");
    }
}
