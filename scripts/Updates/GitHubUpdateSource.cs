using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Wavebreak.Updates;

public sealed class GitHubUpdateSource : IUpdateSource, IDisposable
{
    private readonly HttpClient _http;
    public GitHubUpdateSource(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(8),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        }) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Wavebreak-Updater/1.0");
    }

    public async Task<ReleaseResponse> LatestAsync(UpdateCacheEntry? cache, CancellationToken cancel)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        bounded.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdatePolicy.LatestApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        if (cache?.ETag is { Length: > 0 } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && cache is not null) return new(cache.Json, cache.ETag);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new UpdateException(UpdateProblem.RateLimited, "GitHub request limit or access restriction.");
        if (response.StatusCode == HttpStatusCode.NotFound) throw new UpdateException(UpdateProblem.NoRelease, "No public stable release found.");
        if (response.StatusCode != HttpStatusCode.OK) throw new UpdateException(UpdateProblem.Offline, "Release check unavailable.");
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new UpdateException(UpdateProblem.InvalidRelease, "Release response too large.");
        await using Stream stream = await response.Content.ReadAsStreamAsync(bounded.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(buffer, bounded.Token).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > 2 * 1024 * 1024) throw new UpdateException(UpdateProblem.InvalidRelease, "Release response too large.");
            output.Write(buffer, 0, read);
        }
        string json = new UTF8Encoding(false, true).GetString(output.ToArray());
        return new(json, response.Headers.ETag?.ToString());
    }

    public async Task DownloadAsync(UpdateAsset asset, string destination, IProgress<long> progress, CancellationToken cancel)
    {
        string path = "/" + UpdatePolicy.Repository + "/releases/download/";
        string[] segments = Uri.UnescapeDataString(asset.Url.AbsolutePath).Split('/');
        bool canonical = segments.Length == 7 && SemanticVersion.TryParse(segments[5], out var version) && version.IsStable
            && asset.Name == (asset.Kind == UpdateAssetKind.Installer ? $"Wavebreak-Setup-{version}.exe" : $"Wavebreak-{version}-windows.zip");
        if (!UpdatePolicy.OfficialPath(asset.Url, Uri.UnescapeDataString(asset.Url.AbsolutePath))
            || !Uri.UnescapeDataString(asset.Url.AbsolutePath).StartsWith(path, StringComparison.Ordinal)
            || Path.GetFileName(Uri.UnescapeDataString(asset.Url.AbsolutePath)) != asset.Name
            || !canonical || asset.Size <= 0 || asset.Size > UpdatePolicy.MaxAssetBytes
            || !System.Text.RegularExpressions.Regex.IsMatch(asset.Sha256, "^[a-fA-F0-9]{64}$"))
            throw new UpdateException(UpdateProblem.InvalidRelease, "Unexpected release asset.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        bounded.CancelAfter(TimeSpan.FromMinutes(10));
        using HttpResponseMessage response = await AssetResponse(asset.Url, bounded.Token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
            throw new UpdateException(UpdateProblem.Integrity, "Asset size differs from the official release.");
        string? mime = response.Content.Headers.ContentType?.MediaType;
        if (mime is "text/html" or "application/json" or "text/plain") throw new UpdateException(UpdateProblem.Integrity, "Unexpected asset response.");
        await using Stream input = await response.Content.ReadAsStreamAsync(bounded.Token).ConfigureAwait(false);
        await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, bounded.Token).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > asset.Size) throw new UpdateException(UpdateProblem.Integrity, "Asset exceeds the official size.");
            hash.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), bounded.Token).ConfigureAwait(false);
            progress.Report(total);
        }
        if (total != asset.Size || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException(UpdateProblem.Integrity, "Asset SHA-256 verification failed.");
        await file.FlushAsync(bounded.Token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> AssetResponse(Uri initial, CancellationToken cancel)
    {
        Uri current = initial;
        for (int i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK) return response;
            Uri? location = response.Headers.Location;
            bool redirect = (int)response.StatusCode is 301 or 302 or 303 or 307 or 308;
            response.Dispose();
            if (!redirect || location is null) throw new UpdateException(UpdateProblem.Download, "Asset download unavailable.");
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (!UpdatePolicy.AllowedAssetRedirect(current)) throw new UpdateException(UpdateProblem.Integrity, "Unexpected asset redirect.");
        }
        throw new UpdateException(UpdateProblem.Download, "Too many asset redirects.");
    }
    public void Dispose() => _http.Dispose();
}
