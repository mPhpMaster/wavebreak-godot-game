using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Wavebreak.Updates;

public sealed class UpdateCoordinator
{
    private readonly IUpdateSource _source;
    private readonly IUpdateHandoff _handoff;
    private readonly string? _cachePath;
    private CancellationTokenSource? _operation;
    private PreparedUpdate? _prepared;
    private bool _startupChecked;
    public UpdateContext Context { get; }
    public UpdateSnapshot Snapshot { get; private set; }
    public event Action? Changed;
    public event Action? QuitRequested;
    public bool Busy => Snapshot.Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Preparing or UpdatePhase.HandingOff;
    public bool HasPrepared => _prepared is not null && Snapshot.Release is { } release
        && _prepared.Asset.Name == (Context.Kind == UpdateInstallKind.Installed ? release.Installer?.Name : release.Portable?.Name)
        && _prepared.Asset.Sha256 == (Context.Kind == UpdateInstallKind.Installed ? release.Installer?.Sha256 : release.Portable?.Sha256);

    public UpdateCoordinator(UpdateContext context, IUpdateSource source, IUpdateHandoff handoff, string? cachePath = null)
    {
        Context = context; _source = source; _handoff = handoff; _cachePath = cachePath;
        Snapshot = new(UpdatePhase.Idle, context.Version);
    }

    public Task StartupCheckAsync()
    {
        if (_startupChecked) return Task.CompletedTask;
        _startupChecked = true; return CheckAsync(false);
    }

    public async Task CheckAsync(bool manual = true)
    {
        if (Busy) return;
        using var operation = new CancellationTokenSource(); _operation = operation;
        Set(new(UpdatePhase.Checking, Context.Version));
        try
        {
            if (!SemanticVersion.TryParse(Context.Version, out SemanticVersion installed))
                throw new UpdateException(UpdateProblem.InvalidRelease, "Invalid installed version.");
            UpdateCacheEntry? cache = LoadCache();
            TimeSpan age = cache is null ? TimeSpan.MaxValue : DateTimeOffset.UtcNow - cache.CheckedUtc;
            ReleaseResponse response = !manual && cache is not null && age >= TimeSpan.Zero && age < TimeSpan.FromHours(1)
                ? new(cache.Json, cache.ETag) : await _source.LatestAsync(cache, operation.Token);
            UpdateRelease release = UpdatePolicy.Parse(response.Json);
            SaveCache(new(DateTimeOffset.UtcNow, response.ETag, response.Json));
            Set(new(release.Version.CompareTo(installed) > 0 ? UpdatePhase.Available : UpdatePhase.Current, Context.Version, release));
        }
        catch (OperationCanceledException) { Set(new(UpdatePhase.Cancelled, Context.Version)); }
        catch (Exception error) { Fail(error, UpdateProblem.Offline); }
        finally { if (ReferenceEquals(_operation, operation)) _operation = null; }
    }

    public async Task DownloadAsync(bool installAfterDownload, bool restart, string language)
    {
        if (Busy || Snapshot.Release is not { } release || !Context.CanInstall) return;
        if (!SemanticVersion.TryParse(Context.Version, out SemanticVersion installed) || release.Version.CompareTo(installed) <= 0) return;
        using var operation = new CancellationTokenSource(); _operation = operation;
        string? stage = null;
        try
        {
            UpdatePaths.ValidateRoot(Context.Root);
            UpdateAsset asset = UpdatePolicy.Select(release, Context);
            stage = UpdatePaths.CreateStage();
            Set(new(UpdatePhase.Downloading, Context.Version, release, 0, asset.Size));
            string part = Path.Combine(stage, "download.part");
            var progress = new Progress<long>(count =>
            {
                if (ReferenceEquals(_operation, operation) && !operation.IsCancellationRequested && Snapshot.Phase == UpdatePhase.Downloading)
                    Set(new(UpdatePhase.Downloading, Context.Version, release, count, asset.Size));
            });
            await _source.DownloadAsync(asset, part, progress, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            string path = Path.Combine(stage, asset.Name); File.Move(part, path);
            Set(new(UpdatePhase.Preparing, Context.Version, release, asset.Size, asset.Size));
            string payload = Path.Combine(stage, "payload");
            System.Collections.Generic.IReadOnlyList<UpdatePayloadFile> files = Array.Empty<UpdatePayloadFile>();
            if (asset.Kind == UpdateAssetKind.Portable) files = await PortableUpdatePackage.ExtractAsync(path, payload, operation.Token);
            else { PortableUpdatePackage.ValidatePe(path); MarkInternetZone(path, asset.Url); }
            operation.Token.ThrowIfCancellationRequested();
            _prepared = new(stage, asset, path, payload, files);
            Set(new(UpdatePhase.Ready, Context.Version, release, asset.Size, asset.Size));
        }
        catch (OperationCanceledException)
        {
            Cleanup(stage); Set(new(UpdatePhase.Cancelled, Context.Version, release));
        }
        catch (Exception error) { Cleanup(stage); Fail(error, UpdateProblem.Download, release); }
        finally { if (ReferenceEquals(_operation, operation)) _operation = null; }
        if (installAfterDownload && Snapshot.Phase == UpdatePhase.Ready) await InstallAsync(restart, language);
    }

    public async Task InstallAsync(bool restart, string language)
    {
        if (Busy || !HasPrepared || _prepared is not { } prepared || Snapshot.Release is not { } release || !Context.CanInstall) return;
        using var operation = new CancellationTokenSource(); _operation = operation;
        Set(new(UpdatePhase.HandingOff, Context.Version, release));
        try
        {
            UpdateAsset expected = UpdatePolicy.Select(release, Context);
            if (prepared.Asset.Name != expected.Name || prepared.Asset.Sha256 != expected.Sha256)
                throw new UpdateException(UpdateProblem.Integrity, "Download the current release again before installation.");
            if (!await _handoff.StartAsync(Context, release, prepared, restart, language, operation.Token))
                throw new UpdateException(UpdateProblem.Handoff, "Updater did not confirm readiness.");
            operation.Token.ThrowIfCancellationRequested();
            QuitRequested?.Invoke();
        }
        catch (OperationCanceledException) { Set(new(UpdatePhase.Cancelled, Context.Version, release)); }
        catch (Exception error) { Fail(error, UpdateProblem.Handoff, release); }
        finally { if (ReferenceEquals(_operation, operation)) _operation = null; }
    }

    public void Cancel()
    {
        if (_operation is not { } operation) return;
        operation.Cancel();
        if (Snapshot.Phase != UpdatePhase.HandingOff || _prepared is not { } prepared) return;
        try
        {
            if (UpdatePaths.IsOwnedStage(prepared.Stage)) File.WriteAllText(Path.Combine(prepared.Stage, "cancel.request"), "cancel");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private void Set(UpdateSnapshot value) { Snapshot = value; Changed?.Invoke(); }
    private void Fail(Exception error, UpdateProblem fallback, UpdateRelease? release = null)
        => Set(new(UpdatePhase.Failed, Context.Version, release, Problem: error is UpdateException update ? update.Problem : fallback,
            Detail: error is UpdateException known ? known.Message : error is HttpRequestException ? "Network unavailable." : "Operation could not complete."));
    private UpdateCacheEntry? LoadCache()
    {
        try { return _cachePath is not null && File.Exists(_cachePath) && new FileInfo(_cachePath).Length < 2 * 1024 * 1024 ? JsonSerializer.Deserialize<UpdateCacheEntry>(File.ReadAllText(_cachePath)) : null; }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    private void SaveCache(UpdateCacheEntry entry)
    {
        if (_cachePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            string temp = _cachePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entry)); File.Move(temp, _cachePath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private static void Cleanup(string? stage)
    {
        if (stage is null) return;
        try { UpdatePaths.Cleanup(stage); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private static void MarkInternetZone(string path, Uri url)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + url.AbsoluteUri + "\r\n"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
