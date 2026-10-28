function Get-PayloadPath([string]$Base, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or $Relative.StartsWith('/') -or $Relative.Contains('\') -or $Relative.Contains(':')) { throw 'Invalid payload path.' }
    foreach ($part in $Relative.Split('/')) {
        if ($part -in @('','.','..','.git','.godot','project.godot','profile.cfg','settings.cfg') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $part -match '^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)') { throw 'Unsafe payload component.' }
    }
    $value = Full-Path (Join-Path $Base ($Relative.Replace('/','\')))
    Assert-Below $Base $value
    Assert-NoLinks $Base $value
    return $value
}

function Test-PortablePayload($Request, [string]$Stage, [string]$Root) {
    $payload = Join-Path $Stage 'payload'
    if (-not (Test-Path -LiteralPath $payload -PathType Container)) { throw 'Missing portable payload.' }
    Assert-NoLinks $Stage $payload
    $files = @($Request.files)
    if ($files.Count -lt 5 -or $files.Count -gt 10000) { throw 'Invalid payload file count.' }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$total = 0
    foreach ($file in $files) {
        $source = Get-PayloadPath $payload $file.path
        $null = Get-PayloadPath $Root $file.path
        if (-not $names.Add([string]$file.path) -or -not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Duplicate or missing payload file.' }
        [long]$size = (Get-Item -LiteralPath $source).Length
        if ($size -ne [long]$file.size -or $size -lt 0) { throw 'Payload length mismatch.' }
        $total += $size
        if ($total -gt 2147483648) { throw 'Portable payload exceeds its size limit.' }
        Assert-Hash $source $file.sha256
    }
    foreach ($required in @('Wavebreak.exe','Wavebreak.pck','data_Wavebreak_windows_x86_64/Wavebreak.dll','steam_api64.dll','steam_appid.txt')) {
        if (-not $names.Contains($required)) { throw 'Incomplete Windows portable package.' }
    }
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($Root))
    if ($drive.AvailableFreeSpace -lt $total + 67108864) { throw 'Not enough free space to preserve the previous version.' }
}

function Install-PortablePayload($Request, [string]$Stage, [string]$Root, [bool]$ShowUi) {
    $payload = Join-Path $Stage 'payload'
    $backup = Join-Path $Root ('.wavebreak-update-backup-' + [IO.Path]::GetFileName($Stage))
    Assert-Below $Root $backup
    if (Test-Path -LiteralPath $backup) { throw 'A backup for this update already exists; no overwrite was attempted.' }
    $script:BackupDirectory = $backup
    $script:RollbackCompleted = $false
    New-Item -ItemType Directory -Path $backup | Out-Null
    $applied = [Collections.Generic.List[object]]::new()
    try {
        $files = @($Request.files | Sort-Object path)
        $index = 0
        foreach ($file in $files) {
            Assert-NotCancelled
            $source = Get-PayloadPath $payload $file.path
            $target = Get-PayloadPath $Root $file.path
            $old = Get-PayloadPath $backup $file.path
            New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
            $temporary = $target + '.wavebreak-new-' + [IO.Path]::GetFileName($Stage)
            Assert-Below $Root $temporary
            if (Test-Path -LiteralPath $temporary) { throw 'A pending replacement already exists.' }
            $record = [pscustomobject]@{ target=$target; backup=$old; original=$false; replaced=$false; temporary=$temporary }
            $applied.Add($record)
            Copy-Item -LiteralPath $source -Destination $temporary
            Assert-Hash $temporary $file.sha256
            if (Test-Path -LiteralPath $target) {
                if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw 'A destination is not a regular file.' }
                New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($old)) | Out-Null
                Move-Item -LiteralPath $target -Destination $old
                $record.original = $true
            }
            Move-Item -LiteralPath $temporary -Destination $target
            $record.replaced = $true
            $index++
            Set-UpdateStatus 'applying' ($index.ToString() + '/' + $files.Count) $backup
            if ($ShowUi) { Advance-UpdateProgress ([int](100 * $index / $files.Count)) }
        }
        Assert-NotCancelled
        return $backup
    } catch {
        $rollbackOk = $true
        for ($i = $applied.Count - 1; $i -ge 0; $i--) {
            $record = $applied[$i]
            try {
                Assert-Below $Root $record.target
                Assert-NoLinks $Root $record.target
                if ($record.replaced -and (Test-Path -LiteralPath $record.target -PathType Leaf)) { Remove-Item -LiteralPath $record.target -Force }
                if ($record.original -and (Test-Path -LiteralPath $record.backup -PathType Leaf)) { Move-Item -LiteralPath $record.backup -Destination $record.target }
                if (Test-Path -LiteralPath $record.temporary -PathType Leaf) { Remove-Item -LiteralPath $record.temporary -Force }
            } catch { $rollbackOk = $false }
        }
        $script:RollbackCompleted = $rollbackOk
        if (-not $rollbackOk) { throw ('Update stopped. Recoverable files remain in: ' + $backup) }
        throw
    }
}
