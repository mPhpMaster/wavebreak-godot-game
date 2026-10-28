using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Wavebreak.Updates;

public static class UpdateInstallDiscovery
{
    public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F1C2E7A-8B4D-4E2A-9C61-57A0B3D9E1F4}_is1";

    public static UpdateContext Detect(string version, bool development)
    {
        string exe = System.Environment.ProcessPath ?? "";
        string root = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        using Process process = Process.GetCurrentProcess();
        UpdateInstallKind kind = UpdateInstallKind.Unsupported;
        if (development || UpdatePaths.IsDevelopmentTree(root) || !Path.GetFileName(exe).Equals("Wavebreak.exe", StringComparison.OrdinalIgnoreCase))
            kind = UpdateInstallKind.Development;
        else if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            if (root.Replace('/', '\\').Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase)) kind = UpdateInstallKind.StoreManaged;
            else
            {
                try
                {
                    UpdatePaths.ValidateRoot(root);
                    kind = RegisteredAt(root) ? UpdateInstallKind.Installed
                        : Directory.GetFiles(root, "unins*.exe").Length == 0 ? UpdateInstallKind.Portable : UpdateInstallKind.Unsupported;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or UpdateException or System.Security.SecurityException or ArgumentException) { }
            }
        }
        return new(version, kind, UpdatePaths.Full(root), exe, process.Id, process.StartTime.ToUniversalTime().Ticks);
    }

    private static bool RegisteredAt(string root)
    {
        if (!OperatingSystem.IsWindows()) return false;
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? key = baseKey.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string installed && installed.Length > 0
                    && string.Equals(UpdatePaths.Full(installed), UpdatePaths.Full(root), StringComparison.OrdinalIgnoreCase)) return true;
            }
        return false;
    }
}
