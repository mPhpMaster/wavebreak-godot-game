param(
    [string]$Godot = 'D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe',
    [string]$From = '',
    [switch]$Render,
    [switch]$SkipBuild,
    [switch]$CoopOnly,
    [switch]$RenderOnly
)

$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not (Test-Path -LiteralPath $Godot -PathType Leaf)) { throw "Godot executable missing: $Godot" }
$evidence = Join-Path $env:TEMP ('wavebreak-expeditions-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $evidence | Out-Null

function Assert-Evidence([string]$Path, [string]$Marker) {
    $log = Get-Content -LiteralPath $Path -Raw
    if (-not $log.Contains($Marker) -or $log -match '(?m)^(ERROR:|SCRIPT ERROR:|CITY FAIL|DUALHOOK FAIL|REEF FAIL|EXPEDITION MENU FAIL|CONTENT FAIL|COOP CITY FAIL)') {
        throw "Verification failed: $Path`n$($log.Substring([Math]::Max(0, $log.Length - 3500)))"
    }
    Write-Output "PASS $Marker ($Path)"
}

function Run-Headless([string]$Name, [string[]]$GameArgs, [string]$Marker, [int]$Frames = 1200) {
    if ($From -and -not $script:resuming) { if ($Name -eq $From) { $script:resuming = $true } else { return } }
    $log = Join-Path $evidence ($Name + '.log')
    & $Godot --headless --path $taskRoot --audio-driver Dummy --quit-after $Frames -- @GameArgs *> $log
    Assert-Evidence $log $Marker
}

Push-Location $taskRoot
try {
    if (-not $SkipBuild) {
        dotnet build --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    }
    if (-not $CoopOnly -and -not $RenderOnly) {
    Run-Headless 'overlay-regression-en' @('--check-overlay-regression', '--auto', '--invulnerable') 'OVERLAY REGRESSION PASS'
    Run-Headless 'overlay-regression-ar' @('--check-overlay-regression', '--auto', '--invulnerable', '--lang=ar') 'OVERLAY REGRESSION PASS'
    Run-Headless 'scoreboard-polish' @('--check-scoreboard', '--auto', '--invulnerable') 'SCOREBOARD PASS'
    Run-Headless 'all-map-resources' @('--check-resource-polish', '--auto', '--invulnerable') 'RESOURCES PASS' 2400
    Run-Headless 'platform-pursuit' @('--check-platform-pursuit', '--auto', '--invulnerable') 'PLATFORM PURSUIT PASS'
    Run-Headless 'hook-controls' @('--route=skyline-run', '--check-hook-controls', '--auto', '--invulnerable', '--character=vesper') 'HOOK CONTROLS PASS'
    Run-Headless 'hook-targets' @('--route=skyline-run', '--check-hook-targets', '--auto', '--invulnerable', '--character=vesper') 'HOOK TARGETS PASS'
    Run-Headless 'swim-dash-steering' @('--route=reef-dive', '--check-swim-dash', '--invulnerable', '--character=nori') 'SWIM DASH PASS'
    Run-Headless 'dive-control' @('--route=reef-dive', '--check-dive-control', '--invulnerable', '--character=nori') 'DIVE CONTROL PASS' 2500
    Run-Headless 'shotgun-handling' @('--route=reef-dive', '--check-shotgun-handling', '--invulnerable', '--character=nori') 'SHOTGUN HANDLING PASS' 4500
    Run-Headless 'expedition-weapons' @('--route=reef-dive', '--check-expedition-weapons', '--invulnerable', '--character=nori') 'EXPEDITION WEAPONS PASS'
    Run-Headless 'cave-guardian' @('--route=reef-dive', '--check-guardian', '--invulnerable', '--character=nori') 'GUARDIAN PASS'
    Run-Headless 'leviathan' @('--route=reef-dive', '--check-leviathan', '--invulnerable', '--character=nori') 'LEVIATHAN PASS'
    Run-Headless 'city' @('--route=skyline-run', '--check-city', '--auto', '--invulnerable', '--character=vesper') 'CITY PASS'
    Run-Headless 'rooftop-acquisition' @('--route=skyline-run', '--check-rooftops', '--auto', '--invulnerable', '--character=vesper') 'ROOFTOP PASS'
    Run-Headless 'dual-hooks' @('--route=skyline-run', '--check-dual-hooks', '--auto', '--invulnerable', '--character=vesper') 'DUALHOOK PASS' 2400
    Run-Headless 'classic-after-reef' @('--menu-expedition-check=classic-reef') 'CLASSIC START PASS'
    Run-Headless 'classic-after-city-card' @('--menu-expedition-check=classic-city-card') 'CLASSIC START PASS'
    Run-Headless 'release-notes' @('--menu-shot=unused.png', '--check-release-notes') 'RELEASE NOTES PASS'
    Run-Headless 'menu-city-fresh' @('--menu-expedition-check=city') 'EXPEDITION MENU PASS'
    Run-Headless 'menu-city-override' @('--menu-expedition-check=city-override') 'EXPEDITION MENU PASS'
    Run-Headless 'menu-city-direct' @('--menu-expedition-check=city-direct') 'EXPEDITION MENU PASS'
    Run-Headless 'menu-reef-direct' @('--menu-expedition-check=reef-direct', '--lang=ar') 'EXPEDITION MENU PASS'
    $oldProfile = Join-Path $evidence 'existing-profile.cfg'
    @'
[profile]
character="gale_courier"
[stats]
runs=12
[meta]
route=10
difficulty=1
won_normal=1
'@ | Set-Content -LiteralPath $oldProfile -Encoding utf8
    Run-Headless 'menu-reef-existing' @('--menu-expedition-check=reef', "--profile=$oldProfile", '--lang=ar') 'EXPEDITION MENU PASS'
    Run-Headless 'content-audit' @('--validate-content', '--auto') 'CONTENT AUDIT PASS' 180
    Run-Headless 'nori-swimming' @('--route=reef-dive', '--check-swim', '--auto', '--invulnerable', '--character=nori') 'SWIM PASS'
    Run-Headless 'nori-swim-pose' @('--route=reef-dive', '--check-swim-pose', '--invulnerable', '--character=nori') 'SWIMPOSE PASS'
    Run-Headless 'reef-cave-regression' @('--route=reef-dive', '--check-cave-ui', '--auto', '--invulnerable') 'CAVEUI PASS'
    Run-Headless 'reef-systems' @('--route=reef-dive', '--check-reef-systems', '--auto', '--invulnerable', '--character=nori') 'REEF SYSTEMS PASS' 2400
    Run-Headless 'reef-expedition' @('--route=reef-dive', '--check-reef-expedition', '--check-terrain-cache', '--auto', '--invulnerable', '--character=nori') 'EXPEDITION PASS' 2400
    }

    if (-not $RenderOnly) {
    $port = Get-Random -Minimum 39000 -Maximum 49000
    $hostLog = Join-Path $evidence 'coop-city-host.log'
    $clientLog = Join-Path $evidence 'coop-city-client.log'
    $hostArgs = '--headless --path "' + $taskRoot + '" --audio-driver Dummy --quit-after 3000 --log-file "' + $hostLog +
        '" -- --coop-host=' + $port + ' --coop-players=2 --seed=42 --route=skyline-run --coop-check-city --auto --invulnerable --character=vesper'
    $hostProcess = Start-Process -FilePath $Godot -ArgumentList $hostArgs -PassThru -WindowStyle Hidden
    try {
        Start-Sleep -Milliseconds 800
        & $Godot --headless --path $taskRoot --audio-driver Dummy --quit-after 3000 --log-file $clientLog -- `
            "--coop-join=127.0.0.1:$port" --route=skyline-run --coop-check-city --auto --invulnerable --character=nori *> (Join-Path $evidence 'coop-client-stdout.log')
        if (-not $hostProcess.WaitForExit(30000)) { throw 'Co-op host timed out' }
        Assert-Evidence $hostLog 'COOP CITY PASS'
        Assert-Evidence $clientLog 'COOP CITY PASS'
    }
    finally {
        if (-not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id }
        $hostProcess.Dispose()
    }
    $bossPort = Get-Random -Minimum 39000 -Maximum 49000
    $bossHostLog = Join-Path $evidence 'coop-leviathan-host.log'
    $bossClientLog = Join-Path $evidence 'coop-leviathan-client.log'
    $bossHostArgs = '--headless --path "' + $taskRoot + '" --audio-driver Dummy --quit-after 3000 --log-file "' + $bossHostLog +
        '" -- --coop-host=' + $bossPort + ' --coop-players=2 --seed=42 --route=reef-dive --check-coop-leviathan --invulnerable --character=nori'
    $bossHost = Start-Process -FilePath $Godot -ArgumentList $bossHostArgs -PassThru -WindowStyle Hidden
    try {
        Start-Sleep -Milliseconds 800
        & $Godot --headless --path $taskRoot --audio-driver Dummy --quit-after 3000 --log-file $bossClientLog -- `
            "--coop-join=127.0.0.1:$bossPort" --route=reef-dive --check-coop-leviathan --invulnerable --character=nori *> (Join-Path $evidence 'coop-leviathan-stdout.log')
        if (-not $bossHost.WaitForExit(30000)) { throw 'Co-op Leviathan host timed out' }
        Assert-Evidence $bossHostLog 'COOP LEVIATHAN PASS'
        Assert-Evidence $bossClientLog 'COOP LEVIATHAN PASS'
    }
    finally {
        if (-not $bossHost.HasExited) { Stop-Process -Id $bossHost.Id }
        $bossHost.Dispose()
    }
    }

    if ($Render -or $RenderOnly) {
        foreach ($language in @('en', 'ar')) {
            $shot = Join-Path $evidence ('menu-' + $language + '.png')
            $log = Join-Path $evidence ('menu-render-' + $language + '.log')
            $command = '"' + $Godot + '" --path "' + $taskRoot + '" --windowed --resolution 1280x720 --audio-driver Dummy -- --menu-shot="' +
                $shot + '" --menu-page=expeditions --lang=' + $language
            & (Join-Path $PSScriptRoot 'run-offscreen.ps1') -Command $command -WorkDir $taskRoot -DesktopName ('WBExp' + $language) -TimeoutSec 100 -Out $log
            Assert-Evidence $log 'MENU page=expeditions'
            $prefix = Join-Path $evidence ('selected-' + $language)
            $selectedLog = Join-Path $evidence ('selected-' + $language + '.log')
            $command = '"' + $Godot + '" --path "' + $taskRoot + '" --windowed --resolution 1280x720 --audio-driver Dummy -- --menu-expedition-check=city --menu-review="' + $prefix + '" --lang=' + $language
            & (Join-Path $PSScriptRoot 'run-offscreen.ps1') -Command $command -WorkDir $taskRoot -DesktopName ('WBSelected' + $language) -TimeoutSec 160 -Out $selectedLog
            Assert-Evidence $selectedLog 'EXPEDITION MENU PASS'
        }
        $prefix = Join-Path $evidence 'city'
        $command = '"' + $Godot + '" --path "' + $taskRoot + '" --windowed --resolution 1280x720 --audio-driver Dummy -- --route=skyline-run --check-city --auto --invulnerable --character=vesper --city-capture="' + $prefix + '"'
        $log = Join-Path $evidence 'city-render.log'
        & (Join-Path $PSScriptRoot 'run-offscreen.ps1') -Command $command -WorkDir $taskRoot -DesktopName WBExpCity -TimeoutSec 120 -Out $log
        Assert-Evidence $log 'CITY PASS'
    }
    Write-Output "EXPEDITIONS PASS evidence=$evidence"
}
finally { Pop-Location }


