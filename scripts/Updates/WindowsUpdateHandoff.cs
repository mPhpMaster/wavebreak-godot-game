using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Wavebreak.Updates;

public sealed class WindowsUpdateHandoff : IUpdateHandoff
{
    private readonly string _pendingPath;
    public WindowsUpdateHandoff(string pendingPath) => _pendingPath = pendingPath;
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static void WriteHelpers(string stage)
    {
        if (!UpdatePaths.IsOwnedStage(stage)) throw new UpdateException(UpdateProblem.Location, "Invalid updater stage.");
        foreach (string resource in new[] { "ApplyUpdate.ps1", "UpdatePortable.ps1", "UpdateProgress.ps1" })
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Wavebreak." + resource)
                ?? throw new UpdateException(UpdateProblem.Handoff, "Bundled updater is missing.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            File.WriteAllText(Path.Combine(stage, resource), reader.ReadToEnd(), new UTF8Encoding(true));
        }
    }

    public async Task<bool> StartAsync(UpdateContext context, UpdateRelease release, PreparedUpdate prepared,
        bool restart, string language, CancellationToken cancel)
    {
        if (!OperatingSystem.IsWindows() || !context.CanInstall || !UpdatePaths.IsOwnedStage(prepared.Stage))
            throw new UpdateException(UpdateProblem.Location, "Automatic installation is unavailable here.");
        UpdatePaths.ValidateRoot(context.Root);
        if (!string.Equals(UpdatePaths.Full(context.Executable), UpdatePaths.Full(Path.Combine(context.Root, "Wavebreak.exe")), StringComparison.OrdinalIgnoreCase))
            throw new UpdateException(UpdateProblem.Location, "Unexpected game executable.");
        foreach (Process process in Process.GetProcessesByName("Wavebreak"))
            using (process)
                if (process.Id != context.ProcessId)
                    try
                    {
                        if (string.Equals(process.MainModule?.FileName, context.Executable, StringComparison.OrdinalIgnoreCase))
                            throw new UpdateException(UpdateProblem.Handoff, "Close the other copy of Wavebreak before updating.");
                    }
                    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        WriteHelpers(prepared.Stage);
        var request = new UpdateRequest(1, "Wavebreak", context.Kind == UpdateInstallKind.Installed ? "installed" : "portable",
            release.Version.ToString(), context.Root, context.Executable, context.ProcessId,
            context.ProcessStartedUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture), prepared.Asset.Name,
            prepared.Asset.Sha256, prepared.Files, restart, language == "ar" ? "ar" : "en");
        string requestPath = Path.Combine(prepared.Stage, "request.json");
        File.WriteAllText(requestPath, JsonSerializer.Serialize(request, JsonOptions));
        string cancelFile = Path.Combine(prepared.Stage, "cancel.request");
        if (File.Exists(cancelFile)) File.Delete(cancelFile);
        string ready = Path.Combine(prepared.Stage, "ready.json");
        if (File.Exists(ready)) File.Delete(ready);
        string status = Path.Combine(prepared.Stage, "status.json");
        if (File.Exists(status)) File.Delete(status);
        string powershell = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true, WorkingDirectory = prepared.Stage,
        };
        foreach (string argument in new[] { "-STA", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File",
            Path.Combine(prepared.Stage, "ApplyUpdate.ps1"), "-RequestPath", requestPath }) start.ArgumentList.Add(argument);
        using Process helper = Process.Start(start) ?? throw new UpdateException(UpdateProblem.Handoff, "Updater could not start.");
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(12))
            {
                cancel.ThrowIfCancellationRequested();
                if (File.Exists(ready))
                {
                    using JsonDocument acknowledgement = JsonDocument.Parse(await File.ReadAllTextAsync(ready, cancel));
                    if (acknowledgement.RootElement.GetProperty("stage").GetString() != Path.GetFileName(prepared.Stage))
                        throw new UpdateException(UpdateProblem.Handoff, "Unexpected updater acknowledgement.");
                    Directory.CreateDirectory(Path.GetDirectoryName(_pendingPath)!);
                    await File.WriteAllTextAsync(_pendingPath, JsonSerializer.Serialize(new
                    { stage = Path.GetFileName(prepared.Stage), version = release.Version.ToString() }, JsonOptions), cancel);
                    return true;
                }
                if (File.Exists(status))
                {
                    using JsonDocument report = JsonDocument.Parse(await File.ReadAllTextAsync(status, cancel));
                    if (report.RootElement.GetProperty("phase").GetString() == "failed")
                        throw new UpdateException(UpdateProblem.Handoff,
                            report.RootElement.GetProperty("detail").GetString() ?? "Updater could not prepare installation.");
                }
                if (helper.HasExited) break;
                await Task.Delay(100, cancel);
            }
            throw new UpdateException(UpdateProblem.Handoff, "Updater did not become ready; the game remains open.");
        }
        catch
        {
            try { File.WriteAllText(cancelFile, "cancel"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            if (!helper.HasExited)
                try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch (TimeoutException) { }
            if (helper.HasExited)
                await File.WriteAllTextAsync(Path.Combine(prepared.Stage, "helper-stderr.log"), await helper.StandardError.ReadToEndAsync());
            throw;
        }
    }
}
