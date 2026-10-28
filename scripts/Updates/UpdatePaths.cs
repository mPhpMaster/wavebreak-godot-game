using System;
using System.IO;

namespace Wavebreak.Updates;

public static class UpdatePaths
{
    public static string StageBase => Path.Combine(Path.GetTempPath(), "WavebreakUpdates");
    public static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    public static bool Within(string root, string path) => Full(path).StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static bool IsStage(string stage) => string.Equals(Full(Path.GetDirectoryName(Full(stage))!), Full(StageBase), StringComparison.OrdinalIgnoreCase)
        && Guid.TryParseExact(Path.GetFileName(Full(stage)), "N", out _);

    public static string CreateStage()
    {
        if (Directory.Exists(StageBase) && HasReparsePoint(StageBase))
            throw new UpdateException(UpdateProblem.Location, "Linked updater staging directory.");
        string stage = Path.Combine(StageBase, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, ".wavebreak-stage"), "Wavebreak:" + Path.GetFileName(stage));
        return stage;
    }

    public static void Cleanup(string stage)
    {
        if (!IsStage(stage) || !Directory.Exists(stage)) return;
        string marker = Path.Combine(stage, ".wavebreak-stage");
        if (File.Exists(marker) && File.ReadAllText(marker) == "Wavebreak:" + Path.GetFileName(stage)
            && !HasReparsePoint(stage)) Directory.Delete(stage, true);
    }

    public static bool IsOwnedStage(string stage)
    {
        if (!IsStage(stage) || !Directory.Exists(stage) || HasReparsePoint(stage) || HasReparsePoint(StageBase)) return false;
        string marker = Path.Combine(stage, ".wavebreak-stage");
        return File.Exists(marker) && !HasReparsePoint(marker) && new FileInfo(marker).Length <= 128
            && File.ReadAllText(marker) == "Wavebreak:" + Path.GetFileName(stage);
    }

    public static bool HasReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public static bool IsDevelopmentTree(string root)
    {
        for (DirectoryInfo? directory = new(Full(root)); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "project.godot")) || Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, ".git"))) return true;
        return false;
    }

    public static void ValidateRoot(string root)
    {
        string full = Full(root);
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.Length <= Full(Path.GetPathRoot(full)!).Length || !Directory.Exists(full) || HasReparsePoint(full)
            || IsDevelopmentTree(full) || !File.Exists(Path.Combine(full, "Wavebreak.exe"))
            || !File.Exists(Path.Combine(full, "Wavebreak.pck"))
            || !File.Exists(Path.Combine(full, "data_Wavebreak_windows_x86_64", "Wavebreak.dll")))
            throw new UpdateException(UpdateProblem.Location, "This location cannot be updated automatically.");
        foreach (string relative in new[] { "Wavebreak.exe", "Wavebreak.pck", "data_Wavebreak_windows_x86_64", "data_Wavebreak_windows_x86_64/Wavebreak.dll" })
            if (HasReparsePoint(Path.Combine(full, relative)))
                throw new UpdateException(UpdateProblem.Location, "Linked game files cannot be updated automatically.");
        for (DirectoryInfo? directory = new(full); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UpdateException(UpdateProblem.Location, "Linked installation directories cannot be updated automatically.");
    }
}
