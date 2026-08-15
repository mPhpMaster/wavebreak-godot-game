# Build and run Wavebreak.
#
#   ./run.ps1                    play it
#   ./run.ps1 -Shot out.png      run headless-ish for -After seconds, capture a frame, exit
#   ./run.ps1 -Seed 42           reproducible run
#
# The capture path is how this project verifies itself: a frame taken after real
# simulation shows the arena, the player, live enemies, the HUD and the damage
# numbers at once, which a build log cannot.

param(
    [string]$Shot,
    [float]$After = 6,
    [int]$Seed = 42,
    [string]$Resolution = '1280x720',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$godotDir = 'D:\tools\godot\Godot_v4.7.1-stable_mono_win64'
$gui      = Join-Path $godotDir 'Godot_v4.7.1-stable_mono_win64.exe'
$console  = Join-Path $godotDir 'Godot_v4.7.1-stable_mono_win64_console.exe'
if (-not (Test-Path $gui)) { throw "Godot not found at $godotDir" }

$env:NUGET_PACKAGES = 'D:\tools\megabonk\nuget'
$env:TMP = 'D:\tools\megabonk\tmp'
$env:TEMP = 'D:\tools\megabonk\tmp'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if (-not $SkipBuild) {
    Write-Host 'building...'
    dotnet build -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
}

if ($Shot) {
    # Everything after the bare -- goes to the game rather than the engine.
    & $console --path $PSScriptRoot --resolution $Resolution -- `
        "--shot=$Shot" "--shot-after=$After" "--seed=$Seed"
    if (Test-Path $Shot) {
        Write-Host "captured $Shot" -ForegroundColor Green
        Get-Item $Shot | Select-Object Name, Length
    } else {
        Write-Host "no frame was captured" -ForegroundColor Red
        exit 1
    }
} else {
    & $gui --path $PSScriptRoot --resolution $Resolution -- "--seed=$Seed"
}
