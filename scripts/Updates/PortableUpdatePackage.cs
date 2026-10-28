using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Wavebreak.Updates;

public static class PortableUpdatePackage
{
    public static async Task<IReadOnlyList<UpdatePayloadFile>> ExtractAsync(string archivePath, string payload, CancellationToken cancel)
    {
        return await Task.Run(() => Extract(archivePath, payload, cancel), cancel).ConfigureAwait(false);
    }

    private static IReadOnlyList<UpdatePayloadFile> Extract(string archivePath, string payload, CancellationToken cancel)
    {
        if (!UpdatePaths.IsOwnedStage(Path.GetDirectoryName(payload)!)) throw new UpdateException(UpdateProblem.Location, "Unexpected staging path.");
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 10000) throw new UpdateException(UpdateProblem.Package, "Too many package entries.");
        long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(ZipArchiveEntry Entry, string Name)>();
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancel.ThrowIfCancellationRequested();
            string raw = entry.FullName.Replace('\\', '/');
            while (raw.StartsWith("./", StringComparison.Ordinal)) raw = raw[2..];
            if (raw is "" or ".") continue;
            if (raw.EndsWith('/')) { SafeRelative(raw.TrimEnd('/')); continue; }
            string name = SafeRelative(raw);
            if (!names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new UpdateException(UpdateProblem.Package, "Duplicate or linked package entry.");
            total = checked(total + entry.Length);
            if (entry.Length < 0 || total > 2L * 1024 * 1024 * 1024) throw new UpdateException(UpdateProblem.Package, "Unpacked package too large.");
            files.Add((entry, name));
        }
        foreach (string required in new[] { "Wavebreak.exe", "Wavebreak.pck", "data_Wavebreak_windows_x86_64/Wavebreak.dll", "steam_api64.dll", "steam_appid.txt" })
            if (!names.Contains(required)) throw new UpdateException(UpdateProblem.Package, "Incomplete Windows game package.");
        Directory.CreateDirectory(payload);
        var manifest = new List<UpdatePayloadFile>(files.Count);
        foreach ((ZipArchiveEntry entry, string name) in files)
        {
            cancel.ThrowIfCancellationRequested();
            string destination = Path.GetFullPath(Path.Combine(payload, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!UpdatePaths.Within(payload, destination)) throw new UpdateException(UpdateProblem.Package, "Package path escapes staging.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[65536]; long written = 0; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                cancel.ThrowIfCancellationRequested(); written += count;
                if (written > entry.Length) throw new UpdateException(UpdateProblem.Package, "Package entry exceeds declared length.");
                output.Write(buffer, 0, count); digest.AppendData(buffer, 0, count);
            }
            if (written != entry.Length) throw new UpdateException(UpdateProblem.Package, "Truncated package entry.");
            manifest.Add(new(name, written, Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant()));
        }
        ValidatePe(Path.Combine(payload, "Wavebreak.exe"));
        using var pack = File.OpenRead(Path.Combine(payload, "Wavebreak.pck"));
        Span<byte> header = stackalloc byte[4];
        if (pack.Read(header) != 4 || !header.SequenceEqual("GDPC"u8)) throw new UpdateException(UpdateProblem.Package, "Invalid Godot pack header.");
        return manifest;
    }

    public static string SafeRelative(string name)
    {
        if (name.Length > 240 || name.StartsWith('/') || name.Contains(':') || name.Contains('\\'))
            throw new UpdateException(UpdateProblem.Package, "Invalid package path.");
        foreach (string part in name.Split('/'))
        {
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9'
                || part.Equals(".git", StringComparison.OrdinalIgnoreCase) || part.Equals(".godot", StringComparison.OrdinalIgnoreCase) || part.Equals("project.godot", StringComparison.OrdinalIgnoreCase)
                || part.Equals("profile.cfg", StringComparison.OrdinalIgnoreCase) || part.Equals("settings.cfg", StringComparison.OrdinalIgnoreCase))
                throw new UpdateException(UpdateProblem.Package, "Unsafe package path.");
        }
        return name;
    }

    public static void ValidatePe(string path)
    {
        using var file = File.OpenRead(path); using var reader = new BinaryReader(file);
        if (file.Length < 64 || reader.ReadUInt16() != 0x5a4d) throw new UpdateException(UpdateProblem.Package, "Invalid Windows executable.");
        file.Position = 0x3c; uint offset = reader.ReadUInt32();
        if (offset > file.Length - 4 || offset < 64) throw new UpdateException(UpdateProblem.Package, "Invalid Windows executable header.");
        file.Position = offset;
        if (reader.ReadUInt32() != 0x00004550) throw new UpdateException(UpdateProblem.Package, "Invalid Windows executable signature.");
    }
}
