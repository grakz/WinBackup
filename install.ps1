# ============================================================================
#  install.ps1  --  One-time installer for the backup system.
#
#  Run in an ELEVATED PowerShell (Run as Administrator):
#      Set-ExecutionPolicy -Scope Process Bypass
#      .\install.ps1
#
#  It will:
#    1. Copy all scripts to C:\Program Files\BackupSystem
#    2. Create C:\ProgramData\BackupSystem\config.json (from defaults),
#       auto-detecting the Proton Drive sync folder and helping you fill in
#       the SSD label + serial interactively.
#    3. Register the Proton (daily) and SSD (monthly) scheduled tasks.
#    4. Register the tray GUI to start at logon, and launch it now.
# ============================================================================
$ErrorActionPreference = "Stop"

# Require elevation.
$elevated = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elevated) { throw "Please run this installer in an elevated (Administrator) PowerShell." }

$srcDir     = $PSScriptRoot
$installDir = "C:\Program Files\BackupSystem"
$dataDir    = "C:\ProgramData\BackupSystem"
$user       = "$env:USERDOMAIN\$env:USERNAME"
$psExe      = (Get-Command powershell.exe).Source

# --- 1. Copy scripts -------------------------------------------------------
Write-Host "Installing scripts to $installDir ..."
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item (Join-Path $srcDir "*.ps1") -Destination $installDir -Force
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $dataDir "logs") -Force | Out-Null

. (Join-Path $installDir "lib-backup.ps1")

# --- 2. Create config ------------------------------------------------------
if (Test-Path $Global:ConfigPath) {
    Write-Host "Existing config found - keeping it. (Delete it to start fresh.)"
    $config = Get-BackupConfig
} else {
    Write-Host "Creating new config..."
    $config = Get-Content (Join-Path $srcDir "config.default.json") -Raw | ConvertFrom-Json

    # Auto-detect Proton Drive sync folder.
    $protonGuess = Join-Path $env:USERPROFILE "Proton Drive\My files\Backups"
    $protonBase  = Join-Path $env:USERPROFILE "Proton Drive"
    if (Test-Path $protonBase) {
        Write-Host "Detected Proton Drive folder at: $protonBase"
        $config.proton.syncFolder = $protonGuess
    } else {
        Write-Host "Could not auto-detect the Proton Drive folder."
        $entered = Read-Host "Enter the full path to a Proton-synced folder for backups"
        if ($entered) { $config.proton.syncFolder = $entered }
    }

    # Default source folders to this user's Documents + Pictures.
    $config.sourceFolders = @(
        (Join-Path $env:USERPROFILE "Documents"),
        (Join-Path $env:USERPROFILE "Pictures")
    )

    # Help fill in SSD identity.
    Write-Host ""
    Write-Host "=== SSD identification ==="
    Write-Host "Plug in your backup SSD now, then I'll list candidate volumes."
    Read-Host "Press Enter when the SSD is connected"
    Get-Volume | Where-Object DriveLetter | Format-Table DriveLetter, FileSystemLabel, `
        @{N='SizeGB';E={[math]::Round($_.Size/1GB,1)}} -AutoSize | Out-Host
    $dl = Read-Host "Enter the DRIVE LETTER of your backup SSD (e.g. E)"
    if ($dl) {
        try {
            $vol  = Get-Volume -DriveLetter $dl
            $disk = Get-Partition -DriveLetter $dl | Get-Disk
            $config.ssd.volumeLabel = $vol.FileSystemLabel
            $config.ssd.diskSerial  = $disk.SerialNumber.Trim()
            Write-Host "Recorded SSD: label='$($vol.FileSystemLabel)' serial='$($disk.SerialNumber.Trim())'"
            if ([string]::IsNullOrWhiteSpace($vol.FileSystemLabel)) {
                Write-Host "WARNING: this volume has no label. Give it one (e.g. BACKUP_SSD) and re-run, or set it in the GUI."
            }
        } catch {
            Write-Host "Could not read that drive automatically; set label/serial later in the GUI."
        }
    }

    Save-BackupConfig $config
    Write-Host "Config written to $Global:ConfigPath"
}

# --- 3. Register scheduled tasks -------------------------------------------
Write-Host "Registering scheduled tasks..."

# Proton: daily, unattended (S4U so it runs whether or not you're logged on).
$pt = $config.schedule.protonTime -split ":"
$protonAction  = New-ScheduledTaskAction -Execute $psExe `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$installDir\backup-proton.ps1`""
$protonTrigger = New-ScheduledTaskTrigger -Daily -At ([DateTime]::Today.AddHours([int]$pt[0]).AddMinutes([int]$pt[1]))
$protonPrincipal = New-ScheduledTaskPrincipal -UserId $user -LogonType S4U -RunLevel Highest
$protonSettings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 4)
Register-ScheduledTask -TaskName "BackupSystem - Proton" -Action $protonAction -Trigger $protonTrigger `
    -Principal $protonPrincipal -Settings $protonSettings `
    -Description "Daily incremental backup into the Proton Drive sync folder." -Force | Out-Null
Write-Host "  Registered: BackupSystem - Proton (daily $($config.schedule.protonTime))"

# SSD: monthly, interactive (must run in user session for toast + drive access).
# Use schtasks for the monthly trigger.
$st = $config.schedule.ssdTime -split ":"
$ssdTime = "{0:00}:{1:00}" -f [int]$st[0], [int]$st[1]
schtasks /Create /F /TN "BackupSystem - SSD" `
    /TR "$psExe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$installDir\backup-ssd.ps1`"" `
    /SC MONTHLY /D $([int]$config.schedule.ssdDayOfMonth) /ST $ssdTime /RL HIGHEST /IT | Out-Null
Write-Host "  Registered: BackupSystem - SSD (monthly day $($config.schedule.ssdDayOfMonth) at $ssdTime)"

# --- 4. Tray app autostart -------------------------------------------------
Write-Host "Setting up the tray app to start at logon..."
$trayAction  = New-ScheduledTaskAction -Execute $psExe `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$installDir\backup-tray.ps1`""
$trayTrigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$trayPrincipal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$traySettings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName "BackupSystem - Tray" -Action $trayAction -Trigger $trayTrigger `
    -Principal $trayPrincipal -Settings $traySettings `
    -Description "Backup System tray GUI." -Force | Out-Null
Write-Host "  Registered: BackupSystem - Tray (at logon)"

# Launch the tray now.
Start-Process $psExe -ArgumentList @(
    "-NoProfile","-WindowStyle","Hidden","-ExecutionPolicy","Bypass","-File","`"$installDir\backup-tray.ps1`""
)

Write-Host ""
Write-Host "============================================================"
Write-Host " Installation complete."
Write-Host " The shield icon is now in your system tray."
Write-Host " Left-click it for status; right-click for settings + actions."
Write-Host ""
Write-Host " Test now:"
Write-Host "   Start-ScheduledTask -TaskName 'BackupSystem - Proton'"
Write-Host "   Start-ScheduledTask -TaskName 'BackupSystem - SSD'"
Write-Host "============================================================"
