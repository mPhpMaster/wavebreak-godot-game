using System;
using System.Text.Json;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Meta;

public static class ReleaseNotes
{
    public static string CurrentVersion => ProjectSettings.GetSetting("application/config/version", "").AsString();
    private static string SeenKey(string version) => "release_notes_seen_" + version;
    public static bool Unread(string version) => !string.IsNullOrWhiteSpace(version) && Profile.GetMeta(SeenKey(version)) == 0;
    public static void Acknowledge(string version)
    {
        Profile.SetMeta(SeenKey(version), 1);
        Profile.Save();
    }

    public static ReleaseNotesEntry? LoadCurrent()
    {
        string version = CurrentVersion;
        if (string.IsNullOrWhiteSpace(version) || version.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return null;
        string path = $"res://release-notes/{version}.json";
        if (!Godot.FileAccess.FileExists(path)) return null;
        try
        {
            var notes = JsonSerializer.Deserialize<ReleaseNotesEntry>(Godot.FileAccess.GetFileAsString(path));
            return notes is { Changes.Length: > 0 } && notes.Version == version
                && !string.IsNullOrWhiteSpace(notes.Title)
                && Array.TrueForAll(notes.Changes, item => !string.IsNullOrWhiteSpace(item)) ? notes : null;
        }
        catch (JsonException error) { GD.PushWarning("Release notes could not be read: " + error.Message); return null; }
    }
}
