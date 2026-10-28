param(
    [string]$Godot = 'D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe',
    [ValidateSet('city','reef')][string[]]$Maps = @('city','reef'),
    [ValidateSet('reference','tuned')][string[]]$Modes = @('reference','tuned'),
    [ValidateRange(90,180)][int]$Seconds = 120,
    [string]$OutputDirectory = (Join-Path $env:TEMP ('wavebreak-soak-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not (Test-Path -LiteralPath $Godot -PathType Leaf)) { throw "Godot executable missing: $Godot" }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
foreach ($map in $Maps) {
    $route = if ($map -eq 'city') { 'skyline-run' } else { 'reef-dive' }
    $hero = if ($map -eq 'city') { 'vesper' } else { 'nori' }
    $seed = if ($map -eq 'city') { 632668492 } else { 42 }
    foreach ($mode in $Modes) {
        $prefix = Join-Path $OutputDirectory ($map + '-' + $mode)
        $log = $prefix + '.log'
        $command = '"' + $Godot + '" --path "' + $taskRoot + '" --windowed --resolution 1920x1080 --audio-driver Dummy -- --auto --invulnerable --seed=' + $seed + ' --perf-user-graphics --route=' + $route + ' --character=' + $hero + ' --soak-seconds=' + $Seconds + ' --perf-soak="' + $prefix + '"'
        if ($mode -eq 'reference') { $command += ' --perf-reference' }
        $timer = [Diagnostics.Stopwatch]::StartNew()
        & (Join-Path $PSScriptRoot 'run-offscreen.ps1') -Command $command -WorkDir $taskRoot -DesktopName ('WBSoak' + $map + $mode) -TimeoutSec ($Seconds + 80) -Out $log
        $code = $LASTEXITCODE
        $contents = Get-Content -LiteralPath $log -Raw
        if ($code -ne 0 -or -not $contents.Contains('SOAK COMPLETE') -or $contents -match '(?m)^(ERROR:|SCRIPT ERROR:)') { throw "Soak incomplete: $log" }
        if ($map -eq 'city' -and ($contents -notmatch 'left_acquires=[1-9]' -or $contents -notmatch 'right_acquires=[1-9]' -or $contents -notmatch 'boosts=[1-9]')) { throw "Traversal not exercised: $log" }
        Select-String -LiteralPath $log -Pattern '^SOAK (STARTUP|CONFIG|RESULT segment=(all|steady)|COMPLETE)' | ForEach-Object { $_.Line }
        Write-Output ("Wall seconds: {0:F1}; evidence: {1}" -f $timer.Elapsed.TotalSeconds, $log)
    }
}
