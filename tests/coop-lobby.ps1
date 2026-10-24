# Lobby capture: a host and a guest meet in the CO-OP lobby over ENet (no run)
# and each photographs the page: both players standing side by side on the
# beach with their names and READY state, the host's difficulty, and the
# character panel. Checks each side's MENU line reports two players on the
# beach and the host's tier.
#
#   ./tests/coop-lobby.ps1                       (English, 1280x720)
#   ./tests/coop-lobby.ps1 -Lang ar -Res 1920x1080
param(
    [string]$Lang = 'en',
    [string]$Res = '1280x720',
    [int]$Port = 17781,
    [string]$Difficulty = 'hard'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$godot = 'D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
$offscreen = 'D:\tools\megabonk\run-offscreen.ps1'
if (-not (Test-Path $offscreen)) { $offscreen = Join-Path $PSScriptRoot 'run-offscreen.ps1' }
$shotDir = Join-Path $root 'tests\shots'
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null
$tag = "$Lang-$($Res -replace 'x','-')"
$side = {
    param($offscreen, $cmd, $log, $timeout)
    & powershell -NoProfile -File $offscreen -Command $cmd -Out $log -TimeoutSec $timeout | Out-Null
}
$hShot = Join-Path $shotDir "coop-lobby-host-$tag.png"
$cShot = Join-Path $shotDir "coop-lobby-client-$tag.png"
$hLog = Join-Path $shotDir "coop-lobby-host-$tag.log"
$cLog = Join-Path $shotDir "coop-lobby-client-$tag.log"
Remove-Item $hShot, $cShot, $hLog, $cLog -ErrorAction SilentlyContinue
$common = "--coop-lobby-only --menu-page=coop --lang=$Lang"
$hostCmd = "`"$godot`" --path `"$root`" --resolution $Res -- --coop-host=$Port --coop-players=2 --coop-name=Mariam --coop-hat=sailor_cap --coop-difficulty=$Difficulty $common `"--menu-shot=$hShot`""
$clientCmd = "`"$godot`" --path `"$root`" --resolution $Res -- --coop-join=127.0.0.1:$Port --coop-name=Omar --character=kiln_sister --coop-hat=tricorne $common `"--menu-shot=$cShot`""
$h = Start-Job -ScriptBlock $side -ArgumentList $offscreen, $hostCmd, $hLog, 90
Start-Sleep -Seconds 2
$c = Start-Job -ScriptBlock $side -ArgumentList $offscreen, $clientCmd, $cLog, 90
Wait-Job -Job $h, $c -Timeout 150 | Out-Null
Remove-Job -Job $h, $c -Force -ErrorAction SilentlyContinue
$fail = @()
foreach ($s in @(@{ N = 'host'; L = $hLog; P = $hShot }, @{ N = 'client'; L = $cLog; P = $cShot })) {
    $line = (Get-Content $s.L -ErrorAction SilentlyContinue | Where-Object { $_ -match '^MENU page=coop' } | Select-Object -First 1)
    if (-not $line) { $fail += "$($s.N): no MENU line"; continue }
    if ($line -notmatch 'lineup=2') { $fail += "$($s.N): not two players on the beach: $line" }
    if ($line -notmatch "difficulty=$Difficulty") { $fail += "$($s.N): lobby difficulty is not ${Difficulty}: $line" }
    if (-not (Test-Path $s.P)) { $fail += "$($s.N): no screenshot" }
}
if ($fail.Count) { Write-Host "LOBBY FAIL"; $fail | ForEach-Object { Write-Host "  $_" }; exit 1 }
Write-Host "LOBBY PASS $hShot $cShot"
