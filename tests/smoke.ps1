# Drive several seeded runs and assert the loop actually produces outcomes.
#
# A build log says the code compiles. This says the game plays: for each seed it
# runs the real engine for a fixed number of seconds and checks the run summary
# for signs of life — enemies spawned, things died, xp was earned, levels
# happened. A run that ends with 0 kills is a broken game that compiles fine,
# which is exactly the failure this exists to catch.
#
# Runs use --auto, so the autopilot plays: without it the player stands still,
# no movement verb is ever pressed, and a broken jump or dash passes silently.
# The bot's verb counters in the summary are asserted for the same reason. Its
# rhythm (first dash at 2.1s, slams on every other hop at 2.6s spacing) puts
# several of every verb inside any window over ~10s.
#
# The default window is 30s rather than 20s because the run clock is not wall
# time: the shot timer fires on frame time, while the game clock advances by
# capped physics steps, so on a loaded machine (several Godot instances at once)
# a "20s" run reaches only t=15-17. On an idle machine the bot reaches level 2
# by ~15s of game time; 30s keeps that inside the window under load instead of
# making the level assertion flaky -- or weaker. The summary's t= is printed on
# failure so a load-starved run is recognisable as one.
#
#   ./tests/smoke.ps1
#   ./tests/smoke.ps1 -Seconds 45 -Seeds 1,42,1234

param(
    [float]$Seconds = 30,
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
        "--shot=$shot" "--shot-after=$Seconds" "--seed=$seed" --auto 2>&1 | Out-String

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

    # Movement verbs, counted by the controller as they actually fire -- so a
    # verb the bot requests but the controller refuses shows up as zero here.
    # \b so 'jumps' cannot match inside 'airjumps'.
    $counter = { param($name) if ($line -match "\b$name=(\d+)") { [int]$Matches[1] } else { -1 } }
    $jumps = & $counter 'jumps'
    $airjumps = & $counter 'airjumps'
    $dashes = & $counter 'dashes'
    $slides = & $counter 'slides'
    $slams = & $counter 'slams'
    $peak = if ($line -match 'peakspeed=([\d.]+)') { [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture) } else { -1 }

    $problems = @()
    if ($alive -le 0) { $problems += 'no enemies ever spawned' }
    if ($kills -le 0) { $problems += 'nothing was killed — weapons are not connecting' }
    if ($level -le 1) { $problems += 'never levelled up — xp is not flowing' }
    if (-not (Test-Path $shot)) { $problems += 'no frame captured' }
    if ($jumps -le 0) { $problems += 'never jumped' }
    if ($airjumps -le 0) { $problems += 'never air-jumped' }
    if ($dashes -le 0) { $problems += 'never dashed' }
    if ($slides -le 0) { $problems += 'never slid' }
    if ($slams -le 0) { $problems += 'never slammed' }
    # Walk speed is 6; anything above 10 means dash or slide momentum actually
    # moved the body, not just that the counter ticked.
    if ($peak -le 10) { $problems += "peak speed $peak never beat walking — momentum is not carrying" }

    if ($problems.Count) {
        $failures += "seed $seed : $($problems -join '; ')"
        Write-Host "seed $seed : FAIL — $($problems -join '; ')" -ForegroundColor Red
        Write-Host "  $($line.Trim())"
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
