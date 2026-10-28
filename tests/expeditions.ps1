param(
    [string]$Godot = 'D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe',
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
    Run-Headless 'city' @('--route=skyline-run', '--check-city', '--auto', '--invulnerable', '--character=vesper') 'CITY PASS'
    Run-Headless 'dual-hooks' @('--route=skyline-run', '--check-dual-hooks', '--auto', '--invulnerable', '--character=vesper') 'DUALHOOK PASS' 2400
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
