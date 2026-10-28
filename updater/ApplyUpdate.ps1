param([Parameter(Mandatory)][string]$RequestPath, [switch]$NoUi, [ValidateRange(1,120)][int]$WaitSeconds = 120)
$ErrorActionPreference = 'Stop'
$stage = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$stageId = [IO.Path]::GetFileName($stage)
$status = Join-Path $stage 'status.json'
$cancelPath = Join-Path $stage 'cancel.request'
$lock = $null
$rootLock = $null
$ui = $false
$stageValidated = $false

function Set-UpdateStatus([string]$Phase, [string]$Detail = '', [string]$Backup = '') {
    $value = @{ phase=$Phase; detail=$Detail; backup=$Backup; updatedUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText($status + '.tmp', $value)
    Move-Item -LiteralPath ($status + '.tmp') -Destination $status -Force
}
function Full-Path([string]$Path) { return [IO.Path]::GetFullPath($Path).TrimEnd('\') }
function Assert-Below([string]$Root, [string]$Path) {
    if (-not (Full-Path $Path).StartsWith((Full-Path $Root) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path leaves its allowed directory.' }
}
function Assert-NoLinks([string]$Root, [string]$Path) {
    $current = Full-Path $Path
    while ($current.Length -ge $Root.Length) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked update paths are unsupported.' }
        }
        if ($current.Equals($Root, [StringComparison]::OrdinalIgnoreCase)) { break }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}
function Assert-Root([string]$Root) {
    if ($Root.Length -le ([IO.Path]::GetPathRoot($Root)).TrimEnd('\').Length -or $Root.StartsWith('\\')) { throw 'Unsafe installation root.' }
    Assert-NoLinks $Root $Root
    $ancestor = $Root
    while ($ancestor) {
        if ((Test-Path -LiteralPath (Join-Path $ancestor '.git')) -or (Test-Path -LiteralPath (Join-Path $ancestor 'project.godot'))) { throw 'Development folders cannot be updated.' }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    foreach ($relative in @('Wavebreak.exe','Wavebreak.pck','data_Wavebreak_windows_x86_64\Wavebreak.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $relative) -PathType Leaf)) { throw 'Unrecognized game installation.' }
        Assert-NoLinks $Root (Join-Path $Root $relative)
    }
}
function Assert-Hash([string]$Path, [string]$Expected) {
    if ($Expected -notmatch '^[a-fA-F0-9]{64}$') { throw 'Update integrity verification failed.' }
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '')
        if (-not $actual.Equals($Expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update integrity verification failed.' }
    } finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}
function Test-Cancelled { return (Test-Path -LiteralPath $cancelPath) -or ($ui -and (Test-UpdateCancelled)) }
function Assert-NotCancelled { if (Test-Cancelled) { throw [OperationCanceledException]::new('Update cancelled.') } }

try {
    $expectedBase = Full-Path (Join-Path ([IO.Path]::GetTempPath()) 'WavebreakUpdates')
    $parsedGuid = [Guid]::Empty
    if (-not [Guid]::TryParseExact($stageId, 'N', [ref]$parsedGuid) -or (Full-Path ([IO.Path]::GetDirectoryName($stage))) -ne $expectedBase) { throw 'Unexpected update staging directory.' }
    if ((Full-Path $RequestPath) -ne (Full-Path (Join-Path $stage 'request.json'))) { throw 'Unexpected request file.' }
    if ([IO.File]::ReadAllText((Join-Path $stage '.wavebreak-stage')) -ne ('Wavebreak:' + $stageId)) { throw 'Missing staging ownership marker.' }
    Assert-NoLinks $stage $stage
    $stageValidated = $true
    $lock = [IO.File]::Open((Join-Path $stage 'helper.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    if ((Get-Item -LiteralPath $RequestPath).Length -gt 4194304) { throw 'Update request too large.' }
    $request = [IO.File]::ReadAllText($RequestPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($request.schema -ne 1 -or $request.product -ne 'Wavebreak' -or $request.mode -notin @('installed','portable')) { throw 'Unsupported update request.' }
    if ($request.version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\+[A-Za-z0-9.-]+)?$') { throw 'Invalid stable update version.' }
    $root = Full-Path $request.installRoot
    Assert-Root $root
    $game = Full-Path (Join-Path $root 'Wavebreak.exe')
    if ((Full-Path $request.gameExe) -ne $game -or [int]$request.gamePid -le 0) { throw 'Unexpected game process identity.' }
    $expectedName = if ($request.mode -eq 'installed') { 'Wavebreak-Setup-' + $request.version + '.exe' } else { 'Wavebreak-' + $request.version + '-windows.zip' }
    if ($request.assetName -cne $expectedName) { throw 'Unexpected update asset name.' }
    $asset = Join-Path $stage $expectedName
    Assert-Below $stage $asset
    Assert-Hash $asset $request.assetSha256
    . (Join-Path $stage 'UpdatePortable.ps1')
    if ($request.mode -eq 'portable') { Test-PortablePayload $request $stage $root }
    else {
        $signature = Get-AuthenticodeSignature -LiteralPath $asset
        if ($signature.Status -notin @('Valid','NotSigned')) { throw 'Installer signature is invalid.' }
    }
    if (-not $NoUi -and $request.mode -eq 'portable') {
        . (Join-Path $stage 'UpdateProgress.ps1')
        Initialize-UpdateProgress ($request.language -eq 'ar')
        $ui = $true
    }
    Assert-NoLinks $root (Join-Path $root '.wavebreak-update.lock')
    $rootLock = [IO.File]::Open((Join-Path $root '.wavebreak-update.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    Set-UpdateStatus 'waiting'
    [IO.File]::WriteAllText((Join-Path $stage 'ready.json'), (@{ stage=$stageId } | ConvertTo-Json -Compress))
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        Assert-NotCancelled
        $running = Get-Process -Id ([int]$request.gamePid) -ErrorAction SilentlyContinue
        if ($null -eq $running) { break }
        try {
            $running.Refresh()
            if ($running.HasExited) { break }
            $runningPath = $running.Path
            if ([string]::IsNullOrWhiteSpace($runningPath)) {
                $running.Refresh()
                if ($running.HasExited) { break }
                throw 'The game process identity could not be verified. No files were replaced.'
            }
            if ($running.StartTime.ToUniversalTime().Ticks.ToString() -ne [string]$request.gameStartedUtcTicks -or (Full-Path $runningPath) -ne $game) { break }
        } catch {
            if ($null -eq (Get-Process -Id ([int]$request.gamePid) -ErrorAction SilentlyContinue)) { break }
            throw
        } finally { $running.Dispose() }
        if ($timer.Elapsed.TotalSeconds -gt $WaitSeconds) { throw 'The game did not close. No files were replaced.' }
        Start-Sleep -Milliseconds 100
    }
    Assert-NotCancelled
    if ($request.mode -eq 'installed') {
        Assert-Hash $asset $request.assetSha256
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $asset; $start.UseShellExecute = $true; $start.WorkingDirectory = $stage
        $start.Arguments = '/DIR="' + $root + '" /LOG="' + (Join-Path $stage 'installer.log') + '"'
        $installer = [Diagnostics.Process]::Start($start)
        if ($null -eq $installer) { throw 'The installer did not start.' }
        Set-UpdateStatus 'installer_started'
        try {
            if (-not $installer.WaitForExit(3600000)) { throw 'The installer is still open. Finish it normally; no process was stopped.' }
            if ($installer.ExitCode -ne 0) { throw ('The installer did not complete. Exit code: ' + $installer.ExitCode) }
        } finally { $installer.Dispose() }
        Set-UpdateStatus 'installer_finished'
    } else {
        if ($ui) { Show-UpdateProgress }
        $backup = Install-PortablePayload $request $stage $root $ui
        Set-UpdateStatus 'applied' '' $backup
        if ($request.restart) {
            try {
                $restartInfo = [Diagnostics.ProcessStartInfo]::new($game)
                $restartInfo.WorkingDirectory = $root; $restartInfo.UseShellExecute = $true
                $restarted = [Diagnostics.Process]::Start($restartInfo)
                if ($null -eq $restarted) { throw 'Restart did not start.' }
                try {
                    if ($restarted.WaitForExit(1500) -and $restarted.ExitCode -ne 0) { throw 'The updated game exited during startup.' }
                } finally { $restarted.Dispose() }
            } catch {
                Set-UpdateStatus 'applied_restart_failed' 'Open Wavebreak.exe to start the updated game.' $backup
                if ($ui) { Complete-UpdateProgress $false 'The update completed, but restart failed.' $backup }
                exit 2
            }
        }
        if ($ui) { Complete-UpdateProgress $true '' $backup }
    }
    exit 0
} catch {
    $cancelled = $_.Exception -is [OperationCanceledException]
    $phase = if ($cancelled) { 'cancelled' } else { 'failed' }
    if ($script:RollbackCompleted) { $phase = 'rolled_back' }
    if ($stageValidated) { try { Set-UpdateStatus $phase $_.Exception.Message $script:BackupDirectory } catch { } }
    if ($ui) { Complete-UpdateProgress $false $_.Exception.Message $script:BackupDirectory }
    exit 1
} finally {
    if ($rootLock) { $rootLock.Dispose() }
    if ($lock) { $lock.Dispose() }
}

