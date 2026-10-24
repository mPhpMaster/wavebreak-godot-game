#if TOOLS
using System.IO;
using Godot;
using Wavebreak.Net;

namespace Wavebreak.Editor;

/// <summary>
/// Copies the Steam runtime files next to an exported Windows executable:
///   steam_api64.dll   from thirdparty/steamworks/win64 (Valve's redistributable,
///                     the SDK version Steamworks.NET 2024.8.0 is built against)
///   steam_appid.txt   <see cref="SteamApp.AppId"/>, so the game starts Steam
///                     under the right app when launched outside Steam
/// The game finds the DLL there at run time (<see cref="SteamApp"/>).
/// Done after the export, when the folder exists; a missing DLL is an error
/// in the export log rather than a game that silently has no co-op.
/// </summary>
[Tool]
public partial class SteamExport : EditorExportPlugin
{
    private string? _dir;

    public override string _GetName() => "WavebreakSteam";

    public override void _ExportBegin(string[] features, bool isDebug, string path, uint flags)
    {
        _dir = null;
        foreach (string f in features)
            if (f == "windows") _dir = Path.GetDirectoryName(Path.GetFullPath(ProjectSettings.GlobalizePath(path)));
    }

    public override void _ExportEnd()
    {
        if (_dir is null) return;
        string dll = ProjectSettings.GlobalizePath(SteamApp.VendoredDll);
        if (!File.Exists(dll))
        {
            GD.PushError($"[steam export] missing {dll}: the exported game will have no Steam co-op");
            return;
        }
        Directory.CreateDirectory(_dir);
        File.Copy(dll, Path.Combine(_dir, SteamApp.NativeLibrary + ".dll"), overwrite: true);
        File.WriteAllText(Path.Combine(_dir, "steam_appid.txt"), SteamApp.AppId.ToString());
        GD.Print($"[steam export] steam_api64.dll and steam_appid.txt ({SteamApp.AppId}) -> {_dir}");
        _dir = null;
    }
}
#endif
