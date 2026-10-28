using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wavebreak.Updates;

public static class UpdatePolicy
{
    public const string Repository = "mPhpMaster/wavebreak-godot-game";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    public static readonly Uri LatestApi = new("https://api.github.com/repos/" + Repository + "/releases/latest");
    public const long MaxAssetBytes = 512L * 1024 * 1024;
    private static readonly Regex Hash = new("^sha256:[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant);

    public static UpdateRelease Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
            JsonElement root = document.RootElement;
            string tag = Text(root, "tag_name");
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()
                || !SemanticVersion.TryParse(tag, out SemanticVersion version) || !version.IsStable)
                throw new UpdateException(UpdateProblem.InvalidRelease, "No stable semantic release.");
            var page = new Uri(Text(root, "html_url"));
            if (!OfficialPath(page, "/" + Repository + "/releases/tag/" + tag))
                throw new UpdateException(UpdateProblem.InvalidRelease, "Unexpected release page.");
            UpdateAsset? installer = null, portable = null;
            foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = Text(asset, "name");
                UpdateAssetKind? kind = name == $"Wavebreak-Setup-{version}.exe" ? UpdateAssetKind.Installer
                    : name == $"Wavebreak-{version}-windows.zip" ? UpdateAssetKind.Portable : null;
                if (kind is null) continue;
                long size = asset.GetProperty("size").GetInt64();
                string digest = Text(asset, "digest");
                var url = new Uri(Text(asset, "browser_download_url"));
                string mime = Text(asset, "content_type");
                bool type = kind == UpdateAssetKind.Portable
                    ? new[] { "application/zip", "application/x-zip-compressed", "application/octet-stream" }.Contains(mime)
                    : new[] { "application/octet-stream", "application/x-msdownload", "application/x-msdos-program", "application/vnd.microsoft.portable-executable" }.Contains(mime);
                if (size < (kind == UpdateAssetKind.Installer ? 65536 : 22) || size > MaxAssetBytes || !Hash.IsMatch(digest)
                    || !type || Text(asset, "state") != "uploaded"
                    || !OfficialPath(url, "/" + Repository + "/releases/download/" + tag + "/" + name)) continue;
                var parsed = new UpdateAsset(name, url, size, digest[7..].ToLowerInvariant(), kind.Value);
                if (kind == UpdateAssetKind.Installer) installer = parsed; else portable = parsed;
            }
            return new(version, tag, page, installer, portable);
        }
        catch (UpdateException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or UriFormatException or FormatException)
        { throw new UpdateException(UpdateProblem.InvalidRelease, "Invalid official release metadata."); }
    }

    public static UpdateAsset Select(UpdateRelease release, UpdateContext context) => context.Kind switch
    {
        UpdateInstallKind.Installed when release.Installer is { } installer => installer,
        UpdateInstallKind.Portable when release.Portable is { } portable => portable,
        _ => throw new UpdateException(UpdateProblem.NoAsset, "No supported asset for this installation."),
    };

    public static bool OfficialPath(Uri uri, string path) => uri.Scheme == "https" && uri.Host == "github.com"
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && string.Equals(Uri.UnescapeDataString(uri.AbsolutePath), path, StringComparison.Ordinal);

    public static bool AllowedAssetRedirect(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com";

    private static string Text(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
