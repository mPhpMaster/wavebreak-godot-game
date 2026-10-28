using System;
using System.Collections.Generic;

namespace Wavebreak.Updates;

public enum UpdateInstallKind { Development, Installed, Portable, StoreManaged, Unsupported }
public enum UpdateAssetKind { Installer, Portable }
public enum UpdatePhase { Idle, Checking, Current, Available, Downloading, Preparing, Ready, HandingOff, Failed, Cancelled }
public enum UpdateProblem { None, Offline, RateLimited, NoRelease, InvalidRelease, NoAsset, Integrity, Package, Location, Download, Handoff }
public sealed record UpdateAsset(string Name, Uri Url, long Size, string Sha256, UpdateAssetKind Kind);
public sealed record UpdateRelease(SemanticVersion Version, string Tag, Uri Page, UpdateAsset? Installer, UpdateAsset? Portable);
public sealed record UpdateContext(string Version, UpdateInstallKind Kind, string Root, string Executable,
    int ProcessId, long ProcessStartedUtcTicks)
{
    public bool CanInstall => Kind is UpdateInstallKind.Installed or UpdateInstallKind.Portable;
}
public sealed record UpdateSnapshot(UpdatePhase Phase, string CurrentVersion, UpdateRelease? Release = null,
    long CompletedBytes = 0, long TotalBytes = 0, UpdateProblem Problem = UpdateProblem.None, string Detail = "");
public sealed record UpdateCacheEntry(DateTimeOffset CheckedUtc, string? ETag, string Json);
public sealed record ReleaseResponse(string Json, string? ETag);
public sealed record UpdatePayloadFile(string Path, long Size, string Sha256);
public sealed record PreparedUpdate(string Stage, UpdateAsset Asset, string AssetPath, string Payload,
    IReadOnlyList<UpdatePayloadFile> Files);
public sealed record UpdateRequest(int Schema, string Product, string Mode, string Version, string InstallRoot,
    string GameExe, int GamePid, string GameStartedUtcTicks, string AssetName, string AssetSha256,
    IReadOnlyList<UpdatePayloadFile> Files, bool Restart, string Language);
public sealed class UpdateException : Exception
{
    public UpdateProblem Problem { get; }
    public UpdateException(UpdateProblem problem, string message) : base(message) => Problem = problem;
}
public interface IUpdateSource
{
    System.Threading.Tasks.Task<ReleaseResponse> LatestAsync(UpdateCacheEntry? cache, System.Threading.CancellationToken cancel);
    System.Threading.Tasks.Task DownloadAsync(UpdateAsset asset, string destination,
        IProgress<long> progress, System.Threading.CancellationToken cancel);
}
public interface IUpdateHandoff
{
    System.Threading.Tasks.Task<bool> StartAsync(UpdateContext context, UpdateRelease release,
        PreparedUpdate prepared, bool restart, string language, System.Threading.CancellationToken cancel);
}
