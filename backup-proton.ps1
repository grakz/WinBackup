# ============================================================================
#  backup-proton.ps1  --  Daily incremental backup into the Proton Drive
#  sync folder. The Proton Drive desktop app uploads it automatically.
#
#  Only files changed since the cutoff are written, so there is NO full second
#  copy of your data on disk -- only the recent deltas live in the sync folder.
#  Cutoff = the Nth-most-recent successful SSD backup (config.proton.lookbackBackups,
#  default 2), giving resilience if a monthly SSD backup was missed.
# ============================================================================

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\lib-backup.ps1"
Initialize-BackupRoot

$config  = Get-BackupConfig
$logFile = Join-Path $config.logDir ("proton_{0}.log" -f (Get-Date -Format "yyyy-MM"))
Write-BackupLog "===== Proton backup run started =====" $logFile

$syncRoot = $config.proton.syncFolder
$parent   = Split-Path $syncRoot -Parent
if (-not (Test-Path $parent)) {
    Write-BackupLog "ERROR: Proton sync parent '$parent' missing. Is the Proton Drive app installed and the folder synced? Aborting." $logFile
    $state = Get-BackupState; $state.lastProtonResult = "errors"; Save-BackupState $state
    exit 1
}
if (-not (Test-Path $syncRoot)) { New-Item -ItemType Directory -Path $syncRoot -Force | Out-Null }

# Cutoff from shared logic (handles fallbacks).
$cutoff = Get-ProtonCutoff -Config $config
$today  = Get-Date -Format "yyyy-MM-dd"
Write-BackupLog "Proton incremental cutoff: $cutoff" $logFile

# Each day's delta goes in a dated folder so nothing overwrites prior deltas.
$dayDir = Join-Path $syncRoot $today

$allOk = $true
$totalCopied = 0
foreach ($src in $config.sourceFolders) {
    if (-not (Test-Path $src)) {
        Write-BackupLog "WARN: source '$src' not found, skipping." $logFile
        continue
    }
    $leaf = Split-Path $src -Leaf
    $dest = Join-Path $dayDir $leaf

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
    $totalCopied += $count
    Write-BackupLog "PROTON $leaf : $count changed file(s)." $logFile
}

# If nothing changed, remove the empty dated folder to avoid clutter.
if ($totalCopied -eq 0 -and (Test-Path $dayDir)) {
    try { Remove-Item $dayDir -Recurse -Force } catch {}
    Write-BackupLog "No changes today; nothing written." $logFile
}

if ($allOk) {
    Add-BackupRecord -Kind proton -Type incr
    $state = Get-BackupState; $state.lastProtonResult = "ok"; Save-BackupState $state
    Write-BackupLog "Proton backup OK ($totalCopied files). App will sync to cloud." $logFile
} else {
    $state = Get-BackupState; $state.lastProtonResult = "errors"; Save-BackupState $state
    Write-BackupLog "Proton backup completed WITH ERRORS." $logFile
}
Write-BackupLog "===== Proton backup run finished =====" $logFile
