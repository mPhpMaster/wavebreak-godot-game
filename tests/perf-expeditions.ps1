param(
    [string]$Godot = 'D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe',
    [string]$OutputDirectory = (Join-Path $env:TEMP ('wavebreak-performance-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)

$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not (Test-Path -LiteralPath $Godot -PathType Leaf)) { throw "Godot executable missing: $Godot" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($map in @('city', 'reef')) {
    $route = if ($map -eq 'city') { 'skyline-run' } else { 'reef-dive' }
    $prefix = Join-Path $OutputDirectory $map
    $log = $prefix + '.log'
    $command = '"' + $Godot + '" --path "' + $taskRoot + '" --windowed --resolution 1920x1080 --audio-driver Dummy -- --auto --invulnerable --seed=42 --perf-user-graphics --route=' + $route + ' --perf-tour="' + $prefix + '"'
    if ($map -eq 'city') { $command += ' --perf-expanded' }
    & (Join-Path $PSScriptRoot 'run-offscreen.ps1') -Command $command -WorkDir $taskRoot -DesktopName ('WBPerf' + $map) -TimeoutSec 160 -Out $log
    $contents = Get-Content -LiteralPath $log -Raw
    if ($LASTEXITCODE -ne 0 -or -not $contents.Contains('TOUR COMPLETE') -or $contents -match '(?m)^(ERROR:|SCRIPT ERROR:)') {
        throw "Performance tour failed: $log"
    }
    Select-String -LiteralPath $log -Pattern '^TOUR (QUALITY|RESULT|SCOPES)' | ForEach-Object { $_.Line }
}
Write-Output "Performance evidence: $OutputDirectory"
