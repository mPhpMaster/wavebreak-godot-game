# Drive several seeded runs and assert the loop actually produces outcomes.
#
# A build log says the code compiles. This says the game plays: for each seed it
# runs the real engine for a fixed number of seconds and checks the run summary
# for signs of life — enemies spawned, things died, xp was earned, levels
# happened. A run that ends with 0 kills is a broken game that compiles fine,
# which is exactly the failure this exists to catch.
#
#   ./tests/smoke.ps1
#   ./tests/smoke.ps1 -Seconds 30 -Seeds 1,42,1234

param(
    [float]$Seconds = 20,
    [int[]]$Seeds = @(1, 42, 1234),
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$godot = 'D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
if (-not (Test-Path $godot)) { throw "Godot not found: $godot" }

$env:NUGET_PACKAGES = 'D:\tools\megabonk\nuget'
$env:TMP = 'D:\tools\megabonk\tmp'; $env:TEMP = 'D:\tools\megabonk\tmp'

if (-not $SkipBuild) {
    Push-Location $root
    dotnet build -v q --nologo
    $ok = $LASTEXITCODE -eq 0
    Pop-Location
    if (-not $ok) { throw 'C# build failed' }
}

$shotDir = Join-Path $root 'tests\shots'
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null

$failures = @()
foreach ($seed in $Seeds) {
    $shot = Join-Path $shotDir "seed-$seed.png"
    $out = & $godot --path $root --resolution 1280x720 -- `
        "--shot=$shot" "--shot-after=$Seconds" "--seed=$seed" 2>&1 | Out-String

    # Main prints one summary line: SUMMARY t=.. kills=.. level=.. xp=.. alive=..
    $line = ($out -split "`n" | Where-Object { $_ -match 'SUMMARY' } | Select-Object -First 1)
    if (-not $line) {
        $failures += "seed $seed : no SUMMARY line — the run did not reach the end"
        Write-Host "seed $seed : NO SUMMARY" -ForegroundColor Red
        Write-Host ($out -split "`n" | Select-Object -Last 12 | Out-String)
        continue
    }

    $kills = if ($line -match 'kills=(\d+)') { [int]$Matches[1] } else { -1 }
    $level = if ($line -match 'level=(\d+)') { [int]$Matches[1] } else { -1 }
    $alive = if ($line -match 'alive=(\d+)') { [int]$Matches[1] } else { -1 }

    $problems = @()
    if ($alive -le 0) { $problems += 'no enemies ever spawned' }
    if ($kills -le 0) { $problems += 'nothing was killed — weapons are not connecting' }
    if ($level -le 1) { $problems += 'never levelled up — xp is not flowing' }
    if (-not (Test-Path $shot)) { $problems += 'no frame captured' }

    if ($problems.Count) {
        $failures += "seed $seed : $($problems -join '; ')"
        Write-Host "seed $seed : FAIL — $($problems -join '; ')" -ForegroundColor Red
    } else {
        Write-Host "seed $seed : ok — $line".Trim() -ForegroundColor Green
    }
}

Write-Host ''
if ($failures.Count) {
    Write-Host "SMOKE FAILED ($($failures.Count)/$($Seeds.Count))" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "SMOKE PASSED ($($Seeds.Count) seeds, ${Seconds}s each)" -ForegroundColor Green
Write-Host "frames in $shotDir"
