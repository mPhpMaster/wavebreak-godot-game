namespace Wavebreak.Data;

public sealed record ReleaseNotesEntry(string Version, string Title, string[] Changes);
