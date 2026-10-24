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
#   both   - after the map-1 boss portal the host took the team into the rest
#            room; the client arrived downed on purpose and was revived there;
#            both bots shopped/rested and walked into the CONTINUE portal, and
#            the host left only when everyone stood in it (rest_exit=all).
#            A frame inside the room: tests/shots/coop-rest-host.png / -client.png
#
# Both windows run on the hidden test desktop (run-offscreen.ps1). Each side
# saves a screenshot near the end with both players in view:
#   tests/shots/coop-host.png, tests/shots/coop-client.png
# Host migration (scripts/Net/NetSession.Migration.cs):
#   coop-hostleft  two players, the host quits at 15 s: the guest becomes the
#                  host of its own session and plays on (migrated=1)
#   coop-fallback  same, with migration switched off (--coop-no-migrate): the
#                  guest's run ends on the results screen, never a crash
#   coop-migrate   three processes; the host quits at 20 s, the two guests
#                  elect a new host (lowest slot), the other reconnects on the
#                  fallback port (Port+1), enemies keep moving, the clock keeps
#                  going, and both reach map 2 through the rest room.
#                  Screenshots: coop-migrate-a.png / -b.png, toasts -toast-*.png
# Heroes (NetSession.Test.Heroes.cs):
#   coop-heroes    the host plays Biscuit, the guest Sunny; each holds its own
#                  signature weapon and lands hits with it (the guest's reach
#                  the host). Screenshots coop-heroes-host.png / -client.png
# Late join (NetSession.Snapshot.cs):
#   coop-latejoin  host + guest start; a third player joins about 20 s into the
#                  run, gets the world snapshot, the team's level, and plays.
#
#   ./tests/coop.ps1
#   ./tests/coop.ps1 -Seconds 90 -Port 17777 -SkipBuild

param(
    [float]$Seconds = 60,
    [int]$Port = 7777,
    [switch]$SkipBuild,
    # Run only these scenarios (coop, coop-hostleft, coop-fallback, coop-heroes, coop-migrate, coop-latejoin); all when empty.
    [string[]]$Only = @()
)

$ErrorActionPreference = 'Stop'
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
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
    if ($Only.Count -and $Only -notcontains $name) { Write-Host "[$name] skipped"; return }
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

# Team-awareness checks (NetSession.Awareness.cs): the client lands the
# killing blow on the boss, and both sides check the toast credits it, the
# scoreboard shows kills for both, a down marker appeared, and pings arrived
# (the host passes on only 3 of a 5-ping burst). Screenshots of the
# scoreboard, the boss toast and a ping on each side.
function Hud([string]$side) {
    "--coop-check-hud `"--coop-board-shot=$(Join-Path $shotDir "coop-board-$side.png")`" " +
    "`"--coop-boss-shot=$(Join-Path $shotDir "coop-boss-$side.png")`" `"--coop-ping-shot=$(Join-Path $shotDir "coop-ping-$side.png")`""
}

# 1. A minute of play: sync, hits, kills, shared xp, down + revive, client leaves.
# The host shortens the first phase so the boss, the portal and the second
# map (all host-driven) happen inside the minute too.
# The host picks Hard in the lobby; the client must see it before READY and
# start on it. At 12 s the client opens a real level-up hand and walks with
# the move key while it is open (the compact hand must not stop it), then
# picks with the 1 key.
Invoke-Scenario 'coop' "--coop-players=2 --coop-seconds=$Seconds --auto --invulnerable --seed=7 --phase-seconds=22 --open-portal-at=36 --coop-expect-phase=2 --coop-expect-restroom `"--coop-rest-shot=$(Join-Path $shotDir 'coop-rest-host.png')`" --coop-difficulty=hard `"--coop-down-shot=$(Join-Path $shotDir 'coop-down-host.png')`" $(Hud 'host')" `
                       "--coop-seconds=$Seconds --auto --invulnerable --phase-seconds=22 --coop-expect-phase=2 --coop-expect-restroom --coop-down-before-rest --coop-expect-difficulty=hard --coop-levelup-at=12 `"--coop-levelup-shot=$(Join-Path $shotDir 'coop-levelup-client.png')`" `"--coop-rest-shot=$(Join-Path $shotDir 'coop-rest-client.png')`" `"--coop-down-shot=$(Join-Path $shotDir 'coop-down-client.png')`" $(Hud 'client')" ([int]($Seconds + 150)) $true $true

# 2. The host quits mid-run: the lone guest takes over as the host and plays on.
Invoke-Scenario 'coop-hostleft' "--coop-players=2 --coop-quit-at=15 --auto --invulnerable --seed=11" `
                                "--coop-expect-migration --coop-seconds=42 --coop-down-at=999 --auto --invulnerable" 170 $false $true

# 2b. Fallback: migration off, the guest's run ends on the results screen.
Invoke-Scenario 'coop-fallback' "--coop-players=2 --coop-quit-at=15 --auto --invulnerable --seed=11" `
                                "--coop-no-migrate --auto --invulnerable" 150 $false $true

# Several processes: each side is @{ Name; Args; Delay (s after the previous); Shot }.
function Invoke-Multi([string]$name, [object[]]$sides, [int]$timeout) {
    if ($Only.Count -and $Only -notcontains $name) { Write-Host "[$name] skipped"; return }
    $jobs = @()
    foreach ($s in $sides) {
        $s.Log = Join-Path $shotDir "$name-$($s.Name).log"
        $s.ShotPath = Join-Path $shotDir "$name-$($s.Name).png"
        Remove-Item $s.Log, $s.ShotPath -ErrorAction SilentlyContinue
        if ($s.Delay -gt 0) { Start-Sleep -Seconds $s.Delay }
        if ($s.WaitFile) {
            # Start when another side says so (--coop-mark-file), at most 3 minutes on.
            for ($i = 0; $i -lt 360 -and -not (Test-Path $s.WaitFile); $i++) { Start-Sleep -Milliseconds 500 }
        }
        $cmd = "`"$godot`" --path `"$root`" --resolution 1280x720 -- $($s.Args)" + $(if ($s.Shot) { " `"--coop-shot=$($s.ShotPath)`"" } else { '' })
        Write-Host "[$name] start $($s.Name)"
        $jobs += Start-Job -ScriptBlock $side -ArgumentList $offscreen, $cmd, $s.Log, $timeout
    }
    Wait-Job -Job $jobs -Timeout ($timeout + 90) | Out-Null
    Remove-Job -Job $jobs -Force -ErrorAction SilentlyContinue
    foreach ($s in $sides) {
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
        if ($s.Shot -and -not (Test-Path $s.ShotPath)) { $script:failures += "[$name] $($s.Name): no screenshot" }
        $color = if ($line -match '^COOP PASS') { 'Green' } else { 'Red' }
        Write-Host "[$name] $($s.Name) : $line" -ForegroundColor $color
        $errs = $out -split "`n" | Where-Object { $_ -match 'ERROR|Unhandled exception|NullReference' } | Select-Object -First 5
        if ($errs) { Write-Host "  engine errors in the $($s.Name) log:"; $errs | ForEach-Object { Write-Host "    $($_.Trim())" } }
    }
}

# 2c. Heroes: the host plays Biscuit, the guest plays Sunny (the two longest
# reaches: co-op bots keep their distance, and the kite is short-ranged, so it
# is checked in the single-player runs), each on a profile
# that has them unlocked. Both must hold their own signature weapon and land
# hits with it; the guest's land on puppets and travel to the host like any
# other weapon's (--coop-expect-signature, NetSession.Test.Heroes.cs).
$heroProfile = Join-Path $shotDir 'coop-heroes-profile'
foreach ($who in 'host', 'client') {
    Set-Content -Path "$heroProfile-$who.cfg" -Value "[unlocks]`n`nqh_sunny=true`nqh_biscuit=true`nqh_flick=true`n" -Encoding ascii
}
Invoke-Scenario 'coop-heroes' "--coop-players=2 --coop-seconds=45 --auto --invulnerable --seed=13 --character=biscuit `"--profile=$heroProfile-host.cfg`" --coop-expect-signature" `
                              "--coop-seconds=45 --coop-down-at=999 --auto --invulnerable --character=sunny `"--profile=$heroProfile-client.cfg`" --coop-expect-signature" 190 $true $true

# 3. Host migration with three players: the host quits at 20 s; the guests go on.
$mig = "--auto --invulnerable --phase-seconds=22 --open-portal-at=34"
Invoke-Multi 'coop-migrate' @(
    @{ Name = 'host'; Delay = 0; Shot = $false; Args = "--coop-host=$Port --coop-name=Host --coop-players=3 --coop-quit-at=20 --seed=5 $mig" },
    @{ Name = 'a'; Delay = 4; Shot = $true; Args = "--coop-join=127.0.0.1:$Port --coop-name=GuestA --coop-expect-migration --coop-expect-phase=2 --coop-seconds=80 --coop-down-at=999 `"--coop-toast-shot=$(Join-Path $shotDir 'coop-migrate-toast-a.png')`" $mig" },
    @{ Name = 'b'; Delay = 2; Shot = $true; Args = "--coop-join=127.0.0.1:$Port --coop-name=GuestB --coop-expect-migration --coop-expect-phase=2 --coop-seconds=80 --coop-down-at=999 `"--coop-toast-shot=$(Join-Path $shotDir 'coop-migrate-toast-b.png')`" $mig" }
) 260

# 4. Late join: a third player joins about 20 s into the run, after the
# host's team has cleared map 1 (the portal opens at 12 s), so the joiner
# must catch up with map 2 (or the rest room), the enemies and the level.
$mark = Join-Path $shotDir 'coop-latejoin.mark'
Remove-Item $mark -ErrorAction SilentlyContinue
$lj = "--auto --invulnerable --phase-seconds=10 --open-portal-at=12 --coop-seconds=66 --coop-down-at=999"
Invoke-Multi 'coop-latejoin' @(
    @{ Name = 'host'; Delay = 0; Shot = $true; Args = "--coop-host=$Port --coop-name=Host --coop-players=2 --coop-expect-late-join --seed=9 `"--coop-mark-file=$mark`" --coop-mark-at=18 $lj" },
    @{ Name = 'a'; Delay = 4; Shot = $false; Args = "--coop-join=127.0.0.1:$Port --coop-name=GuestA $lj" },
    @{ Name = 'late'; Delay = 0; WaitFile = $mark; Shot = $true; Args = "--coop-join=127.0.0.1:$Port --coop-name=Late --coop-late-join $lj" }
) 240

Write-Host ''
if ($failures.Count) {
    Write-Host "COOP TEST FAILED" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "COOP TEST PASSED" -ForegroundColor Green
Write-Host "screenshots: $(Join-Path $shotDir 'coop-host.png') , $(Join-Path $shotDir 'coop-client.png') , $(Join-Path $shotDir 'coop-rest-host.png') , $(Join-Path $shotDir 'coop-rest-client.png') , $(Join-Path $shotDir 'coop-hostleft-client.png') , $(Join-Path $shotDir 'coop-migrate-a.png') , $(Join-Path $shotDir 'coop-migrate-b.png') , $(Join-Path $shotDir 'coop-latejoin-late.png')"
