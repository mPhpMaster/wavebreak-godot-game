#if TOOLS
using Godot;

namespace Wavebreak.Editor;

/// <summary>Editor plugin that registers <see cref="SteamExport"/>: every
/// Windows export gets steam_api64.dll and steam_appid.txt beside the exe.</summary>
[Tool]
public partial class SteamExportPlugin : EditorPlugin
{
    private SteamExport? _export;

    public override void _EnterTree()
    {
        _export = new SteamExport();
        AddExportPlugin(_export);
    }

    public override void _ExitTree()
    {
        if (_export is not null) RemoveExportPlugin(_export);
        _export = null;
    }
}
#endif
