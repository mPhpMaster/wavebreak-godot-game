using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;
using Wavebreak.Meta;

namespace Wavebreak.Net;

/// <summary>
/// Steam for co-op only: start-up, the callback pump, the App ID.
///
/// When Steam starts:
///   * never in single-player: only when the CO-OP page opens, its RETRY is
///     pressed, or the game was launched to join a lobby (+connect_lobby);
///   * never in an automated run (tests, captures) unless --steam-test is given.
/// If it cannot start (Steam not running, not signed in, DLL missing) the
/// CO-OP page says "Steam must be running to play co-op" with a RETRY button;
/// nothing else in the game notices.
///
/// App ID: <see cref="AppId"/> is Valve's public test app 480 ("Spacewar").
/// Any Steam user can use it with no store page, which is why friends see
/// "Spacewar" in their friends list and invite dialog. Releasing under the
/// game's own name needs its own App ID from Steamworks: change the constant
/// (and steam_appid.txt, written from it), nothing else.
/// </summary>
public static class SteamApp
{
    /// <summary>Steam App ID. 480 = Spacewar, Valve's free test app.</summary>
    public const uint AppId = 480;

    /// <summary>Native library name Steamworks.NET imports (64-bit Windows).</summary>
    public const string NativeLibrary = "steam_api64";

    /// <summary>Where the DLL lives in the source tree (next to the exe once exported).</summary>
    public const string VendoredDll = "res://thirdparty/steamworks/win64/steam_api64.dll";

    public static bool Ready { get; private set; }

    /// <summary>Why the last start failed (for the log; the page shows one message).</summary>
    public static string LastError { get; private set; } = "";

    public static ulong LocalSteamId { get; private set; }
    public static string PersonaName { get; private set; } = "";

    /// <summary>--steam-test: let an automated run start Steam (live checks).</summary>
    public static bool TestFlag
    {
        get
        {
            foreach (string a in OS.GetCmdlineUserArgs()) if (a == "--steam-test" || a.StartsWith("--steam-test=")) return true;
            return false;
        }
    }

    /// <summary>Automated runs keep their hands off Steam unless asked.</summary>
    public static bool Allowed => !Launch.Automated || TestFlag;

    private static bool _resolverSet;
    private static SteamPump? _pump;

    /// <summary>Start Steam if it is not running yet. Cheap when already up.
    /// Returns false (and sets <see cref="LastError"/>) when it cannot.</summary>
    public static bool TryInit(SceneTree tree)
    {
        if (Ready) return true;
        if (!Allowed)
        {
            LastError = "automated run (pass --steam-test to use Steam)";
            return false;
        }
        if (!OperatingSystem.IsWindows() || !System.Environment.Is64BitProcess)
        {
            LastError = "Steam co-op is built for 64-bit Windows";
            return false;
        }
        try
        {
            return InitCore(tree);
        }
        catch (Exception e)   // DLL missing or wrong, Steam gone mid-call: never take the game down
        {
            LastError = $"{e.GetType().Name}: {e.Message}";
            GD.Print($"[steam] init failed: {LastError}");
            return false;
        }
    }

    private static bool InitCore(SceneTree tree)
    {
        SetResolver();
        // The SDK finds the App ID in steam_appid.txt in the working directory
        // or in these variables. Both, so a launch from anywhere works.
        System.Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
        System.Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());
        WriteAppIdFile();

        if (!Packsize.Test())
        {
            LastError = "Steamworks.NET was built for another platform (packsize)";
            GD.Print($"[steam] {LastError}");
            return false;
        }
        ESteamAPIInitResult r = SteamAPI.InitEx(out string message);
        if (r != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
        {
            LastError = $"{r}: {message}";
            GD.Print($"[steam] init failed: {LastError}");
            return false;
        }
        Ready = true;
        LastError = "";
        LocalSteamId = SteamUser.GetSteamID().m_SteamID;
        PersonaName = SteamFriends.GetPersonaName() ?? "";
        // Start the relay handshake now so the first connection is quick.
        SteamNetworkingUtils.InitRelayNetworkAccess();
        GD.Print($"[steam] ready: app {SteamUtils.GetAppID().m_AppId}, user {PersonaName} ({LocalSteamId}), overlay {(SteamUtils.IsOverlayEnabled() ? "on" : "off")}");

        _pump = new SteamPump { Name = "SteamPump" };
        tree.Root.CallDeferred(Node.MethodName.AddChild, _pump);
        SteamLobby.Install();
        return true;
    }

    /// <summary>Called by the pump when the game quits.</summary>
    internal static void Shutdown()
    {
        if (!Ready) return;
        SteamLobby.Leave();
        Ready = false;
        SteamAPI.Shutdown();
        GD.Print("[steam] shut down");
    }

    /// <summary>Directory of the running executable (the exported game's folder).</summary>
    public static string ExeDir => Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";

    /// <summary>Exported builds: put steam_appid.txt next to the exe if the
    /// export did not (or it says another id). Editor runs rely on the
    /// environment variables instead of writing into the Godot install.</summary>
    private static void WriteAppIdFile()
    {
        if (!OS.HasFeature("template")) return;
        try
        {
            string path = Path.Combine(ExeDir, "steam_appid.txt");
            if (!File.Exists(path) || File.ReadAllText(path).Trim() != AppId.ToString())
                File.WriteAllText(path, AppId.ToString());
        }
        catch (Exception e)
        {
            GD.Print($"[steam] could not write steam_appid.txt ({e.Message}); using the environment");
        }
    }

    /// <summary>Find steam_api64.dll next to the exe (exported) or in the
    /// source tree (editor and test runs), before the default search.</summary>
    private static void SetResolver()
    {
        if (_resolverSet) return;
        _resolverSet = true;
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (!name.StartsWith(NativeLibrary, StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        foreach (string candidate in new[]
                 {
                     Path.Combine(ExeDir, NativeLibrary + ".dll"),
                     ProjectSettings.GlobalizePath(VendoredDll),
                 })
        {
            if (candidate.Length > 0 && File.Exists(candidate)
                && System.Runtime.InteropServices.NativeLibrary.TryLoad(candidate, out IntPtr handle))
                return handle;
        }
        return IntPtr.Zero;   // fall back to the default search
    }
}
