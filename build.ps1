# Build Wavebreak for Windows: C# assemblies, the exported game, the portable
# zip and the installer. Outputs land in build/ (git-ignored).
#
#   ./build.ps1                 # everything
#   ./build.ps1 -SkipInstaller  # game + zip only
#   ./build.ps1 -Run            # also launch the built game when done
#
# Paths default to this machine's layout; override with parameters or the
# GODOT / ISCC environment variables.

param(
    [string]$Godot = $(if ($env:GODOT) { $env:GODOT } else { 'D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' }),
    [string]$Iscc = $(if ($env:ISCC) { $env:ISCC } else { 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }),
    [switch]$SkipInstaller,
    [switch]$SkipZip,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

# Keep caches and temp files off C: (see the project's machine notes).
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = 'D:\tools\megabonk\nuget' }
if (-not $env:TMP) { $env:TMP = 'D:\tools\megabonk\tmp'; $env:TEMP = $env:TMP }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$version = (Select-String -Path "$root\project.godot" -Pattern '^config/version="([^"]+)"').Matches[0].Groups[1].Value
Write-Host "Wavebreak $version" -ForegroundColor Cyan

function Step($name, [scriptblock]$body) {
    Write-Host "==> $name" -ForegroundColor Yellow
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $body
    Write-Host ("    done in {0:0.0}s" -f $sw.Elapsed.TotalSeconds) -ForegroundColor DarkGray
}

if (-not (Test-Path $Godot)) { throw "Godot not found: $Godot (set -Godot or `$env:GODOT)" }

Step 'Compile C#' {
    dotnet build -v q --nologo "$root\Wavebreak.csproj"
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
}

Step 'Import assets' {
    & $Godot --headless --path $root --import 2>&1 | Out-Null
}

$out = "$root\build\windows"
Step 'Export game' {
    if (Get-Process Wavebreak -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$out*" }) {
        throw "Wavebreak.exe from $out is running; close it first."
    }
    New-Item -ItemType Directory -Force $out | Out-Null
    $log = "$root\build\export.log"
    # One string, each value quoted: Start-Process joins an array with bare
    # spaces, which split the preset name 'Windows Desktop' in two.
    $argLine = "--headless --path `"$root`" --export-release `"Windows Desktop`" `"$out\Wavebreak.exe`""
    $p = Start-Process -FilePath $Godot -ArgumentList $argLine `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err" -PassThru -NoNewWindow
    # The exporter can linger after it is done. It copies the C# assemblies
    # AFTER saving the pack, so stop it only once the game's DLL is fresh;
    # killing it right after the pack shipped the previous build's code.
    $started = Get-Date
    $dll = "$out\data_Wavebreak_windows_x86_64\Wavebreak.dll"
    $deadline = $started.AddMinutes(10)
    while (-not $p.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $packed = Select-String -Path $log -Pattern 'DONE.*savepack' -Quiet -ErrorAction SilentlyContinue
        $fresh = (Test-Path $dll) -and (Get-Item $dll).LastWriteTime -ge $started
        if ($packed -and $fresh) {
            Start-Sleep -Seconds 5
            if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        }
    }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    if (-not (Select-String -Path $log -Pattern 'DONE.*savepack' -Quiet)) { throw "export failed, see $log" }
    if (-not (Test-Path $dll) -or (Get-Item $dll).LastWriteTime -lt $started) {
        throw "export left an old Wavebreak.dll (the game would run stale code); see $log"
    }
    foreach ($f in 'Wavebreak.exe', 'Wavebreak.pck', 'steam_api64.dll', 'steam_appid.txt') {
        if (-not (Test-Path "$out\$f")) { throw "export is missing $f" }
    }
}

if (-not $SkipZip) {
    Step 'Portable zip' {
        $zip = "$root\build\Wavebreak-$version-windows.zip"
        Remove-Item $zip -ErrorAction SilentlyContinue
        # tar reads files with sharing, so this works even while the game is open.
        & "$env:WINDIR\System32\tar.exe" -a -c -f $zip -C $out .
        if ($LASTEXITCODE -ne 0) { throw 'zip failed' }
        Write-Host "    $zip"
    }
}

if (-not $SkipInstaller) {
    Step 'Installer' {
        if (-not (Test-Path $Iscc)) { throw "Inno Setup not found: $Iscc (set -Iscc or `$env:ISCC)" }
        & $Iscc /Q "/DAppVersion=$version" "$root\installer\Wavebreak.iss"
        if ($LASTEXITCODE -ne 0) { throw 'installer build failed' }
        Write-Host "    $root\build\installer\Wavebreak-Setup-$version.exe"
    }
}

Write-Host "Build complete: $out" -ForegroundColor Green
if ($Run) { Start-Process -FilePath "$out\Wavebreak.exe" -WorkingDirectory $out }
