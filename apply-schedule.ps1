# ============================================================================
#  apply-schedule.ps1  --  Re-applies the schedule from config.json to the
#  already-registered scheduled tasks. Called by the GUI after saving, and by
#  the installer. Safe to run repeatedly.
# ============================================================================
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\lib-backup.ps1"
$config = Get-BackupConfig

function Parse-HHMM($s) {
    $parts = $s -split ":"
    return @{ H = [int]$parts[0]; M = [int]$parts[1] }
}

# Proton: daily.
try {
    $pt = Parse-HHMM $config.schedule.protonTime
    $trig = New-ScheduledTaskTrigger -Daily -At ([DateTime]::Today.AddHours($pt.H).AddMinutes($pt.M))
    Set-ScheduledTask -TaskName "BackupSystem - Proton" -Trigger $trig | Out-Null
    Write-Host "Proton schedule updated to daily $($config.schedule.protonTime)."
} catch { Write-Host "WARN: could not update Proton schedule: $_" }

# SSD: monthly on a given day-of-month. New-ScheduledTaskTrigger has no native
# monthly option, so we build the trigger via the CIM/COM monthly definition.
try {
    $st = Parse-HHMM $config.schedule.ssdTime
    $day = [int]$config.schedule.ssdDayOfMonth
    # Use schtasks.exe for the monthly trigger - simplest reliable path.
    $time = "{0:00}:{1:00}" -f $st.H, $st.M
    schtasks /Change /TN "BackupSystem - SSD" /SD 01/01/2020 2>$null | Out-Null
    # Recreate the monthly schedule cleanly:
    schtasks /Create /F /TN "BackupSystem - SSD" `
        /TR "powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$PSScriptRoot\backup-ssd.ps1`"" `
        /SC MONTHLY /D $day /ST $time /RL HIGHEST /IT | Out-Null
    Write-Host "SSD schedule updated to monthly day $day at $time."
} catch { Write-Host "WARN: could not update SSD schedule: $_" }
