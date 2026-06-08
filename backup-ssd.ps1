# ============================================================================
#  backup-ssd.ps1  --  Air-gapped local SSD backup.
#  Monthly cadence. Yearly full + monthly incremental, plain files.
#  Prompts (toast) to connect the SSD, verifies by label+serial, backs up,
#  then dismounts the volume to restore the air-gap.
# ============================================================================

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\lib-backup.ps1"
Initialize-BackupRoot

$config  = Get-BackupConfig
$logFile = Join-Path $config.logDir ("ssd_{0}.log" -f (Get-Date -Format "yyyy-MM"))
Write-BackupLog "===== SSD backup run started =====" $logFile

# --- Is a backup due this month? -------------------------------------------
$state = Get-BackupState
$thisMonth = Get-Date -Format "yyyy-MM"
$alreadyThisMonth = @($state.ssdBackups | Where-Object {
    (Get-Date ([DateTime]$_.time) -Format "yyyy-MM") -eq $thisMonth
}).Count -gt 0

if ($alreadyThisMonth) {
    Write-BackupLog "SSD backup already done for $thisMonth. Nothing to do." $logFile
    exit 0
}

# --- Locate the SSD; prompt if absent --------------------------------------
$driveLetter = Find-BackupSsd -Config $config
if (-not $driveLetter) {
    Write-BackupLog "SSD not connected. Prompting user via toast." $logFile
    Show-Toast "Backup time" "Please connect your backup SSD ($($config.ssd.volumeLabel)) to run this month's backup."
    $maxTries = [int]$config.ssd.connectWaitMinutes * 6   # 10s polls
    for ($i = 0; $i -lt $maxTries; $i++) {
        Start-Sleep -Seconds 10
        $driveLetter = Find-BackupSsd -Config $config
        if ($driveLetter) { break }
    }
}
if (-not $driveLetter) {
    Write-BackupLog "SSD not connected within wait window. Will retry next run." $logFile
    Show-Toast "Backup skipped" "Backup SSD was not connected in time. It will be retried."
    $state.lastSsdResult = "skipped"; Save-BackupState $state
    exit 0
}
Write-BackupLog "SSD detected at drive $driveLetter`:" $logFile

# --- Paths -----------------------------------------------------------------
$root    = "${driveLetter}:\$($config.ssd.backupSubdir)"
$year    = Get-Date -Format "yyyy"
$month   = Get-Date -Format "yyyy-MM"
$fullDir = Join-Path $root "${year}_FULL"
$incrDir = Join-Path $root "${month}_INCR"
if (-not (Test-Path $root)) { New-Item -ItemType Directory -Path $root -Force | Out-Null }

$doFull = -not (Test-Path $fullDir)

# Cutoff for incremental = most recent successful SSD backup time.
$lastSsd = @($state.ssdBackups | Sort-Object { [DateTime]$_.time } -Descending | Select-Object -First 1)
$cutoff  = if ($lastSsd) { [DateTime]$lastSsd.time } else { [DateTime]::MinValue }

# --- Run -------------------------------------------------------------------
$allOk = $true
foreach ($src in $config.sourceFolders) {
    if (-not (Test-Path $src)) {
        Write-BackupLog "WARN: source '$src' not found, skipping." $logFile
        continue
    }
    $leaf = Split-Path $src -Leaf

    if ($doFull) {
        $dest = Join-Path $fullDir $leaf
        Write-BackupLog "FULL  $src  ==>  $dest" $logFile
        & robocopy "$src" "$dest" /MIR /COPY:DAT /DCOPY:DAT /R:2 /W:5 /NP /LOG+:"$logFile" /TEE | Out-Null
        if ($LASTEXITCODE -ge 8) { $allOk = $false; Write-BackupLog "ERROR: robocopy full failed for $src (code $LASTEXITCODE)" $logFile }
    } else {
        $dest = Join-Path $incrDir $leaf
        Write-BackupLog "INCR  $src  ==>  $dest  (cutoff $cutoff)" $logFile
        $changed = Get-ChildItem -Path $src -Recurse -File -ErrorAction SilentlyContinue |
                   Where-Object { $_.LastWriteTime -gt $cutoff }
        $count = 0
        foreach ($f in $changed) {
            $rel = $f.FullName.Substring($src.Length).TrimStart('\')
            $tp  = Join-Path $dest $rel
            $td  = Split-Path $tp -Parent
            if (-not (Test-Path $td)) { New-Item -ItemType Directory -Path $td -Force | Out-Null }
            try { Copy-Item -Path $f.FullName -Destination $tp -Force; $count++ }
            catch { $allOk = $false; Write-BackupLog "ERROR copy $($f.FullName): $_" $logFile }
        }
        Write-BackupLog "INCR  $leaf : $count changed file(s)." $logFile
    }
}

# --- Record result ---------------------------------------------------------
if ($allOk) {
    Add-BackupRecord -Kind ssd -Type ($(if ($doFull) {"full"} else {"incr"}))
    $state = Get-BackupState; $state.lastSsdResult = "ok"; Save-BackupState $state
    Write-BackupLog "SSD backup OK for $thisMonth." $logFile
} else {
    $state = Get-BackupState; $state.lastSsdResult = "errors"; Save-BackupState $state
    Write-BackupLog "SSD backup completed WITH ERRORS for $thisMonth." $logFile
}

# --- Air-gap: dismount -------------------------------------------------------
if ($config.ssd.dismountAfterBackup) {
    try {
        Write-BackupLog "Dismounting $driveLetter`: to restore air-gap." $logFile
        & mountvol "${driveLetter}:" /p
        Write-BackupLog "Volume dismounted." $logFile
    } catch { Write-BackupLog "WARN: dismount failed: $_" $logFile }
}

if ($allOk) {
    Show-Toast "Backup complete" "Backup finished and the SSD was dismounted. Safe to unplug."
} else {
    Show-Toast "Backup finished with errors" "Completed with errors - check the log. SSD dismounted."
}
Write-BackupLog "===== SSD backup run finished =====" $logFile
