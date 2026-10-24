# Two-process co-op check: one game hosts, one joins over localhost, both on
# autopilot for about a minute, then each prints a COOP PASS/FAIL line.
#
# What it proves (see scripts/Net/NetSession.Test.cs for the details):
#   host   - the client joined, its hits reached the host's enemies, its
#            transform streamed in, and its departure removed its player
#            without ending the run
#   client - the host's enemies, drops and chests arrived, its hits were
#            confirmed as kills, experience was shared, it went down on
#            purpose and the host's bot revived it
#
# Both windows run on the hidden test desktop (run-offscreen.ps1). Each side
# saves a screenshot near the end with both players in view:
#   tests/shots/coop-host.png, tests/shots/coop-client.png
# A second, shorter scenario has the host quit mid-run and checks the client
# lands on the title's CO-OP page with the message (coop-hostleft-client.png).
#
#   ./tests/coop.ps1
#   ./tests/coop.ps1 -Seconds 90 -Port 17777 -SkipBuild

param(
    [float]$Seconds = 60,
    [int]$Port = 7777,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$godot = 'D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
if (-not (Test-Path $godot)) { throw "Godot not found: $godot" }
$offscreen = 'D:\tools\megabonk\run-offscreen.ps1'
if (-not (Test-Path $offscreen)) { $offscreen = Join-Path $PSScriptRoot 'run-offscreen.ps1' }

$env:NUGET_PACKAGES = 'D:\tools\megabonk\nuget'
$env:TMP = 'D:\tools\megabonk\tmp'; $env:TEMP = 'D:\tools\megabonk\tmp'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if (-not $SkipBuild) {
    Push-Location $root
    dotnet build -v q --nologo
    $ok = $LASTEXITCODE -eq 0
    Pop-Location
    if (-not $ok) { throw 'C# build failed' }
}

$shotDir = Join-Path $root 'tests\shots'
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null

# Each side runs in a background job; the call operator passes the quoted
# command line through exactly as smoke.ps1 does.
$side = {
    param($offscreen, $cmd, $log, $timeout)
    & powershell -NoProfile -File $offscreen -Command $cmd -Out $log -TimeoutSec $timeout | Out-Null
}

$failures = @()

# One scenario: start the host, then the client, wait for both, check each
# side's COOP line (and its screenshot when one is expected).
function Invoke-Scenario([string]$name, [string]$hostArgs, [string]$clientArgs, [int]$timeout, [bool]$hostShot, [bool]$clientShot) {
    $hostLog = Join-Path $shotDir "$name-host.log"
    $clientLog = Join-Path $shotDir "$name-client.log"
    $hShot = Join-Path $shotDir "$name-host.png"
    $cShot = Join-Path $shotDir "$name-client.png"
    Remove-Item $hostLog, $clientLog, $hShot, $cShot -ErrorAction SilentlyContinue
    $hostCmd = "`"$godot`" --path `"$root`" --resolution 1280x720 -- --coop-host=$Port --coop-name=Host $hostArgs" +
               $(if ($hostShot) { " `"--coop-shot=$hShot`"" } else { '' })
    $clientCmd = "`"$godot`" --path `"$root`" --resolution 1280x720 -- --coop-join=127.0.0.1:$Port --coop-name=Guest $clientArgs" +
                 $(if ($clientShot) { " `"--coop-shot=$cShot`"" } else { '' })

    Write-Host "[$name] host on port $Port, client joins 127.0.0.1:$Port"
    $h = Start-Job -ScriptBlock $side -ArgumentList $offscreen, $hostCmd, $hostLog, $timeout
    Start-Sleep -Seconds 4
    $c = Start-Job -ScriptBlock $side -ArgumentList $offscreen, $clientCmd, $clientLog, $timeout
    Wait-Job -Job $h, $c -Timeout ($timeout + 60) | Out-Null
    Remove-Job -Job $h, $c -Force -ErrorAction SilentlyContinue

    foreach ($s in @(@{ Name = 'host'; Log = $hostLog; Shot = $hShot; Want = $hostShot },
                     @{ Name = 'client'; Log = $clientLog; Shot = $cShot; Want = $clientShot })) {
        $out = Get-Content $s.Log -Raw -ErrorAction SilentlyContinue
        $line = ($out -split "`n" | Where-Object { $_ -match '^COOP (PASS|FAIL)' } | Select-Object -First 1)
        if (-not $line) {
            $script:failures += "[$name] $($s.Name): no COOP line"
            Write-Host "[$name] $($s.Name) : NO COOP LINE" -ForegroundColor Red
            Write-Host ($out -split "`n" | Select-Object -Last 15 | Out-String)
            continue
        }
        $line = $line.Trim()
        if ($line -match '^COOP FAIL') { $script:failures += "[$name] $($s.Name): $line" }
        if ($s.Want -and -not (Test-Path $s.Shot)) { $script:failures += "[$name] $($s.Name): no screenshot" }
        $color = if ($line -match '^COOP PASS') { 'Green' } else { 'Red' }
        Write-Host "[$name] $($s.Name) : $line" -ForegroundColor $color
        $errs = $out -split "`n" | Where-Object { $_ -match 'ERROR|Unhandled exception|NullReference' } | Select-Object -First 5
        if ($errs) { Write-Host "  engine errors in the $($s.Name) log:"; $errs | ForEach-Object { Write-Host "    $($_.Trim())" } }
    }
}

# 1. A minute of play: sync, hits, kills, shared xp, down + revive, client leaves.
# The host shortens the first phase so the boss, the portal and the second
# map (all host-driven) happen inside the minute too.
# The host picks Hard in the lobby; the client must see it before READY and
# start on it. At 12 s the client opens a real level-up hand and walks with
# the move key while it is open (the compact hand must not stop it), then
# picks with the 1 key.
Invoke-Scenario 'coop' "--coop-players=2 --coop-seconds=$Seconds --auto --invulnerable --seed=7 --phase-seconds=22 --open-portal-at=36 --coop-expect-phase=2 --coop-difficulty=hard `"--coop-down-shot=$(Join-Path $shotDir 'coop-down-host.png')`"" `
                       "--coop-seconds=$Seconds --auto --invulnerable --phase-seconds=22 --coop-expect-phase=2 --coop-expect-difficulty=hard --coop-levelup-at=12 `"--coop-levelup-shot=$(Join-Path $shotDir 'coop-levelup-client.png')`" `"--coop-down-shot=$(Join-Path $shotDir 'coop-down-client.png')`"" ([int]($Seconds + 150)) $true $true

# 2. The host quits mid-run: the client must land on the CO-OP page with a message.
Invoke-Scenario 'coop-hostleft' "--coop-players=2 --coop-quit-at=15 --auto --invulnerable --seed=11" `
                                "--coop-expect-host-leave --auto --invulnerable" 150 $false $true

Write-Host ''
if ($failures.Count) {
    Write-Host "COOP TEST FAILED" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "COOP TEST PASSED" -ForegroundColor Green
Write-Host "screenshots: $(Join-Path $shotDir 'coop-host.png') , $(Join-Path $shotDir 'coop-client.png') , $(Join-Path $shotDir 'coop-hostleft-client.png')"
