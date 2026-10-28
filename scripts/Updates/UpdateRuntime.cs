using System;
using System.IO;
using System.Text.Json;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Updates;

public static class UpdateRuntime
{
    private static UpdateCoordinator? _current;
    public static string Version => ProjectSettings.GetSetting("application/config/version", "0.0.0").AsString();
    public static string RecoveryNote { get; private set; } = "";
    public static UpdateCoordinator? ForMenu()
    {
        if (UpdateFixtures.Requested) return UpdateFixtures.Create();
        if (Launch.Automated) return null;
        if (_current is not null) return _current;
        string directory = Path.Combine(OS.GetUserDataDir(), "updates");
        string pending = Path.Combine(directory, "pending.json");
        RecoveryNote = ReadRecovery(pending);
        var context = UpdateInstallDiscovery.Detect(Version, OS.HasFeature("editor") || OS.IsDebugBuild());
        return _current = new(context, new GitHubUpdateSource(), new WindowsUpdateHandoff(pending), Path.Combine(directory, "check.json"));
    }

    private static string ReadRecovery(string pending)
    {
        try
        {
            if (!File.Exists(pending) || new FileInfo(pending).Length > 16384) return "";
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(pending));
            string? id = document.RootElement.GetProperty("stage").GetString();
            if (!Guid.TryParseExact(id, "N", out _)) return "";
            string statusPath = Path.Combine(UpdatePaths.StageBase, id!, "status.json");
            if (!File.Exists(statusPath) || new FileInfo(statusPath).Length > 65536) return "The previous update did not finish. Check for updates to retry.";
            using JsonDocument status = JsonDocument.Parse(File.ReadAllText(statusPath));
            return status.RootElement.GetProperty("phase").GetString() switch
            {
                "applied" or "installer_finished" => "The previous update completed.",
                "applied_restart_failed" => "The update completed, but automatic restart failed.",
                _ => "The previous update did not finish. Check for updates to retry.",
            };
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { return "The previous update status could not be read."; }
    }
}
