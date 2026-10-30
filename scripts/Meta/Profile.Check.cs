using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Wavebreak.Meta;

/// <summary>--check-profile: saving and recovery on real files in a temp folder,
/// never the player's. Prints PROFILE ok/FAIL lines and PROFILE PASS/FAIL.</summary>
public static partial class Profile
{
    public static bool CheckRequested => Array.IndexOf(OS.GetCmdlineUserArgs(), "--check-profile") >= 0;

    public static bool RunCheck()
    {
        int checks = 0;
        var failed = new List<string>();
        void Expect(string what, bool ok)
        {
            checks++;
            GD.Print($"PROFILE {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failed.Add(what);
        }

        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wavebreak-check-profile-{OS.GetProcessId()}").Replace('\\', '/');
        System.IO.Directory.CreateDirectory(dir);
        string path = dir + "/profile.cfg";

        ConfigFile Sample(long kills)
        {
            var c = new ConfigFile();
            c.SetValue("profile", "version", Version);
            c.SetValue("stats", TotalKills, kills);
            c.SetValue(EndSection, EndKey, true);
            return c;
        }
        long Kills(ConfigFile? c) => c is null ? -1 : c.GetValue("stats", TotalKills, -1).AsInt64();
        void Reset()
        {
            foreach (string f in System.IO.Directory.GetFiles(dir)) System.IO.File.Delete(f);
        }
        void WriteRaw(string to, string text) => System.IO.File.WriteAllText(to, text);
        string Good(long kills) => $"[profile]\n\nversion={Version}\n\n[stats]\n\n{TotalKills}={kills}\n\n[{EndSection}]\n\n{EndKey}=true\n";

        // Saving.
        Expect("first save writes the file", WriteSafely(Sample(1), path) == Error.Ok && FileAccess.FileExists(path));
        Expect("first save leaves no backup yet", !FileAccess.FileExists(path + ".bak"));
        WriteSafely(Sample(2), path);
        Expect("second save reads back", Kills(LoadBest(path, out bool r1)) == 2 && !r1);
        var bak = new ConfigFile();
        Expect("the backup holds the previous save", bak.Load(path + ".bak") == Error.Ok && Kills(bak) == 1);
        Expect("no temp files stay behind", !FileAccess.FileExists(path + ".tmp") && !FileAccess.FileExists(path + ".bak.tmp"));
        string text = System.IO.File.ReadAllText(path);
        Expect("the end marker is the file's last section", text.TrimEnd().EndsWith($"{EndKey}=true") && text.LastIndexOf('[') == text.IndexOf($"[{EndSection}]", StringComparison.Ordinal));

        // No moment without the file, measured: a second thread looks for it
        // through 300 saves.
        long misses = 0, looks = 0;
        int failedSaves = 0;
        using (var stop = new CancellationTokenSource())
        {
            string absolute = path;
            Task watcher = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    looks++;
                    if (!System.IO.File.Exists(absolute)) misses++;
                    Thread.Sleep(0);
                }
            });
            for (int i = 0; i < 300; i++) if (WriteSafely(Sample(i), path) != Error.Ok) failedSaves++;
            stop.Cancel();
            watcher.Wait();
        }
        Expect($"the file never went missing during 300 saves ({looks} looks, {misses} misses)", misses == 0 && looks > 0);
        Expect($"every save got through a file held open by others ({failedSaves} failed)", failedSaves == 0);

        // Recovery.
        Reset();
        WriteRaw(path, "[stats\nbroken = = =");
        WriteRaw(path + ".bak", Good(10));
        Expect("a corrupt file recovers from the backup", Kills(LoadBest(path, out bool r2)) == 10 && r2);
        Expect("the corrupt file is quarantined", FileAccess.FileExists(path + ".corrupt") && !FileAccess.FileExists(path));

        WriteRaw(path, "[stats\nbroken again = = =");
        LoadBest(path, out _);
        Expect("a second quarantine keeps the first", FileAccess.FileExists(path + ".corrupt") && FileAccess.FileExists(path + ".corrupt.1"));

        Reset();
        WriteRaw(path + ".tmp", Good(20));
        WriteRaw(path + ".bak", Good(10));
        Expect("a missing file recovers from a complete temp, the newest copy", Kills(LoadBest(path, out bool r3)) == 20 && r3);

        Reset();
        WriteRaw(path + ".tmp", Good(20)[..(Good(20).IndexOf($"[{EndSection}]", StringComparison.Ordinal))]);
        WriteRaw(path + ".bak", Good(10));
        Expect("a temp cut short is passed over for the backup", Kills(LoadBest(path, out bool r4)) == 10 && r4);

        Reset();
        WriteRaw(path, Good(30)[..(Good(30).IndexOf($"[{EndSection}]", StringComparison.Ordinal))]);
        WriteRaw(path + ".bak", Good(10));
        Expect("a file cut short that still parses counts as corrupt", Kills(LoadBest(path, out _)) == 10 && FileAccess.FileExists(path + ".corrupt"));

        Reset();
        WriteRaw(path, "");
        WriteRaw(path + ".bak", Good(10));
        Expect("an empty file counts as corrupt", Kills(LoadBest(path, out _)) == 10);

        Reset();
        WriteRaw(path, "[stats\nbroken = = =");
        Expect("nothing readable at all is a new player, quarantine kept", LoadBest(path, out bool r5) is null && !r5 && FileAccess.FileExists(path + ".corrupt"));

        Reset();
        Expect("no files at all is a new player", LoadBest(path, out _) is null);

        // Versions.
        Reset();
        WriteRaw(path, $"[profile]\n\nversion=1\n\n[stats]\n\n{TotalKills}=5\n");
        ConfigFile? old = LoadBest(path, out bool r6);
        Expect("a version 1 file (no end marker) reads as it is", Kills(old) == 5 && !r6 && ReadVersion(old!) == 1);

        Reset();
        WriteRaw(path, "[unlocks]\n\nqh_sunny=true\n");
        ConfigFile? hand = LoadBest(path, out _);
        Expect("a hand-written file with no version reads as version 1", hand is not null && ReadVersion(hand) == 1);

        Reset();
        WriteRaw(path, $"[profile]\n\nversion=\"banana\"\n\n[stats]\n\n{TotalKills}=6\n");
        Expect("a nonsense version reads as version 1", Kills(LoadBest(path, out _)) == 6);

        Reset();
        WriteRaw(path, $"[profile]\n\nversion=99\n\n[stats]\n\n{TotalKills}=7\n\n[{EndSection}]\n\n{EndKey}=true\n");
        ConfigFile? newer = LoadBest(path, out _);
        Expect("a newer file still reads", Kills(newer) == 7 && ReadVersion(newer!) == 99);
        Migrate(newer!, 99);
        Expect("no migration runs on a newer file", Kills(newer) == 7);

        Reset();
        System.IO.Directory.Delete(dir);
        GD.Print(failed.Count == 0 ? $"PROFILE PASS checks={checks}" : $"PROFILE FAIL checks={checks} failed={failed.Count}");
        return failed.Count == 0;
    }
}
